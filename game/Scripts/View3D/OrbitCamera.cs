using Godot;

namespace OpenGG.View3D;

/// <summary>A camera that orbits a target: drag to rotate, wheel to zoom, keys to step.</summary>
public sealed class OrbitCamera(Camera3D camera)
{
    public Camera3D Camera { get; } = camera;
    public Vector3 Target { get; set; }
    public float Yaw { get; set; } = 0.8f;
    public float Pitch { get; set; } = 0.35f;
    public float Distance { get; set; } = 6f;
    public float MinDistance { get; set; } = 0.35f;
    public float MaxDistance { get; set; } = 9f;
    public float MinPitch { get; set; } = -0.2f;
    public float MaxPitch { get; set; } = 1.45f;
    /// <summary>Lowest camera height (keeps it above the floor).</summary>
    public float MinHeight { get; set; } = 0.12f;
    /// <summary>How long an animated move takes.</summary>
    public float MoveSeconds { get; set; } = 0.6f;
    /// <summary>An animated move starts at full speed and slows into place (else it eases in and out).</summary>
    public bool EaseOut { get; set; }

    Vector3 fromTarget, toTarget, fromPos, toPos;
    float k = 1f;

    public Vector3 Position => Target + Offset(Yaw, Pitch, Distance);

    public static Vector3 Offset(float yaw, float pitch, float dist) =>
        new(Mathf.Cos(pitch) * Mathf.Sin(yaw) * dist, Mathf.Sin(pitch) * dist, Mathf.Cos(pitch) * Mathf.Cos(yaw) * dist);

    public void Rotate(float dYaw, float dPitch)
    {
        k = 1f;
        Yaw += dYaw;
        Pitch = Mathf.Clamp(Pitch + dPitch, MinPitch, MaxPitch);
    }

    public void Zoom(float factor)
    {
        k = 1f;
        Distance = Mathf.Clamp(Distance * factor, MinDistance, MaxDistance);
    }

    public void Pan(Vector2 delta)
    {
        k = 1f;
        var basis = Camera.GlobalTransform.Basis;
        float scale = Distance * 0.0015f;
        Target += (-basis.X * delta.X + basis.Y * delta.Y) * scale;
    }

    /// <summary>Look at <paramref name="target"/> from <paramref name="position"/>, optionally animated.</summary>
    public void MoveTo(Vector3 position, Vector3 target, bool animate)
    {
        if (!animate)
        {
            SetFrom(position, target);
            k = 1f;
            Apply();
            return;
        }
        fromPos = Position;
        fromTarget = Target;
        toPos = position;
        toTarget = target;
        k = 0f;
    }

    void SetFrom(Vector3 position, Vector3 target)
    {
        Target = target;
        var off = position - target;
        Distance = Mathf.Clamp(off.Length(), MinDistance, MaxDistance);
        Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(off.Y / Mathf.Max(off.Length(), 1e-4f), -1f, 1f)), MinPitch, MaxPitch);
        Yaw = Mathf.Atan2(off.X, off.Z);
    }

    public void Update(double dt)
    {
        if (k < 1f)
        {
            k = Mathf.Min(1f, k + (float)dt / MoveSeconds);
            float e = EaseOut ? 1 - Mathf.Pow(1 - k, 4) : k < 0.5f ? 2 * k * k : 1 - Mathf.Pow(-2 * k + 2, 2) / 2;
            var pos = fromPos.Lerp(toPos, e);
            var tgt = fromTarget.Lerp(toTarget, e);
            SetFrom(pos, tgt);
        }
        Apply();
    }

    void Apply()
    {
        var pos = Position;
        if (pos.Y < MinHeight) pos.Y = MinHeight;
        if (Camera.IsInsideTree()) Camera.LookAtFromPosition(pos, Target, Vector3.Up);
        else Camera.Transform = Transform3D.Identity.LookingAt(Target - pos, Vector3.Up).Translated(pos);
    }
}
