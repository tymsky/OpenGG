using Godot;

namespace OpenGG.Ui;

/// <summary>The sign-in sheet's BETA stamp, in both looks: red letters and the game's version in a double frame, tilted
/// like a rubber stamp. Drawn in code; it takes no clicks.</summary>
public partial class BetaStamp : Control
{
    public BetaStamp() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var ink = new Color(0.78f, 0.1f, 0.12f, 0.85f);
        string version = ProjectSettings.GetSetting("application/config/version").AsString();
        const int big = 20, small = 11, gap = 6;
        float wBeta = Look.Heavy.GetStringSize("BETA", HorizontalAlignment.Left, -1, big).X;
        float wVersion = Look.Plain.GetStringSize(version, HorizontalAlignment.Left, -1, small).X;
        var half = Size / 2;
        DrawSetTransform(half, Mathf.DegToRad(-4), Vector2.One);
        var frame = new Rect2(-half, Size);
        DrawRect(frame, ink, filled: false, width: 2.5f);
        DrawRect(frame.Grow(-4), ink, filled: false, width: 1);
        float x = -(wBeta + gap + wVersion) / 2;
        DrawString(Look.Heavy, new Vector2(x, 7), "BETA", HorizontalAlignment.Left, -1, big, ink);
        DrawString(Look.Plain, new Vector2(x + wBeta + gap, 7), version, HorizontalAlignment.Left, -1, small, ink);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
