using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace BigWalk.ModMenu.Replay
{
    /// <summary>
    /// A stretch of recorded play. Every frame is one self-contained block of bytes:
    ///   float time
    ///   camera: position (3 float), rotation (4 short), field of view (float)
    ///   byte players; for each: root position (3 float), root rotation (4 short),
    ///                 then for every bone: local position (3 short, millimetres), local rotation (4 short)
    ///   ushort props; for each: ushort id, position (3 float), rotation (4 short)
    /// Frames do not depend on each other, so any moment can be shown directly.
    /// </summary>
    public sealed class Clip
    {
        public const string Magic = "BWR1";
        public string[] BonePaths = new string[0];
        public readonly Dictionary<ushort, string> PropNames = new Dictionary<ushort, string>();
        public readonly List<byte[]> Frames = new List<byte[]>();
        public readonly List<float> Times = new List<float>();
        public string Name = "";

        public float Start => Times.Count > 0 ? Times[0] : 0f;
        public float End => Times.Count > 0 ? Times[Times.Count - 1] : 0f;
        public float Duration => End - Start;

        public long Bytes
        {
            get
            {
                long n = 0;
                foreach (byte[] f in Frames) n += f.Length;
                return n;
            }
        }

        /// <summary>Index of the last frame at or before the given time.</summary>
        public int IndexAt(float time)
        {
            int lo = 0, hi = Times.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Times[mid] <= time) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public void Save(string path)
        {
            using (var file = File.Create(path))
            using (var gz = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                WriteHeader(w, BonePaths);
                foreach (var kv in PropNames) WriteProp(w, kv.Key, kv.Value);
                foreach (byte[] f in Frames) WriteFrame(w, f);
                w.Write((byte)0);
            }
        }

        public static void WriteHeader(BinaryWriter w, string[] bonePaths)
        {
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(bonePaths.Length);
            foreach (string p in bonePaths) w.Write(p);
        }

        public static void WriteProp(BinaryWriter w, ushort id, string name)
        {
            w.Write((byte)1);
            w.Write(id);
            w.Write(name ?? "");
        }

        public static void WriteFrame(BinaryWriter w, byte[] frame)
        {
            w.Write((byte)2);
            w.Write(frame.Length);
            w.Write(frame);
        }

        public static Clip Load(string path)
        {
            var clip = new Clip { Name = Path.GetFileNameWithoutExtension(path) };
            using (var file = File.OpenRead(path))
            using (var gz = new GZipStream(file, CompressionMode.Decompress))
            using (var r = new BinaryReader(gz, Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("not a Big Walk Mod Menu recording");
                int bones = r.ReadInt32();
                if (bones < 0 || bones > 4096) throw new InvalidDataException("damaged file");
                clip.BonePaths = new string[bones];
                for (int i = 0; i < bones; i++) clip.BonePaths[i] = r.ReadString();

                // A recording cut short (crash, power loss) simply ends where the data ends.
                try
                {
                    while (true)
                    {
                        byte type = r.ReadByte();
                        if (type == 0) break;
                        if (type == 1)
                        {
                            ushort id = r.ReadUInt16();
                            clip.PropNames[id] = r.ReadString();
                        }
                        else if (type == 2)
                        {
                            int len = r.ReadInt32();
                            if (len < 4 || len > 1 << 22) break;
                            byte[] frame = r.ReadBytes(len);
                            if (frame.Length < len) break;
                            clip.Frames.Add(frame);
                            clip.Times.Add(BitConverter.ToSingle(frame, 0));
                        }
                        else break;
                    }
                }
                catch (EndOfStreamException) { }
                catch (InvalidDataException) { }
            }
            return clip;
        }
    }

    /// <summary>Compact encodings shared by the recorder and the player.</summary>
    public static class Codec
    {
        public static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x); w.Write(v.y); w.Write(v.z);
        }

        public static Vector3 ReadVector(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>Millimetre steps, good for +-32 units: bone offsets, not world positions.</summary>
        public static void WriteLocal(BinaryWriter w, Vector3 v)
        {
            w.Write(Pack(v.x * 1000f)); w.Write(Pack(v.y * 1000f)); w.Write(Pack(v.z * 1000f));
        }

        public static Vector3 ReadLocal(BinaryReader r) => new Vector3(r.ReadInt16() / 1000f, r.ReadInt16() / 1000f, r.ReadInt16() / 1000f);

        public static void Write(BinaryWriter w, Quaternion q)
        {
            w.Write(Pack(q.x * 32767f)); w.Write(Pack(q.y * 32767f)); w.Write(Pack(q.z * 32767f)); w.Write(Pack(q.w * 32767f));
        }

        public static Quaternion ReadRotation(BinaryReader r)
        {
            var q = new Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f);
            float len = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return len > 1e-5f ? new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len) : Quaternion.identity;
        }

        private static short Pack(float v) => (short)Math.Clamp((int)MathF.Round(v), short.MinValue, short.MaxValue);
    }
}
