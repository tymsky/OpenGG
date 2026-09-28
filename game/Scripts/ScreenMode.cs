using System;
using System.Linq;
using Godot;

namespace OpenGG;

/// <summary>
/// The window. The original ran full screen at 640 × 480 only; OpenGG opens in a window sized to the screen, or full
/// screen (F11 or Alt+Enter anywhere, or the sign-in sheet's SCREEN button; the choice is kept in the settings). The
/// 640 × 480 game is kept 4:3 with black bars. With pixel-perfect scaling it is shown at whole multiples of its size
/// only (every pixel the same size, wider bars); else it fills the window's height or width.
/// Command line: <c>--fullscreen</c> or <c>--windowed</c> for this run; with the engine's <c>--resolution</c> (the tours
/// and the lab) the window is left as given and the settings are not used.
/// </summary>
public partial class ScreenMode : Node
{
    public const int Width = 640, Height = 480;

    /// <summary>The window's mode or scaling changed (the pointer's size and the sign-in sheet's buttons follow).</summary>
    public static event Action? Changed;

    static Window Root => ((SceneTree)Engine.GetMainLoop()).Root;

    static bool started;

    /// <summary>A scripted run (the tours, the sound and parts checks, the lab) or one given a window size on the engine's
    /// command line (which the engine may not pass on): the window is left as it is, the settings not used.</summary>
    static bool ResolutionGiven =>
        Args.Has("autoshot") || Args.Has("soundcheck") || Args.Has("partscheck") || Args.Has("p2check") || Args.Has("playtest") || Args.Has("sheetshot") || Args.Has("lab") || Args.Has("export-scene")
        || OS.GetCmdlineArgs().Any(a => a == "--resolution" || a.StartsWith("--resolution=", StringComparison.Ordinal));

    /// <summary>A scripted run or one given a window size (see <see cref="ResolutionGiven"/>): no first-run notes.</summary>
    public static bool Scripted => ResolutionGiven;

    public static bool Fullscreen => Root.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    public static bool PixelPerfect => Root.ContentScaleStretch == Window.ContentScaleStretchEnum.Integer;

    /// <summary>How many screen pixels one of the game's pixels takes now.</summary>
    public static float Scale => Root.GetStretchTransform().Scale.X;

    /// <summary>At start: the window as the settings (or the command line) have it, and the keys that switch it.</summary>
    public static void Start()
    {
        if (started) return;
        started = true;
        var root = Root;
        root.CallDeferred(Node.MethodName.AddChild, new ScreenMode { Name = "ScreenMode" });
        var s = Settings.Load();
        bool given = ResolutionGiven;
        root.ContentScaleStretch = s.PixelPerfect && !given ? Window.ContentScaleStretchEnum.Integer : Window.ContentScaleStretchEnum.Fractional;
        bool full = Args.Has("fullscreen") || (!Args.Has("windowed") && !given && s.Fullscreen);
        if (full) root.Mode = Window.ModeEnum.Fullscreen;
        else if (!given) Callable.From(FitWindow).CallDeferred();
    }

    /// <summary>F11 or Alt+Enter: full screen or a window. Taken before anything else sees the keys (this node is the
    /// last of the tree, and the input goes from the last node to the first), so Enter confirms no box meanwhile.</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode != Key.F11 && !(k.Keycode is Key.Enter or Key.KpEnter && k.AltPressed)) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Full screen or a window, kept in the settings.</summary>
    public static void Toggle()
    {
        var root = Root;
        bool full = !Fullscreen;
        root.Mode = full ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        if (!full && !ResolutionGiven) Callable.From(FitWindow).CallDeferred();
        var s = Settings.Load();
        s.Fullscreen = full;
        s.Save();
        Changed?.Invoke();
    }

    /// <summary>Whole multiples of 640 × 480 only, or filling the window; kept in the settings.</summary>
    public static void SetPixelPerfect(bool on)
    {
        Root.ContentScaleStretch = on ? Window.ContentScaleStretchEnum.Integer : Window.ContentScaleStretchEnum.Fractional;
        var s = Settings.Load();
        s.PixelPerfect = on;
        s.Save();
        Changed?.Invoke();
    }

    /// <summary>The window as big as the screen allows (above the taskbar, its frame counted), in the middle of it: a
    /// whole multiple of 640 × 480 where one of at least 2 fits, else the largest 4:3 size that does.</summary>
    static void FitWindow()
    {
        var root = Root;
        if (root.Mode != Window.ModeEnum.Windowed) return;
        var usable = DisplayServer.ScreenGetUsableRect(root.CurrentScreen);
        var frame = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize();
        float k = Math.Min((usable.Size.X - frame.X) / (float)Width, (usable.Size.Y - frame.Y) / (float)Height);
        if (k >= 2) k = MathF.Floor(k);
        k = Math.Max(0.5f, k);
        root.Size = new Vector2I((int)MathF.Round(Width * k), (int)MathF.Round(Height * k));
        root.MoveToCenter();
        Changed?.Invoke();
    }

    /// <summary>
    /// What the screen shows of the game, without the bars around it: at the window's own resolution, or at the game's
    /// 640 × 480 (<paramref name="gameSize"/>: the Camera's snapshots, as the original's).
    /// </summary>
    public static Image GameImage(Viewport viewport, bool gameSize)
    {
        var img = viewport.GetTexture().GetImage();
        // The picture holds the whole window, the bars too, when it is the window's size.
        if (viewport is Window w && img.GetSize() == w.Size)
        {
            var rect = w.GetStretchTransform() * new Rect2(0, 0, Width, Height);
            var game = new Rect2I((Vector2I)rect.Position.Round(), (Vector2I)rect.Size.Round()).Intersection(new Rect2I(Vector2I.Zero, img.GetSize()));
            if (game.HasArea() && game.Size != img.GetSize()) img = img.GetRegion(game);
        }
        if (gameSize && img.GetSize() != new Vector2I(Width, Height)) img.Resize(Width, Height, Image.Interpolation.Lanczos);
        return img;
    }
}
