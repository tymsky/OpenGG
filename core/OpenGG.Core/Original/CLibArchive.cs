// The original game's cLib archives (Data/Gfx24.dat, Sound16.dat, Scenes.dat, Jobs/random.dat, ...), read in memory
// from the player's own copy. Layout: docs/formats/dat-archives.md. The layout and the key were worked out from the
// data files alone (the executable was not read).

using System.IO.Compression;
using System.Text;

namespace OpenGG.Core.Original;

/// <summary>One cLib archive: its files by name, each decrypted and inflated when asked for.</summary>
public sealed class CLibArchive
{
    /// <summary>The 1024-byte table every archive is XORed with (restarting at each file's name block and data).</summary>
    static readonly byte[] Key = Convert.FromBase64String(
        "CQ2azCMIpamqkXbNYZgJF67RfQn0d05SDNXeY98tTa4JsMiOwcsFHu2vtOh7q6O8R+LiTb+8rdZrwnvezvDpzFASds+OxtErrAzF" +
        "Xy8O36eMbCwkm9SZrMGWPqbDHOA1mxNOYy0yp10BpZbMTe46qwKCvHPXGkJSaCicQBj/SEJVBAnCYRg4ZCBRCVUiNMewxadq8Z5J" +
        "5exhZy5xRUmcW+5y+MW6IwsNhfwZQKC+Q4/RzoEPrAjLS7P8Xep+5bl8JS7bDjOOHihBdJjqJkdOlMdboBQtJxe+a0+0vGxcXzSR" +
        "SDHWtmYjqMpVFyuQ653SNyUDFYigP4K9QfauCBWpSnxqNGDcODzXBJD39IOIIcAMepxHkUFmANlk9Ql202TP+WoR7wZB0bXUV+6W" +
        "YK28Bx8lT++tcPw2ypt/xuWwhh74R24QhtOyUEJsCVRMqENWh2a0WmCoeRAgMJR90gRsMHQQEbAQjg04zZO2JQx6sOmtEKkK9aLE" +
        "oh/6hHQMy/QWwrAukH4wJvtclNQqWuLe2JR+rnzyUCvJaIwYOMCo31msUEm5HYOEgP5U70LUxcz2FvKv6Cot0neAYyHgfjDP4pZU" +
        "BnrvKVzgls74p76QlNPS0PJYcTSMKFvO3xb0liBEmXAgtPY1egT4cFRQydCwXq1YgZOeJbzGsHEBkH2qwZLE8n965NRAi5oWYjB6" +
        "KH6wfvPUlDyaKhbeGCxyXpra0Iup0Eyo5ICg72HUUPVJ5UHkGPqUFdb85V5+0jJH6CItBr/yW/tgplRrZnLUTlr9kbToFv44jxZw" +
        "ouMWsH4EXXSoIJtO/yaw2qC0scBopnTjlryO0ASw8SCQdkUc4+OK4HjCEDGs8Oxs427oGKuOnAbed8ksykAkPH7QvVEG9BgSvnym" +
        "6C5crUcKcDHtSMqY3LCQnZEAcDXl6tC8UF+UDjJklUAe4jzD6Lyftt8sC+AgoC5f6nUMGuovOby0kn6o0VKw8/lisKo0zVz3vJGC" +
        "EZp0/lQoGwA8GZGvvAhOmLQQoVAsKjGcn/NysJhuEHEs8HwEI25IaOs+/NbOd0MMGkAk5A5QDVU+9DiiZjhGaMZAHYuS8P1NgIL4" +
        "VDCojUEQcMmFklhc+N+UCup8JRSOksyr6CynUp+sK9wgfHZvKuV8gsoTKTwEIo6oCTLwbZm68Hp2cUzXiBECMSrMjlSsA5AUu5En" +
        "hBCKePQQ6WCsssWc1yOGyPi60Im0sKzIC3ZY6AP+HLr2d5OEsgBwPHKQNY/2dDhSfkR2iB4a0bwgUKmFpJQIDlAs4RnosIPpbG5s" +
        "vJXEAu7QIWaE/lg36GxVohuqD0KwPkZbDm00ziqPuXyMskaIcwrQ1kGa4IL5XbRntA==");

    readonly byte[] body;
    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    readonly record struct Entry(int Start, int Length, bool Compressed);

    /// <summary>The files in the archive, as named in it ("dir/name.ext").</summary>
    public IEnumerable<string> Names => entries.Keys;

    CLibArchive(byte[] body) => this.body = body;

    public static CLibArchive Open(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Reads an archive from its bytes: a text header ending in 1A 00, then the body.</summary>
    public static CLibArchive Read(byte[] raw)
    {
        int headerEnd = raw.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x1A, 0x00]);
        if (headerEnd < 0) throw new InvalidDataException("Not a cLib archive (no header).");
        var archive = new CLibArchive(raw[(headerEnd + 2)..]);
        archive.Index();
        return archive;
    }

    /// <summary>A file's bytes (decrypted, inflated), or null if the archive has no such file.</summary>
    public byte[]? Read(string name)
    {
        if (!entries.TryGetValue(name, out var e)) return null;
        var data = Decrypt(e.Start, e.Length);
        return e.Compressed ? Inflate(data) : data;
    }

    public bool Contains(string name) => entries.ContainsKey(name);

    // ---- the walk -----------------------------------------------------------------------------------

    /// <summary>
    /// Body: 8 bytes X and 8 bytes X ^ "cLib!317", then the files. Each file: 28 bytes (not needed), a 288-byte name
    /// block ("dir/name.ext", NUL, filler), then its data: a zlib stream, or the file as it is. The first file ("the
    /// lstream header") holds 8 bytes. A file's data runs up to 28 bytes before the next name block; a zlib stream is
    /// checked by the Adler-32 at its end, so a name look-alike inside compressed data is passed over.
    /// </summary>
    void Index()
    {
        // The lstream header's name block, then its 8 bytes, then the first file's 28 bytes.
        int pos = 16 + 28 + 288 + 8 + 28;
        while (pos + 288 <= body.Length)
        {
            string name = NameAt(pos) ?? throw new InvalidDataException($"No file name at {pos}.");
            int start = pos + 288;
            var head = Decrypt(start, Math.Min(2, body.Length - start));
            bool zlib = head.Length == 2 && (head[0] & 0x0F) == 8 && ((head[0] << 8) | head[1]) % 31 == 0;
            int next = start;
            while (true)
            {
                next = NextName(next + 1);
                if (!zlib || next >= body.Length + 28 || CompleteStream(start, next - 28 - start)) break;
            }
            entries[name] = new Entry(start, Math.Min(next - 28, body.Length) - start, zlib);
            pos = next;
        }
    }

    static bool NameChar(byte c) =>
        c is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'/' or (byte)'_' or (byte)'-' or (byte)'.' or (byte)' ';

    /// <summary>The name in the name block at <paramref name="p"/>, if it decrypts to one ("dir/name.ext").</summary>
    string? NameAt(int p)
    {
        if (p + 288 > body.Length) return null;
        var sb = new StringBuilder();
        for (int i = 0; i < 288; i++)
        {
            byte c = (byte)(body[p + i] ^ Key[(28 + i) & 1023]);
            if (c == 0) break;
            if (!NameChar(c) || (i == 0 && c is (byte)'/' or (byte)'.')) return null;
            sb.Append((char)c);
        }
        var name = sb.ToString();
        return name.Length >= 5 && name[^4] == '.' ? name : null;
    }

    /// <summary>The next name block at or after <paramref name="from"/>, or past the end if there is none.</summary>
    int NextName(int from)
    {
        byte k0 = Key[28];
        for (int p = from; p + 288 <= body.Length; p++)
        {
            byte c = (byte)(body[p] ^ k0);
            if (!NameChar(c) || c is (byte)'/' or (byte)'.') continue;
            if (NameAt(p) is not null) return p;
        }
        return body.Length + 28;
    }

    /// <summary>Does a whole zlib stream fill these bytes (its Adler-32 at the end matching what it inflates to)?</summary>
    bool CompleteStream(int start, int length)
    {
        if (length < 6) return false;
        var data = Decrypt(start, length);
        try
        {
            var output = Inflate(data);
            uint want = (uint)(data[^4] << 24 | data[^3] << 16 | data[^2] << 8 | data[^1]);
            return Adler32(output) == want;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    byte[] Decrypt(int start, int length)
    {
        var d = new byte[length];
        for (int i = 0; i < length; i++) d[i] = (byte)(body[start + i] ^ Key[i & 1023]);
        return d;
    }

    static byte[] Inflate(byte[] zlib)
    {
        using var z = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    static uint Adler32(byte[] d)
    {
        uint a = 1, b = 0;
        foreach (byte x in d)
        {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
        }
        return b << 16 | a;
    }
}

/// <summary>
/// The original's archives in a game folder, read in memory: its pictures (the 24-bit set), sounds (the 16-bit set),
/// 3D scenes and the random jobs' words and faces. Files are named by archive and path: "Gfx24/workshopup.tga",
/// "Sound16/click.wav", "Scenes/carlot.3ds", "random/comments.txt". Nothing is written to disk.
/// </summary>
public sealed class OriginalArchives
{
    static readonly (string Name, string File)[] Known =
    [
        ("Gfx24", "Data/Gfx24.dat"),
        ("Sound16", "Data/Sound16.dat"),
        ("Scenes", "Data/Scenes.dat"),
        ("random", "Data/Jobs/random.dat"),
    ];

    readonly Dictionary<string, Lazy<CLibArchive?>> archives = new(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; }

    OriginalArchives(string folder) => Folder = folder;

    /// <summary>The archives of the game in <paramref name="folder"/>, or null if it has none. Each is read the first
    /// time one of its files is wanted.</summary>
    public static OriginalArchives? Open(string folder)
    {
        var a = new OriginalArchives(folder);
        foreach (var (name, file) in Known)
        {
            var path = Path.Combine(folder, file);
            if (File.Exists(path)) a.archives[name] = new Lazy<CLibArchive?>(() => Load(path));
        }
        return a.archives.Count > 0 ? a : null;
    }

    static CLibArchive? Load(string path)
    {
        try { return CLibArchive.Open(path); }
        catch (Exception) { return null; }
    }

    /// <summary>Does the folder have this archive ("Gfx24")?</summary>
    public bool Has(string archive) => archives.ContainsKey(archive);

    /// <summary>A file's bytes by its path ("Gfx24/workshopup.tga"; any case), or null.</summary>
    public byte[]? Read(string path)
    {
        var (archive, name) = Split(path);
        return archive is not null && archives.TryGetValue(archive, out var a) && a.Value is { } arc ? arc.Read(name) : null;
    }

    public bool Exists(string path)
    {
        var (archive, name) = Split(path);
        return archive is not null && archives.TryGetValue(archive, out var a) && a.Value is { } arc && arc.Contains(name);
    }

    /// <summary>The files of an archive ("Gfx24"), as named in it.</summary>
    public IEnumerable<string> Files(string archive) =>
        archives.TryGetValue(archive, out var a) && a.Value is { } arc ? arc.Names : [];

    static (string? Archive, string Name) Split(string path)
    {
        path = path.Replace('\\', '/');
        int slash = path.IndexOf('/');
        return slash < 0 ? (null, path) : (path[..slash], path[(slash + 1)..]);
    }
}
