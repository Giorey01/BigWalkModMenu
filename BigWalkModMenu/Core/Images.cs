using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using StbImageSharp;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>Image files to textures, decoded in managed code (PNG, JPG, BMP).</summary>
    public static class Images
    {
        /// <summary>RGBA bytes, top row first. Returns false when the file cannot be decoded.</summary>
        public static bool Read(string path, out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = height = 0;
            try
            {
                StbImage.stbi_set_flip_vertically_on_load(0);
                ImageResult img = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
                if (img == null || img.Width <= 0 || img.Height <= 0 || img.Data == null) return false;
                rgba = img.Data;
                width = img.Width;
                height = img.Height;
                return true;
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Images] Could not read '{path}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Decodes image bytes that came from another player. The size written in the file header is
        /// checked before any pixel memory is allocated, so a tiny file cannot ask for gigabytes.
        /// </summary>
        public static bool DecodeChecked(byte[] file, int maxSide, out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = height = 0;
            if (file == null || file.Length < 16) return false;
            try
            {
                using (var stream = new MemoryStream(file, false))
                {
                    ImageInfo? info = ImageInfo.FromStream(stream);
                    if (!info.HasValue) return false;
                    if (info.Value.Width <= 0 || info.Value.Height <= 0 || info.Value.Width > maxSide || info.Value.Height > maxSide) return false;
                }
                StbImage.stbi_set_flip_vertically_on_load(0);
                ImageResult img = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
                if (img == null || img.Data == null || img.Width <= 0 || img.Height <= 0 || img.Width > maxSide || img.Height > maxSide) return false;
                if (img.Data.Length != img.Width * img.Height * 4) return false;
                rgba = img.Data;
                width = img.Width;
                height = img.Height;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] _template;
        private static int _templateScan, _dqtFrom, _dqtTo, _sofDims;

        // The header our own encoder writes: everything up to the picture data, parsed once.
        private static bool EnsureTemplate()
        {
            if (_template != null) return _templateScan > 0;
            _template = EncodeJpeg(new byte[8 * 8 * 4], 8, 8, 80);
            byte[] t = _template;
            _templateScan = 0;
            int i = 2;
            while (i + 4 <= t.Length && t[i] == 0xFF)
            {
                int marker = t[i + 1], length = (t[i + 2] << 8) | t[i + 3];
                if (marker == 0xDB) { _dqtFrom = i + 4; _dqtTo = i + 2 + length; }   // quantisation tables: change with the quality
                else if (marker == 0xC0) _sofDims = i + 5;                           // height and width, two bytes each
                i += 2 + length;
                if (marker == 0xDA) { _templateScan = i; break; }
            }
            if (_dqtTo - _dqtFrom != 130 || _sofDims == 0) _templateScan = 0; // not the layout this check was written for
            return _templateScan > 0;
        }

        /// <summary>
        /// True only for a JPEG laid out exactly like the ones this mod writes. The decoder can hang on
        /// a header with made-up segment lengths (found by feeding it damaged files), so for pictures
        /// from other players nothing but the table values, the size and the picture data may differ,
        /// and the picture data may not contain any marker except the final end-of-image.
        /// </summary>
        public static bool IsOwnJpeg(byte[] file, int maxSide)
        {
            if (file == null || !EnsureTemplate()) return false;
            byte[] t = _template;
            int scan = _templateScan;
            if (file.Length < scan + 2) return false;
            for (int i = 0; i < scan; i++)
            {
                bool free = (i >= _dqtFrom && i < _dqtTo && i != _dqtFrom && i != _dqtFrom + 65) || (i >= _sofDims && i < _sofDims + 4);
                if (free)
                {
                    if (i < _dqtTo && i >= _dqtFrom && file[i] == 0) return false; // a table value is never zero
                    continue;
                }
                if (file[i] != t[i]) return false;
            }
            int height = (file[_sofDims] << 8) | file[_sofDims + 1], width = (file[_sofDims + 2] << 8) | file[_sofDims + 3];
            if (width < 1 || height < 1 || width > maxSide || height > maxSide) return false;

            int end = file.Length - 2;
            if (file[end] != 0xFF || file[end + 1] != 0xD9) return false;
            for (int i = scan; i < end; i++)
            {
                if (file[i] != 0xFF) continue;
                if (file[i + 1] != 0x00) return false; // inside the picture data FF must be followed by 00
                i++;
            }
            return true;
        }

        /// <summary>Decodes a picture received from another player: our own JPEG layout only.</summary>
        public static bool DecodeOwnJpeg(byte[] file, int maxSide, out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = height = 0;
            return IsOwnJpeg(file, maxSide) && DecodeChecked(file, maxSide, out rgba, out width, out height);
        }

        // ---- our JPEG without its header (live video: the header is 600 bytes that never change) ----

        private static readonly System.Collections.Generic.Dictionary<int, byte[]> _headers = new System.Collections.Generic.Dictionary<int, byte[]>();

        /// <summary>Length of the fixed header of a JPEG written by this mod, or 0 when it cannot be told.</summary>
        public static int OwnJpegHeaderLength => EnsureTemplate() ? _templateScan : 0;

        /// <summary>The picture data of one of our own JPEGs, without the header. Null for anything else.</summary>
        public static byte[] StripOwnJpeg(byte[] file, int maxSide)
        {
            if (!IsOwnJpeg(file, maxSide)) return null;
            var data = new byte[file.Length - _templateScan];
            Buffer.BlockCopy(file, _templateScan, data, 0, data.Length);
            return data;
        }

        /// <summary>
        /// Puts the header back in front of picture data from <see cref="StripOwnJpeg"/>: the one this mod's
        /// encoder writes for that size and quality. The result still has to pass <see cref="DecodeOwnJpeg"/>.
        /// </summary>
        public static byte[] RebuildOwnJpeg(byte[] data, int width, int height, int quality)
        {
            if (data == null || data.Length < 2 || width < 1 || height < 1 || width > 4096 || height > 4096) return null;
            if (quality < 1 || quality > 85 || !EnsureTemplate()) return null;
            if (!_headers.TryGetValue(quality, out byte[] header))
            {
                byte[] sample = EncodeJpeg(new byte[8 * 8 * 4], 8, 8, quality);
                if (sample.Length < _templateScan) return null;
                header = new byte[_templateScan];
                Buffer.BlockCopy(sample, 0, header, 0, _templateScan);
                _headers[quality] = header;
            }
            var file = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, file, 0, header.Length);
            file[_sofDims] = (byte)(height >> 8); file[_sofDims + 1] = (byte)height;
            file[_sofDims + 2] = (byte)(width >> 8); file[_sofDims + 3] = (byte)width;
            Buffer.BlockCopy(data, 0, file, header.Length, data.Length);
            return file;
        }

        /// <summary>JPEG bytes from RGBA pixels (top row first). Alpha is ignored.</summary>
        public static byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality)
        {
            using (var stream = new MemoryStream())
            {
                new StbImageWriteSharp.ImageWriter().WriteJpg(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream, quality);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Shrinks RGBA pixels so the longer side is at most maxSide, painting transparent areas with the
        /// given colour (JPEG has no transparency).
        /// </summary>
        public static byte[] FlattenAndShrink(byte[] rgba, int width, int height, int maxSide, byte fillR, byte fillG, byte fillB, out int outWidth, out int outHeight)
        {
            float scale = Math.Min(1f, maxSide / (float)Math.Max(width, height));
            outWidth = Math.Max(1, (int)MathF.Round(width * scale));
            outHeight = Math.Max(1, (int)MathF.Round(height * scale));
            var result = new byte[outWidth * outHeight * 4];
            for (int y = 0; y < outHeight; y++)
            {
                int y0 = y * height / outHeight, y1 = Math.Max(y0 + 1, (y + 1) * height / outHeight);
                for (int x = 0; x < outWidth; x++)
                {
                    int x0 = x * width / outWidth, x1 = Math.Max(x0 + 1, (x + 1) * width / outWidth);
                    float r = 0f, g = 0f, b = 0f;
                    int count = 0;
                    for (int sy = y0; sy < y1 && sy < height; sy++)
                    {
                        int at = (sy * width + x0) * 4;
                        for (int sx = x0; sx < x1 && sx < width; sx++, at += 4)
                        {
                            float a = rgba[at + 3] / 255f;
                            r += rgba[at] * a + fillR * (1f - a);
                            g += rgba[at + 1] * a + fillG * (1f - a);
                            b += rgba[at + 2] * a + fillB * (1f - a);
                            count++;
                        }
                    }
                    int o = (y * outWidth + x) * 4;
                    if (count == 0) count = 1;
                    result[o] = (byte)(r / count);
                    result[o + 1] = (byte)(g / count);
                    result[o + 2] = (byte)(b / count);
                    result[o + 3] = 255;
                }
            }
            return result;
        }

        /// <summary>Loads an image shrunk (by whole pixel blocks) so that its longer side is at most maxSize.</summary>
        public static Texture2D LoadTexture(string path, int maxSize)
        {
            if (!Read(path, out byte[] rgba, out int w, out int h)) return null;
            int step = Math.Max(1, (int)Math.Ceiling(Math.Max(w, h) / (double)maxSize));
            int tw = Math.Max(1, w / step), th = Math.Max(1, h / step);

            var pixels = new Il2CppStructArray<Color32>(tw * th);
            int area = step * step;
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < step; j++)
                    {
                        int row = ((y * step + j) * w + x * step) * 4;
                        for (int i = 0; i < step; i++)
                        {
                            r += rgba[row]; g += rgba[row + 1]; b += rgba[row + 2]; a += rgba[row + 3];
                            row += 4;
                        }
                    }
                    // Textures are stored bottom row first.
                    pixels[(th - 1 - y) * tw + x] = new Color32((byte)(r / area), (byte)(g / area), (byte)(b / area), (byte)(a / area));
                }
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }
    }
}
