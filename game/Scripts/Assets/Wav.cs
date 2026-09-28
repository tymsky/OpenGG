using System;
using System.Text;
using Godot;

namespace OpenGG.Assets;

/// <summary>Reads PCM WAV files into an AudioStreamWav (8 or 16 bit, mono or stereo).</summary>
public static class Wav
{
    public static AudioStreamWav? Load(byte[] bytes)
    {
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return null;
        int channels = 1, rate = 22050, bits = 16;
        byte[]? data = null;
        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            string id = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (size < 0 || body + size > bytes.Length) size = bytes.Length - body;
            if (id == "fmt ")
            {
                int format = BitConverter.ToInt16(bytes, body);
                if (format != 1) return null; // PCM only
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                data = new byte[size];
                Array.Copy(bytes, body, data, 0, size);
            }
            pos = body + size + (size & 1);
        }
        if (data is null || (bits != 8 && bits != 16)) return null;
        if (bits == 8)
            for (int i = 0; i < data.Length; i++) data[i] ^= 0x80; // WAV 8-bit is unsigned, Godot wants signed
        return new AudioStreamWav
        {
            Data = data,
            Format = bits == 8 ? AudioStreamWav.FormatEnum.Format8Bits : AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = channels == 2,
        };
    }
}
