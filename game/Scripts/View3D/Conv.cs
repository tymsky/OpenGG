using Godot;
using OpenGG.Core.Content;

namespace OpenGG.View3D;

/// <summary>Conversions between content math (Vec3, doubles) and Godot types.</summary>
public static class Conv
{
    public static Vector3 V(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static Vec3 V(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Content Euler angles (XYZ order, as in the ai pack): R = Rx * Ry * Rz.</summary>
    public static Basis EulerXyz(this Vec3 r) =>
        new Basis(Vector3.Right, (float)r.X) * new Basis(Vector3.Up, (float)r.Y) * new Basis(Vector3.Back, (float)r.Z);

    /// <summary>A rotation taking +Y onto <paramref name="dir"/>.</summary>
    public static Basis FromUp(Vector3 dir)
    {
        dir = dir.Normalized();
        if (dir.LengthSquared() < 1e-8f) return Basis.Identity;
        var axis = Vector3.Up.Cross(dir);
        float dot = Mathf.Clamp(Vector3.Up.Dot(dir), -1f, 1f);
        if (axis.LengthSquared() < 1e-8f) return dot > 0 ? Basis.Identity : new Basis(Vector3.Right, Mathf.Pi);
        return new Basis(axis.Normalized(), Mathf.Acos(dot));
    }

    public static Color Hex(string hex) => Color.FromHtml(hex);
}
