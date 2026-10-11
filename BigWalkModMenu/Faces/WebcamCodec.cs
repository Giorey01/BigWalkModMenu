using System;

namespace BigWalk.ModMenu.Faces
{
    /// <summary>
    /// Live pictures for friends. Each one is a small JPEG from this mod's own encoder with its fixed
    /// header cut off, split over at most three messages:
    ///
    ///   [number][piece index * 16 + piece count][side][quality][zoom][wrap][flags] picture data
    ///
    /// Nothing is kept or sent again: a picture that does not arrive whole is dropped when the next one
    /// starts. The quality follows the size, so a picture normally fits in two messages.
    /// </summary>
    public static class WebcamCodec
    {
        public const int SendSide = 128, MaxSide = 160, MaxPieces = 3, PieceHeader = 7;
        public const int MinQuality = 10, MaxQuality = 70;

        public sealed class Picture
        {
            public byte[] Rgba;
            public int Side;
            public float Zoom, Wrap;
            public bool Mirror;
        }

        public sealed class Encoder
        {
            private int _quality = 45;
            private byte _number;

            public int Quality { get; private set; }
            public int Bytes { get; private set; }

            /// <summary>The messages for one picture, or null when it cannot be made small enough right now.</summary>
            public byte[][] Encode(byte[] rgba, int side, int maxBody, float zoom, float wrap, bool mirror)
            {
                int room = maxBody - PieceHeader;
                if (rgba == null || side < 16 || side > MaxSide || rgba.Length != side * side * 4 || room < 100) return null;
                int target = room * 2;

                byte[] data = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    data = Images.StripOwnJpeg(Images.EncodeJpeg(rgba, side, side, _quality), MaxSide);
                    if (data == null) return null;
                    Quality = _quality;
                    if (data.Length <= target) break;
                    // Too big: the next try (and the next pictures) are coarser, in proportion to the excess.
                    _quality = Math.Max(MinQuality, Math.Min(_quality - 2, (int)(_quality * (float)target / data.Length * 0.9f)));
                    if (Quality == MinQuality) break;
                }
                if (data.Length < target * 0.72f && _quality < MaxQuality) _quality += 2;
                if (data.Length > room * MaxPieces) return null;

                Bytes = data.Length;
                int count = (data.Length + room - 1) / room;
                var pieces = new byte[count][];
                byte number = ++_number;
                for (int i = 0; i < count; i++)
                {
                    int offset = i * room, length = Math.Min(room, data.Length - offset);
                    var w = new WireWriter();
                    w.U8(number).U8((i << 4) | count).U8(side).U8(Quality);
                    w.Q8(zoom, 0.4f, 3f).Q8(wrap, 0f, 1f).U8(mirror ? 1 : 0);
                    byte[] head = w.ToArray();
                    var piece = new byte[head.Length + length];
                    Buffer.BlockCopy(head, 0, piece, 0, head.Length);
                    Buffer.BlockCopy(data, offset, piece, head.Length, length);
                    pieces[i] = piece;
                }
                return pieces;
            }
        }

        /// <summary>Puts one sender's pieces back together. Everything it is given is untrusted.</summary>
        public sealed class Decoder
        {
            private const int MaxPieceBytes = 1200;
            private readonly byte[][] _pieces = new byte[MaxPieces][];
            private int _number = -1, _count, _side, _quality, _have;

            /// <summary>Takes one message; returns a picture when it was the last piece of a valid one.</summary>
            public Picture Add(byte[] body)
            {
                if (body == null || body.Length <= PieceHeader || body.Length > PieceHeader + MaxPieceBytes) return null;
                var r = new WireReader(body);
                int number = r.U8(), where = r.U8(), side = r.U8(), quality = r.U8();
                float zoom = r.Q8(0.4f, 3f), wrap = r.Q8(0f, 1f);
                bool mirror = (r.U8() & 1) != 0;
                int index = where >> 4, count = where & 15;
                if (r.Failed || count < 1 || count > MaxPieces || index >= count) return null;
                if (side < 16 || side > MaxSide || quality < 1 || quality > 85) return null;

                if (number != _number || count != _count || side != _side || quality != _quality)
                {
                    // A new picture: whatever was missing from the previous one will not be waited for.
                    _number = number; _count = count; _side = side; _quality = quality; _have = 0;
                    Array.Clear(_pieces, 0, _pieces.Length);
                }
                if (_pieces[index] != null) return null;
                _pieces[index] = r.Raw(r.Remaining);
                if (++_have < _count) return null;

                int total = 0;
                for (int i = 0; i < _count; i++) total += _pieces[i].Length;
                var data = new byte[total];
                for (int i = 0, at = 0; i < _count; i++)
                {
                    Buffer.BlockCopy(_pieces[i], 0, data, at, _pieces[i].Length);
                    at += _pieces[i].Length;
                }
                _number = -1;
                Array.Clear(_pieces, 0, _pieces.Length);

                byte[] file = Images.RebuildOwnJpeg(data, side, side, quality);
                if (file == null || !Images.DecodeOwnJpeg(file, MaxSide, out byte[] rgba, out int width, out int height)) return null;
                if (width != side || height != side) return null;
                return new Picture { Rgba = rgba, Side = side, Zoom = zoom, Wrap = wrap, Mirror = mirror };
            }
        }
    }
}
