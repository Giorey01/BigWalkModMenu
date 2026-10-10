using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigWalk.ModMenu.Character
{
    [BepInPlugin("com.bigwalk.modmenu.character", "Big Walk Mod Menu - Personaggio", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new CharacterModule());
        }
    }

    /// <summary>Colour as hue/saturation/value plus an on/off switch.</summary>
    public sealed class Tint
    {
        public bool On;
        public float H, S = 1f, V = 1f;

        public Tint(float h = 0f, float s = 1f, float v = 1f, bool on = false)
        {
            H = h; S = s; V = v; On = on;
        }

        public Color Colour => Game.Hsv(H, S, V);

        public void Load(ModConfig c, string key)
        {
            On = c.GetBool(key + ".On", On);
            H = c.GetFloat(key + ".H", H);
            S = c.GetFloat(key + ".S", S);
            V = c.GetFloat(key + ".V", V);
        }

        public void Save(ModConfig c, string key)
        {
            c.Set(key + ".On", On);
            c.Set(key + ".H", H);
            c.Set(key + ".S", S);
            c.Set(key + ".V", V);
        }
    }

    /// <summary>Everything chosen for one player slot.</summary>
    public sealed class Look
    {
        // 0 head, 1 torso, 2 legs: the three parts the game itself colours.
        public readonly Tint[] Body = { new Tint(), new Tint(0.33f), new Tint(0.6f) };
        public float Torso = 1f, Bottom = 1f, Hands = 1f, Feet = 1f, Limbs = 1f;
        public float NoseSize = 1f, NoseLength = 1f;
        public int Hat;
        public float HatSize = 1f, HatHeight;
        public readonly Tint HatColour = new Tint(0f, 0f, 0.12f, true);
        public readonly Tint HatSecond = new Tint(0f, 0.9f, 0.9f, true);
        public readonly bool[] Accessories = new bool[Wardrobe.AccessoryNames.Length];
        public readonly Tint AccessoryColour = new Tint(0f, 0f, 0.1f, true);
        public readonly Tint Glow = new Tint(0.12f, 0.5f, 1f);
        public float GlowPower = 1f;
        public readonly Tint Trail = new Tint(0.55f, 0.8f, 1f);
        public float TrailLength = 3f;

        private const byte WireVersion = 1;

        private static void Write(WireWriter w, Tint t)
        {
            w.Bool(t.On).Q8(t.H, 0f, 1f).Q8(t.S, 0f, 1f).Q8(t.V, 0f, 1f);
        }

        private static void Read(WireReader r, Tint t)
        {
            t.On = r.Bool();
            t.H = r.Q8(0f, 1f);
            t.S = r.Q8(0f, 1f);
            t.V = r.Q8(0f, 1f);
        }

        /// <summary>The look as sent to friends. The ranges are the ones of the sliders in the menu.</summary>
        public byte[] ToWire()
        {
            var w = new WireWriter();
            w.U8(0).U8(WireVersion); // first byte: key of the retained message
            foreach (Tint t in Body) Write(w, t);
            w.Q8(Torso, 0.3f, 3f).Q8(Bottom, 0.3f, 3f).Q8(Hands, 0.3f, 4f).Q8(Feet, 0.3f, 4f).Q8(Limbs, 0.3f, 4f);
            w.Q8(NoseSize, 0.2f, 5f).Q8(NoseLength, 0.2f, 8f);
            w.U8(Hat).Q8(HatSize, 0.4f, 3f).Q8(HatHeight, -0.6f, 1.5f);
            Write(w, HatColour);
            Write(w, HatSecond);
            int mask = 0;
            for (int i = 0; i < Accessories.Length && i < 8; i++) if (Accessories[i]) mask |= 1 << i;
            w.U8(mask);
            Write(w, AccessoryColour);
            Write(w, Glow);
            w.Q8(GlowPower, 0f, 3f);
            Write(w, Trail);
            w.Q8(TrailLength, 0.5f, 15f);
            return w.ToArray();
        }

        /// <summary>Reads a look received from another player. Returns null when the data is not a valid look.</summary>
        public static Look FromWire(byte[] body)
        {
            var r = new WireReader(body);
            r.U8();
            if (r.U8() != WireVersion) return null;
            var look = new Look();
            foreach (Tint t in look.Body) Read(r, t);
            look.Torso = r.Q8(0.3f, 3f);
            look.Bottom = r.Q8(0.3f, 3f);
            look.Hands = r.Q8(0.3f, 4f);
            look.Feet = r.Q8(0.3f, 4f);
            look.Limbs = r.Q8(0.3f, 4f);
            look.NoseSize = r.Q8(0.2f, 5f);
            look.NoseLength = r.Q8(0.2f, 8f);
            look.Hat = r.Int(0, Wardrobe.HatNames.Length - 1);
            look.HatSize = r.Q8(0.4f, 3f);
            look.HatHeight = r.Q8(-0.6f, 1.5f);
            Read(r, look.HatColour);
            Read(r, look.HatSecond);
            int mask = r.U8();
            for (int i = 0; i < look.Accessories.Length && i < 8; i++) look.Accessories[i] = (mask & (1 << i)) != 0;
            Read(r, look.AccessoryColour);
            Read(r, look.Glow);
            look.GlowPower = r.Q8(0f, 3f);
            Read(r, look.Trail);
            look.TrailLength = r.Q8(0.5f, 15f);
            return r.Failed ? null : look;
        }

        public void Load(ModConfig c, string p)
        {
            Body[0].Load(c, p + "Head");
            Body[1].Load(c, p + "Torso");
            Body[2].Load(c, p + "Legs");
            Torso = c.GetFloat(p + "Scale.Torso", 1f);
            Bottom = c.GetFloat(p + "Scale.Bottom", 1f);
            Hands = c.GetFloat(p + "Scale.Hands", 1f);
            Feet = c.GetFloat(p + "Scale.Feet", 1f);
            Limbs = c.GetFloat(p + "Scale.Limbs", 1f);
            NoseSize = c.GetFloat(p + "Nose.Size", 1f);
            NoseLength = c.GetFloat(p + "Nose.Length", 1f);
            Hat = Mathf.Clamp(c.GetInt(p + "Hat", 0), 0, Wardrobe.HatNames.Length - 1);
            HatSize = c.GetFloat(p + "Hat.Size", 1f);
            HatHeight = c.GetFloat(p + "Hat.Height", 0f);
            HatColour.Load(c, p + "Hat.Colour");
            HatSecond.Load(c, p + "Hat.Second");
            for (int i = 0; i < Accessories.Length; i++) Accessories[i] = c.GetBool(p + "Accessory." + i, false);
            AccessoryColour.Load(c, p + "Accessory.Colour");
            Glow.Load(c, p + "Glow");
            GlowPower = c.GetFloat(p + "Glow.Power", 1f);
            Trail.Load(c, p + "Trail");
            TrailLength = c.GetFloat(p + "Trail.Length", 3f);
        }

        public void Save(ModConfig c, string p)
        {
            Body[0].Save(c, p + "Head");
            Body[1].Save(c, p + "Torso");
            Body[2].Save(c, p + "Legs");
            c.Set(p + "Scale.Torso", Torso);
            c.Set(p + "Scale.Bottom", Bottom);
            c.Set(p + "Scale.Hands", Hands);
            c.Set(p + "Scale.Feet", Feet);
            c.Set(p + "Scale.Limbs", Limbs);
            c.Set(p + "Nose.Size", NoseSize);
            c.Set(p + "Nose.Length", NoseLength);
            c.Set(p + "Hat", Hat);
            c.Set(p + "Hat.Size", HatSize);
            c.Set(p + "Hat.Height", HatHeight);
            HatColour.Save(c, p + "Hat.Colour");
            HatSecond.Save(c, p + "Hat.Second");
            for (int i = 0; i < Accessories.Length; i++) c.Set(p + "Accessory." + i, Accessories[i]);
            AccessoryColour.Save(c, p + "Accessory.Colour");
            Glow.Save(c, p + "Glow");
            c.Set(p + "Glow.Power", GlowPower);
            Trail.Save(c, p + "Trail");
            c.Set(p + "Trail.Length", TrailLength);
        }
    }

    public sealed class CharacterModule : MenuModule
    {
        private sealed class ScaledPart
        {
            public Transform T;
            public Vector3 Original;
            public int Group; // 0 torso, 1 bottom, 2 hands, 3 feet, 4 nose
        }

        private sealed class Limb
        {
            public LimbSpline Spline;
            public float Original;
        }

        /// <summary>What the mod has attached to or changed on one character.</summary>
        private sealed class Body
        {
            public PlayerCharacter Pc;
            public string Key;
            public float NextKeyCheck;
            public readonly List<ScaledPart> Parts = new List<ScaledPart>();
            public readonly List<Limb> Limbs = new List<Limb>();
            public readonly bool[] Coloured = new bool[3];
            public Transform Anchor;          // child of head_mesh: head centre, head radius = 1
            public Renderer HeadRenderer;     // the head LOD the game shows or hides
            public GameObject Outfit;
            public string OutfitKey;
            public Material HatMat, HatSecondMat, AccessoryMat;
            public readonly List<Renderer> OutfitRenderers = new List<Renderer>();
            public Light Glow;
            public TrailRenderer Trail;
            public Material TrailMat;
        }

        public override string Id => "character";
        public override string Title => "Personaggio";
        public override int Order => 20;

        public const byte LookMessage = 16;

        private readonly Look[] _looks = { new Look(), new Look(), new Look(), new Look() };
        // What friends sent about their own character, by network id.
        private readonly Dictionary<uint, Look> _remote = new Dictionary<uint, Look>();
        private readonly List<uint> _gone = new List<uint>();
        // Looks read from friends' profile files, by player key.
        private readonly Dictionary<string, Look> _fileLooks = new Dictionary<string, Look>();
        private bool _sendPending, _sharedLast = true;
        private float _sendAt;
        private byte[] _lastSent;
        private readonly Dictionary<int, Body> _bodies = new Dictionary<int, Body>();
        private readonly List<int> _stale = new List<int>();
        private MaterialPropertyBlock _block;
        private string _colourProperty;
        private int _slot, _colourTarget;
        private static readonly string[] ColourTargets = { "Testa", "Busto", "Gambe", "Cappello", "Cappello (dettagli)", "Accessori", "Luce", "Scia" };
        private static readonly string[] PartNames = { "torso_mesh", "bottom_mesh", "localBottom_mesh", "left_hand_mesh", "right_hand_mesh", "local_left_hand_mesh", "local_right_hand_mesh", "left_foot_mesh", "right_foot_mesh", "nose_mesh" };
        private static readonly int[] PartGroups = { 0, 1, 1, 2, 2, 2, 2, 3, 3, 4 };

        public override void Load(ModConfig c)
        {
            for (int i = 0; i < 4; i++) _looks[i].Load(c, $"P{i + 1}.");
        }

        public override void Save(ModConfig c)
        {
            for (int i = 0; i < 4; i++) _looks[i].Save(c, $"P{i + 1}.");
        }

        public override void Init()
        {
            Net.Register(LookMessage, true, OnLook);
            Profiles.Register("look", 200,
                () => { int slot = LocalSlot(); return slot >= 0 ? _looks[slot].ToWire() : null; },
                (key, data) => { Look look = Look.FromWire(data); if (look != null) _fileLooks[key] = look; });
            Net.Connected += () => { _lastSent = null; QueueSend(); };
        }

        private void OnLook(uint sender, byte[] body)
        {
            Look look = Look.FromWire(body);
            if (look == null) return;
            _remote[sender] = look;
        }

        private void QueueSend()
        {
            _sendPending = true;
            _sendAt = Time.unscaledTime + 0.4f; // wait for the slider to settle
        }

        private static int LocalSlot()
        {
            for (int i = 0; i < 4; i++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(i);
                try { if (pc != null && pc.isLocalPlayer) return i; } catch { }
            }
            return -1;
        }

        public override void Update()
        {
            // Sharing switched on or off: friends must get the look, or a plain one to remove it.
            if (GeneralModule.ShareMine != _sharedLast)
            {
                _sharedLast = GeneralModule.ShareMine;
                QueueSend();
            }
            if (_sendPending && Time.unscaledTime >= _sendAt && Net.Ready)
            {
                int slot = LocalSlot();
                if (slot >= 0)
                {
                    _sendPending = false;
                    byte[] wire = (GeneralModule.ShareMine ? _looks[slot] : new Look()).ToWire();
                    bool same = _lastSent != null && _lastSent.Length == wire.Length;
                    for (int i = 0; same && i < wire.Length; i++) same = _lastSent[i] == wire[i];
                    if (!same && Net.Send(LookMessage, wire)) _lastSent = wire;
                }
            }

            if (_remote.Count > 0 && Time.frameCount % 300 == 0)
            {
                _gone.Clear();
                foreach (uint id in _remote.Keys) if (Identity.ByNetId(id) == null) _gone.Add(id);
                foreach (uint id in _gone) _remote.Remove(id);
            }
        }

        // A friend's own choice wins over what was set for that slot here.
        private Look LookFor(PlayerCharacter pc, int slot, Body body)
        {
            if (GeneralModule.ShowFriends && (_remote.Count > 0 || _fileLooks.Count > 0))
            {
                bool local = false;
                try { local = pc.isLocalPlayer; } catch { }
                if (!local)
                {
                    if (_remote.TryGetValue(Identity.NetId(pc), out Look remote)) return remote;
                    if (_fileLooks.Count > 0)
                    {
                        // The game learns a player's id a moment after they appear: look again now and then.
                        if (string.IsNullOrEmpty(body.Key) && Time.unscaledTime > body.NextKeyCheck)
                        {
                            body.NextKeyCheck = Time.unscaledTime + 3f;
                            body.Key = Identity.Key(pc);
                        }
                        if (!string.IsNullOrEmpty(body.Key) && _fileLooks.TryGetValue(body.Key, out Look fromFile)) return fromFile;
                    }
                }
            }
            return _looks[slot];
        }

        public override void LateUpdate()
        {
            // Drop characters that no longer exist (their attached objects went with them).
            _stale.Clear();
            foreach (var kv in _bodies) if (kv.Value.Pc == null) _stale.Add(kv.Key);
            foreach (int id in _stale) _bodies.Remove(id);

            for (int slot = 0; slot < 4; slot++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(slot);
                if (pc == null) continue;
                int id = pc.GetInstanceID();
                if (!_bodies.TryGetValue(id, out Body body))
                {
                    body = Discover(pc);
                    _bodies[id] = body;
                }
                Look look = LookFor(pc, slot, body);
                try
                {
                    ApplyColours(body, look);
                    ApplyScales(body, look);
                    ApplyOutfit(body, look);
                    ApplyGlow(body, look);
                    ApplyTrail(body, look);
                }
                catch (Il2CppInterop.Runtime.ObjectCollectedException)
                {
                    // Something attached to this character is gone: take our things off and start over next frame.
                    Forget(id, body);
                }
            }
        }

        private void Forget(int id, Body body)
        {
            try { if (Game.Alive(body.Outfit)) UnityEngine.Object.Destroy(body.Outfit); } catch { }
            try { if (Game.Alive(body.Anchor)) UnityEngine.Object.Destroy(body.Anchor.gameObject); } catch { }
            try { if (Game.Alive(body.Glow)) UnityEngine.Object.Destroy(body.Glow.gameObject); } catch { }
            try { if (Game.Alive(body.Trail)) UnityEngine.Object.Destroy(body.Trail.gameObject); } catch { }
            _bodies.Remove(id);
        }

        public override void OnObjectsLost()
        {
            _block = null;
            foreach (var kv in new List<KeyValuePair<int, Body>>(_bodies)) Forget(kv.Key, kv.Value);
        }

        private Body Discover(PlayerCharacter pc)
        {
            var body = new Body { Pc = pc };
            var names = new HashSet<string>(PartNames);
            foreach (Transform t in pc.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !names.Contains(t.name)) continue;
                // "nose_mesh" is also the name of a mesh object; the one to scale is the holder under head_mesh.
                if (t.name == "nose_mesh" && (t.parent == null || t.parent.name != "head_mesh")) continue;
                body.Parts.Add(new ScaledPart { T = t, Original = t.localScale, Group = PartGroups[Array.IndexOf(PartNames, t.name)] });
            }

            try
            {
                foreach (LimbSpline ls in pc.GetComponentsInChildren<LimbSpline>(true))
                {
                    if (ls != null) body.Limbs.Add(new Limb { Spline = ls, Original = ls.armThickness });
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("limbs", $"[Character] Limb splines not available: {ex.Message}"); }

            ModMenuApi.Log.LogInfo($"[Character] '{pc.name}': {body.Parts.Count} scalable parts, {body.Limbs.Count} limb splines.");
            return body;
        }

        // ---- colours ----

        private static bool IsNose(Renderer r) => r.name.ToLowerInvariant().Contains("nose");

        private void ApplyColours(Body body, Look look)
        {
            PlayerLooks looks = body.Pc.looks;
            if (looks == null) return;
            if (!Game.Alive(_block)) _block = new MaterialPropertyBlock();
            if (_colourProperty == null)
            {
                try { _colourProperty = PlayerLooks.lookColorPropertyName; } catch { }
                if (string.IsNullOrEmpty(_colourProperty)) _colourProperty = "_Color0";
            }

            for (int part = 0; part < 3; part++)
            {
                Il2CppReferenceArray<PlayerLooks.LooksRenderer> renderers = part == 0 ? looks.headRenderers : part == 1 ? looks.torsoRenderers : looks.legsRenderers;
                if (renderers == null) continue;
                Tint tint = look.Body[part];
                if (!tint.On && !body.Coloured[part]) continue;

                Color colour = tint.Colour;
                foreach (PlayerLooks.LooksRenderer lr in renderers)
                {
                    Renderer r = lr != null ? lr.renderer : null;
                    // The nose has its own colour setting in the faces tab.
                    if (r == null || (part == 0 && IsNose(r))) continue;
                    if (tint.On)
                    {
                        // The game writes its look colour through a property block too, so keep ours on top.
                        r.GetPropertyBlock(_block);
                        _block.SetColor(_colourProperty, colour);
                        r.SetPropertyBlock(_block);
                    }
                    else
                    {
                        r.SetPropertyBlock(null);
                    }
                }

                if (!tint.On)
                {
                    // Just released: let the game put its own colour back.
                    try
                    {
                        looks.SetColor(renderers, part == 0 ? looks.headColor : part == 1 ? looks.torsoColor : looks.legsColor);
                    }
                    catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Character] Could not restore the look colour: {ex.Message}"); }
                }
                body.Coloured[part] = tint.On;
            }
        }

        // ---- proportions ----

        private static void ApplyScales(Body body, Look look)
        {
            foreach (ScaledPart part in body.Parts)
            {
                if (part.T == null) continue;
                Vector3 target;
                switch (part.Group)
                {
                    case 0: target = part.Original * look.Torso; break;
                    case 1: target = part.Original * look.Bottom; break;
                    case 2: target = part.Original * look.Hands; break;
                    case 3: target = part.Original * look.Feet; break;
                    // The nose points along the holder's X axis.
                    default: target = new Vector3(part.Original.x * look.NoseSize * look.NoseLength, part.Original.y * look.NoseSize, part.Original.z * look.NoseSize); break;
                }
                if ((part.T.localScale - target).sqrMagnitude > 1e-8f) part.T.localScale = target;
            }

            foreach (Limb limb in body.Limbs)
            {
                if (limb.Spline == null) continue;
                float target = limb.Original * look.Limbs;
                if (Mathf.Abs(limb.Spline.armThickness - target) > 1e-5f) limb.Spline.armThickness = target;
            }
        }

        // ---- hats and accessories ----

        private static bool EnsureAnchor(Body body)
        {
            if (body.Anchor != null) return true;
            Transform container = Game.HeadContainer(body.Pc);
            if (container == null) return false;

            // Head centre and radius, measured on the head LOD in the container's space.
            Transform lod = container.Find("head_mesh_LOD0");
            MeshFilter filter = lod != null ? lod.GetComponent<MeshFilter>() : null;
            Vector3 centre = Vector3.zero;
            float radius = 0.2f;
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds b = filter.sharedMesh.bounds;
                float meshRadius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                centre = lod.localPosition + lod.localRotation * Vector3.Scale(b.center, lod.localScale);
                radius = meshRadius * lod.localScale.x;
                body.HeadRenderer = lod.GetComponent<Renderer>();
            }

            var go = new GameObject("bwmm_head_anchor");
            go.layer = container.gameObject.layer;
            go.transform.SetParent(container, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(radius, radius, radius);
            body.Anchor = go.transform;
            ModMenuApi.Log.LogInfo($"[Character] Head anchor on '{container.name}': centre {centre}, radius {radius:0.000}.");
            return true;
        }

        private static string OutfitKey(Look look)
        {
            var sb = new StringBuilder();
            sb.Append(look.Hat);
            foreach (bool a in look.Accessories) sb.Append(a ? '1' : '0');
            return sb.ToString();
        }

        private void ApplyOutfit(Body body, Look look)
        {
            string key = OutfitKey(look);
            bool wanted = look.Hat != 0 || Array.IndexOf(look.Accessories, true) >= 0;

            if (body.OutfitKey != key)
            {
                body.OutfitKey = key;
                if (body.Outfit != null) UnityEngine.Object.Destroy(body.Outfit);
                body.Outfit = null;
                body.OutfitRenderers.Clear();
                if (wanted && EnsureAnchor(body)) BuildOutfit(body, look);
            }
            if (body.Outfit == null) return;

            // The hat part can be resized and raised; accessories stay glued to the face.
            Transform hat = body.Outfit.transform.Find("hat");
            if (hat != null)
            {
                hat.localScale = new Vector3(look.HatSize, look.HatSize, look.HatSize);
                hat.localPosition = new Vector3(0f, look.HatHeight, 0f);
            }

            Game.SetMaterialColour(body.HatMat, look.HatColour.Colour);
            Game.SetMaterialColour(body.HatSecondMat, look.HatSecond.Colour);
            Game.SetMaterialColour(body.AccessoryMat, look.AccessoryColour.Colour);

            // Follow the head: in first person the game hides your own head, and the hat must go with it.
            Renderer head = body.HeadRenderer;
            if (head != null)
            {
                bool visible = head.enabled && head.gameObject.activeInHierarchy;
                var shadows = head.shadowCastingMode;
                int layer = head.gameObject.layer;
                foreach (Renderer r in body.OutfitRenderers)
                {
                    if (r == null) continue;
                    if (r.enabled != visible) r.enabled = visible;
                    if (r.shadowCastingMode != shadows) r.shadowCastingMode = shadows;
                    if (r.gameObject.layer != layer) r.gameObject.layer = layer;
                }
            }
        }

        private void BuildOutfit(Body body, Look look)
        {
            if (body.HatMat == null)
            {
                body.HatMat = Game.NewMaterial(look.HatColour.Colour);
                body.HatSecondMat = Game.NewMaterial(look.HatSecond.Colour);
                body.AccessoryMat = Game.NewMaterial(look.AccessoryColour.Colour);
            }
            if (body.HatMat == null) return; // no usable shader

            var root = new GameObject("bwmm_outfit");
            root.transform.SetParent(body.Anchor, false);
            var hatRoot = new GameObject("hat");
            hatRoot.transform.SetParent(root.transform, false);
            var accRoot = new GameObject("accessories");
            accRoot.transform.SetParent(root.transform, false);

            foreach (ShapePart part in Wardrobe.Hat(look.Hat))
            {
                AddPart(body, hatRoot.transform, part, part.Secondary ? body.HatSecondMat : body.HatMat);
            }
            var acc = new List<ShapePart>();
            for (int i = 0; i < look.Accessories.Length; i++)
            {
                acc.Clear();
                if (look.Accessories[i]) Wardrobe.AddAccessory(acc, i);
                // Inner ears take the hat's detail colour so they stand out.
                foreach (ShapePart part in acc) AddPart(body, accRoot.transform, part, part.Secondary ? body.HatSecondMat : body.AccessoryMat);
            }
            body.Outfit = root;
        }

        private static void AddPart(Body body, Transform parent, ShapePart part, Material material)
        {
            var go = new GameObject("part");
            go.layer = body.Anchor.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = part.Position;
            go.transform.localRotation = Quaternion.Euler(part.Euler);
            go.transform.localScale = part.Scale;
            go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            body.OutfitRenderers.Add(renderer);
        }

        // ---- light and trail ----

        private static void ApplyGlow(Body body, Look look)
        {
            if (!look.Glow.On)
            {
                if (body.Glow != null && body.Glow.enabled) body.Glow.enabled = false;
                return;
            }
            if (body.Glow == null)
            {
                Transform head = Game.HeadBone(body.Pc);
                if (head == null) return;
                var go = new GameObject("bwmm_glow");
                go.transform.SetParent(head, false);
                body.Glow = go.AddComponent<Light>();
                body.Glow.type = LightType.Point;
                body.Glow.shadows = LightShadows.None;
            }
            body.Glow.enabled = true;
            body.Glow.color = look.Glow.Colour;
            body.Glow.range = 4f + 8f * look.GlowPower;
            body.Glow.intensity = 1.5f + 6f * look.GlowPower;
        }

        private static void ApplyTrail(Body body, Look look)
        {
            if (!look.Trail.On)
            {
                if (body.Trail != null && body.Trail.enabled)
                {
                    body.Trail.enabled = false;
                    body.Trail.Clear();
                }
                return;
            }
            if (body.Trail == null)
            {
                body.TrailMat = Game.NewMaterial(look.Trail.Colour);
                if (body.TrailMat == null) return;
                var go = new GameObject("bwmm_trail");
                go.transform.SetParent(body.Pc.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.72f, 0f); // just above the ground
                body.Trail = go.AddComponent<TrailRenderer>();
                body.Trail.sharedMaterial = body.TrailMat;
                body.Trail.startWidth = 0.22f;
                body.Trail.endWidth = 0.02f;
                body.Trail.minVertexDistance = 0.15f;
                body.Trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            body.Trail.enabled = true;
            body.Trail.time = look.TrailLength;
            Game.SetMaterialColour(body.TrailMat, look.Trail.Colour);
        }

        // ---- menu ----

        public override void DrawTab(Ui ui)
        {
            var labels = new string[4];
            for (int i = 0; i < 4; i++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(i);
                labels[i] = pc != null ? Game.PlayerLabel(pc, i) : $"Giocatore {i + 1} (assente)";
            }
            ui.Choice("Modifica", ref _slot, labels);
            Look look = _looks[_slot];
            bool changed = false;

            PlayerCharacter editing = Solo.GetPlayerForSlot(_slot);
            if (editing != null && _slot == LocalSlot())
            {
                if (Net.Ready && GeneralModule.ShareMine) ui.Note("<color=#88ff88>Questo \u00e8 il tuo aspetto: lo vedono anche gli amici con la mod.</color>");
            }
            else if (editing != null && GeneralModule.ShowFriends && _remote.ContainsKey(Identity.NetId(editing)))
            {
                ui.Note("<color=#ffee88>Questo amico ha scelto il suo aspetto, ed \u00e8 quello che vedi. Per usare il tuo, spegni \"Mostra l'aspetto degli amici\" in Generale.</color>", 3);
            }

            ui.Header("Colori");
            ui.Dropdown("character.colour", "Colore di", ColourTargets, ref _colourTarget);
            Tint tint = TintFor(look, _colourTarget);
            if (_colourTarget <= 2)
            {
                changed |= ui.Toggle("Colore personalizzato", ref tint.On);
            }
            else if (_colourTarget >= 6)
            {
                changed |= ui.Toggle(_colourTarget == 6 ? "Luce sulla testa" : "Scia", ref tint.On);
            }
            if (ui.ColorWheel(ref tint.H, ref tint.S, ref tint.V))
            {
                if (_colourTarget <= 2) tint.On = true;
                changed = true;
            }
            if (_colourTarget == 6) changed |= ui.Slider($"Potenza della luce: {Mathf.RoundToInt(look.GlowPower * 100f)}%", ref look.GlowPower, 0f, 3f, 0.1f);
            if (_colourTarget == 7) changed |= ui.Slider($"Durata della scia: {look.TrailLength:0.0} s", ref look.TrailLength, 0.5f, 15f, 0.5f);

            ui.Header("Proporzioni");
            changed |= ui.Slider($"Busto: {Pct(look.Torso)}", ref look.Torso, 0.3f, 3f, 0.05f);
            changed |= ui.Slider($"Bacino: {Pct(look.Bottom)}", ref look.Bottom, 0.3f, 3f, 0.05f);
            changed |= ui.Slider($"Mani: {Pct(look.Hands)}", ref look.Hands, 0.3f, 4f, 0.05f);
            changed |= ui.Slider($"Piedi: {Pct(look.Feet)}", ref look.Feet, 0.3f, 4f, 0.05f);
            changed |= ui.Slider($"Spessore di braccia e gambe: {Pct(look.Limbs)}", ref look.Limbs, 0.3f, 4f, 0.05f);
            changed |= ui.Slider($"Naso, grandezza: {Pct(look.NoseSize)}", ref look.NoseSize, 0.2f, 5f, 0.05f);
            changed |= ui.Slider($"Naso, lunghezza: {Pct(look.NoseLength)}", ref look.NoseLength, 0.2f, 8f, 0.1f);
            if (ui.Button("Proporzioni originali"))
            {
                look.Torso = look.Bottom = look.Hands = look.Feet = look.Limbs = look.NoseSize = look.NoseLength = 1f;
                changed = true;
            }
            ui.Note("La grandezza della testa è nella scheda Facce.");

            ui.Header("Cappello");
            changed |= ui.Dropdown("character.hat", "Cappello", Wardrobe.HatNames, ref look.Hat);
            if (look.Hat != 0)
            {
                changed |= ui.Slider($"Grandezza: {Pct(look.HatSize)}", ref look.HatSize, 0.4f, 3f, 0.05f);
                changed |= ui.Slider($"Altezza: {look.HatHeight:+0.00;-0.00;0}", ref look.HatHeight, -0.6f, 1.5f, 0.02f);
            }

            ui.Header("Accessori");
            for (int i = 0; i < look.Accessories.Length; i++)
            {
                changed |= ui.Toggle(Wardrobe.AccessoryNames[i], ref look.Accessories[i]);
            }
            ui.Note("I colori di cappello e accessori si scelgono qui sopra, in \"Colore di\".");

            ui.Space();
            int hit = ui.Buttons("Copia su tutti", "Togli tutto");
            if (hit == 0)
            {
                var c = new ModConfig(System.IO.Path.Combine(DataPath, "_copy.tmp"));
                look.Save(c, "X.");
                for (int i = 0; i < 4; i++) if (i != _slot) _looks[i].Load(c, "X.");
                changed = true;
            }
            else if (hit == 1)
            {
                var blank = new ModConfig(System.IO.Path.Combine(DataPath, "_blank.tmp"));
                new Look().Save(blank, "X.");
                look.Load(blank, "X.");
                changed = true;
            }

            if (changed)
            {
                Dirty = true;
                if (_slot == LocalSlot()) QueueSend();
            }
        }

        private static Tint TintFor(Look look, int target)
        {
            switch (target)
            {
                case 0: case 1: case 2: return look.Body[target];
                case 3: return look.HatColour;
                case 4: return look.HatSecond;
                case 5: return look.AccessoryColour;
                case 6: return look.Glow;
                default: return look.Trail;
            }
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
