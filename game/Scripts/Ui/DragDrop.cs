using System;
using Godot;

namespace OpenGG.Ui;

/// <summary>
/// A Parts Bin slot. Pressing it picks the part up (see <c>App.StartCarry</c>): as in the original, the slot goes along
/// with the pointer until the button is let go over the car, REPAIR or SCRAP.
/// </summary>
public partial class BinSlot : Button
{
    public string Uid { get; set; } = "";
    public Texture2D? Thumb { get; set; }
    /// <summary>The left button went down on it, at this point of the slot.</summary>
    public Action<Vector2>? Grabbed { get; set; }

    public override void _GuiInput(InputEvent e)
    {
        if (Uid.Length > 0 && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) Grabbed?.Invoke(mb.Position);
    }
}

/// <summary>Covers the 3D view: mouse input for picking and camera.</summary>
public partial class ViewInput : Control
{
    public ViewInput()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        FocusMode = FocusModeEnum.None;
    }
}
