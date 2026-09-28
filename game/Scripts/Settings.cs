using System;
using System.IO;
using CoreJson = OpenGG.Core.Content.Json;

namespace OpenGG;

/// <summary>Per-user settings, stored next to the saves.</summary>
public sealed class Settings
{
    /// <summary>Folder of the player's own copy of the original game (null = use the bundled ai pack).</summary>
    public string? OriginalFolder { get; set; }
    /// <summary>Play with the original's content when the folder is set.</summary>
    public bool UseOriginal { get; set; } = true;
    /// <summary>Show the original's own screens, fonts, cursors, sounds and scenes when the folder is set (else ours, drawn
    /// in code).</summary>
    public bool UseOriginalLook { get; set; } = true;
    /// <summary>Folder of a skin (a skin.json with the screens, fonts, cursors and sounds to use), or null.</summary>
    public string? SkinFolder { get; set; }
    /// <summary>Full screen, else a window (F11 or Alt+Enter switches; see <see cref="ScreenMode"/>).</summary>
    public bool Fullscreen { get; set; }
    /// <summary>The 640 × 480 game at whole multiples of its size only (every pixel the same size, wider bars), else
    /// filling the window.</summary>
    public bool PixelPerfect { get; set; }
    /// <summary>The beta's note (one car model without the original's files) has been shown.</summary>
    public bool BetaNoteSeen { get; set; }

    static string FilePath => Path.Combine(Paths.User, "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? CoreJson.Parse<Settings>(File.ReadAllText(FilePath)) : new Settings();
        }
        catch (Exception)
        {
            return new Settings();
        }
    }

    public void Save() => File.WriteAllText(FilePath, CoreJson.Write(this, indented: true));
}
