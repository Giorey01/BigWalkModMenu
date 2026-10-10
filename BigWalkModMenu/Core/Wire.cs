using System;
using System.IO;
using System.Text;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// Reads a message that came from another player. Nothing in it is trusted: every read is checked
    /// against the end of the data, numbers are forced into the range the caller allows, and after the
    /// first problem the reader is marked Failed and returns harmless defaults. Callers check Failed
    /// once at the end and drop the whole message.
    /// </summary>
    public sealed class WireReader
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly int _end;

        public bool Failed { get; private set; }
        public int Remaining => Failed ? 0 : _end - _pos;

        public WireReader(byte[] data, int offset = 0, int count = -1)
        {
            _data = data ?? Array.Empty<byte>();
            if (offset < 0 || offset > _data.Length) { offset = _data.Length; Failed = true; }
            if (count < 0) count = _data.Length - offset;
            if (count > _data.Length - offset) { count = _data.Length - offset; Failed = true; }
            _pos = offset;
            _end = offset + count;
        }

        private bool Need(int n)
        {
            if (Failed) return false;
            if (n < 0 || _end - _pos < n) { Failed = true; return false; }
            return true;
        }

        public byte U8()
        {
            return Need(1) ? _data[_pos++] : (byte)0;
        }

        public bool Bool() => U8() != 0;

        public ushort U16()
        {
            if (!Need(2)) return 0;
            ushort v = (ushort)(_data[_pos] | (_data[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public uint U32()
        {
            if (!Need(4)) return 0;
            uint v = (uint)(_data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public ulong U64()
        {
            ulong lo = U32();
            ulong hi = U32();
            return lo | (hi << 32);
        }

        /// <summary>A whole number that must lie in [min, max]; anything else fails the message.</summary>
        public int Int(int min, int max)
        {
            int v = U8();
            if (v < min || v > max) { Failed = true; return min; }
            return v;
        }

        /// <summary>A value sent as one byte spread over [min, max]. Always inside the range.</summary>
        public float Q8(float min, float max) => min + (max - min) * (U8() / 255f);

        /// <summary>A value sent as two bytes spread over [min, max]. Always inside the range.</summary>
        public float Q16(float min, float max) => min + (max - min) * (U16() / 65535f);

        /// <summary>Bytes prefixed by a two-byte length, refused when longer than maxLength.</summary>
        public byte[] Bytes(int maxLength)
        {
            int n = U16();
            if (n > maxLength) { Failed = true; return Array.Empty<byte>(); }
            return Raw(n);
        }

        public byte[] Raw(int count)
        {
            if (!Need(count)) return Array.Empty<byte>();
            var result = new byte[count];
            Buffer.BlockCopy(_data, _pos, result, 0, count);
            _pos += count;
            return result;
        }

        /// <summary>UTF-8 text of at most maxBytes, with control characters and rich-text brackets removed.</summary>
        public string Text(int maxBytes)
        {
            int n = U8();
            if (n > maxBytes) { Failed = true; return ""; }
            if (!Need(n)) return "";
            string s;
            try { s = new UTF8Encoding(false, true).GetString(_data, _pos, n); }
            catch (ArgumentException) { Failed = true; return ""; }
            _pos += n;

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                // The text ends up in a GUI label: no markup, no line breaks, no invisible characters.
                if (c == '<' || c == '>' || char.IsControl(c) || char.IsSurrogate(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public sealed class WireWriter
    {
        private readonly MemoryStream _stream = new MemoryStream(128);

        public int Length => (int)_stream.Length;

        public WireWriter U8(int v) { _stream.WriteByte((byte)v); return this; }
        public WireWriter Bool(bool v) => U8(v ? 1 : 0);

        public WireWriter U16(int v)
        {
            _stream.WriteByte((byte)v);
            _stream.WriteByte((byte)(v >> 8));
            return this;
        }

        public WireWriter U32(uint v)
        {
            _stream.WriteByte((byte)v);
            _stream.WriteByte((byte)(v >> 8));
            _stream.WriteByte((byte)(v >> 16));
            _stream.WriteByte((byte)(v >> 24));
            return this;
        }

        public WireWriter U64(ulong v) => U32((uint)v).U32((uint)(v >> 32));

        public WireWriter Q8(float v, float min, float max)
        {
            float t = max > min ? (v - min) / (max - min) : 0f;
            if (float.IsNaN(t)) t = 0f;
            return U8((int)MathF.Round(Math.Clamp(t, 0f, 1f) * 255f));
        }

        public WireWriter Q16(float v, float min, float max)
        {
            float t = max > min ? (v - min) / (max - min) : 0f;
            if (float.IsNaN(t)) t = 0f;
            return U16((int)MathF.Round(Math.Clamp(t, 0f, 1f) * 65535f));
        }

        public WireWriter Bytes(byte[] data, int offset, int count)
        {
            U16(count);
            _stream.Write(data, offset, count);
            return this;
        }

        public WireWriter Raw(byte[] data)
        {
            if (data != null) _stream.Write(data, 0, data.Length);
            return this;
        }

        public WireWriter Text(string s, int maxBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(Math.Min(bytes.Length, maxBytes), 255);
            // Never cut a character in half.
            while (n > 0 && n < bytes.Length && (bytes[n] & 0xC0) == 0x80) n--;
            U8(n);
            _stream.Write(bytes, 0, n);
            return this;
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    /// <summary>Small non-cryptographic hash, used only to tell whether two images are the same file.</summary>
    public static class Fnv
    {
        public static uint Hash(byte[] data)
        {
            uint h = 2166136261;
            if (data == null) return h;
            for (int i = 0; i < data.Length; i++)
            {
                h ^= data[i];
                h *= 16777619;
            }
            return h;
        }
    }

    /// <summary>
    /// Puts back together a file that arrives in numbered pieces. Total size and piece count are capped
    /// before anything is stored, and a piece can never land outside the announced size.
    /// </summary>
    public sealed class ChunkAssembler
    {
        public readonly int MaxBytes, MaxChunks;
        private byte[] _data;
        private bool[] _have;
        private int _chunkSize, _missing;
        public uint Hash { get; private set; }

        public ChunkAssembler(int maxBytes, int maxChunks)
        {
            MaxBytes = maxBytes;
            MaxChunks = maxChunks;
        }

        public bool Active => _data != null;

        /// <summary>Starts a new transfer. Returns false when the announced sizes are not acceptable.</summary>
        public bool Begin(uint hash, int totalBytes, int chunkSize)
        {
            _data = null;
            if (totalBytes <= 0 || totalBytes > MaxBytes || chunkSize < 64 || chunkSize > 2048) return false;
            int chunks = (totalBytes + chunkSize - 1) / chunkSize;
            if (chunks > MaxChunks) return false;
            Hash = hash;
            _chunkSize = chunkSize;
            _data = new byte[totalBytes];
            _have = new bool[chunks];
            _missing = chunks;
            return true;
        }

        /// <summary>Adds one piece. Returns the finished file once every piece is in and the hash matches, else null.</summary>
        public byte[] Add(uint hash, int index, byte[] piece)
        {
            if (_data == null || hash != Hash || piece == null) return null;
            if (index < 0 || index >= _have.Length) return null;
            int offset = index * _chunkSize;
            int expected = Math.Min(_chunkSize, _data.Length - offset);
            if (piece.Length != expected) return null;
            if (!_have[index])
            {
                _have[index] = true;
                _missing--;
            }
            Buffer.BlockCopy(piece, 0, _data, offset, expected);
            if (_missing > 0) return null;

            byte[] done = _data;
            _data = null;
            return Fnv.Hash(done) == hash ? done : null;
        }
    }
}
