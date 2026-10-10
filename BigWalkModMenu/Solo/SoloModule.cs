using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BigWalk.ModMenu.SoloPlay
{
    [BepInPlugin("com.bigwalk.modmenu.solo", "Big Walk Mod Menu - Solo", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new SoloModule());
            var harmony = new Harmony("com.bigwalk.modmenu.solo");
            ModMenuApi.ApplyPatch(harmony, typeof(FollowerPatch));
            ModMenuApi.ApplyPatch(harmony, typeof(CameraUpdatePatch));
            ModMenuApi.ApplyPatch(harmony, typeof(CameraTakePatch));
        }
    }

    /// <summary>
    /// Playing Big Walk alone with up to four characters. Only while hosting: extra characters are
    /// real network players spawned from the game's own player prefab and handed to the host's
    /// connection one at a time (Mirror's ReplacePlayerForConnection), so the game treats whichever
    /// one is selected as "the" local player and the others as remote ones standing in the world.
    /// </summary>
    public sealed class SoloModule : MenuModule
    {
        public static SoloModule Instance;
        private const int Slots = Solo.SlotCount;

        public override string Id => "solo";
        public override string Title => "Solo";
        public override int Order => 15;

        // Settings.
        private bool _enabled;
        private int _maxExtra = 3;
        private bool _stayOnCurrent;
        private float _followSpeed = 1.35f, _stopDistance = 1.4f, _catchUpDistance = 14f;
        private readonly Hotkey _spawnKey = new Hotkey("Spawn", "Add a character", KeyCode.Plus);
        private readonly Hotkey _spawnKeyAlt = new Hotkey("SpawnAlt", "Add (second key)", KeyCode.KeypadPlus);
        private readonly Hotkey _gatherKey = new Hotkey("Gather", "Gather everyone here", KeyCode.R);
        private readonly Hotkey _followKey = new Hotkey("Follow", "Follow me on/off", KeyCode.None);
        private readonly Hotkey[] _slotKeys =
        {
            new Hotkey("Slot1", "Switch to character 1", KeyCode.Alpha1),
            new Hotkey("Slot2", "Switch to character 2", KeyCode.Alpha2),
            new Hotkey("Slot3", "Switch to character 3", KeyCode.Alpha3),
            new Hotkey("Slot4", "Switch to character 4", KeyCode.Alpha4),
        };

        // Session state.
        private readonly uint[] _ids = Solo.SlotIds;
        private uint _switchingTo;          // a hand-over is in flight until this id is the local player
        private int _switchDeadline;
        private int _deleteAfterSwitch = -1; // slot to remove once control has left it
        private int _returnTo = -1;          // slot to go back to after a spawn ("stay on the current one")
        private bool _follow;
        private int _removeSlot = -1, _removeAtFrame;
        private Vector3 _startPosition;
        private Quaternion _startRotation = Quaternion.identity;
        private bool _startSaved;
        private readonly Vector3[] _homeSpot = new Vector3[Slots];
        private readonly Quaternion[] _homeRotation = new Quaternion[Slots];
        private readonly int[] _homeUntil = new int[Slots], _homeNextCheck = new int[Slots];

        // Follower bookkeeping, per slot.
        private readonly float[] _lastJump = new float[Slots];
        private readonly float[] _lastCheck = new float[Slots];
        private readonly float[] _stuckSince = new float[Slots];
        private readonly Vector3[] _lastSpot = new Vector3[Slots];

        public SoloModule()
        {
            Instance = this;
        }

        private static int _originalMod = -1;

        /// <summary>The stand-alone mod this tab replaces is loaded too: two of these cannot share the characters.</summary>
        internal static bool OriginalModLoaded
        {
            get
            {
                if (_originalMod < 0)
                {
                    _originalMod = 0;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        if (asm.GetName().Name == "BigWalkSoloMod") { _originalMod = 1; break; }
                }
                return _originalMod == 1;
            }
        }

        internal bool Busy => _switchingTo != 0;
        private bool Settling => Busy || _removeSlot >= 0;
        internal bool FollowOn => _enabled && _follow;

        /// <summary>True when there is at least one extra character: only then do the patches change anything.</summary>
        internal bool HasExtras
        {
            get
            {
                if (!_enabled || OriginalModLoaded) return false;
                for (int i = 1; i < Slots; i++) if (_ids[i] != 0) return true;
                return false;
            }
        }

        public override void Load(ModConfig c)
        {
            _enabled = c.GetBool("Enabled", false);
            _maxExtra = Mathf.Clamp(c.GetInt("MaxExtra", 3), 1, Slots - 1);
            _stayOnCurrent = c.GetBool("StayOnCurrent", false);
            _followSpeed = Mathf.Clamp(c.GetFloat("Follow.Speed", 1.35f), 0.5f, 3f);
            _stopDistance = Mathf.Clamp(c.GetFloat("Follow.StopDistance", 1.4f), 0.5f, 6f);
            _catchUpDistance = Mathf.Clamp(c.GetFloat("Follow.CatchUpDistance", 14f), 4f, 80f);
            foreach (Hotkey k in AllKeys()) k.Load(c);
        }

        public override void Save(ModConfig c)
        {
            c.Set("Enabled", _enabled);
            c.Set("MaxExtra", _maxExtra);
            c.Set("StayOnCurrent", _stayOnCurrent);
            c.Set("Follow.Speed", _followSpeed);
            c.Set("Follow.StopDistance", _stopDistance);
            c.Set("Follow.CatchUpDistance", _catchUpDistance);
            foreach (Hotkey k in AllKeys()) k.Save(c);
        }

        private System.Collections.Generic.IEnumerable<Hotkey> AllKeys()
        {
            yield return _spawnKey;
            yield return _spawnKeyAlt;
            yield return _gatherKey;
            yield return _followKey;
            foreach (Hotkey k in _slotKeys) yield return k;
        }

        // ---- identities ----

        private static bool Hosting => NetworkServer.activeHost && NetworkServer.localConnection != null;

        private static bool TryIdentity(uint netId, out NetworkIdentity identity)
        {
            identity = null;
            if (netId == 0 || NetworkServer.spawned == null) return false;
            return NetworkServer.spawned.TryGetValue(netId, out identity) && identity != null;
        }

        private int SlotOf(uint netId)
        {
            if (netId == 0) return -1;
            for (int i = 0; i < Slots; i++) if (_ids[i] == netId) return i;
            return -1;
        }

        private int FirstFreeSlot()
        {
            for (int i = 1; i <= _maxExtra && i < Slots; i++) if (_ids[i] == 0) return i;
            return -1;
        }

        private void ForgetEverything()
        {
            Array.Clear(_ids, 0, _ids.Length);
            _switchingTo = 0;
            _deleteAfterSwitch = -1;
            _removeSlot = -1;
            _returnTo = -1;
            _follow = false;
            _startSaved = false;
            for (int i = 0; i < Slots; i++) { ResetProgress(i, Vector3.zero); _homeUntil[i] = 0; }
        }

        // ---- per frame ----

        public override void Update()
        {
            NetworkIdentity me = Hosting ? NetworkClient.localPlayer : null;
            if (OriginalModLoaded) return;
            if (!_enabled || me == null)
            {
                // Left the walk, or switched the tab off with nobody extra around: nothing to remember.
                if (me == null && (_ids[0] != 0 || _switchingTo != 0)) ForgetEverything();
                return;
            }

            // Slots whose character is gone (the game removed it) are free again.
            for (int i = 0; i < Slots; i++)
            {
                if (_ids[i] != 0 && _ids[i] != _switchingTo && !TryIdentity(_ids[i], out _))
                {
                    _ids[i] = 0;
                    ResetProgress(i, Vector3.zero);
                }
            }
            if (SlotOf(me.netId) < 0)
            {
                if (_ids[0] == 0) _ids[0] = me.netId;
                else { int free = FirstFreeSlot(); if (free > 0) _ids[free] = me.netId; }
            }
            if (!_startSaved && me.transform.position.sqrMagnitude > 1f)
            {
                _startPosition = me.transform.position;
                _startRotation = me.transform.rotation;
                _startSaved = true;
            }

            FinishSwitch(me);
            KeepNewcomersInPlace();
            if (_removeSlot >= 0 && !Busy && Time.frameCount >= _removeAtFrame)
            {
                int slot = _removeSlot;
                _removeSlot = -1;
                if (_ids[slot] != me.netId && TryIdentity(_ids[slot], out NetworkIdentity doomed)) Remove(slot, doomed);
            }
            if (HasExtras && Time.frameCount % 90 == 0) KeepSensesOnActive(me);

            if (_gatherKey.Down) GatherAt(me.transform.position, me.transform.rotation);
            if (_followKey.Down) SetFollow(!_follow);
            if (Settling) return;
            if (_spawnKey.Down || _spawnKeyAlt.Down) Spawn(me, FirstFreeSlot());
            else
            {
                for (int i = 0; i < Slots; i++) if (_slotKeys[i].Down) { Select(me, i); break; }
            }
        }

        // ---- spawn, switch, delete ----

        // Where slot n stands relative to a point facing a direction: a loose diamond behind it.
        private static Vector3 FormationAt(Vector3 position, Quaternion rotation, int slot, bool tight)
        {
            float side = tight ? 1.15f : 1.6f, back = tight ? 1.15f : 2.4f;
            Vector3 offset;
            switch (slot)
            {
                case 0: offset = new Vector3(0f, 0.15f, -back * 1.65f); break;
                case 1: offset = new Vector3(-side, 0.15f, -back); break;
                case 2: offset = new Vector3(side, 0.15f, -back); break;
                default: offset = new Vector3(0f, 0.15f, -back * 2.35f); break;
            }
            return position + rotation * offset;
        }

        private static Vector3 Formation(Transform active, int slot, bool tight) => FormationAt(active.position, active.rotation, slot, tight);

        private void Spawn(NetworkIdentity me, int slot)
        {
            if (Busy) return;
            if (slot < 1 || slot >= Slots || slot > _maxExtra)
            {
                ModMenuApi.Toast($"Solo: you already have every character ({_maxExtra + 1}).");
                return;
            }
            if (_ids[slot] != 0) return;
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null || manager.playerPrefab == null)
            {
                ModMenuApi.Toast("Solo: the game is not ready to create characters yet.");
                return;
            }

            int previous = SlotOf(me.netId);
            // Taken before the game touches the new character: spawning can already move it.
            Vector3 spot = Formation(me.transform, slot, true);
            Quaternion facing = me.transform.rotation;
            GameObject body = UnityEngine.Object.Instantiate(manager.playerPrefab, spot, facing);
            NetworkIdentity identity = body != null ? body.GetComponent<NetworkIdentity>() : null;
            if (identity == null)
            {
                if (body != null) UnityEngine.Object.Destroy(body);
                ModMenuApi.Log.LogError("[Solo] The player prefab did not produce a network player.");
                return;
            }
            PlayerCharacter old = me.GetComponent<PlayerCharacter>();
            if (!NetworkServer.ReplacePlayerForConnection(NetworkServer.localConnection, body, false))
            {
                UnityEngine.Object.Destroy(body);
                ModMenuApi.Log.LogError("[Solo] The game refused the new character.");
                return;
            }
            CalmDown(body.GetComponent<PlayerCharacter>());
            BecomeBystander(old);
            _ids[slot] = identity.netId;
            BeginSwitch(identity.netId);
            _returnTo = _stayOnCurrent ? previous : -1;
            // See KeepNewcomersInPlace.
            _homeSpot[slot] = spot;
            _homeRotation[slot] = facing;
            _homeUntil[slot] = Time.frameCount + 900;
            _homeNextCheck[slot] = 0;
            ModMenuApi.Log.LogInfo($"[Solo] Character added in slot {slot + 1} (net id {identity.netId}).");
        }

        internal bool Select(NetworkIdentity me, int slot)
        {
            if (Busy) return false;
            if (slot < 0 || slot >= Slots || _ids[slot] == 0)
            {
                ModMenuApi.Toast($"Solo: character {slot + 1} does not exist yet.");
                return false;
            }
            if (_ids[slot] == me.netId) return true;
            if (!TryIdentity(_ids[slot], out NetworkIdentity target)) return false;
            return HandOver(me, target);
        }

        private bool HandOver(NetworkIdentity me, NetworkIdentity target)
        {
            PlayerCharacter old = me.GetComponent<PlayerCharacter>();
            if (!NetworkServer.ReplacePlayerForConnection(NetworkServer.localConnection, target.gameObject, false))
            {
                ModMenuApi.Log.LogError("[Solo] The game refused the character switch.");
                return false;
            }
            CalmDown(target.GetComponent<PlayerCharacter>());
            BecomeBystander(old);
            BeginSwitch(target.netId);
            return true;
        }

        private void BeginSwitch(uint netId)
        {
            _switchingTo = netId;
            _switchDeadline = Time.frameCount + 180;
        }

        // Seen in play: for a few seconds after it appears the game keeps putting a new character at the
        // centre of the map (0, 0), and a correction made too early is undone. So each newcomer is watched
        // for a while and brought back whenever it turns up there. Only that exact spot counts: a character
        // that has simply been walked away is left alone.
        private void KeepNewcomersInPlace()
        {
            for (int i = 1; i < Slots; i++)
            {
                if (_homeUntil[i] == 0) continue;
                if (Time.frameCount > _homeUntil[i] || !TryIdentity(_ids[i], out NetworkIdentity identity)) { _homeUntil[i] = 0; continue; }
                if (Time.frameCount < _homeNextCheck[i]) continue;
                Vector3 p = identity.transform.position;
                bool atCentre = p.x * p.x + p.z * p.z < 25f;
                if (!atCentre || Vector3.Distance(p, _homeSpot[i]) < 6f) continue;
                Solo.SafeTeleport(identity.GetComponent<PlayerCharacter>(), _homeSpot[i], _homeRotation[i]);
                _homeNextCheck[i] = Time.frameCount + 20;
                ModMenuApi.LogOnce("solo-home-" + _ids[i], $"[Solo] The game moved new character {_ids[i]} to the centre of the map; bringing it back.");
            }
        }

        // The hand-over is done when Mirror reports the new character as the local player.
        private void FinishSwitch(NetworkIdentity me)
        {
            if (_switchingTo == 0) return;
            bool arrived = me.netId == _switchingTo;
            if (!arrived && Time.frameCount <= _switchDeadline) return;
            if (!arrived)
            {
                ModMenuApi.Log.LogWarning("[Solo] The character switch timed out; control stays where it is.");
                _deleteAfterSwitch = -1;
                _returnTo = -1;
            }
            _switchingTo = 0;
            BecomeProtagonist(me.GetComponent<PlayerCharacter>());
            KeepSensesOnActive(me);
            for (int i = 0; i < Slots; i++) ResetProgress(i, Vector3.zero);

            if (_deleteAfterSwitch >= 0)
            {
                // Not in this same frame: the change of owner is still on its way to the client side,
                // and removing the character under it brought the game down in testing.
                _removeSlot = _deleteAfterSwitch;
                _removeAtFrame = Time.frameCount + 45;
                _deleteAfterSwitch = -1;
            }
            else if (_returnTo >= 0)
            {
                int slot = _returnTo;
                _returnTo = -1;
                Select(me, slot);
            }
        }

        private void Delete(NetworkIdentity me, int slot)
        {
            if (Busy || _removeSlot >= 0) return;
            if (slot < 1 || slot >= Slots || _ids[slot] == 0) return;
            if (!TryIdentity(_ids[slot], out NetworkIdentity target)) { _ids[slot] = 0; return; }
            if (target.netId != me.netId) { Remove(slot, target); return; }
            // Deleting the one being played: step back into the first character, then remove it.
            if (!TryIdentity(_ids[0], out NetworkIdentity first))
            {
                ModMenuApi.Toast("Solo: cannot remove this character, the first one is gone.");
                return;
            }
            _deleteAfterSwitch = slot;
            if (!HandOver(me, first)) _deleteAfterSwitch = -1;
        }

        private void Remove(int slot, NetworkIdentity target)
        {
            PlayerCharacter pc = target.GetComponent<PlayerCharacter>();
            try
            {
                // What the game does for a player who leaves: put down what is held, get out of a pose.
                if (pc != null && pc.playerNetworking != null)
                {
                    pc.playerNetworking.ServerDropPropAutomatic(false);
                    pc.playerNetworking.ServerExitPoseAuto();
                }
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Solo] Cleanup before removing a character was incomplete: {ex.Message}"); }
            NetworkServer.Destroy(target.gameObject);
            _ids[slot] = 0;
            ResetProgress(slot, Vector3.zero);
            ModMenuApi.Log.LogInfo($"[Solo] Character in slot {slot + 1} removed.");
        }

        // ---- what a character is while played / not played ----

        private static void ZeroInput(PlayerCharacter pc)
        {
            if (pc == null) return;
            try
            {
                if (pc.rb != null)
                {
                    pc.rb.linearVelocity = new Vector3(0f, pc.rb.linearVelocity.y, 0f);
                    pc.rb.angularVelocity = Vector3.zero;
                }
                if (pc.mover != null) { pc.mover.localControlsVelocity = Vector3.zero; pc.mover.ResetPosition(); }
                if (pc.sprinter != null) pc.sprinter.isSprinting = false;
                if (pc.playerNetworking != null) pc.playerNetworking.NetworkcontrolsVelocity = Vector3.zero;
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-zero", $"[Solo] Could not clear a character's movement: {ex.Message}"); }
        }

        // Before control arrives: no leftover sprint, jump or crouch from the last time it was played.
        private void CalmDown(PlayerCharacter pc)
        {
            if (pc == null) return;
            ZeroInput(pc);
            try
            {
                if (pc.sprinter != null) pc.sprinter.sprintIsToggledOn = false;
                if (pc.jumper != null) { pc.jumper.jumpInQueue = false; pc.jumper.justJumped = false; pc.jumper.Jumpness = 0f; }
                if (pc.croucher != null) pc.croucher.ClearToggles();
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-calm", $"[Solo] Could not reset a character's controls: {ex.Message}"); }
        }

        private static ListenerMover Ears(PlayerCharacter pc)
        {
            try { return pc != null && pc.cameraMinder != null ? pc.cameraMinder.listenerMover : null; } catch { return null; }
        }

        // Gives the camera back and shows the whole body, as other players are seen.
        private static void BecomeBystander(PlayerCharacter pc)
        {
            if (pc == null) return;
            ZeroInput(pc);
            try
            {
                if (pc.cameraMinder != null) pc.cameraMinder.ReturnCamera();
                ListenerMover ears = Ears(pc);
                if (ears != null && ears != Ears(Game.LocalPlayer)) ears.enabled = false;
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-bystander-cam", $"[Solo] Could not release the camera: {ex.Message}"); }
            try { if (pc.looks != null) pc.looks.SetBodyToRemoteMode(); }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-bystander-body", $"[Solo] Could not show the body: {ex.Message}"); }
        }

        private static void BecomeProtagonist(PlayerCharacter pc)
        {
            if (pc == null) return;
            try
            {
                if (pc.decisions != null) pc.decisions.ClearWindUp();
                if (pc.head != null) { pc.head.runningTotalLookSpin = 0f; pc.head.SetHeadStateLocal(); }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-protagonist-head", $"[Solo] Could not reset the head: {ex.Message}"); }
            try { if (pc.looks != null) pc.looks.SetBodyToLocalMode(); }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-protagonist-body", $"[Solo] Could not hide the body: {ex.Message}"); }
            try
            {
                ListenerMover ears = Ears(pc);
                if (ears != null) ears.enabled = true;
                if (pc.cameraMinder != null) pc.cameraMinder.TakeCamera();
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-protagonist-cam", $"[Solo] Could not take the camera: {ex.Message}"); }
        }

        // Only the selected character listens, and only its footsteps count as "mine".
        private void KeepSensesOnActive(NetworkIdentity me)
        {
            try
            {
                ListenerMover mine = Ears(me.GetComponent<PlayerCharacter>());
                var remoteSteps = FootstepSound._remotePlayers;
                for (int i = 0; i < Slots; i++)
                {
                    if (!TryIdentity(_ids[i], out NetworkIdentity identity)) continue;
                    PlayerCharacter pc = identity.GetComponent<PlayerCharacter>();
                    bool active = identity.netId == me.netId;
                    FootstepSound steps = pc != null ? pc.footstepSound : null;
                    if (remoteSteps != null && steps != null)
                    {
                        bool listed = remoteSteps.Contains(steps);
                        if (!active && !listed) remoteSteps.Add(steps);
                        else if (active && listed) remoteSteps.Remove(steps);
                    }
                    // Each character that was played keeps asking for footstep jobs every frame, and the game
                    // answers with an error in the log each time: only the selected one may ask.
                    if (steps != null)
                    {
                        FootstepSoundJobScheduler scheduler = steps.GetComponent<FootstepSoundJobScheduler>();
                        if (scheduler != null && scheduler.enabled != active) scheduler.enabled = active;
                    }
                    ListenerMover ears = Ears(pc);
                    if (ears == null) continue;
                    if (active) ears.enabled = true;
                    else if (mine == null || ears != mine) ears.enabled = false;
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-senses", $"[Solo] Could not sort out listeners and footsteps: {ex.Message}"); }
        }

        // ---- gather and follow ----

        private void GatherAt(Vector3 position, Quaternion rotation)
        {
            NetworkIdentity me = NetworkClient.localPlayer;
            if (me == null) return;
            int moved = 0;
            for (int i = 0; i < Slots; i++)
            {
                if (_ids[i] == 0 || _ids[i] == me.netId || !TryIdentity(_ids[i], out NetworkIdentity identity)) continue;
                Vector3 spot = FormationAt(position, rotation, i, true);
                Solo.SafeTeleport(identity.GetComponent<PlayerCharacter>(), spot, rotation);
                ResetProgress(i, spot);
                moved++;
            }
            if (moved > 0) ModMenuApi.Toast($"Solo: {moved} characters gathered.");
        }

        private void SetFollow(bool on)
        {
            _follow = on;
            NetworkIdentity me = NetworkClient.localPlayer;
            for (int i = 0; i < Slots; i++)
            {
                ResetProgress(i, Vector3.zero);
                if (on || me == null || _ids[i] == me.netId || !TryIdentity(_ids[i], out NetworkIdentity identity)) continue;
                Brake(identity.GetComponent<PlayerCharacter>());
            }
            ModMenuApi.Toast(on ? "Solo: the others follow you" : "Solo: the others stay put");
        }

        private void ResetProgress(int slot, Vector3 position)
        {
            _lastJump[slot] = -100f;
            _lastCheck[slot] = Time.time;
            _stuckSince[slot] = 0f;
            _lastSpot[slot] = position;
        }

        // Seconds this follower has been trying to move without getting anywhere.
        private float StuckFor(int slot, Vector3 position, float distanceToGoal)
        {
            float now = Time.time;
            if (_lastSpot[slot] == Vector3.zero) { ResetProgress(slot, position); return 0f; }
            if (now - _lastCheck[slot] >= 0.7f)
            {
                bool still = Vector3.Distance(position, _lastSpot[slot]) < 0.25f && distanceToGoal > 1.5f;
                if (!still) _stuckSince[slot] = 0f;
                else if (_stuckSince[slot] <= 0f) _stuckSince[slot] = now;
                _lastSpot[slot] = position;
                _lastCheck[slot] = now;
            }
            return _stuckSince[slot] > 0f ? now - _stuckSince[slot] : 0f;
        }

        private float Speed(PlayerCharacter pc, bool sprinting)
        {
            float floor = sprinting ? 8.2f : 5.2f;
            PlayerTunings t = pc.tunings;
            if (t == null) return floor;
            bool swimming = pc.ground != null && pc.ground.isSwimming;
            float own = swimming ? (sprinting ? t.swimForwardSprintSpeed : t.swimForwardSpeed) : (sprinting ? t.forwardSprintSpeed : t.forwardSpeed);
            return Mathf.Max(own > 0.1f ? own * _followSpeed : floor, floor);
        }

        private static void Steer(PlayerCharacter pc, Vector3 controls, bool sprinting)
        {
            if (pc.mover != null) pc.mover.localControlsVelocity = controls;
            if (pc.sprinter != null) pc.sprinter.isSprinting = sprinting;
            if (pc.playerNetworking != null) pc.playerNetworking.NetworkcontrolsVelocity = controls;
        }

        private static void Brake(PlayerCharacter pc)
        {
            if (pc == null) return;
            if (pc.rb != null)
            {
                Vector3 v = pc.rb.linearVelocity;
                Vector3 flat = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), Vector3.zero, 28f * Time.fixedDeltaTime);
                pc.rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }
            Steer(pc, Vector3.zero, false);
        }

        private void Hop(PlayerCharacter pc, int slot)
        {
            if (Time.time - _lastJump[slot] < 0.65f) return;
            PlayerGround ground = pc.ground;
            if (ground == null || ground.isSwimming || (!ground.isGrounded && !ground.isOnJumpableGround)) return;
            PlayerTunings t = pc.tunings;
            if (pc.rb == null || t == null) return;
            Vector3 v = pc.rb.linearVelocity;
            float up = Mathf.Max(v.y, t.jumpForce);
            if (t.maxUpwardsVelocity > 0f) up = Mathf.Min(up, t.maxUpwardsVelocity);
            pc.rb.linearVelocity = new Vector3(v.x, up, v.z);
            _lastJump[slot] = Time.time;
            if (pc.jumper != null) { pc.jumper.justJumped = true; pc.jumper.Jumpness = 1f; pc.jumper.OnJump(); }
        }

        /// <summary>Called after each physics step of every character: walks the ones that are following.</summary>
        internal void DriveFollower(PlayerCharacter pc)
        {
            if (pc == null || !Hosting) return;
            NetworkIdentity me = NetworkClient.localPlayer;
            if (me == null || pc.netId == me.netId) return;
            int slot = SlotOf(pc.netId);
            Rigidbody rb = pc.rb;
            if (slot < 0 || rb == null) return;

            Transform leader = me.transform;
            Vector3 toGoal = Formation(leader, slot, false) - rb.position;
            float distance = toGoal.magnitude;
            float fromLeader = Vector3.Distance(rb.position, leader.position);
            float stuck = StuckFor(slot, rb.position, distance);

            // Too far, stuck far away, or stuck on another floor: appear next to the leader.
            bool lost = fromLeader > _catchUpDistance * 2f
                        || (fromLeader > _catchUpDistance && stuck > 4f)
                        || (Mathf.Abs(leader.position.y - rb.position.y) > 10f && stuck > 2.5f);
            if (lost)
            {
                Solo.SafeTeleport(pc, Formation(leader, slot, true), leader.rotation);
                ResetProgress(slot, rb.position);
                return;
            }

            var flat = new Vector3(toGoal.x, 0f, toGoal.z);
            float flatDistance = flat.magnitude;
            if (flatDistance <= _stopDistance) { Brake(pc); return; }

            Vector3 direction = flat / flatDistance;
            PlayerCharacter leaderPc = me.GetComponent<PlayerCharacter>();
            bool hurry = (leaderPc != null && leaderPc.sprinter != null && leaderPc.sprinter.isSprinting) || distance > 3f || stuck > 0f;
            float walk = Speed(pc, false), run = Speed(pc, true);
            float top = hurry ? run : walk;
            float least = hurry ? run * 0.88f : walk * 0.9f;
            Vector3 wanted = direction * Mathf.Clamp(flatDistance * 2.4f, least, top);

            Vector3 v = rb.linearVelocity;
            float grip = pc.ground != null && pc.ground.isGrounded ? 32f : 14f;
            Vector3 next = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), wanted, grip * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector3(next.x, v.y, next.z);
            Steer(pc, wanted, hurry);
            if (stuck > 0.8f && distance > 1.5f) Hop(pc, slot);
            if (flatDistance > 0.2f) rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(direction, Vector3.up), 0.18f));
        }

#if DEV
        public override string DevCommand(string[] a)
        {
            NetworkIdentity me = Hosting ? NetworkClient.localPlayer : null;
            string what = a.Length > 2 ? a[2] : "state";
            if (me != null)
            {
                if (what == "spawn") Spawn(me, FirstFreeSlot());
                else if (what == "select") Select(me, int.Parse(a[3]));
                else if (what == "delete") Delete(me, int.Parse(a[3]));
                else if (what == "follow") SetFollow(!_follow);
                else if (what == "gather") GatherAt(me.transform.position, me.transform.rotation);
            }
            string s = $"enabled={_enabled} hosting={Hosting} me={(me != null ? me.netId : 0)} busy={Settling} follow={_follow} original={OriginalModLoaded} slots=";
            for (int i = 0; i < Slots; i++)
            {
                s += _ids[i];
                if (TryIdentity(_ids[i], out NetworkIdentity id)) { Vector3 p = id.transform.position; s += $"@({p.x:0.0},{p.y:0.0},{p.z:0.0})"; }
                s += " ";
            }
            return s;
        }
#endif

        // ---- menu ----

        public override void DrawTab(Ui ui)
        {
            bool changed = false;
            ui.Header("Playing alone");
            if (OriginalModLoaded)
            {
                ui.Note("<color=#ff8888>The stand-alone \"Big Walk Solo Player\" mod is installed too: both do the same job and would get in each other's way. Remove the folder BepInEx/plugins/BigWalkSoloMod to use this tab.</color>", 4);
                return;
            }
            bool wasEnabled = _enabled;
            changed |= ui.Toggle("Several characters for one player", ref _enabled);
            if (wasEnabled && !_enabled)
            {
                _enabled = true;
                if (HasExtras) { ModMenuApi.Toast("Solo: remove the extra characters first."); changed = false; }
                else _enabled = false;
            }
            ui.Note("Up to four characters that you control one at a time. Only works in walks you host.", 2);
            if (!_enabled) { if (changed) Dirty = true; return; }

            NetworkIdentity me = Hosting ? NetworkClient.localPlayer : null;
            ui.Header("Characters");
            if (me == null)
            {
                ui.Note(Game.InWorld ? "<color=#ffaa66>You are a guest in this walk: only the host can create extra characters.</color>" : "Host a walk to use the characters.", 2);
            }
            else
            {
                bool was = GUI.enabled;
                GUI.enabled = was && !Settling;
                for (int i = 0; i < Slots; i++) SlotRow(ui, me, i);
                GUI.enabled = was;
                if (Busy) ui.Note("Switching character…");

                bool follow = _follow;
                if (ui.Toggle("The others follow me", ref follow)) SetFollow(follow);
                int hit = ui.Buttons("Gather here", "To the start", "Save start");
                if (hit == 0) GatherAt(me.transform.position, me.transform.rotation);
                else if (hit == 1)
                {
                    if (!_startSaved) { _startPosition = me.transform.position; _startRotation = me.transform.rotation; }
                    Solo.SafeTeleport(me.GetComponent<PlayerCharacter>(), _startPosition, _startRotation);
                    GatherAt(_startPosition, _startRotation);
                }
                else if (hit == 2)
                {
                    _startPosition = me.transform.position;
                    _startRotation = me.transform.rotation;
                    _startSaved = true;
                    ModMenuApi.Toast("Solo: start point saved");
                }
            }

            ui.Header("Rules");
            changed |= ui.SliderInt($"Extra characters: up to {_maxExtra}", ref _maxExtra, 1, Slots - 1);
            changed |= ui.Toggle("After adding one, stay on mine", ref _stayOnCurrent);
            changed |= ui.Slider($"Follower speed: {Mathf.RoundToInt(_followSpeed * 100f)}%", ref _followSpeed, 0.5f, 3f, 0.05f);
            changed |= ui.Slider($"They stop {_stopDistance:0.0} m from their spot", ref _stopDistance, 0.5f, 6f, 0.1f);
            changed |= ui.Slider($"Beyond {_catchUpDistance:0} m they reappear nearby", ref _catchUpDistance, 4f, 80f, 1f);

            ui.Header("Keys");
            foreach (Hotkey k in AllKeys()) changed |= ui.HotkeyRow(k);
            ui.Note("Keys do not respond while you type in chat.");

            if (changed) Dirty = true;
        }

        private void SlotRow(Ui ui, NetworkIdentity me, int slot)
        {
            Rect r = ui.Reserve(24f);
            Ui.Tint(r, new Color(1f, 1f, 1f, 0.05f));
            bool present = _ids[slot] != 0 && TryIdentity(_ids[slot], out _);
            bool active = present && _ids[slot] == me.netId;
            string name = slot == 0 ? "1  Your character" : $"{slot + 1}  Extra character";
            GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width * 0.5f, 20f), active ? "<b>" + name + "</b>" : name);
            float bw = r.width * 0.22f, bx = r.x + r.width - bw * 2f - 6f;
            if (present)
            {
                if (active) GUI.Label(new Rect(bx + 6f, r.y + 3f, bw, 20f), "<color=#88ff88>in use</color>");
                else if (GUI.Button(new Rect(bx, r.y + 2f, bw, 20f), "Use")) Select(me, slot);
                if (slot > 0 && GUI.Button(new Rect(bx + bw + 4f, r.y + 2f, bw, 20f), "Remove")) Delete(me, slot);
            }
            else if (slot > 0)
            {
                if (slot > _maxExtra) GUI.Label(new Rect(bx + bw + 4f, r.y + 3f, bw, 20f), "<color=#aaaaaa>disabled</color>");
                else if (GUI.Button(new Rect(bx + bw + 4f, r.y + 2f, bw, 20f), "Add")) Spawn(me, slot);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.FixedUpdate))]
    public static class FollowerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerCharacter __instance)
        {
            SoloModule m = SoloModule.Instance;
            if (m == null || !m.FollowOn) return;
            try { m.DriveFollower(__instance); }
            catch (Exception ex) { ModMenuApi.LogOnce("solo-follow", $"[Solo] Follower update failed: {ex}"); }
        }
    }

    // Every character carries its own camera minder, and each believes the screen is its own once it
    // has been played. Only the selected character's is allowed to run.
    internal static class CameraOwnership
    {
        internal static bool Allow(PlayerCameraMinder minder)
        {
            SoloModule m = SoloModule.Instance;
            if (m == null || !m.HasExtras || minder == null || !NetworkServer.activeHost) return true;
            try
            {
                PlayerCharacter pc = minder.playerCharacter;
                NetworkIdentity me = NetworkClient.localPlayer;
                if (pc == null || me == null) return true;
                return pc.netId == me.netId;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(PlayerCameraMinder), nameof(PlayerCameraMinder.Update))]
    public static class CameraUpdatePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerCameraMinder __instance) => CameraOwnership.Allow(__instance);
    }

    [HarmonyPatch(typeof(PlayerCameraMinder), nameof(PlayerCameraMinder.TakeCamera))]
    public static class CameraTakePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerCameraMinder __instance) => CameraOwnership.Allow(__instance);
    }
}
