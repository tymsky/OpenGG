using System;
using System.IO;
using Godot;

namespace OpenGG;

/// <summary>Where things live on disk.</summary>
public static class Paths
{
    /// <summary>
    /// The folder with content packs (data/ai ...). Next to the executable in an exported build,
    /// one level above the Godot project when run from the editor or `godot --path game`.
    /// </summary>
    public static string DataDir
    {
        get
        {
            var args = Args.Get("data");
            if (args is not null) return args;
            if (!OS.HasFeature("editor") && !OS.HasFeature("debug_run_from_source"))
            {
                var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
                var exported = Path.Combine(exeDir, "data");
                if (Directory.Exists(exported)) return exported;
            }
            return Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"));
        }
    }

    public static string AiPack => Path.Combine(DataDir, "ai");

    /// <summary>OpenGG's own look's CC0 pictures (data/hd, see its LICENSES.md).</summary>
    public static string HdPack => Path.Combine(DataDir, "hd");

    /// <summary>Per-user folder (saves, settings, snapshots); `--user <dir>` puts them elsewhere (tests that make and
    /// delete mechanics).</summary>
    public static string User => Args.Get("user") is { } dir ? Path.GetFullPath(dir) : ProjectSettings.GlobalizePath("user://");

    public static string Snapshots
    {
        get
        {
            var dir = Path.Combine(User, "snapshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

/// <summary>Command-line options after `--`, e.g. `godot --path game -- --original "D:\Games\GHG"`.</summary>
public static class Args
{
    public static string? Get(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == $"--{name}" && i + 1 < args.Length) return args[i + 1];
            if (a.StartsWith($"--{name}=", StringComparison.Ordinal)) return a[(name.Length + 3)..];
        }
        return null;
    }

    public static bool Has(string name) => Array.Exists(OS.GetCmdlineUserArgs(), a => a == $"--{name}" || a.StartsWith($"--{name}=", StringComparison.Ordinal));
}
