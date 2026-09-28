using System;
using System.IO;
using OpenGG.Core.Original;

namespace OpenGG.Ui;

/// <summary>Where a skin's files come from: a folder on disk, or the original game's archives read in memory. Paths are
/// relative ("Gfx24/workshopup.tga") and found in any letter case.</summary>
public abstract class SkinFiles
{
    /// <summary>The file's path as it is stored (in its own letter case), or null if there is no such file.</summary>
    public abstract string? Find(string rel);

    /// <summary>The file's bytes, or null.</summary>
    public abstract byte[]? Read(string rel);
}

/// <summary>A skin folder's files.</summary>
public sealed class FolderFiles(string root) : SkinFiles
{
    public override string? Find(string rel)
    {
        var path = Path.Combine(root, rel);
        if (File.Exists(path)) return rel;
        var dir = Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir)) return null;
        var name = Path.GetFileName(path);
        foreach (var f in Directory.EnumerateFiles(dir))
            if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return Path.GetRelativePath(root, f);
        return null;
    }

    public override byte[]? Read(string rel) => Find(rel) is { } found ? File.ReadAllBytes(Path.Combine(root, found)) : null;
}

/// <summary>The original game's files, straight from the player's archives (nothing is written to disk).</summary>
public sealed class ArchiveFiles(OriginalArchives archives) : SkinFiles
{
    public override string? Find(string rel)
    {
        rel = rel.Replace('\\', '/');
        return archives.Exists(rel) ? rel : null;
    }

    public override byte[]? Read(string rel) => archives.Read(rel.Replace('\\', '/'));
}

/// <summary>One source over another: a skin that changes the original's look, its own files first.</summary>
public sealed class LayeredFiles(SkinFiles top, SkinFiles under) : SkinFiles
{
    public override string? Find(string rel) => top.Find(rel) ?? under.Find(rel);

    public override byte[]? Read(string rel) => top.Read(rel) ?? under.Read(rel);
}
