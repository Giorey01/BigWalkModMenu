using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>Shortcuts into the game that every module needs.</summary>
    public static class Game
    {
        private static int _playersFrame = -1;
        private static readonly List<PlayerCharacter> _players = new List<PlayerCharacter>();
        private static Shader _litShader;
        private static bool _litShaderSearched;

        public static PlayerCharacter LocalPlayer
        {
            get
            {
                try
                {
                    var local = NetworkClient.localPlayer;
                    return local != null ? local.GetComponent<PlayerCharacter>() : null;
                }
                catch { return null; }
            }
        }

        /// <summary>Every spawned character, refreshed once per frame.</summary>
        public static List<PlayerCharacter> Players
        {
            get
            {
                if (_playersFrame == Time.frameCount) return _players;
                _playersFrame = Time.frameCount;
                _players.Clear();
                try
                {
                    var all = PlayerCharacter.allPlayerCharacters;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            var pc = all[i];
                            if (pc != null) _players.Add(pc);
                        }
                    }
                }
                catch { }
                return _players;
            }
        }

        public static bool InWorld => LocalPlayer != null;

        private static int _typingFrame = -1;
        private static bool _typing;

        /// <summary>
        /// True while the player is writing in the game (text chat, signs...). Letter and number keys
        /// belong to the text then: no mod shortcut may react to them.
        /// </summary>
        public static bool IsTyping
        {
            get
            {
                if (_typingFrame == Time.frameCount) return _typing;
                _typingFrame = Time.frameCount;
                _typing = false;
                try { _typing = ControlsManager.textInputModeActive; } catch { }
                if (!_typing)
                {
                    try
                    {
                        PlayerCharacter pc = LocalPlayer;
                        _typing = pc != null && pc.playerNetworking != null && pc.playerNetworking.isTextChatting;
                    }
                    catch { }
                }
                return _typing;
            }
        }

        /// <summary>
        /// False for null and for a game object whose handle went dead. That was seen in play (a texture
        /// kept in a field threw "Object was garbage collected in IL2CPP domain"), so anything cached
        /// across frames is checked with this and simply built again when it is gone.
        /// </summary>
        public static bool Alive(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o)
        {
            try { return o != null && !o.WasCollected; } catch { return false; }
        }

        /// <summary>Opens a folder in a file manager. The game runs under Proton, where xdg-open is not reachable.</summary>
        public static void OpenFolder(string path, string shortName)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + path + "\"", UseShellExecute = false });
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Game] Could not open '{path}': {ex.Message}");
            }
            ModMenuApi.Toast("Folder: " + shortName, 6f);
        }

        public static Camera PlayerCamera
        {
            get
            {
                try
                {
                    var pc = LocalPlayer;
                    var refs = pc != null && pc.cameraMinder != null ? pc.cameraMinder.playerCameraReferences : null;
                    if (refs != null && refs.playerCamera != null) return refs.playerCamera;
                }
                catch { }
                try { return Camera.main; } catch { return null; }
            }
        }

        public static Camera PlayerUiCamera
        {
            get
            {
                try
                {
                    var pc = LocalPlayer;
                    var refs = pc != null && pc.cameraMinder != null ? pc.cameraMinder.playerCameraReferences : null;
                    return refs != null ? refs.playerUICamera : null;
                }
                catch { return null; }
            }
        }

        public static string PlayerLabel(PlayerCharacter pc, int index)
        {
            bool local = false;
            try { local = pc != null && pc.isLocalPlayer; } catch { }
            return local ? $"Player {index + 1} (you)" : $"Player {index + 1}";
        }

        public static Transform HeadBone(PlayerCharacter pc)
        {
            try
            {
                if (pc != null && pc.registry != null && pc.registry.headBone != null) return pc.registry.headBone;
            }
            catch { }
            return FindDeep(pc != null ? pc.transform : null, "head_bone");
        }

        /// <summary>The object that holds the head LODs and the nose ("head_mesh").</summary>
        public static Transform HeadContainer(PlayerCharacter pc)
        {
            Transform bone = HeadBone(pc);
            if (bone == null) return null;
            Transform t = bone.Find("head_mesh");
            return t != null ? t : FindDeep(bone, "head_mesh");
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            int n = root.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>A lit shader that accepts a colour and a texture (the game's own player shader does not).</summary>
        public static Shader LitShader
        {
            get
            {
                if (_litShaderSearched) return _litShader;
                _litShaderSearched = true;
                foreach (string name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Sprites/Default" })
                {
                    try
                    {
                        Shader s = Shader.Find(name);
                        if (s != null) { _litShader = s; ModMenuApi.Log.LogInfo($"[Game] Lit shader: {name}"); break; }
                    }
                    catch { }
                }
                if (_litShader == null) ModMenuApi.Log.LogWarning("[Game] No usable lit shader found.");
                return _litShader;
            }
        }

        public static Material NewMaterial(Color colour)
        {
            Shader s = LitShader;
            if (s == null) return null;
            var m = new Material(s);
            m.hideFlags = HideFlags.HideAndDontSave;
            SetMaterialColour(m, colour);
            try { if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f); } catch { }
            return m;
        }

        public static void SetMaterialColour(Material m, Color colour)
        {
            if (m == null) return;
            try { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); } catch { }
            try { if (m.HasProperty("_Color")) m.SetColor("_Color", colour); } catch { }
        }

        public static Color Hsv(float h, float s, float v)
        {
            h -= MathF.Floor(h);
            float f = h * 6f;
            int i = (int)f;
            f -= i;
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (i % 6)
            {
                case 0: return new Color(v, t, p, 1f);
                case 1: return new Color(q, v, p, 1f);
                case 2: return new Color(p, v, t, 1f);
                case 3: return new Color(p, q, v, 1f);
                case 4: return new Color(t, p, v, 1f);
                default: return new Color(v, p, q, 1f);
            }
        }
    }

    /// <summary>
    /// Stops the game from reading the player's input while a menu or a free camera is using the
    /// mouse and keyboard. Each user asks with its own owner object and releases it when done.
    /// </summary>
    public static class InputLock
    {
        private static readonly HashSet<object> _look = new HashSet<object>();
        private static readonly HashSet<object> _all = new HashSet<object>();
        private static readonly HashSet<object> _cursor = new HashSet<object>();
        private static PlayerHead _frozenHead;
        private static bool _mapsDisabled;
        private static bool _cursorWasFree;

        /// <summary>The view no longer follows the mouse.</summary>
        public static bool LookLocked => _look.Count > 0 || _all.Count > 0;
        /// <summary>Movement, actions and view are all ignored.</summary>
        public static bool AllLocked => _all.Count > 0;
        public static bool CursorFree => _cursor.Count > 0;

        public static void Set(object owner, bool look, bool all, bool cursor)
        {
            if (look) _look.Add(owner); else _look.Remove(owner);
            if (all) _all.Add(owner); else _all.Remove(owner);
            if (cursor) _cursor.Add(owner); else _cursor.Remove(owner);
        }

        public static void Release(object owner) => Set(owner, false, false, false);

        internal static void Update()
        {
            PlayerCharacter pc = Game.LocalPlayer;

            // View: PlayerHead.Update reads the mouse/stick (see PlayerHeadUpdatePatch).
            PlayerHead head = null;
            try { head = pc != null ? pc.head : null; } catch { }
            bool look = LookLocked;
            if (_frozenHead != null && (!look || head == null || head.Pointer != _frozenHead.Pointer))
            {
                try { _frozenHead.bypassUpdate = false; } catch { }
                _frozenHead = null;
            }
            if (look && head != null && _frozenHead == null)
            {
                try
                {
                    if (!head.bypassUpdate) { head.bypassUpdate = true; _frozenHead = head; }
                }
                catch { }
            }

            // Everything else: switch off the player's Rewired maps, and let the game rebuild them afterwards.
            bool all = AllLocked;
            if (all)
            {
                try
                {
                    var input = pc != null ? pc.inputPlayer : null;
                    if (input != null) { input.controllers.maps.SetAllMapsEnabled(false); _mapsDisabled = true; }
                }
                catch (Exception ex) { ModMenuApi.LogOnce("inputlock", $"[InputLock] Could not disable input maps: {ex.Message}"); }
            }
            else if (_mapsDisabled)
            {
                _mapsDisabled = false;
                try { ControlsManager.Refresh(); }
                catch (Exception ex) { ModMenuApi.Log.LogWarning($"[InputLock] ControlsManager.Refresh failed: {ex.Message}"); }
            }

            bool free = CursorFree;
            if (free)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_cursorWasFree && Game.InWorld)
            {
                try { CursorManager.SetLocked(); } catch { }
            }
            _cursorWasFree = free;
        }
    }

    [HarmonyPatch(typeof(PlayerHead), nameof(PlayerHead.Update))]
    public static class PlayerHeadUpdatePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerHead __instance)
        {
            if (!InputLock.LookLocked) return true;
            try
            {
                var pc = __instance != null ? __instance.pc : null;
                return pc == null || !pc.isLocalPlayer;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// The four character slots of the Solo tab (several characters played by one person). The tab
    /// lives in its own DLL and fills SlotIds; without it a slot is simply the n-th spawned player.
    /// </summary>
    public static class Solo
    {
        public const int SlotCount = 4;
        /// <summary>Network id of the character in each slot, zero when the slot is empty.</summary>
        public static readonly uint[] SlotIds = new uint[SlotCount];

        public static uint GetSlotNetId(int slot) => slot >= 0 && slot < SlotCount ? SlotIds[slot] : 0u;

        /// <summary>Slot 0-3: the Solo tab's character when it has one, otherwise the n-th spawned player.</summary>
        public static PlayerCharacter GetPlayerForSlot(int slot)
        {
            uint netId = GetSlotNetId(slot);
            if (netId != 0)
            {
                try
                {
                    if (NetworkServer.spawned != null && NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
                    {
                        var pc = identity.GetComponent<PlayerCharacter>();
                        if (pc != null) return pc;
                    }
                }
                catch { }

                try
                {
                    if (NetworkClient.spawned != null && NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
                    {
                        var pc = identity.GetComponent<PlayerCharacter>();
                        if (pc != null) return pc;
                    }
                }
                catch { }
            }

            try
            {
                var all = PlayerCharacter.allPlayerCharacters;
                if (all != null && slot >= 0 && slot < all.Count) return all[slot];
            }
            catch { }
            return null;
        }

        public static void SafeTeleport(PlayerCharacter character, Vector3 position, Quaternion rotation)
        {
            if (character == null) return;
            try
            {
                if (character.grease != null)
                {
                    character.grease.Teleport(position, rotation, false);
                }
                else if (character.rb != null)
                {
                    character.rb.position = position;
                    character.rb.rotation = rotation;
                }
                else
                {
                    character.transform.SetPositionAndRotation(position, rotation);
                }

                if (character.rb != null)
                {
                    character.rb.linearVelocity = Vector3.zero;
                    character.rb.angularVelocity = Vector3.zero;
                }
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Solo] SafeTeleport failed: {ex.Message}");
            }
        }

        public static void TeleportAllToPosition(Vector3 basePos, Quaternion baseRot)
        {
            for (int i = 0; i < 4; i++)
            {
                var pc = GetPlayerForSlot(i);
                if (pc == null) continue;

                Vector3 offset = Vector3.zero;
                if (i == 1) offset = baseRot * (-Vector3.right * 1.4f);
                else if (i == 2) offset = baseRot * (Vector3.right * 1.4f);
                else if (i == 3) offset = baseRot * (-Vector3.forward * 1.8f);

                SafeTeleport(pc, basePos + offset, baseRot);
            }
        }
    }
}
