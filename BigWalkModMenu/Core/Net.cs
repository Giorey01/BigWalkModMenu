using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>Who a character is, in a form that stays the same from one session to the next.</summary>
    public static class Identity
    {
        public static uint NetId(PlayerCharacter pc)
        {
            try { return pc != null ? pc.netId : 0u; } catch { return 0u; }
        }

        /// <summary>Digits only: safe to use in a file name. Empty when the game does not know the player yet.</summary>
        public static string Key(PlayerCharacter pc)
        {
            try
            {
                PlayerNetworking pn = pc != null ? pc.playerNetworking : null;
                if (pn == null) return "";
                if (pn.userPlatformId != 0) return pn.userPlatformId.ToString();
                string epic = pn.epicUserId;
                if (!string.IsNullOrEmpty(epic)) return "9" + Fnv.Hash(System.Text.Encoding.UTF8.GetBytes(epic));
            }
            catch { }
            return "";
        }

        public static string Name(PlayerCharacter pc)
        {
            try
            {
                PlayerNetworking pn = pc != null ? pc.playerNetworking : null;
                if (pn != null && !string.IsNullOrEmpty(pn.username)) return pn.username;
            }
            catch { }
            return "";
        }

        public static PlayerCharacter ByNetId(uint netId)
        {
            if (netId == 0) return null;
            foreach (PlayerCharacter pc in Game.Players) if (NetId(pc) == netId) return pc;
            return null;
        }
    }

    /// <summary>
    /// Messages between players who all run the mod. They travel inside the game's own voice-chat
    /// message (Mirror id 8290, "ushort length + bytes"), told apart by two signature bytes — the
    /// technique smolMods.Core uses, with a different signature so the two can live side by side.
    ///
    ///   client -> host : [B7][4D][protocol][type][body]
    ///   host -> client : [B7][4D][protocol][type][sender netId, 4 bytes][body]
    ///
    /// The host writes the sender from the connection the message came in on, so nobody can speak for
    /// someone else. Everything received is untrusted: see WireReader.
    /// </summary>
    public static class Net
    {
        public const byte Protocol = 1;
        private const ushort VoiceMessageId = 8290;
        private const byte Magic0 = 0xB7, Magic1 = 0x4D;
        private const byte TypeHello = 0, TypeHelloAck = 1;
        public const byte FirstUserType = 16;
        private const int HeaderBytes = 4;
        private const int MaxFailures = 5;

        private sealed class Handler
        {
            public bool Retained;
            public Action<uint, byte[]> OnReceive;
        }

        private sealed class Peer
        {
            public float Tokens = 60f;
            public int Dropped;
            public bool Ignored;
            public float NextCatchUp; // a hello triggers a replay of everyone's state: not more often than this
        }

        private static readonly Dictionary<byte, Handler> _handlers = new Dictionary<byte, Handler>();
        private static readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();          // host: modded connections
        private static readonly Dictionary<uint, Dictionary<int, byte[]>> _retained = new Dictionary<uint, Dictionary<int, byte[]>>(); // host: last state per sender
        private static readonly Queue<KeyValuePair<int, byte[]>> _outbox = new Queue<KeyValuePair<int, byte[]>>();
        private static readonly List<int> _scratch = new List<int>();
        private static readonly List<uint> _scratchIds = new List<uint>();

        private static NetworkMessageDelegate _mineServer, _mineClient, _origServer, _origClient;
        private static bool _hookedServer, _hookedClient, _underSmolServer, _underSmolClient;
        private static bool _sessionActive, _hostModded, _disabled, _versionWarned;
        private static int _failures, _helloAttempts;
        private static float _sessionStart, _nextTick, _nextHello, _lastTokenTime;
        private static int _maxPayload = 1000;
        private static string _problem;

        /// <summary>Raised when messages can start flowing (as host, or once the host answered). Send your state now.</summary>
        public static event Action Connected;
        /// <summary>Raised on the host and on clients when someone new may need our state again.</summary>
        public static event Action<uint> PeerJoined;

        public static bool IsHost
        {
            get { try { return NetworkServer.active; } catch { return false; } }
        }

        /// <summary>True when a message sent now reaches the other modded players.</summary>
        public static bool Ready => !_disabled && _sessionActive && (IsHost ? _hookedServer : _hostModded && _hookedClient);
        public static int MaxBody => _maxPayload - HeaderBytes - 4;
        public static int ModdedPeers => _peers.Count;

        public static uint LocalNetId => Identity.NetId(Game.LocalPlayer);

        public static string Status
        {
            get
            {
                if (_disabled) return "spenta: " + (_problem ?? "errore");
                if (!_sessionActive) return "non in partita";
                if (IsHost) return _hookedServer ? $"attiva, sei l'host ({_peers.Count} amici con la mod)" : "in attesa della chat vocale";
                if (_hostModded) return "attiva";
                return _helloAttempts >= 4 ? "l'host non ha la mod" : "cerco l'host…";
            }
        }

        /// <summary>
        /// Registers a message type (16-255). With retained = true the first body byte is a key: the host
        /// keeps the last message per sender and key and hands it to players who join later.
        /// </summary>
        public static void Register(byte type, bool retained, Action<uint, byte[]> onReceive)
        {
            if (type < FirstUserType || onReceive == null) throw new ArgumentException("message type must be 16 or above");
            if (_handlers.ContainsKey(type)) { ModMenuApi.Log.LogWarning($"[Net] Message type {type} registered twice, ignored."); return; }
            _handlers[type] = new Handler { Retained = retained, OnReceive = onReceive };
        }

        /// <summary>Sends to every other modded player. Returns false when the channel is not ready or the body is too long.</summary>
        public static bool Send(byte type, byte[] body)
        {
            if (!Ready || !_handlers.TryGetValue(type, out Handler handler)) return false;
            body = body ?? Array.Empty<byte>();
            if (body.Length > MaxBody || (handler.Retained && body.Length == 0)) return false;
            try
            {
                if (IsHost)
                {
                    uint me = LocalNetId;
                    if (me == 0) return false;
                    if (handler.Retained) Retain(me, type, body);
                    Relay(type, me, body, -1);
                }
                else
                {
                    NetworkConnection server = NetworkClient.connection;
                    if (server == null) return false;
                    server.Send(ToSegment(Frame(type, null, body)), 0);
                }
                return true;
            }
            catch (Exception ex)
            {
                Fail("send: " + ex.Message);
                return false;
            }
        }

        // ---- frames ----

        private static byte[] Frame(byte type, uint? sender, byte[] body)
        {
            int payload = HeaderBytes + (sender.HasValue ? 4 : 0) + body.Length;
            var frame = new byte[4 + payload];
            frame[0] = (byte)(VoiceMessageId & 0xFF);
            frame[1] = (byte)(VoiceMessageId >> 8);
            frame[2] = (byte)(payload & 0xFF);
            frame[3] = (byte)(payload >> 8);
            frame[4] = Magic0;
            frame[5] = Magic1;
            frame[6] = Protocol;
            frame[7] = type;
            int at = 8;
            if (sender.HasValue)
            {
                uint s = sender.Value;
                frame[at++] = (byte)s; frame[at++] = (byte)(s >> 8); frame[at++] = (byte)(s >> 16); frame[at++] = (byte)(s >> 24);
            }
            Buffer.BlockCopy(body, 0, frame, at, body.Length);
            return frame;
        }

        private static Il2CppSystem.ArraySegment<byte> ToSegment(byte[] frame)
        {
            var array = new Il2CppStructArray<byte>(frame.Length);
            for (int i = 0; i < frame.Length; i++) array[i] = frame[i];
            return new Il2CppSystem.ArraySegment<byte>(array);
        }

        // Looks at the next voice message without consuming it unless it carries our signature.
        private static bool TryTake(NetworkReader reader, out byte[] payload)
        {
            payload = null;
            int start = reader.Position;
            try
            {
                ushort length = NetworkReaderExtensions.ReadUShort(reader);
                if (length < HeaderBytes || length > _maxPayload || length > reader.Remaining)
                {
                    reader.Position = start;
                    return false;
                }
                byte m0 = reader.ReadByte(), m1 = reader.ReadByte();
                if (m0 != Magic0 || m1 != Magic1)
                {
                    reader.Position = start;
                    return false;
                }
                var data = new byte[length];
                data[0] = m0;
                data[1] = m1;
                for (int i = 2; i < length; i++) data[i] = reader.ReadByte();
                payload = data;
                return true;
            }
            catch
            {
                try { reader.Position = start; } catch { }
                return false;
            }
        }

        private static void CallOriginal(NetworkMessageDelegate original, NetworkConnection conn, NetworkReader reader, int channelId)
        {
            try
            {
                if (original != null) original.Invoke(conn, reader, channelId);
            }
            catch { }
        }

        // ---- receiving ----

        private static void ServerReceive(NetworkConnection conn, NetworkReader reader, int channelId)
        {
            byte[] payload = null;
            if (_disabled || !TryTake(reader, out payload))
            {
                CallOriginal(_origServer, conn, reader, channelId);
                return;
            }
            try
            {
                if (payload[2] != Protocol) { WarnVersion(); return; }
                byte type = payload[3];
                int connId = conn != null ? conn.connectionId : -1;
                if (connId <= 0) return; // 0 is the host's own loop-back connection

                if (type == TypeHello)
                {
                    if (!_peers.TryGetValue(connId, out Peer known))
                    {
                        known = new Peer();
                        _peers[connId] = known;
                        ModMenuApi.Log.LogInfo($"[Net] Connection {connId} has the mod ({_peers.Count} in total).");
                    }
                    // Repeated hellos must not turn into a flood of replayed state.
                    if (known.Ignored || Time.unscaledTime < known.NextCatchUp) return;
                    known.NextCatchUp = Time.unscaledTime + 10f;
                    Enqueue(connId, Frame(TypeHelloAck, null, Array.Empty<byte>()));
                    // Bring the newcomer up to date with everyone's current state.
                    foreach (var sender in _retained)
                    {
                        foreach (var entry in sender.Value) Enqueue(connId, Frame((byte)(entry.Key >> 8), sender.Key, entry.Value));
                    }
                    uint joined = SenderOf(conn);
                    if (joined != 0) SafeRaise(PeerJoined, joined);
                    return;
                }

                if (!_peers.TryGetValue(connId, out Peer peer) || peer.Ignored) return;
                if (!_handlers.TryGetValue(type, out Handler handler)) return;

                // Rate limit per connection.
                if (peer.Tokens < 1f)
                {
                    if (++peer.Dropped == 300)
                    {
                        peer.Ignored = true;
                        ModMenuApi.Log.LogWarning($"[Net] Connection {connId} sends far too much: ignored for this session.");
                    }
                    return;
                }
                peer.Tokens -= 1f;

                uint senderId = SenderOf(conn);
                if (senderId == 0) return;
                int bodyLength = payload.Length - HeaderBytes;
                if (bodyLength > MaxBody || (handler.Retained && bodyLength == 0)) return;
                var body = new byte[bodyLength];
                Buffer.BlockCopy(payload, HeaderBytes, body, 0, bodyLength);

                if (handler.Retained) Retain(senderId, type, body);
                Relay(type, senderId, body, connId);
                Dispatch(handler, type, senderId, body);
            }
            catch (Exception ex)
            {
                Fail("server receive: " + ex.Message);
            }
        }

        private static void ClientReceive(NetworkConnection conn, NetworkReader reader, int channelId)
        {
            byte[] payload = null;
            if (_disabled || !TryTake(reader, out payload))
            {
                CallOriginal(_origClient, conn, reader, channelId);
                return;
            }
            try
            {
                if (IsHost) return; // the host hears everything on the server side already
                if (payload[2] != Protocol) { WarnVersion(); return; }
                byte type = payload[3];
                if (type == TypeHelloAck)
                {
                    if (!_hostModded)
                    {
                        _hostModded = true;
                        ModMenuApi.Log.LogInfo("[Net] The host has the mod: channel open.");
                        SafeRaise(Connected);
                    }
                    return;
                }
                if (!_handlers.TryGetValue(type, out Handler handler)) return;
                if (payload.Length < HeaderBytes + 4) return;
                uint senderId = (uint)(payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24));
                if (senderId == 0 || senderId == LocalNetId) return;
                var body = new byte[payload.Length - HeaderBytes - 4];
                Buffer.BlockCopy(payload, HeaderBytes + 4, body, 0, body.Length);
                Dispatch(handler, type, senderId, body);
            }
            catch (Exception ex)
            {
                Fail("client receive: " + ex.Message);
            }
        }

        private static uint SenderOf(NetworkConnection conn)
        {
            try
            {
                return conn != null && conn.identity != null ? conn.identity.netId : 0u;
            }
            catch { return 0u; }
        }

        private static void Dispatch(Handler handler, byte type, uint sender, byte[] body)
        {
            try { handler.OnReceive(sender, body); }
            catch (Exception ex) { ModMenuApi.LogOnce("net-handler-" + type, $"[Net] Handler for message {type} failed: {ex}"); }
        }

        private static void SafeRaise(Action action)
        {
            try { action?.Invoke(); } catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Net] Event handler failed: {ex.Message}"); }
        }

        private static void SafeRaise(Action<uint> action, uint arg)
        {
            try { action?.Invoke(arg); } catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Net] Event handler failed: {ex.Message}"); }
        }

        // ---- host side ----

        private static void Retain(uint sender, byte type, byte[] body)
        {
            if (!_retained.TryGetValue(sender, out var entries))
            {
                entries = new Dictionary<int, byte[]>();
                _retained[sender] = entries;
            }
            int key = (type << 8) | body[0];
            // A cap per player, so a faulty client cannot fill the host's memory.
            if (!entries.ContainsKey(key) && entries.Count >= 96) return;
            entries[key] = body;
        }

        private static void Relay(byte type, uint sender, byte[] body, int exceptConnId)
        {
            if (_peers.Count == 0) return;
            byte[] frame = Frame(type, sender, body);
            foreach (var kv in _peers)
            {
                if (kv.Key != exceptConnId) Enqueue(kv.Key, frame);
            }
        }

        private static void Enqueue(int connId, byte[] frame)
        {
            if (_outbox.Count > 4000) return; // something is very wrong: drop instead of growing without end
            _outbox.Enqueue(new KeyValuePair<int, byte[]>(connId, frame));
        }

        private static void Flush()
        {
            // A few per frame: catching a newcomer up must not flood the connection.
            for (int i = 0; i < 12 && _outbox.Count > 0; i++)
            {
                var item = _outbox.Dequeue();
                try
                {
                    var connections = NetworkServer.connections;
                    if (connections != null && connections.TryGetValue(item.Key, out NetworkConnectionToClient conn) && conn != null)
                    {
                        conn.Send(ToSegment(item.Value), 0);
                    }
                }
                catch (Exception ex) { Fail("relay: " + ex.Message); }
            }
        }

        // ---- hooks ----

        private static bool Same(NetworkMessageDelegate a, NetworkMessageDelegate b)
        {
            return a != null && b != null && a.Pointer == b.Pointer;
        }

        // smolMods.Core replaces the same handler and checks it again every second. Wrapping on top of
        // its wrapper would make it wrap us in turn and, as it keeps a single "original" field, loop
        // forever. So when its wrapper is installed we sit underneath it instead.
        private static class Smol
        {
            private static bool _searched;
            private static FieldInfo _origServer, _origClient, _ourServer, _ourClient;
            public static bool Present, Usable;

            public static void Find()
            {
                if (_searched) return;
                _searched = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "smolMods.Core") continue;
                    Present = true;
                    Type t = asm.GetType("SmolMods.Core.ModChannel", false);
                    if (t == null) break;
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                    _origServer = t.GetField("_origServer", flags);
                    _origClient = t.GetField("_origClient", flags);
                    _ourServer = t.GetField("_ourServer", flags);
                    _ourClient = t.GetField("_ourClient", flags);
                    Usable = _origServer != null && _origClient != null && _ourServer != null && _ourClient != null
                             && _origServer.FieldType == typeof(NetworkMessageDelegate) && _ourServer.FieldType == typeof(NetworkMessageDelegate);
                    break;
                }
                if (Present) ModMenuApi.Log.LogInfo($"[Net] smolMods.Core found, {(Usable ? "sharing the voice hook with it" : "its internals are not the expected ones")}.");
            }

            public static NetworkMessageDelegate Ours(bool server) => (NetworkMessageDelegate)(server ? _ourServer : _ourClient).GetValue(null);
            public static NetworkMessageDelegate Orig(bool server) => (NetworkMessageDelegate)(server ? _origServer : _origClient).GetValue(null);
            public static void SetOrig(bool server, NetworkMessageDelegate value) => (server ? _origServer : _origClient).SetValue(null, value);
        }

        private static void EnsureHook(bool server)
        {
            var handlers = server ? NetworkServer.handlers : NetworkClient.handlers;
            if (handlers == null || !handlers.TryGetValue(VoiceMessageId, out NetworkMessageDelegate current) || current == null) return;

            NetworkMessageDelegate mine = server ? _mineServer : _mineClient;
            if (mine == null)
            {
                mine = server
                    ? DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(new Action<NetworkConnection, NetworkReader, int>(ServerReceive))
                    : DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(new Action<NetworkConnection, NetworkReader, int>(ClientReceive));
                if (server) _mineServer = mine; else _mineClient = mine;
            }
            if (Same(current, mine)) { SetHooked(server, true, false); return; }

            if (Smol.Present)
            {
                if (!Smol.Usable) { Disable("versione di smolMods.Core non riconosciuta"); return; }
                NetworkMessageDelegate theirs = Smol.Ours(server);
                if (Same(current, theirs))
                {
                    NetworkMessageDelegate theirOriginal = Smol.Orig(server);
                    if (Same(theirOriginal, mine)) { SetHooked(server, true, true); return; } // already underneath
                    if (theirOriginal == null || Same(theirOriginal, theirs)) return;
                    if (server) _origServer = theirOriginal; else _origClient = theirOriginal;
                    Smol.SetOrig(server, mine);
                    SetHooked(server, true, true);
                    ModMenuApi.Log.LogInfo($"[Net] {(server ? "Server" : "Client")} hook installed under smolMods.Core.");
                    return;
                }
                // Not wrapped by them yet: give them a moment to go first, the simple case.
                if (Time.unscaledTime - _sessionStart < 3f) return;
            }

            if (server) _origServer = current; else _origClient = current;
            handlers[VoiceMessageId] = mine;
            SetHooked(server, true, false);
            ModMenuApi.Log.LogInfo($"[Net] {(server ? "Server" : "Client")} hook installed.");
        }

        private static void SetHooked(bool server, bool hooked, bool underSmol)
        {
            if (server) { _hookedServer = hooked; _underSmolServer = underSmol; }
            else { _hookedClient = hooked; _underSmolClient = underSmol; }
        }

        private static void Unhook(bool server)
        {
            try
            {
                NetworkMessageDelegate mine = server ? _mineServer : _mineClient;
                NetworkMessageDelegate original = server ? _origServer : _origClient;
                if (mine != null && original != null)
                {
                    var handlers = server ? NetworkServer.handlers : NetworkClient.handlers;
                    if (handlers != null && handlers.TryGetValue(VoiceMessageId, out NetworkMessageDelegate current) && Same(current, mine))
                    {
                        handlers[VoiceMessageId] = original;
                    }
                    if (Smol.Present && Smol.Usable && Same(Smol.Orig(server), mine)) Smol.SetOrig(server, original);
                }
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Net] Unhook: {ex.Message}"); }
            if (server) _origServer = null; else _origClient = null;
            SetHooked(server, false, false);
        }

        // ---- upkeep ----

        internal static void Tick()
        {
            if (_disabled || _handlers.Count == 0) return;
            try
            {
                if (_hookedServer && IsHost) Flush();
                RefillTokens();
                if (Time.unscaledTime < _nextTick) return;
                _nextTick = Time.unscaledTime + 1f;

                bool active = NetworkClient.active || NetworkServer.active;
                if (!active)
                {
                    if (_sessionActive) EndSession();
                    return;
                }
                if (!_sessionActive)
                {
                    _sessionActive = true;
                    _sessionStart = Time.unscaledTime;
                    Smol.Find();
                    ReadLimit();
                }

                bool wasReady = Ready;
                if (NetworkServer.active) EnsureHook(true);
                if (NetworkClient.active) EnsureHook(false);
                if (!wasReady && Ready && IsHost) SafeRaise(Connected);

                // Client: say hello to the host, a few times at most.
                if (!IsHost && !_hostModded && _hookedClient && _helloAttempts < 4 && NetworkClient.isConnected
                    && Game.InWorld && Time.unscaledTime >= _nextHello)
                {
                    _helloAttempts++;
                    _nextHello = Time.unscaledTime + 2f * _helloAttempts;
                    NetworkConnection serverConn = NetworkClient.connection;
                    if (serverConn != null) serverConn.Send(ToSegment(Frame(TypeHello, null, Array.Empty<byte>())), 0);
                }

                if (IsHost) Prune();
            }
            catch (Exception ex)
            {
                Fail("tick: " + ex.Message);
            }
        }

        private static void ReadLimit()
        {
            try
            {
                // The voice code copies each message into buffers of this size: ours must fit too.
                int buffer = Dissonance.Integrations.MirrorIgnorance.DissonanceNetworkMessageExtensions.BufferLength;
                if (buffer >= 256) _maxPayload = Mathf.Min(buffer - 8, 1100);
            }
            catch { }
            ModMenuApi.Log.LogInfo($"[Net] Largest message: {_maxPayload} bytes.");
        }

        private static void RefillTokens()
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - _lastTokenTime, 0f, 1f);
            _lastTokenTime = now;
            if (_peers.Count == 0) return;
            foreach (Peer p in _peers.Values) p.Tokens = Mathf.Min(60f, p.Tokens + 30f * dt);
        }

        private static void Prune()
        {
            var connections = NetworkServer.connections;
            if (connections == null) return;
            _scratch.Clear();
            foreach (int id in _peers.Keys) if (!connections.ContainsKey(id)) _scratch.Add(id);
            foreach (int id in _scratch) _peers.Remove(id);

            // Forget the state of players who are gone.
            if (_retained.Count == 0) return;
            _scratchIds.Clear();
            foreach (uint netId in _retained.Keys) if (Identity.ByNetId(netId) == null) _scratchIds.Add(netId);
            foreach (uint netId in _scratchIds) _retained.Remove(netId);
        }

        private static void EndSession()
        {
            Unhook(true);
            Unhook(false);
            _sessionActive = false;
            _hostModded = false;
            _helloAttempts = 0;
            _nextHello = 0f;
            _peers.Clear();
            _retained.Clear();
            _outbox.Clear();
        }

        private static void WarnVersion()
        {
            if (_versionWarned) return;
            _versionWarned = true;
            ModMenuApi.Log.LogWarning("[Net] A player uses a different version of the mod menu: their messages are ignored.");
            ModMenuApi.Toast("Un amico ha una versione diversa della mod: il suo aspetto non verrà condiviso.", 6f);
        }

        private static void Fail(string what)
        {
            _failures++;
            ModMenuApi.Log.LogWarning($"[Net] {what} ({_failures}/{MaxFailures})");
            if (_failures >= MaxFailures) Disable("troppi errori");
        }

        // From here on every voice message goes straight to the original handler.
        private static void Disable(string reason)
        {
            if (_disabled) return;
            _disabled = true;
            _problem = reason;
            ModMenuApi.Log.LogError($"[Net] Channel switched off: {reason}.");
            try { Unhook(true); Unhook(false); } catch { }
        }
    }
}
