using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigWalk.ModMenu.Extras
{
    /// <summary>Decoded sound: interleaved samples between -1 and 1.</summary>
    public sealed class Pcm
    {
        public float[] Samples;
        public int Channels, Rate;
        public string Error;
        public float Seconds => Samples != null && Channels > 0 && Rate > 0 ? Samples.Length / (float)(Channels * Rate) : 0f;
    }

    /// <summary>WAV, OGG and MP3 files decoded in managed code. Decode() is safe to call from another thread.</summary>
    public static class AudioLoader
    {
        public static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };
        private const int MaxSeconds = 15 * 60;

        public static bool IsAudio(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        public static Pcm Decode(string path)
        {
            try
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".wav": return DecodeWav(path);
                    case ".ogg": return DecodeOgg(path);
                    case ".mp3": return DecodeMp3(path);
                    default: return new Pcm { Error = "formato non supportato" };
                }
            }
            catch (Exception ex)
            {
                return new Pcm { Error = ex.Message };
            }
        }

        private static Pcm DecodeOgg(string path)
        {
            using (var reader = new NVorbis.VorbisReader(path))
            {
                int channels = reader.Channels, rate = reader.SampleRate;
                return ReadAll(channels, rate, (buffer, count) => reader.ReadSamples(buffer, 0, count));
            }
        }

        private static Pcm DecodeMp3(string path)
        {
            using (var reader = new NLayer.MpegFile(path))
            {
                int channels = reader.Channels, rate = reader.SampleRate;
                return ReadAll(channels, rate, (buffer, count) => reader.ReadSamples(buffer, 0, count));
            }
        }

        private static Pcm ReadAll(int channels, int rate, Func<float[], int, int> read)
        {
            if (channels <= 0 || rate <= 0) return new Pcm { Error = "file audio non valido" };
            long limit = (long)MaxSeconds * rate * channels;
            var chunks = new List<float[]>();
            var sizes = new List<int>();
            long total = 0;
            while (total < limit)
            {
                var buffer = new float[rate * channels]; // one second at a time
                int got = read(buffer, buffer.Length);
                if (got <= 0) break;
                chunks.Add(buffer);
                sizes.Add(got);
                total += got;
            }
            var samples = new float[total - total % channels];
            int offset = 0;
            for (int i = 0; i < chunks.Count && offset < samples.Length; i++)
            {
                int n = Math.Min(sizes[i], samples.Length - offset);
                Array.Copy(chunks[i], 0, samples, offset, n);
                offset += n;
            }
            return new Pcm { Samples = samples, Channels = channels, Rate = rate };
        }

        private static Pcm DecodeWav(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 44 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return new Pcm { Error = "non è un file WAV" };

            int format = 0, channels = 0, rate = 0, bits = 0;
            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                int size = BitConverter.ToInt32(data, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > data.Length) size = data.Length - body;

                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(data, body);
                    channels = BitConverter.ToUInt16(data, body + 2);
                    rate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToUInt16(data, body + 14);
                    // WAVE_FORMAT_EXTENSIBLE keeps the real format in the sub-format field.
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(data, body + 24);
                }
                else if (id == "data")
                {
                    if (channels <= 0 || rate <= 0) return new Pcm { Error = "WAV senza intestazione" };
                    int bytes = bits / 8;
                    if (bytes <= 0) return new Pcm { Error = "WAV non supportato" };
                    int count = size / bytes;
                    count -= count % channels;
                    count = (int)Math.Min(count, (long)MaxSeconds * rate * channels);
                    var samples = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        int p = body + i * bytes;
                        if (format == 3 && bits == 32) samples[i] = BitConverter.ToSingle(data, p);
                        else if (format == 1 && bits == 16) samples[i] = BitConverter.ToInt16(data, p) / 32768f;
                        else if (format == 1 && bits == 8) samples[i] = (data[p] - 128) / 128f;
                        else if (format == 1 && bits == 24) samples[i] = ((data[p] | (data[p + 1] << 8) | ((sbyte)data[p + 2] << 16))) / 8388608f;
                        else if (format == 1 && bits == 32) samples[i] = BitConverter.ToInt32(data, p) / 2147483648f;
                        else return new Pcm { Error = $"WAV non supportato (formato {format}, {bits} bit)" };
                    }
                    return new Pcm { Samples = samples, Channels = channels, Rate = rate };
                }
                pos = body + size + (size & 1);
            }
            return new Pcm { Error = "WAV senza dati" };
        }

        /// <summary>Main thread only.</summary>
        public static AudioClip ToClip(Pcm pcm, string name)
        {
            if (pcm == null || pcm.Samples == null || pcm.Samples.Length == 0) return null;
            int frames = pcm.Samples.Length / pcm.Channels;
            AudioClip clip = AudioClip.Create(name, frames, pcm.Channels, pcm.Rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;

            // One block copy into the IL2CPP array (its data starts after the four-pointer header).
            var array = new Il2CppStructArray<float>(pcm.Samples.Length);
            Marshal.Copy(pcm.Samples, 0, array.Pointer + 4 * IntPtr.Size, pcm.Samples.Length);
            clip.SetData(array, 0);
            return clip;
        }
    }
}
