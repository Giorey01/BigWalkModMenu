using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BigWalk.ModMenu.Faces
{
    /// <summary>
    /// A very small web server on this computer only (127.0.0.1). It hands out the webcam page and takes
    /// the pictures that page sends back. The game cannot open a webcam itself, a browser can.
    ///
    ///   GET  /         the page
    ///   GET  /status   what the mod wants: {"on":true,"side":128,"fps":15}
    ///   POST /frame    one picture: side x side RGBA bytes, top row first
    ///
    /// Nothing here touches the game: it runs on its own threads and the mod picks the newest picture up
    /// from its Update. Every request is untrusted: any program or web page on this computer can reach
    /// the port, so /status and /frame need the key that only the mod's own address carries, the Host
    /// must be this server and a request coming from another site's page is refused.
    /// </summary>
    public sealed class WebcamServer
    {
        public const int MinSide = 32, MaxSide = 256;
        private const int MaxHeaderBytes = 8192;

        private readonly object _lock = new object();
        private readonly string _key;
        private readonly byte[] _page;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private int _clients;

        private byte[] _frame;
        private int _frameSide;
        private bool _frameFace;
        private long _frames, _lastRequestTicks;

        /// <summary>What the page is told: capture or not, picture size and pictures per second.</summary>
        public volatile bool Wanted;
        public volatile int Side = 128, Fps = 15;

        public int Port { get; private set; }
        public string Error { get; private set; }
        public bool Running => _running;
        public long Frames => Interlocked.Read(ref _frames);

        /// <summary>Seconds since the page last asked for anything; large when no page is open.</summary>
        public double SecondsSinceRequest
        {
            get
            {
                long t = Interlocked.Read(ref _lastRequestTicks);
                return t == 0 ? double.MaxValue : (DateTime.UtcNow.Ticks - t) / (double)TimeSpan.TicksPerSecond;
            }
        }

        public WebcamServer(string key, string pageHtml)
        {
            _key = key ?? "";
            _page = Encoding.UTF8.GetBytes(pageHtml ?? "");
        }

        public bool Start(int port)
        {
            if (_running) return true;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(8);
                Port = port;
                Error = null;
                _running = true;
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "bwmm-webcam" };
                _thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _running = false;
                try { _listener?.Stop(); } catch { }
                _listener = null;
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        /// <summary>The newest picture not handed out yet, or false. The caller owns the array.</summary>
        public bool TryTakeFrame(out byte[] rgba, out int side, out bool faceFound)
        {
            lock (_lock)
            {
                rgba = _frame;
                side = _frameSide;
                faceFound = _frameFace;
                _frame = null;
            }
            return rgba != null;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_running) Thread.Sleep(50); continue; }

                // A page needs one or two connections; more than a handful is something else knocking.
                if (Interlocked.Increment(ref _clients) > 8)
                {
                    Interlocked.Decrement(ref _clients);
                    try { client.Close(); } catch { }
                    continue;
                }
                var t = new Thread(() => Serve(client)) { IsBackground = true, Name = "bwmm-webcam-client" };
                t.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 5000;
                using (NetworkStream stream = client.GetStream())
                {
                    var header = new byte[MaxHeaderBytes];
                    while (_running && HandleRequest(stream, header)) { }
                }
            }
            catch { }
            finally
            {
                try { client.Close(); } catch { }
                Interlocked.Decrement(ref _clients);
            }
        }

        // Reads one request and answers it. Returns false when the connection must be closed.
        private bool HandleRequest(NetworkStream stream, byte[] header)
        {
            // Header: byte by byte up to the empty line (requests are tiny, pictures come after it).
            int n = 0, state = 0;
            while (state < 4)
            {
                if (n >= header.Length) return false;
                int b = stream.ReadByte();
                if (b < 0) return false;
                header[n++] = (byte)b;
                if (b == (state % 2 == 0 ? '\r' : '\n')) state++; else state = b == '\r' ? 1 : 0;
            }

            string[] lines = Encoding.ASCII.GetString(header, 0, n).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;
            string[] first = lines[0].Split(' ');
            if (first.Length < 3) return false;
            string method = first[0], path = first[1];
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            int length = 0;
            if (headers.TryGetValue("Content-Length", out string lengthText)
                && (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0)) return false;
            if (headers.ContainsKey("Transfer-Encoding")) return false;

            // Only our own address: a web page that reaches this port under another name (or from another
            // site) gets nothing.
            string hostA = "127.0.0.1:" + Port, hostB = "localhost:" + Port;
            headers.TryGetValue("Host", out string host);
            bool hostOk = host == hostA || host == hostB;
            bool originOk = !headers.TryGetValue("Origin", out string origin) || origin == "http://" + hostA || origin == "http://" + hostB;
            if (!hostOk || !originOk) { Reply(stream, "403 Forbidden", null, null, false); return false; }

            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);

            if (method == "GET" && path == "/")
            {
                if (length != 0) return false;
                Reply(stream, "200 OK", "text/html; charset=utf-8", _page, true);
                return true;
            }

            headers.TryGetValue("X-Key", out string key);
            if (!SameText(key, _key)) { Reply(stream, "403 Forbidden", null, null, false); return false; }

            if (method == "GET" && path == "/status")
            {
                if (length != 0) return false;
                string json = "{\"on\":" + (Wanted ? "true" : "false") + ",\"side\":" + Side.ToString(CultureInfo.InvariantCulture)
                              + ",\"fps\":" + Fps.ToString(CultureInfo.InvariantCulture) + "}";
                Reply(stream, "200 OK", "application/json", Encoding.ASCII.GetBytes(json), true);
                return true;
            }

            if (method == "POST" && path == "/frame")
            {
                // The size is known before a single byte of the picture is read.
                int side = (int)Math.Round(Math.Sqrt(length / 4.0));
                if (side < MinSide || side > MaxSide || side * side * 4 != length) { Reply(stream, "400 Bad Request", null, null, false); return false; }
                var body = new byte[length];
                int got = 0;
                while (got < length)
                {
                    int r = stream.Read(body, got, length - got);
                    if (r <= 0) return false;
                    got += r;
                }
                if (Wanted)
                {
                    headers.TryGetValue("X-Face", out string face);
                    lock (_lock)
                    {
                        _frame = body; // an unread older picture is simply replaced
                        _frameSide = side;
                        _frameFace = face == "1";
                    }
                    Interlocked.Increment(ref _frames);
                }
                Reply(stream, "204 No Content", null, null, true);
                return true;
            }

            Reply(stream, "404 Not Found", null, null, false);
            return false;
        }

        private static bool SameText(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length || b.Length == 0) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void Reply(NetworkStream stream, string status, string type, byte[] body, bool keepAlive)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            if (type != null) sb.Append("Content-Type: ").Append(type).Append("\r\n");
            sb.Append("Content-Length: ").Append(body != null ? body.Length : 0).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n");
            sb.Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");
            sb.Append("\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            if (body != null && body.Length > 0) stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
