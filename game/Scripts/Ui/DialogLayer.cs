using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace OpenGG.Ui;

public readonly record struct DialogButton(string Label, string Value, bool Primary = false);

/// <summary>
/// Modal dialogs in the classic style: a yellow panel, a black title bar with a "!" (or a wrench), dark text
/// and black buttons with yellow lettering. Laid out in the 640 × 480 screen like the original's.
/// </summary>
public partial class DialogLayer : Control
{
    sealed class Open
    {
        public required Control Root;
        public required TaskCompletionSource<string> Done;
        public string Cancel = "cancel";
        public string? Primary;
        public bool QuietClose;
        public string? QuietAnswer;
    }

    readonly List<Open> stack = [];

    public DialogLayer() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>Plays a sound by id (the game's audio). Measured on the original: a box opening sounds "snd.dialog",
    /// one closed with its buttons "snd.dialog_close" (after the button's click).</summary>
    public Action<string>? Sound { get; set; }

    /// <summary>The sound of the next box to open instead of "snd.dialog" (the job boxes have their own); "" for none.</summary>
    public string? NextOpenSound { get; set; }

    /// <summary>The next box to open closes without its sound when answered this (it takes you to another screen).</summary>
    public string? NextQuietAnswer { get; set; }

    void Opened(Open open)
    {
        var sound = NextOpenSound ?? "snd.dialog";
        NextOpenSound = null;
        open.QuietAnswer = NextQuietAnswer;
        NextQuietAnswer = null;
        if (sound.Length > 0) Sound?.Invoke(sound);
    }

    public bool IsOpen => stack.Count > 0;

    /// <summary>Checks: the title of the box on top (null: none; "" for a panel without one).</summary>
    public string? TopTitle
    {
        get
        {
            if (stack.Count == 0) return null;
            var root = stack[^1].Root;
            var frame = root as DialogFrame ?? root.GetChildren().OfType<DialogFrame>().FirstOrDefault();
            return frame?.Title ?? "";
        }
    }

    /// <summary>With a skin: the original's dialog box, which always comes up in the same place.</summary>
    public static readonly Rect2 SkinBox = new(200, 135, 247, 171);

    /// <summary>A label in one of the skin's bitmap fonts (null without it).</summary>
    public static Label? SkinText(string fontId, string text, Rect2 r, HorizontalAlignment align = HorizontalAlignment.Center, VerticalAlignment valign = VerticalAlignment.Center, bool wrap = true)
    {
        if (UiSkin.Font(fontId) is not { } f) return null;
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = valign,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = r.Position,
            Size = r.Size,
            ClipText = !wrap,
        };
        l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", UiSkin.FontSize(f));
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeConstantOverride("line_spacing", 0);
        return l;
    }

    /// <summary>A plain dialog: title, centred text, buttons. Sized like the original's small boxes.</summary>
    public Task<string> Show(string title, string text, IReadOnlyList<DialogButton>? buttons = null)
    {
        buttons ??= [new DialogButton("OK", "ok", true)];
        if (UiSkin.Picture("dialog.frame") is { } framePic && SkinText("dialog", text, new Rect2(13, 30, 223, 96)) is { } words)
        {
            // Measured: the words are centred on the box (lines 16 pixels apart), the buttons 132 down: two at 11 and
            // 128, one at 71 (a pixel right of the middle).
            var box = new DialogFrame { Title = title, Picture = framePic, Position = SkinBox.Position, Size = SkinBox.Size };
            box.AddChild(words);
            return Present(box, buttons, new Vector2(0, 132), SkinBox.Size.X, 108, 9, firstX: buttons.Count == 1 ? 71 : null);
        }
        var f = Look.CondensedBold;
        const int size = 13;
        const float width = 240;
        var body = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        body.AddThemeFontOverride("font", f);
        body.AddThemeFontSizeOverride("font_size", size);
        body.AddThemeColorOverride("font_color", Look.DialogText);
        body.AddThemeConstantOverride("line_spacing", -1);
        // Height from the wrapped text, the original's boxes grow with it.
        var para = new TextParagraph { Width = width - 24 };
        para.AddString(text, f, size);
        float textH = Mathf.Max(34, para.GetSize().Y + 6);
        float h = 22 + 12 + textH + 10 + 28 + 10;
        var rect = new Rect2((640 - width) / 2, Mathf.Max(60, (480 - h) / 2 - 30), width, h);
        var panel = Frame(title, rect, wrench: false);
        body.Position = new Vector2(12, 22 + 10);
        body.Size = new Vector2(width - 24, textH);
        panel.AddChild(body);
        return Present(panel, buttons, new Vector2(0, h - 10 - 28), width);
    }

    /// <summary>A dialog with a custom body placed at a fixed rectangle (the job dialogs, the Decal Browser).</summary>
    public Task<string> ShowCustom(string title, Rect2 rect, Control body, IReadOnlyList<DialogButton> buttons, float buttonsY, bool wrench = false, float buttonWidth = 106)
    {
        var panel = Frame(title, rect, wrench);
        body.Position = new Vector2(0, 22);
        body.Size = new Vector2(rect.Size.X, rect.Size.Y - 22);
        panel.AddChild(body);
        return Present(panel, buttons, new Vector2(0, buttonsY), rect.Size.X, buttonWidth);
    }

    /// <summary>A dialog on a picture of the skin's (the job dialogs): the body goes over the whole picture,
    /// the buttons at their own places.</summary>
    public Task<string> ShowPicture(string title, Texture2D picture, Vector2 at, Control body, IReadOnlyList<DialogButton> buttons, Vector2[] buttonsAt)
    {
        var panel = new DialogFrame { Title = title, Picture = picture, Position = at, Size = picture.GetSize() };
        body.Position = Vector2.Zero;
        body.Size = panel.Size;
        panel.AddChild(body);
        var done = new TaskCompletionSource<string>();
        var back = new Control { MouseFilter = MouseFilterEnum.Stop };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        back.AddChild(panel);
        var open = new Open { Root = back, Done = done };
        for (int i = 0; i < buttons.Count; i++)
        {
            var value = buttons[i].Value;
            var btn = Button(buttons[i].Label, buttonsAt[Math.Min(i, buttonsAt.Length - 1)], 108);
            btn.Pressed += () => Close(open, value);
            panel.AddChild(btn);
            if (buttons[i].Primary) open.Primary = value;
        }
        open.Cancel = buttons.Count > 1 ? buttons[^1].Value : buttons[0].Value;
        stack.Add(open);
        AddChild(back);
        Opened(open);
        return done.Task;
    }

    /// <summary>A dialog that is all its own panel (the original's Decal Browser): its buttons answer through
    /// <paramref name="answer"/>; Esc answers <paramref name="cancel"/>, Enter <paramref name="primary"/>.</summary>
    public Task<string> ShowPanel(Control panel, string cancel, string? primary, out Action<string> answer)
    {
        var done = new TaskCompletionSource<string>();
        var back = new Control { MouseFilter = MouseFilterEnum.Stop };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        back.AddChild(panel);
        var open = new Open { Root = back, Done = done, Cancel = cancel, Primary = primary };
        answer = value => Close(open, value);
        stack.Add(open);
        AddChild(back);
        Opened(open);
        return done.Task;
    }

    /// <summary>A dialog button: the skin's picture of it when it has one, else ours.</summary>
    static DrawnButton Button(string label, Vector2 at, float width)
    {
        var key = "dialog." + label.ToLowerInvariant();
        if (UiSkin.Picture(key + ".up") is { } up)
            return new PictureButton { SkinUp = up, SkinDown = UiSkin.Picture(key + ".down"), Position = at, Size = up.GetSize() };
        return new BlackButton { Label = label, Position = at, Size = new Vector2(width, UiSkin.Active ? 30 : 28) };
    }

    Task<string> Present(Control panel, IReadOnlyList<DialogButton> buttons, Vector2 buttonsAt, float width, float buttonWidth = 104, float gap = 8, float? firstX = null)
    {
        var done = new TaskCompletionSource<string>();
        var back = new Control { MouseFilter = MouseFilterEnum.Stop };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        back.AddChild(panel);
        var open = new Open { Root = back, Done = done };
        float total = buttons.Count * buttonWidth + (buttons.Count - 1) * gap;
        float x = firstX ?? Mathf.Ceil((width - total) / 2);
        foreach (var b in buttons)
        {
            var value = b.Value;
            var btn = Button(b.Label, new Vector2(x, buttonsAt.Y), buttonWidth);
            btn.Pressed += () => Close(open, value);
            panel.AddChild(btn);
            x += buttonWidth + gap;
            if (b.Primary) open.Primary = value;
        }
        open.Cancel = buttons.Count > 1 ? buttons[^1].Value : buttons[0].Value;
        stack.Add(open);
        AddChild(back);
        Opened(open);
        return done.Task;
    }

    /// <summary>The yellow box and its title bar.</summary>
    static Control Frame(string title, Rect2 rect, bool wrench)
    {
        var panel = new DialogFrame { Title = title, Wrench = wrench, Position = rect.Position, Size = rect.Size };
        return panel;
    }

    /// <summary>Closes the dialog on top without an answer (the Decal Browser moves from its first page to the second).</summary>
    public void CloseTop()
    {
        if (stack.Count == 0) return;
        stack[^1].QuietClose = true;
        Close(stack[^1], stack[^1].Cancel);
    }

    /// <summary>Answers the top dialog with its main button (as Enter does).</summary>
    public void ConfirmTop()
    {
        if (stack.Count > 0 && stack[^1].Primary is { } p) Close(stack[^1], p);
    }

    void Close(Open open, string value)
    {
        if (!stack.Remove(open)) return;
        if (!open.QuietClose && value != open.QuietAnswer) Sound?.Invoke("snd.dialog_close");
        open.Root.QueueFree();
        open.Done.TrySetResult(value);
    }

    public override void _Input(InputEvent e)
    {
        if (stack.Count == 0 || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        var top = stack[^1];
        if (key.Keycode == Key.Escape)
        {
            Close(top, top.Cancel);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter && !key.AltPressed && top.Primary is { } p)
        {
            Close(top, p);
            GetViewport().SetInputAsHandled();
        }
    }

    public async Task Alert(string title, string text) => await Show(title, text, [new DialogButton("OK", "ok", true)]);

    public async Task<bool> Confirm(string title, string text, string ok = "OK", string cancel = "CANCEL") =>
        await Show(title, text, [new DialogButton(ok, "ok", true), new DialogButton(cancel, "cancel")]) == "ok";
}

/// <summary>The dialog box: yellow grit, a dark border, a black title bar with a "!" or a wrench.</summary>
public partial class DialogFrame : Control
{
    public string Title { get; set; } = "";
    public bool Wrench { get; set; }
    /// <summary>With a skin: the original's picture of the box (the title goes on its bar).</summary>
    public Texture2D? Picture { get; set; }

    public DialogFrame() => MouseFilter = MouseFilterEnum.Stop;

    public override void _Draw()
    {
        if (Picture is not null)
        {
            DrawTexture(Picture, Vector2.Zero);
            // Measured: the title is centred between the icon and the right edge (from 11, half the room left over,
            // rounded down: right to the pixel on the small box, the job box and the Impact Wrench box), its letters'
            // tops 11 down (the glyphs' pictures start 9 down).
            if (UiSkin.Font("dialogtitle") is { } tf)
            {
                int ts = UiSkin.FontSize(tf);
                float w = tf.GetStringSize(Title, HorizontalAlignment.Left, -1, ts).X;
                DrawString(tf, new Vector2(11 + Mathf.Floor((Size.X - 10 - w) / 2), 9 + tf.GetAscent(ts)), Title, HorizontalAlignment.Left, -1, ts);
            }
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r.Grow(2), new Color(0, 0, 0, 0.55f));
        DrawTextureRect(Look.YellowTexture, r, tile: true);
        DrawRect(r, Color.FromHtml("#3a2a08"), filled: false, width: 2);
        Bevel.Draw(this, r.Grow(-2), new Color(1, 1, 1, 0.3f), new Color(0, 0, 0, 0.35f));
        var bar = new Rect2(3, 3, Size.X - 6, 19);
        DrawRect(bar, Look.DialogTitle);
        if (Wrench)
        {
            // A little spanner, drawn with lines.
            var c = new Vector2(14, 12);
            DrawLine(c + new Vector2(-6, 6), c + new Vector2(4, -4), Color.FromHtml("#d8a431"), 3);
            DrawCircle(c + new Vector2(5, -5), 3.5f, Color.FromHtml("#d8a431"));
            DrawCircle(c + new Vector2(6, -6), 1.6f, Look.DialogTitle);
        }
        else
        {
            var c = new Vector2(14, 12.5f);
            DrawCircle(c, 7, Color.FromHtml("#d8a431"));
            DrawString(Look.Heavy, c + new Vector2(-2.5f, 4.5f), "!", HorizontalAlignment.Left, -1, 12, Colors.Black);
        }
        var f = Look.CondensedBold;
        const int size = 13;
        DrawString(f, new Vector2(0, bar.Position.Y + (bar.Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size)), Title, HorizontalAlignment.Center, Size.X, size, Look.DialogTitleText);
    }
}

/// <summary>A button that is only its pictures (the skin's dialog buttons).</summary>
public partial class PictureButton : DrawnButton
{
    /// <summary>Drawn down while set (the Decal Browser's chosen size).</summary>
    public bool Lit { get; set; }

    public override void _Draw() => DrawSkin(Down || Lit);

    /// <summary>A button cut out of a screen's raised and pressed pictures at <paramref name="r"/> (the pictures'
    /// own coordinates), placed there.</summary>
    public static PictureButton Cut(UiSkin.ScreenPictures screen, Rect2 r) => new()
    {
        SkinUp = screen.Up is { } up ? new AtlasTexture { Atlas = up, Region = r } : null,
        SkinDown = screen.Down is { } down ? new AtlasTexture { Atlas = down, Region = r } : null,
        Position = r.Position,
        Size = r.Size,
    };
}

/// <summary>A dialog button: a black plate with yellow lettering.</summary>
public partial class BlackButton : DrawnButton
{
    public string Label { get; set; } = "";
    public int FontSize { get; set; } = 13;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Down ? Color.FromHtml("#000000") : Hovered ? Color.FromHtml("#262626") : Color.FromHtml("#101010"));
        Bevel.Draw(this, r, Down ? new Color(0, 0, 0, 1) : new Color(1, 1, 1, 0.3f), Down ? new Color(1, 1, 1, 0.3f) : new Color(0, 0, 0, 1), 2);
        var f = Look.Heavy;
        var off = Down ? Vector2.One : Vector2.Zero;
        Text(Label, f, FontSize, new Vector2(0, Baseline(f, FontSize, Size.Y)) + off, Size.X, HorizontalAlignment.Center, Disabled ? Color.FromHtml("#6b5a2a") : Color.FromHtml("#e0b449"));
    }
}
