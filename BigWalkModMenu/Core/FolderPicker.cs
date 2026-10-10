using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// Choosing a folder from inside the menu: a list of sub-folders to walk through, built from plain
    /// buttons (no text box, which IMGUI under IL2CPP handles badly). A path can also be pasted.
    /// </summary>
    public sealed class FolderPicker
    {
        private const int MaxShown = 120;

        public bool IsOpen { get; private set; }
        private string _path = "";
        private readonly List<string> _children = new List<string>();
        private string _problem;
        private bool _listed;

        public void Open(string startAt)
        {
            IsOpen = true;
            Go(Directory.Exists(startAt) ? startAt : "");
        }

        public void Close() => IsOpen = false;

        private void Go(string path)
        {
            _path = path ?? "";
            _listed = false;
            _problem = null;
        }

        private void List()
        {
            _listed = true;
            _children.Clear();
            try
            {
                if (_path.Length == 0)
                {
                    // Under Proton these are the Wine drives: Z: is the whole Linux disk.
                    _children.AddRange(Directory.GetLogicalDrives());
                }
                else
                {
                    foreach (string dir in Directory.GetDirectories(_path))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.Length > 0 && name[0] != '.' && name[0] != '$') _children.Add(dir);
                    }
                    _children.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _problem = "Cannot read this folder: " + ex.Message;
            }
        }

        /// <summary>Draws the picker. Returns the chosen folder on the frame it is confirmed, otherwise null.</summary>
        public string Draw(Ui ui)
        {
            if (!IsOpen) return null;
            if (!_listed) List();

            ui.Label(_path.Length == 0 ? "<b>Drives</b>" : "<b>" + _path + "</b>", 2);
            int nav = ui.Buttons("↑ Up", "Drives", "Paste path");
            if (nav == 0 && _path.Length > 0)
            {
                string parent = null;
                try { parent = Path.GetDirectoryName(_path.TrimEnd('\\', '/')); } catch { }
                Go(string.IsNullOrEmpty(parent) ? "" : parent);
                return null;
            }
            if (nav == 1) { Go(""); return null; }
            if (nav == 2)
            {
                string pasted = "";
                try { pasted = (GUIUtility.systemCopyBuffer ?? "").Trim().Trim('"'); } catch { }
                if (pasted.Length > 0 && Directory.Exists(pasted)) Go(pasted);
                else _problem = "The clipboard does not hold the path of an existing folder.";
                return null;
            }

            if (_problem != null) ui.Note("<color=#ff8888>" + _problem + "</color>", 2);
            if (_path.Length > 0)
            {
                int pick = ui.Buttons("Use this folder", "Cancel");
                if (pick == 0) { IsOpen = false; return _path; }
                if (pick == 1) { IsOpen = false; return null; }
            }
            else if (ui.Button("Cancel")) { IsOpen = false; return null; }

            int shown = Math.Min(_children.Count, MaxShown);
            for (int i = 0; i < shown; i++)
            {
                string child = _children[i];
                string name = _path.Length == 0 ? child : Path.GetFileName(child);
                if (ui.Button("▸ " + name)) { Go(child); return null; }
            }
            if (_children.Count > shown) ui.Note($"…and {_children.Count - shown} more: open a folder to narrow it down, or paste the path.");
            if (_children.Count == 0 && _problem == null) ui.Note("No sub-folders here.");
            return null;
        }
    }
}
