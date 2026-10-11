using System;
using System.IO;
using System.Security.Cryptography;
using BigWalk.ModMenu;
using UnityEngine;

namespace BigWalk.ModMenu.Faces
{
    /// <summary>
    /// Your webcam on your own head, live. The game has no access to cameras, so a page in your browser
    /// reads the camera, follows your face and hands the pictures to <see cref="WebcamServer"/>; here they
    /// become the face of your character and go out to friends (<see cref="WebcamCodec"/>).
    /// It is never on by itself: the switch is off at every start of the game.
    /// </summary>
    public static class Webcam
    {
        public const byte PictureMessage = 22;
        public const int DefaultPort = 37845;
        private const float SendEvery = 1f / 8f;   // pictures a second for friends
        private const float StaleAfter = 3f;       // without a new picture for this long, the photo comes back

        public static bool On;
        public static int Port = DefaultPort;
        public static string Key = "";
        public static float Zoom = 1f, Wrap = 0.35f;
        public static bool Mirror;

        public static readonly FaceEntry Entry = new FaceEntry { Name = "webcam", Live = true };

        private static WebcamServer _server;
        private static readonly WebcamCodec.Encoder _encoder = new WebcamCodec.Encoder();
        private static float _lastPicture = -100f, _nextSend, _rateFrom;
        private static long _countedFrom;
        private static float _rate, _sendRate;
        private static int _sentInWindow;
        private static bool _faceFound, _shared;
        private static float _fillR = 60f, _fillG = 60f, _fillB = 60f;

        /// <summary>True while fresh pictures are arriving: the webcam is what goes on my head.</summary>
        public static bool Showing => On && Entry.Source != null && Time.unscaledTime - _lastPicture < StaleAfter;

        public static string Address => $"http://127.0.0.1:{Port}/?k={Key}";

        public static void Load(ModConfig c)
        {
            Port = Mathf.Clamp(c.GetInt("Webcam.Port", DefaultPort), 1024, 65535);
            Key = c.GetString("Webcam.Key", "");
            Zoom = Mathf.Clamp(c.GetFloat("Webcam.Zoom", 1f), 0.4f, 3f);
            Wrap = Mathf.Clamp01(c.GetFloat("Webcam.Wrap", 0.35f));
            Mirror = c.GetBool("Webcam.Mirror", false);
            if (Key.Length < 16)
            {
                // Made once and kept, so the page's address can be bookmarked.
                var bytes = new byte[12];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                Key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        public static void Save(ModConfig c)
        {
            c.Set("Webcam.Port", Port);
            c.Set("Webcam.Key", Key);
            c.Set("Webcam.Zoom", Zoom);
            c.Set("Webcam.Wrap", Wrap);
            c.Set("Webcam.Mirror", Mirror);
        }

        public static void Init()
        {
            Net.Register(PictureMessage, false, OnPicture);
        }

        private static string ReadPage()
        {
            using (Stream s = typeof(Webcam).Assembly.GetManifestResourceStream("webcam.html"))
            {
                if (s == null) return "<p>The webcam page is missing from this build of the mod.</p>";
                using (var reader = new StreamReader(s)) return reader.ReadToEnd();
            }
        }

        private static void EnsureServer()
        {
            if (_server != null && _server.Running) return;
            if (_server == null) _server = new WebcamServer(Key, ReadPage()) { Side = WebcamCodec.SendSide, Fps = 15 };
            if (_server.Start(Port)) Plugin.LogSource?.LogInfo($"[Webcam] Page ready on 127.0.0.1:{Port}.");
            else Plugin.LogSource?.LogWarning($"[Webcam] Could not open port {Port}: {_server.Error}");
        }

        public static void Update()
        {
            if (!On)
            {
                if (_server != null) _server.Wanted = false;
                _shared = false;
                return;
            }
            if (_server == null || !_server.Running)
            {
                // Tried again now and then: the port may be taken for a moment.
                if (Time.frameCount % 120 == 0 || _server == null) EnsureServer();
                if (_server == null || !_server.Running) return;
            }
            // The camera only runs during a walk.
            _server.Wanted = Game.InWorld;

            float now = Time.unscaledTime;
            if (now - _rateFrom >= 2f)
            {
                long frames = _server.Frames;
                _rate = (frames - _countedFrom) / (now - _rateFrom);
                _sendRate = _sentInWindow / (now - _rateFrom);
                _countedFrom = frames;
                _sentInWindow = 0;
                _rateFrom = now;
            }

            if (!_server.TryTakeFrame(out byte[] rgba, out int side, out bool faceFound)) return;
            _faceFound = faceFound;
            _lastPicture = now;

            Entry.Source = rgba;
            Entry.Width = Entry.Height = side;
            Entry.Settings.Zoom = Zoom;
            Entry.Settings.Wrap = Wrap;
            Entry.Settings.Mirror = Mirror;
            Entry.Settings.CenterX = Entry.Settings.CenterY = 0.5f;
            Entry.Fill = BorderColour(rgba, side);
            Entry.LiveDirty = true;

            _shared = GeneralModule.ShareMine && Net.Ready;
            if (_shared && now >= _nextSend)
            {
                _nextSend = now + SendEvery;
                Send(rgba, side);
            }
        }

        // The back of the head takes the colour of the picture's edge, eased so it does not flicker.
        private static Color32 BorderColour(byte[] rgba, int side)
        {
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < side; i++)
            {
                foreach (int o in new[] { i * 4, ((side - 1) * side + i) * 4, i * side * 4, (i * side + side - 1) * 4 })
                {
                    r += rgba[o]; g += rgba[o + 1]; b += rgba[o + 2]; n++;
                }
            }
            if (n > 0)
            {
                _fillR += (r / (float)n - _fillR) * 0.1f;
                _fillG += (g / (float)n - _fillG) * 0.1f;
                _fillB += (b / (float)n - _fillB) * 0.1f;
            }
            return new Color32((byte)_fillR, (byte)_fillG, (byte)_fillB, 255);
        }

        // ---- friends ----

        private static void Send(byte[] rgba, int side)
        {
            try
            {
                if (side != WebcamCodec.SendSide)
                {
                    rgba = Images.FlattenAndShrink(rgba, side, side, WebcamCodec.SendSide, 0, 0, 0, out side, out _);
                }
                byte[][] pieces = _encoder.Encode(rgba, side, Net.MaxBody, Zoom, Wrap, Mirror);
                if (pieces == null) return;
                foreach (byte[] piece in pieces) if (!Net.Send(PictureMessage, piece)) return;
                _sentInWindow++;
            }
            catch (Exception ex)
            {
                ModMenuApi.LogOnce("webcam-send", $"[Webcam] Could not send a picture: {ex.Message}");
            }
        }

        private static void OnPicture(uint sender, byte[] body)
        {
            // Only from players who already told us about their head (that message always comes first).
            if (!FaceSync.Remote.TryGetValue(sender, out RemoteFace rf)) return;
            WebcamCodec.Picture picture = rf.Decoder.Add(body);
            if (picture == null) return;
            float now = Time.unscaledTime;
            if (now - rf.VideoAt < 0.04f) return; // faster than anyone sends: not worth the work
            rf.VideoAt = now;

            if (rf.Video == null) rf.Video = new FaceEntry { Name = "webcam_" + sender, Live = true };
            FaceEntry e = rf.Video;
            e.Source = picture.Rgba;
            e.Width = e.Height = picture.Side;
            e.Settings.Zoom = picture.Zoom;
            e.Settings.Wrap = picture.Wrap;
            e.Settings.Mirror = picture.Mirror;
            e.Settings.CenterX = e.Settings.CenterY = 0.5f;
            // Same idea as BorderColour, from the corners only: it runs for every friend.
            int last = (picture.Side * picture.Side - 1) * 4, row = (picture.Side - 1) * 4;
            byte[] p = picture.Rgba;
            Color32 old = e.Fill;
            e.Fill = new Color32(
                (byte)(old.r + ((p[0] + p[row] + p[last - row] + p[last]) / 4 - old.r) / 8),
                (byte)(old.g + ((p[1] + p[row + 1] + p[last - row + 1] + p[last + 1]) / 4 - old.g) / 8),
                (byte)(old.b + ((p[2] + p[row + 2] + p[last - row + 2] + p[last + 2]) / 4 - old.b) / 8), 255);
            e.LiveDirty = true;
        }

        /// <summary>A friend's live picture, while it is fresh.</summary>
        public static FaceEntry VideoOf(RemoteFace rf)
        {
            return rf != null && rf.Video != null && rf.Video.Source != null && Time.unscaledTime - rf.VideoAt < StaleAfter ? rf.Video : null;
        }

        // ---- menu ----

        private static void OpenPage()
        {
            try { GUIUtility.systemCopyBuffer = Address; } catch { }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Address, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[Webcam] Could not open the browser: {ex.Message}");
            }
            ModMenuApi.Toast("Webcam page: address copied. If no browser opened, paste it into one.", 7f);
        }

        /// <summary>Returns true when a setting that is saved changed.</summary>
        public static bool Draw(Ui ui)
        {
            bool changed = false;
            ui.Header("Webcam");
            bool was = On;
            ui.Toggle("Live webcam on my head", ref On);
            if (On && !was) EnsureServer();
            if (!On)
            {
                ui.Note("Your camera, live, instead of the photo. A page in your browser reads the camera and follows your face. Friends in the walk who have the mod see it too.", 3);
                return false;
            }

            if (_server == null || !_server.Running)
            {
                ui.Note($"<color=#ff8888>Could not open port {Port} on this computer{(_server != null && _server.Error != null ? ": " + _server.Error : "")}.</color>", 2);
            }
            else if (!Game.InWorld) ui.Note("<color=#ffee88>The camera starts when you are in a walk.</color>");
            else if (_server.SecondsSinceRequest > 4.0) ui.Note("<color=#ffee88>Waiting for the page: open it in your browser and leave it open.</color>");
            else if (!Showing) ui.Note("<color=#ffee88>The page is open, no picture yet: allow the camera in the browser.</color>");
            else
            {
                string line = $"<color=#88ff88>Live: {_rate:0} pictures a second{(_faceFound ? "" : ", no face in sight")}.</color>";
                ui.Note(line);
                if (_shared) ui.Note($"To friends: {_sendRate:0.0} a second, {_encoder.Bytes} bytes each (quality {_encoder.Quality}).");
                else ui.Note(GeneralModule.ShareMine ? "Friends do not see it: " + Net.Status + "." : "Friends do not see it: sharing is off in General.");
            }

            int hit = ui.Buttons("Open the page", "Copy its address");
            if (hit == 0) OpenPage();
            else if (hit == 1)
            {
                try { GUIUtility.systemCopyBuffer = Address; ModMenuApi.Toast("Address copied: paste it into your browser."); }
                catch { ModMenuApi.Toast("Could not copy. The address is in the Faces tab."); }
            }
            ui.Note(Address, 1);

            changed |= ui.Slider($"Size on the head: {Mathf.RoundToInt(Zoom * 100f)}%", ref Zoom, 0.4f, 3f, 0.05f);
            changed |= ui.Slider($"Wrap around the head: {Mathf.RoundToInt(Wrap * 100f)}%", ref Wrap, 0f, 1f, 0.05f);
            changed |= ui.Toggle("Mirror", ref Mirror);
            ui.Note("While it is on, everyone in the walk with the mod sees your camera and could record it. The camera stops when you leave the walk, switch this off or close the page.", 3);
            return changed;
        }

        /// <summary>A reminder on screen for as long as the camera is wanted.</summary>
        public static void DrawOverlay()
        {
            if (!On || !Game.InWorld) return;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            string text = Showing
                ? (_shared ? "<color=#ff5555>●</color> <b>WEBCAM LIVE</b> — friends see you" : "<color=#ff5555>●</color> <b>WEBCAM LIVE</b> — only you see it")
                : "<color=#ffcc55>○</color> Webcam on, no picture";
            var rect = new Rect(Screen.width * 0.5f - 130f, 6f, 260f, 22f);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 1f, rect.width - 16f, 20f), "<size=12>" + text + "</size>");
        }
    }
}
