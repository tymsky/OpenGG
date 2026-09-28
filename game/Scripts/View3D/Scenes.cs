using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenGG.Ui;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;

namespace OpenGG.View3D;

/// <summary>A 3D place shown in the main view (Workshop, Auction, Car Lot).</summary>
public abstract partial class StageScene : Node3D
{
    public Camera3D Camera { get; } = new() { Fov = 45, Near = 0.03f, Far = 120 };
    public OrbitCamera Orbit { get; }
    protected AssetStore Assets { get; }

    protected StageScene(AssetStore assets)
    {
        Assets = assets;
        Orbit = new OrbitCamera(Camera);
        AddChild(Camera);
    }

    public override void _Process(double delta) => Orbit.Update(delta);

    protected Node3D? Place(string id, Vector3 pos, float rotY = 0)
    {
        if (Assets.Model(id) is not { } m) return null;
        var o = m.Instantiate();
        o.Position = pos;
        o.Rotation = new Vector3(0, rotY, 0);
        AddChild(o);
        return o;
    }

    protected StandardMaterial3D TexturedMat(string texId, Vector2 repeat, float roughness)
    {
        var mat = new StandardMaterial3D { Roughness = roughness, Metallic = 0.02f };
        if (Assets.Texture(texId) is { } tex)
        {
            mat.AlbedoTexture = tex;
            mat.Uv1Scale = new Vector3(repeat.X, repeat.Y, 1);
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
        return mat;
    }

    protected static WorldEnvironment MakeEnvironment(Color background, Color ambient, float ambientEnergy, bool sky, float fogDensity)
    {
        if (ModernLook.On) return new WorldEnvironment { Environment = ModernLook.Outdoor(background.Lightened(0.35f), fogDensity) };
        var env = new Godot.Environment
        {
            BackgroundMode = sky ? Godot.Environment.BGMode.Sky : Godot.Environment.BGMode.Color,
            BackgroundColor = background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambient,
            AmbientLightEnergy = ambientEnergy,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.4f,
            FogEnabled = fogDensity > 0,
            FogLightColor = background,
            FogDensity = fogDensity,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.32f, 0.55f, 0.82f),
                    SkyHorizonColor = new Color(0.72f, 0.8f, 0.88f),
                    GroundBottomColor = new Color(0.12f, 0.12f, 0.12f),
                    GroundHorizonColor = new Color(0.45f, 0.45f, 0.43f),
                },
            },
        };
        return new WorldEnvironment { Environment = env };
    }
}

/// <summary>
/// How the original's WorkShop camera moves, fitted to three sweeps of its views (about 75 screenshots of two
/// cars, from the front and from the side, from straight on to the ends of the tilt). The Up and Down keys turn
/// the view's pitch θ (to ±70°); the eye moves on a circle in its vertical plane around a point behind the
/// region's centre, by an angle that lags θ by up to 11.7°. So a tilted view looks a little past the centre
/// (the car shows higher when seen from above, lower from below) and the eye backs off as it climbs.
/// </summary>
public static class WorkshopRig
{
    /// <summary>The circle's centre behind the region's centre, and its radius, in units of D0.</summary>
    public const float Behind = 0.2499f, Radius = 1.1812f;
    /// <summary>The eye's angle on the circle is θ − Lead·x/(1 + x⁴)^¼, x = θ / Knee.</summary>
    public static readonly float Lead = Mathf.DegToRad(11.66f), Knee = Mathf.DegToRad(24.74f);
    /// <summary>How far the view tilts either way (the limits seen were 66–69.5°).</summary>
    public static readonly float MaxPitch = Mathf.DegToRad(70);

    /// <summary>The eye for a view at yaw/pitch of a region centred at <paramref name="centre"/> whose level view
    /// is <paramref name="level"/> away from it.</summary>
    public static Vector3 Eye(float yaw, float pitch, Vector3 centre, float level)
    {
        float d0 = level / (Radius - Behind);
        var back = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        float x = pitch / Knee;
        float phi = pitch - Lead * x / Mathf.Pow(1 + x * x * x * x, 0.25f);
        return centre - back * (Behind * d0) + (back * Mathf.Cos(phi) + Vector3.Up * Mathf.Sin(phi)) * (Radius * d0);
    }
}

/// <summary>The Workshop: the car alone on a plain background, as in the original.</summary>
public partial class WorkshopScene : StageScene
{
    public const float LiftY = 0.95f;

    readonly Node3D carRoot = new() { Name = "CarRoot", Position = new Vector3(0, LiftY, 0) };
    public VehicleView? View { get; private set; }
    public View Region { get; private set; } = Core.Content.View.Complete;
    Game game;

    public WorkshopScene(AssetStore assets, Game game) : base(assets)
    {
        this.game = game;
        Name = "Workshop";
        Orbit.MinDistance = 0.35f;
        Orbit.MaxDistance = 100f;
        // Nothing is under the car: the eye goes down to look at the underside.
        Orbit.MinHeight = -1000;
        Orbit.MinPitch = -WorkshopRig.MaxPitch;
        Orbit.MaxPitch = WorkshopRig.MaxPitch;
        // As in the original (fitted to its screenshots): the first car is seen straight from the front, the
        // camera level with the car's middle. The angle then stays as you leave it, across tabs and cars; each
        // tab only re-centres and zooms.
        Orbit.Yaw = Mathf.Pi / 2;
        Orbit.Pitch = 0f;
        Orbit.Target = new Vector3(0, LiftY + 0.5f, 0);
        // Measured (films at 60 a second): every move of the original's view (another tab, onto a part for its bolts,
        // back out, framed again as a part comes off or goes on) is over in about four frames, most of it in the first
        // (0.64-0.68 of the way, then 0.91-0.94, 0.99). Our own look glides.
        if (!ModernLook.On)
        {
            Orbit.MoveSeconds = 4f / 60;
            Orbit.EaseOut = true;
        }
    }

    /// <summary>The region the view is framed on (its centre and the level view's distance), while it is.</summary>
    Vector3 frameCentre;
    float frameLevel;
    bool framed;
    /// <summary>Our mouse-wheel zoom on top of the original's framing.</summary>
    float zoom = 1;

    /// <summary>Lab: the region's centre and the level view's distance the view is framed on.</summary>
    public (Vector3 Centre, float Level) Framing => (frameCentre, frameLevel);

    public Game Game
    {
        get => game;
        set => game = value;
    }

    public override void _Ready()
    {
        if (ModernLook.On)
        {
            // OpenGG's own look: the same plain grey, a studio's light and shadows (see ModernLook).
            studio = ModernLook.Studio(new Color(0.29f, 0.286f, 0.29f));
            AddChild(new WorldEnvironment { Environment = studio });
            ModernLook.AddLights(Camera);
            AddChild(ModernLook.ShadowCatcher(LiftY));
            AddChild(carRoot);
            UpdateContact();
            return;
        }
        // As in the original: the car alone on a plain grey background, lit from the front.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.29f, 0.286f, 0.29f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.85f, 0.88f),
            AmbientLightEnergy = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.98f, 0.94f),
            LightEnergy = 1.05f,
            ShadowEnabled = false,
            Transform = Transform3D.Identity.LookingAt(new Vector3(-3, -6, -4), Vector3.Up).Translated(new Vector3(3, 6, 4)),
        });
        AddChild(new DirectionalLight3D
        {
            LightColor = new Color(0.8f, 0.84f, 0.9f),
            LightEnergy = 0.35f,
            ShadowEnabled = false,
            Transform = Transform3D.Identity.LookingAt(new Vector3(4, -2, 3), Vector3.Up).Translated(new Vector3(-4, 2, -3)),
        });
        AddChild(carRoot);
    }

    // ---- the engine running --------------------------------------------------------------------------

    double engineRun, shakeNext;
    int shakeAcross = 2;
    readonly RandomNumberGenerator shake = new();

    /// <summary>The WorkShop view's height on the 640 × 480 screen, for the shake in its pixels.</summary>
    const float ViewPixels = 257;

    /// <summary>
    /// The engine runs for <paramref name="seconds"/>. Measured on the original's frames of a start: the whole engine
    /// shook, now and then a pixel or two off its place (up to two across, one up or down; about half the frames
    /// still), while its crank turned; it all stopped before the start's sound had ended. An engine cranking without
    /// catching shakes a pixel either way at most, its fan turning, for as long as the crank's sound (a film of an F350).
    /// </summary>
    public void RunEngine(double seconds, int across = 2)
    {
        engineRun = seconds;
        shakeNext = 0;
        shakeAcross = across;
    }

    public void StopEngine()
    {
        engineRun = 0;
        Camera.HOffset = 0;
        Camera.VOffset = 0;
        View?.StopSpin();
    }

    /// <summary>Our own look's studio, turned with the view.</summary>
    Godot.Environment? studio;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (studio is not null)
        {
            ModernLook.TurnStudio(studio, Camera);
            UpdateWheelShadows();
        }
        if (showIn >= 0 && (showIn -= delta) < 0) StartSpin();
        if (spinning) Spin(delta);
        if (engineRun <= 0) return;
        engineRun -= delta;
        if (engineRun <= 0 || View is null)
        {
            StopEngine();
            return;
        }
        View.Spin(delta);
        shakeNext -= delta;
        if (shakeNext > 0) return;
        shakeNext = 1 / 30.0;
        // One of the view's pixels at the distance of what the view is framed on.
        float px = 2 * Camera.GlobalPosition.DistanceTo(frameCentre) * Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2) / ViewPixels;
        bool still = shake.Randf() < 0.5f;
        Camera.HOffset = still ? 0 : shake.RandiRange(-shakeAcross, shakeAcross) * px;
        Camera.VOffset = still ? 0 : shake.RandiRange(-1, 1) * px;
    }

    /// <summary>Lab: one model on its own where the car stands (the car hidden), its middle over the lift; the model's
    /// middle in the world, for the camera to look at.</summary>
    public Vector3 ShowModel(string id)
    {
        if (View is { } v) v.Visible = false;
        carRoot.GetNodeOrNull("LabModel")?.Free();
        if (Assets.Model(id) is not { } m) return carRoot.GlobalPosition;
        var node = m.Instantiate(shadows: true);
        node.Name = "LabModel";
        node.Position = new Vector3(-m.Bounds.GetCenter().X, -m.Bounds.Position.Y, -m.Bounds.GetCenter().Z);
        carRoot.AddChild(node);
        return carRoot.GlobalPosition + new Vector3(0, m.Bounds.Size.Y / 2, 0);
    }

    // ---- a finished job's car on show -------------------------------------------------------------------

    /// <summary>The finished job's car (no longer in the game's state), shown until the WorkShop is cleared.</summary>
    VehicleView? finished;
    /// <summary>Time left before the view goes to COMPLETE and starts circling (negative: not waiting).</summary>
    double showIn = -1;
    bool spinning;
    double spinTime;

    /// <summary>How far above level the view circles a finished car (fitted on four films of the original: 17.8–18.0°).</summary>
    public static readonly float ShowPitch = Mathf.DegToRad(17.9f);
    /// <summary>How fast it circles: as the arrow keys turn the view (fitted: 4.99–5.04 radians a second).</summary>
    const float SpinRate = 5.0f;
    /// <summary>The turn it makes on top of that as it starts, fading in about a tenth of a second (fitted: 71° and 72°
    /// after a part was dropped on the car, 38° after the last bolt of bolt mode).</summary>
    static readonly float SpinKick = Mathf.DegToRad(60), SpinKickTime = 0.1f;

    /// <summary>
    /// A job's car is finished. Measured on four films of the original's job ends: the car stays as it is for a tenth of
    /// a second (the last part on it), then the view goes to COMPLETE at a set height, whatever it was, and circles the
    /// car as if it turned on the spot, until <see cref="StopShowcase"/> (Job Complete! comes up). It is the view's own
    /// angle: the next car comes in seen from where it stopped. The car does not drive off.
    /// </summary>
    public void Showcase(double delay = 0.1)
    {
        if (View is not { } v) return;
        View = null;
        finished = v;
        showIn = delay;
        spinning = false;
    }

    void StartSpin()
    {
        showIn = -1;
        if (finished is not { } v || !IsInstanceValid(v)) return;
        Region = Core.Content.View.Complete;
        v.SetView(Region);
        if (!FrameOn(v, animate: false)) return;
        Orbit.Pitch = ShowPitch;
        spinning = true;
        spinTime = 0;
        Place(animate: false);
    }

    void Spin(double delta)
    {
        float t0 = (float)spinTime;
        spinTime += delta;
        float t1 = (float)spinTime;
        // The view turns as the arrow keys turn it, with a quicker start (the kick fades as e^(-t/τ)).
        float kick = SpinKick * (Mathf.Exp(-t0 / SpinKickTime) - Mathf.Exp(-t1 / SpinKickTime));
        Orbit.Yaw += SpinRate * (float)delta + kick;
        Place(animate: false);
    }

    /// <summary>The finished car stays where the view stopped (measured: under Job Complete! and the boxes after it).</summary>
    public void StopShowcase()
    {
        showIn = -1;
        spinning = false;
    }

    /// <summary>The finished car is gone: the WorkShop is cleared.</summary>
    public void EndShowcase()
    {
        StopShowcase();
        if (finished is { } v && IsInstanceValid(v)) v.QueueFree();
        finished = null;
    }

    /// <summary>A finished job's car is on show (the view is its own meanwhile).</summary>
    public bool ShowcaseOn => finished is not null;

    public void SetVehicle(string? id)
    {
        if (View?.VehicleId == id && View is not null) return;
        StopEngine();
        View?.QueueFree();
        View = null;
        if (id is null || !game.State.Vehicles.ContainsKey(id)) return;
        var view = new VehicleView(game, Assets, id, full: true);
        View = view;
        carRoot.AddChild(view);
        SetRegion(Region, animate: false);
    }

    public void SetRegion(View region, bool animate = true)
    {
        Region = region;
        View?.SetView(region);
        Frame(animate);
        UpdateContact();
    }

    /// <summary>Our own look: the car's soft shadow where it stands, going with it (a finished job's car keeps it); lighter
    /// under the running gear alone (no body over it), none under the ENGINE tab's engine, shown on its own up in the air.</summary>
    void UpdateContact()
    {
        if (!ModernLook.On || View is not { } v || !v.IsInsideTree()) return;
        var shadow = v.GetNodeOrNull<MeshInstance3D>("ContactShadow");
        if (shadow is null)
        {
            if (v.Bounds(Core.Content.View.Complete) is not { } box) return;
            shadow = ModernLook.ContactShadow(box.Size.X, box.Size.Z);
            v.AddChild(shadow);
            shadow.GlobalPosition = new Vector3(box.GetCenter().X, LiftY + 0.004f, box.GetCenter().Z);
        }
        shadow.Visible = Region != Core.Content.View.Engine;
        ModernLook.SetStrength(shadow, Region == Core.Content.View.RunningGear ? 0.22f : 0.5f);
    }

    /// <summary>Our own look: a small dark patch under each wheel standing on the floor, following it as it comes off.</summary>
    readonly Dictionary<string, MeshInstance3D> wheelShadows = [];

    void UpdateWheelShadows()
    {
        var car = View ?? finished;
        var seen = new HashSet<string>();
        if (car is not null && IsInstanceValid(car))
            foreach (var (id, box) in car.ShownWheels())
            {
                // Only a wheel down on the floor (not one held up in the air).
                float lift = box.Position.Y - LiftY;
                if (lift > 0.08f) continue;
                seen.Add(id);
                if (!wheelShadows.TryGetValue(id, out var s))
                {
                    // The tyre's width across, a third of its height along the car.
                    s = ModernLook.ContactShadow(box.Size.Y * 0.36f, Mathf.Min(box.Size.X, box.Size.Z) * 0.9f, 0.6f, 0.1f);
                    wheelShadows[id] = s;
                    AddChild(s);
                }
                s.Visible = true;
                s.GlobalPosition = new Vector3(box.GetCenter().X, LiftY + 0.006f, box.GetCenter().Z);
                s.Rotation = new Vector3(0, box.Size.X < box.Size.Z ? Mathf.Pi / 2 : 0, 0);
                ModernLook.SetStrength(s, 0.6f * (1 - Mathf.Clamp(lift / 0.08f, 0, 1)));
            }
        foreach (var (id, s) in wheelShadows)
            if (!seen.Contains(id)) s.Visible = false;
    }

    /// <summary>Point the camera at what the current tab is about.</summary>
    public void Frame(bool animate = true)
    {
        if (View is not null) FrameOn(View, animate);
    }

    bool FrameOn(VehicleView view, bool animate)
    {
        if (!IsInsideTree() || !view.IsInsideTree() || view.Bounds(Region) is not { } box) return false;
        float size = box.Size.Length();
        // Keep the angle, re-centre on the tab's region and zoom to it (the engine fills the view). Fitted to the
        // original's screenshots: level, the eye is 1.077 times the region's diagonal away (1.069–1.084 for the
        // three cars fitted; RUNNING GEAR the same, an F350's running gear from the front within 2–4 pixels of the
        // original's; ENGINE 0.92).
        frameCentre = box.GetCenter();
        frameLevel = Mathf.Max(1.1f, size * (Region switch { Core.Content.View.Engine => 0.92f, _ => 1.077f }));
        framed = true;
        Place(animate);
        return true;
    }

    /// <summary>The camera where the original's rig puts it for the current angles (<see cref="WorkshopRig"/>).</summary>
    void Place(bool animate)
    {
        float level = frameLevel * zoom;
        var eye = WorkshopRig.Eye(Orbit.Yaw, Orbit.Pitch, frameCentre, level);
        // The orbit camera looks from the eye along the view's angles (at a point that far down the line of sight).
        var target = eye - OrbitCamera.Offset(Orbit.Yaw, Orbit.Pitch, level);
        Orbit.MoveTo(eye, target, animate);
    }

    /// <summary>Turns the view (the arrow keys, our right-button drag): on the rig while framed on a region,
    /// round the part in bolt mode.</summary>
    public void Turn(float dYaw, float dPitch)
    {
        Orbit.Rotate(dYaw, dPitch);
        if (framed) Place(animate: false);
    }

    /// <summary>Our mouse wheel: closer or further, the original's framing kept.</summary>
    public void Zoom(float factor)
    {
        if (!framed)
        {
            Orbit.Zoom(factor);
            return;
        }
        zoom = Mathf.Clamp(zoom * factor, 0.25f, 2.5f);
        Place(animate: false);
    }

    /// <summary>How far bolt mode's camera stands from the part's bolts, in the part's longest side (fitted to the
    /// original's wheels, seen face on and edge on).</summary>
    public const float BoltZoom = 1.47f;

    /// <summary>Bolt mode: the view onto the part. True when that brings the camera in closer (a zoom).</summary>
    public bool FocusSlot(string slotId)
    {
        if (View is null) return false;
        // Measured: an engine part's bolts are done where the view is; the view does not move at all.
        if (View.SlotRegion(slotId) == Core.Content.Region.Engine) return false;
        // Measured: elsewhere (the running gear) the view keeps its angle and comes in on the middle of the part's
        // bolts, not of the part (a wheel seen from the front showed off to the side, its bolts in the middle), from
        // about 1.4 times the part's longest side; turning then goes round them.
        framed = false;
        var target = View.BoltCentre(slotId) ?? View.SlotCenter(slotId);
        float size = View.SlotBounds(slotId) is { } box ? Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)) : 1f;
        float dist = Mathf.Max(BoltZoom * size, Orbit.MinDistance);
        var dir = OrbitCamera.Offset(Orbit.Yaw, Orbit.Pitch, 1);
        bool closer = dist < Orbit.Distance - 0.05f;
        Orbit.MoveTo(target + dir * dist, target, animate: true);
        return closer;
    }

    /// <summary>What is under the cursor, limited to what can be worked on in the current tab.</summary>
    public Hit? Pick(Vector2 viewportPos)
    {
        if (View is null) return null;
        var origin = Camera.ProjectRayOrigin(viewportPos);
        var dir = Camera.ProjectRayNormal(viewportPos);
        var hit = Picker.Cast(View.Targets.Where(View.Pickable), origin, dir);
        if (hit is not { } h) return null;
        switch (h.Pick)
        {
            case BodyPick:
                return Region is Core.Content.View.Body or Core.Content.View.Complete ? h : null;
            // In COMPLETE the parts are picked for their region (the original lights it and goes to its tab).
            case PartPick p when !View.Interactive(p.SlotId) && Region != Core.Content.View.Complete:
            case FastenerPick f when !View.Interactive(f.SlotId):
                return null; // parts of other regions still hide what is behind them
            default:
                return h;
        }
    }

    /// <summary>The point of the car's paint picture under the pointer (Body Paint), with its panel's square and the
    /// place of the part it is on (null on the car's fixed body).</summary>
    public (Vector2 Uv, Rect2 Cell, string? SlotId)? PaintPoint(Vector2 viewportPos)
    {
        if (View is null || Pick(viewportPos) is not { } h) return null;
        Node3D? node = h.Pick switch
        {
            PartPick p => View.ModelOf(p.SlotId),
            _ => null,
        };
        var origin = Camera.ProjectRayOrigin(viewportPos);
        var dir = Camera.ProjectRayNormal(viewportPos);
        if (node is not null)
            return Picker.PaintUv(node, origin, dir, View.PaintMaterialName, h.Distance) is { } pr ? (pr.Uv, pr.Cell, ((PartPick)h.Pick).SlotId) : null;
        if (h.Pick is BodyPick)
            foreach (var b in View.BodyNodes())
                if (Picker.PaintUv(b, origin, dir, View.PaintMaterialName, h.Distance) is { } r) return (r.Uv, r.Cell, null);
        return null;
    }

    /// <summary>
    /// A decal's pixels laid on the car the original's way (measured): for each point of the view, the point of the car's
    /// paint picture on the surface seen there, that surface's square of the picture, and the place of the part it is on
    /// (null: the car's fixed body). Only
    /// what is seen first counts, and only a body panel's painted surface; elsewhere (another region's part, a surface
    /// the paint picture doesn't cover, the background) nothing. The rays share one reading of the car.
    /// </summary>
    public List<(int Index, Vector2 Uv, Rect2 Cell, string? SlotId)> PaintTexels(IReadOnlyList<Vector2> points)
    {
        var result = new List<(int, Vector2, Rect2, string?)>();
        if (View is not { } view || Region != Core.Content.View.Body) return result;
        var batch = new RayBatch(view.Targets);
        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            if (batch.Cast(Camera.ProjectRayOrigin(pt), Camera.ProjectRayNormal(pt)) is not { Mesh: { } mesh } h) continue;
            string? slot;
            switch (h.Pick)
            {
                case BodyPick:
                    slot = null;
                    break;
                case PartPick pp when view.Interactive(pp.SlotId):
                    slot = pp.SlotId;
                    break;
                default:
                    continue;
            }
            if (Picker.PaintUvAt(mesh, h.Face, h.Wb, h.Wc, view.PaintMaterialName) is not { } uv) continue;
            result.Add((i, uv.Uv, uv.Cell, slot));
        }
        return result;
    }

    /// <summary>Car-local position and normal of a hit (for decals).</summary>
    public (Vec3 Pos, Vec3 Normal) ToCarSpace(Hit h)
    {
        var inv = View!.CarTransform.AffineInverse();
        return ((inv * h.Point).V(), (inv.Basis * h.Normal).Normalized().V());
    }

    /// <summary>"Use arrow keys to rotate view": held keys turn the view (measured: about 270–300° a second,
    /// stopping the moment the key is let go).</summary>
    public void RotateHeld(float yaw, float pitch, double dt)
    {
        const float rate = 5f;
        if (yaw != 0 || pitch != 0) Turn(yaw * rate * (float)dt, pitch * rate * (float)dt);
    }
}

/// <summary>One car on display: the Auction stage or the Car Lot.</summary>
public partial class ShowroomScene : StageScene
{
    public enum Kind { Auction, Lot }

    readonly Kind kind;
    VehicleView? view;
    float carY;
    double spin;
    public Game Game { get; set; }

    public ShowroomScene(AssetStore assets, Game game, Kind kind) : base(assets)
    {
        Game = game;
        this.kind = kind;
        Name = kind.ToString();
        Camera.Fov = 42;
        Orbit.MinDistance = 3;
        Orbit.MaxDistance = 16;
        Orbit.MinPitch = 0.02f;
        Orbit.MaxPitch = 1.35f;
    }

    /// <summary>The skin's scene, when it has one: the car goes where the scene marks it.</summary>
    Model3ds? original;
    Vector3 carAt;
    /// <summary>The Car Lot's bays (the skin's car_01 ..., else our own row), in the world.</summary>
    readonly List<Vector3> bays = [];
    /// <summary>Where the camera stands and what it looks at in front of each bay.</summary>
    readonly List<(Vector3 Eye, Vector3 Look)> lotViews = [];
    /// <summary>Your parked cars, by bay.</summary>
    readonly List<VehicleView> parked = [];

    /// <summary>
    /// How fast the Car Lot's camera glides along the lane while ◄ or ► is held, in bays a second. Measured holding ◄:
    /// the figures and the marker went on to the next car every 1.00 to 1.10 s, the original drawing the lot some 57 to
    /// 60 times a second. It moves by so much a frame: the earlier clicks of about 80 ms (a thirtieth of a bay each, the
    /// second car's figures at the twelfth) fit the same step at half that frame rate.
    /// </summary>
    public const float GlideSpeed = 0.95f;

    /// <summary>What the shadow under a parked car leaves of the asphalt's brightness (measured on four captures: 0.46).</summary>
    public const float LotShadow = 0.46f;

    /// <summary>Where the camera is along the lane, in bays from the first (0: in front of the first bay).</summary>
    public float LotPos { get; private set; }

    /// <summary>The camera goes from the first bay to the last, cars parked there or not (measured: the empty lot's
    /// camera walked along the lane with ◄ held).</summary>
    float LotEnd => Mathf.Max(0, lotViews.Count - 1);

    /// <summary>From a 3ds scene (units, Z up) to ours (metres, Y up).</summary>
    public static Vector3 FromScene(Vector3 v) => new Vector3(v.X, v.Z, -v.Y) * (float)Core.Original.OrigSpace.MetersPerUnit;

    /// <summary>
    /// The original's scene: its meshes, its light, its camera (the view is fixed), and the car where the
    /// scene's BoundBox is. Helper objects (the box, cameras, car places) are not drawn.
    /// </summary>
    bool BuildOriginal()
    {
        var id = kind == Kind.Auction ? "auction" : "lot";
        if (UiSkin.Scene(id) is not { } m || m.Cameras.Count == 0) return false;
        original = m;
        static bool Helper(string n) => n.StartsWith("BoundBox", System.StringComparison.OrdinalIgnoreCase) || n.StartsWith("play_", System.StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("car_", System.StringComparison.OrdinalIgnoreCase) || n.StartsWith("CamFocus", System.StringComparison.OrdinalIgnoreCase);
        AddChild(m.Build(FromScene, name => UiSkin.SceneTexture(id, name), mesh => !Helper(mesh.Name), light: kind == Kind.Lot ? LotLight : AuctionLight));
        // The picture behind (sky) is drawn by the screen; the 3D clears to nothing.
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            },
        });
        foreach (var l in m.Lights)
        {
            var from = FromScene(l.Position);
            AddChild(new DirectionalLight3D { LightColor = l.Color, LightEnergy = 0.9f, Transform = Transform3D.Identity.LookingAt(-from, Vector3.Up) });
        }
        var cam = m.Cameras[0];
        Orbit.MinDistance = 0.01f;
        Orbit.MaxDistance = 1000;
        Orbit.MinPitch = -1.5f;
        Orbit.MaxPitch = 1.5f;
        Orbit.MinHeight = -100;
        // The original's engine keeps its own field of view, not the scene's lens: 45° tall everywhere (set below for
        // the Auction and the Car Lot).
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Width;
        Camera.Fov = 63;
        Camera.Far = 400;
        Orbit.MoveTo(FromScene(cam.Position), FromScene(cam.Target), animate: false);
        if (m.Find("BoundBox") is { } box)
        {
            var b = box.Bounds();
            carAt = FromScene(new Vector3(b.GetCenter().X, b.GetCenter().Y, b.Position.Z));
        }
        if (kind == Kind.Auction)
        {
            // Fitted to captures of three cars on the original's stage by their outlines and the stage floor's edges
            // (the scene's Camera02 is not what it shows): the view as tall as the whole Auction view (the screen's
            // stage front covers its lower part) and 45° from top to bottom, as everywhere in the original's engine;
            // looking straight along the stage (its edges come out level) and a little down. Every car stands with its
            // own origin on one spot a little left of and in front of the BoundBox's (fitted per car, the three spots
            // agreed within 1.5 cm across; the cars' box middles do not line up).
            Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            Camera.Fov = 45;
            var look = new Vector3(0, Mathf.Sin(AuctionPitch), -Mathf.Cos(AuctionPitch));
            Orbit.MoveTo(AuctionEye, AuctionEye + look * 4, animate: false);
            carAt += AuctionCarShift;
        }
        // The lot: its bays in order and the ground under them. A car stands at its bay marker's own origin, not at the
        // marker's middle (measured: with it every marker came within a pixel of the original's, over fourteen captures).
        var ground = m.Find("Floor")?.Bounds().Position.Z ?? 0;
        var sceneBays = new List<Vector3>();
        for (int i = 1; m.Find($"car_{i:00}") is { } c; i++)
        {
            var at = c.Matrix?.Origin ?? c.Bounds().GetCenter();
            sceneBays.Add(new Vector3(at.X, at.Y, ground));
        }
        if (sceneBays.Count > 0)
        {
            // The camera, fitted to the original's empty lot (its bay lines and brick wall) and checked on fourteen
            // captures taken as it drove along the lane: above the path's start (play_01), a little below its middle,
            // looking a fixed way (off the path towards the bays and a little down); it drives along the lane without
            // turning. The view is 45° tall, as in the WorkShop (so 67.5° across the lot's wider view).
            var start = m.Find("play_01") is { } p1 ? p1.Bounds().GetCenter() : sceneBays[0] + new Vector3(0, 0, 100);
            var look = new Vector3(Mathf.Cos(LotPitch) * Mathf.Cos(LotYaw), Mathf.Cos(LotPitch) * Mathf.Sin(LotYaw), Mathf.Sin(LotPitch));
            foreach (var b in sceneBays)
            {
                bays.Add(FromScene(b));
                var eye = new Vector3(start.X, b.Y + (start.Y - sceneBays[0].Y), start.Z + LotEyeDrop);
                lotViews.Add((FromScene(eye), FromScene(eye + look * 500)));
            }
            Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            Camera.Fov = 45;
            PlaceLotCamera();
        }
        return true;
    }

    /// <summary>The Auction's stage floor comes out at its texture's own colour numbers (measured: 0.99 to 1.0 of them).</summary>
    static readonly Model3ds.SceneLight AuctionLight = new(1f, 0f, Vector3.Up);

    /// <summary>The Auction's camera with the original's scene (the world, metres): the eye, and how far it looks down.</summary>
    static readonly Vector3 AuctionEye = new(0.3738f, 1.1299f, 3.7616f);
    const float AuctionPitch = -0.1377f;

    /// <summary>Where a car's origin stands on the Auction's stage, from the BoundBox's spot (metres).</summary>
    static readonly Vector3 AuctionCarShift = new(-0.121f, 0, 0.184f);

    /// <summary>The Car Lot's camera in the original's scene (units, Z up): its height against play_01's middle, and
    /// which way it looks (turned from the path towards the bays, and down). Fitted by the bay lines and the wall.</summary>
    const float LotEyeDrop = -7.0f, LotYaw = 0.4846f, LotPitch = -0.1426f;

    /// <summary>
    /// How the original lights the Car Lot's scene, fitted to its empty lot surface by surface (the asphalt, the lines,
    /// the wall, the sign, the houses' walls and roofs, a post): ambient 0.585 and a light of 0.588 from the side of
    /// the first bays, level with the ground (so the ground and the walls facing the lane get only the ambient, and the
    /// houses' walls facing the first bays come out at their full colour). The scene's own lamp (Omni01) is not it.
    /// </summary>
    static readonly Model3ds.SceneLight LotLight = new(0.585f, 0.588f, new Vector3(0.2402f, 0.0087f, 0.9707f));

    public override void _Ready()
    {
        if (BuildOriginal()) return;
        AddChild(MakeEnvironment(new Color(0.56f, 0.75f, 0.91f), new Color(0.8f, 0.88f, 1f), 0.8f, sky: true, fogDensity: 0.004f));
        var sun = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.95f, 0.86f),
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 30,
            Transform = Transform3D.Identity.LookingAt(new Vector3(-8, -14, -9), Vector3.Up).Translated(new Vector3(8, 14, 9)),
        };
        if (ModernLook.On) ModernLook.Soften(sun);
        AddChild(sun);
        if (kind == Kind.Auction)
        {
            // Our own look: the lawn out to the haze.
            float lawnSize = ModernLook.On ? 1200 : 200;
            var lawn = (ModernLook.On ? ModernLook.Surface("Grass004", 1.6f, new Vector2(lawnSize, lawnSize)) : null) ?? new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.54f, 0.24f), Roughness = 1 };
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(lawnSize, lawnSize) }, MaterialOverride = lawn });
            Place("prop.stage", Vector3.Zero);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 7; c++)
                    // Facing the stage (a chair's seat faces its +X), their backs to the camera in the back rows.
                    Place("prop.chair", new Vector3(-3.6f + c * 1.2f, 0, 4.2f + r * 1.2f), Mathf.Pi / 2);
            carY = 0.5f;
            // As in the original: the car side-on on the stage, the chairs in front, seen from the back rows.
            Orbit.MoveTo(new Vector3(0, 2.2f, 8.2f), new Vector3(0, carY + 0.35f, 0), animate: false);
        }
        else
        {
            // Our own lot: a row of twelve bays going off to the left, their backs against the fence (as the original's
            // against its wall), the camera in the lane in front of them.
            const float pitch = 3.3f, fenceZ = -3.6f;
            // Asphalt as dark as old asphalt is (the photograph's is a light, dusty grey). In our own look the lot ends
            // at the fence, a field beyond it out to the haze.
            var lotSize = ModernLook.On ? new Vector2(140, 80) : new Vector2(90, 50);
            var lotAt = ModernLook.On ? new Vector3(-18, 0, fenceZ + lotSize.Y / 2) : new Vector3(-18, 0, 0);
            var asphalt = (ModernLook.On ? ModernLook.Surface("Asphalt031", 2.2f, lotSize, mean: new Color(0.34f, 0.34f, 0.35f)) : null)
                ?? TexturedMat("tex.asphalt", new Vector2(14, 8), 0.95f);
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = lotSize }, Position = lotAt, MaterialOverride = asphalt });
            if (ModernLook.On && ModernLook.Surface("Grass004", 1.6f, new Vector2(1200, 1200)) is { } field)
                AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(1200, 1200) }, Position = new Vector3(-18, -0.02f, 0), MaterialOverride = field });
            var lineMat = new StandardMaterial3D { AlbedoColor = new Color(0.91f, 0.89f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            for (int i = -1; i < Core.Sim.Game.LotBays; i++)
                AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(0.12f, 6) }, Position = new Vector3(-i * pitch - pitch / 2, 0.01f, 0), MaterialOverride = lineMat });
            // The office by the first bay, its back to the fence; the lamps behind the fence, their arms over the bays.
            Place("prop.booth", new Vector3(9.5f, 0, fenceZ + 1.4f));
            Place("prop.lightpole", new Vector3(6, 0, fenceZ - 0.5f), -Mathf.Pi / 2);
            Place("prop.lightpole", new Vector3(-20, 0, fenceZ - 0.5f), -Mathf.Pi / 2);
            for (int x = 12; x >= -44; x -= 4) Place("prop.fence", new Vector3(x, 0, fenceZ));
            for (int i = 0; i < Core.Sim.Game.LotBays; i++)
            {
                var b = new Vector3(-i * pitch, 0, 0);
                bays.Add(b);
                lotViews.Add((b + new Vector3(4.5f, 2.6f, 7.5f), b + new Vector3(0, carY + 0.7f, 0)));
            }
            PlaceLotCamera();
        }
    }

    /// <summary>
    /// The Car Lot: your cars in its bays from the first, in the order the lot keeps them; the camera stays where it
    /// is along the lane, as far as the last car.
    /// </summary>
    public void ShowLot(IReadOnlyList<string> lot)
    {
        var ids = lot.Where(Game.State.Vehicles.ContainsKey).Take(bays.Count).ToList();
        if (!parked.Select(v => v.VehicleId).SequenceEqual(ids))
        {
            foreach (var v in parked) v.QueueFree();
            parked.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                var v = new VehicleView(Game, Assets, ids[i], full: false) { Position = bays[i], Rotation = new Vector3(0, FacingLane(i), 0) };
                parked.Add(v);
                AddChild(v);
                // The original's cars cast a dark rectangle on the lot (measured: the asphalt under it at 0.46); our own
                // lot has real shadows.
                if (original is not null) v.AddShadow(LotShadow);
            }
        }
        else
            foreach (var v in parked) v.Sync(animateChanges: false);
        LotPos = Mathf.Clamp(LotPos, 0, LotEnd);
        PlaceLotCamera();
    }

    /// <summary>
    /// Which way a car stands in its bay (its turn about the vertical). Seen in the original's Car Lot: along the bay's
    /// lines, its front to the lane (the tutorial's race car showed its front bumper and a front wheel, the gap of its
    /// missing front door just behind it).
    /// </summary>
    float FacingLane(int i)
    {
        var row = bays[Math.Min(i + 1, bays.Count - 1)] - bays[Math.Max(i - 1, 0)];
        row.Y = 0;
        row = row.Normalized();
        var d = bays[i] - lotViews[i].Eye;
        d.Y = 0;
        d = (d - row * d.Dot(row)).Normalized();
        // The car's nose is its +X (the WorkShop's first view, from +X, is the car's front); turn it to −d.
        return Mathf.Atan2(d.Z, -d.X);
    }

    bool lotOpened;

    /// <summary>Opening the Car Lot. Measured: the first time in a run of the game it opens on the first bay; after that
    /// the camera is where it was left, for the whole run (through the other screens, and another mechanic signed in).</summary>
    public void OpenLot()
    {
        if (lotOpened) return;
        lotOpened = true;
        SetLotPos(0);
    }

    /// <summary>The cars' light, measured: on the lot the scene's own, on the Auction's stage the WorkShop's with less
    /// ambient; without the original's scene the WorkShop's.</summary>
    public void LightCars()
    {
        if (original is null) OrigLook.UseWorkshop();
        else if (kind == Kind.Auction) OrigLook.UseAuction();
        else OrigLook.UseScene(LotLight, Camera.GlobalTransform.Basis.Inverse() * LotLight.TowardsLight);
    }

    /// <summary>The camera in front of this point of the row (in bays from the first).</summary>
    public void SetLotPos(float pos)
    {
        LotPos = Mathf.Clamp(pos, 0, LotEnd);
        PlaceLotCamera();
    }

    /// <summary>◄ or ► held (<paramref name="dir"/> +1 goes on along the row, to the left): the camera glides along the
    /// lane. Measured: a click moves it a short way, not a bay; it stops at the first bay.</summary>
    public bool Glide(float dir, double dt)
    {
        float to = Mathf.Clamp(LotPos + dir * GlideSpeed * (float)dt, 0, LotEnd);
        if (to == LotPos) return false;
        LotPos = to;
        PlaceLotCamera();
        return true;
    }

    void PlaceLotCamera()
    {
        if (lotViews.Count == 0) return;
        int i = Mathf.Clamp((int)Mathf.Floor(LotPos), 0, lotViews.Count - 1);
        int j = Math.Min(i + 1, lotViews.Count - 1);
        float f = Mathf.Clamp(LotPos - i, 0, 1);
        Orbit.MoveTo(lotViews[i].Eye.Lerp(lotViews[j].Eye, f), lotViews[i].Look.Lerp(lotViews[j].Look, f), animate: false);
    }

    /// <summary>
    /// The parked car the Car Lot's figures are about, and where it is across the view (in the view's pixels).
    /// Measured: it is the car nearest the middle of the view, and the gold marker under the view sits under it.
    /// </summary>
    public (int Index, Vector2 At)? LotChoice()
    {
        if (!IsInsideTree()) return null;
        float mid = GetViewport().GetVisibleRect().Size.X / 2;
        (int, Vector2)? best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < parked.Count; i++)
        {
            var at = parked[i].GlobalPosition + Vector3.Up * 0.6f;
            if (Camera.IsPositionBehind(at)) continue;
            var p = Camera.UnprojectPosition(at);
            if (Mathf.Abs(p.X - mid) < bestD)
            {
                bestD = Mathf.Abs(p.X - mid);
                best = (i, p);
            }
        }
        return best;
    }

    /// <summary>Lab: the car on show (the Auction's), its triangles as drawn, to a file.</summary>
    public void ExportCar(string path) => view?.ExportTriangles(path);

    /// <summary>Lab: the camera (origin, basis rows, horizontal field of view) and each parked car's origin and turn.</summary>
    public string Geometry()
    {
        static string V(Vector3 v) => $"{v.X:0.0000},{v.Y:0.0000},{v.Z:0.0000}";
        var t = Camera.GlobalTransform;
        var cars = parked.Select(v => $"{v.VehicleId}@{V(v.GlobalPosition)}/{v.GlobalRotation.Y:0.0000}");
        return $"cam {V(t.Origin)} x {V(t.Basis.X)} y {V(t.Basis.Y)} z {V(t.Basis.Z)} fov {Camera.Fov:0.00} keep {Camera.KeepAspect} | {string.Join(" ", cars)}";
    }

    /// <summary>The parked car under the pointer (its bay), if any.</summary>
    public int? PickParked(Vector2 viewportPos)
    {
        var origin = Camera.ProjectRayOrigin(viewportPos);
        var dir = Camera.ProjectRayNormal(viewportPos);
        int? best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < parked.Count; i++)
            if (Picker.Cast(parked[i].Targets, origin, dir) is { } h && h.Distance < bestD)
            {
                bestD = h.Distance;
                best = i;
            }
        return best;
    }

    /// <summary>The car on the stage; with <paramref name="driveOn"/> (the Auction's next car) the car there drives off
    /// to the right and this one drives on from the left (see <see cref="Swap"/>), with <paramref name="entering"/>
    /// the first car as the Auction's screen comes up.</summary>
    public void Show(string? vehicleId, bool driveOn = false, bool entering = false)
    {
        if (view is not null && view.VehicleId == vehicleId && IsInstanceValid(view))
        {
            view.Sync(animateChanges: false);
            return;
        }
        var before = view;
        view = null;
        if (driveOn && before is not null && IsInstanceValid(before))
        {
            leaving?.QueueFree();
            leaving = before;
            leavingFrom = before.Position;
        }
        else before?.QueueFree();
        if (vehicleId is null || !Game.State.Vehicles.ContainsKey(vehicleId)) return;
        view = original is not null
            ? new VehicleView(Game, Assets, vehicleId, full: false) { Position = carAt }
            : new VehicleView(Game, Assets, vehicleId, full: false) { Position = new Vector3(0, carY, 0), Rotation = new Vector3(0, kind == Kind.Auction ? 0 : -0.6f, 0) };
        home = view.Position;
        spin = 0;
        AddChild(view);
        // Measured: coming to the Auction, its first car drives on as the next one does, in sight 0.17 s after the
        // screen comes up and at its place about 0.4 s later.
        swapAt = !driveOn ? -1 : entering ? SwapArriveAt - EntryArrive : 0;
        if (driveOn) Swap(swapAt);
    }

    /// <summary>When the Auction's first car starts driving on, after the screen is set up (seconds): in sight about
    /// 0.17 s after the screen shows, as the original's.</summary>
    const double EntryArrive = 0.24;

    /// <summary>
    /// The Auction's cars changing places, measured on 60 fps films of a car's time running out and of Skip Car: the car
    /// drives off to the right, faster and faster, out of the view in about 0.3 s; the stage stays empty a moment; the
    /// next car drives on from the left and slows to its place, there about 0.75 s after the change began, when its
    /// figures come up (<see cref="Core.Sim.Game.AuctionSwap"/>).
    /// </summary>
    void Swap(double t)
    {
        if (leaving is not null && IsInstanceValid(leaving))
        {
            if (t >= SwapLeave)
            {
                leaving.QueueFree();
                leaving = null;
            }
            else leaving.Position = leavingFrom + Vector3.Right * SwapRun * (float)(t * t / (SwapLeave * SwapLeave));
        }
        if (view is null) return;
        double s = (t - SwapArriveAt) / (Core.Sim.Game.AuctionSwap - SwapArriveAt);
        view.Visible = s >= 0;
        view.Position = home + Vector3.Left * SwapRun * (float)Math.Pow(1 - Math.Clamp(s, 0, 1), 2);
        if (s >= 1) swapAt = -1;
    }

    VehicleView? leaving;
    Vector3 leavingFrom, home;
    /// <summary>Seconds into the cars' change (-1: none).</summary>
    double swapAt = -1;
    /// <summary>How far off its place a car is when it is out of the Auction's view (metres), how long the leaving car
    /// takes to get there, and when the next one comes into sight.</summary>
    const float SwapRun = 7f;
    const double SwapLeave = 0.3, SwapArriveAt = 0.37;

    public override void _Process(double delta)
    {
        base._Process(delta);
        spin += delta;
        if (swapAt >= 0) Swap(swapAt += delta);
    }
}

/// <summary>
/// Small rendered pictures of parts for the Parts Bin, the Purchase Bin and the Catalog. A bin's picture is the part
/// as the original shows it there (measured on the captures of session 6): from the car's left side, level, the part's
/// box diagonal over the whole picture (the slot draws it 50 pixels tall), a damaged part in bare metal; a part under
/// the pointer turns (see <see cref="Turn"/>).
/// </summary>
public partial class Thumbnails : SubViewport
{
    readonly AssetStore assets;
    readonly Dictionary<string, Texture2D?> cache = [];
    readonly Dictionary<string, List<System.Action<Texture2D>>> waiting = [];
    readonly Queue<Shot> queue = new();
    readonly Camera3D cam = new() { Fov = 30, Near = 0.01f, Far = 50 };
    readonly Node3D holder = new();
    bool busy;
    /// <summary>Our own look's studio, turned to the camera.</summary>
    Godot.Environment? studio;

    public Thumbnails(AssetStore assets, int size = 112)
    {
        this.assets = assets;
        Size = new Vector2I(size, size);
        OwnWorld3D = true;
        TransparentBg = true;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        Msaa3D = Msaa.Msaa4X;
    }

    public override void _Ready()
    {
        AddChild(cam);
        AddChild(holder);
        if (ModernLook.On)
        {
            studio = ModernLook.Studio(Colors.Transparent, transparent: true);
            AddChild(new WorldEnvironment { Environment = studio });
            ModernLook.AddLights(cam);
            return;
        }
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.7f,
            // As the WorkShop's view: the original's parts light themselves (OrigLook) in its colour numbers, which a
            // tone curve would bend (they came out paler and whiter than the original's pictures).
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = env });
        // From above, behind the camera and to its left, turned with it (the original's parts light themselves).
        cam.AddChild(new DirectionalLight3D { LightEnergy = 1.6f, Transform = Transform3D.Identity.LookingAt(new Vector3(0.35f, -0.7f, -0.6f), Vector3.Up) });
    }

    /// <summary>Checks: every picture made so far, to files named by their keys.</summary>
    public void SaveAll(string dir)
    {
        foreach (var (key, tex) in cache)
            tex?.GetImage().SavePng(System.IO.Path.Combine(dir, "thumb_" + string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".png"));
    }

    /// <summary>How a picture looks at its part: the original's Parts Bin (from the car's left side, level, the box's
    /// diagonal over the picture), the original's Catalog (see <see cref="CatalogDistance"/>), or ours (from above and
    /// in front).</summary>
    public enum Pose { Ours, Bin, Catalog }

    /// <summary>What a picture shows: a model, how it is drawn (bare metal by condition, or its own look with its paint
    /// in <see cref="Paint"/>, the material named <see cref="PaintMaterial"/>), from where, turned about the vertical by
    /// <see cref="Angle"/> (bins).</summary>
    public readonly record struct Shot(string Model, int? Bare, Pose Pose, int Angle, string? Paint = null, string? PaintMaterial = null)
    {
        public string Key => $"{Model}|{Bare?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}|{Pose}|{Angle}|{Paint}";
    }

    /// <summary>
    /// The original's Catalog, measured on its captures of a pickup's twenty parts: each part's mesh in its own frame
    /// (not as it sits on the car: a flywheel, a fan and a crankshaft, which the car turns a quarter round, show face on
    /// and end on), from its -Z side, level, in perspective from <see cref="CatalogDistance"/> times the distance of its
    /// farthest corner from its origin, that sphere drawn <see cref="CatalogRadius"/> pixels round the origin, which sits
    /// in the middle of the card. The picture fills the sphere: draw it that big. (Fitted all at once on the outlines:
    /// 0.91 of each picture's pixels shared on average, against 0.84 drawn without perspective.)
    /// </summary>
    public const float CatalogDistance = 2.8f, CatalogRadius = 25f;

    /// <summary>The picture for a Catalog card: with <paramref name="original"/> as the original draws it (see
    /// <see cref="CatalogDistance"/>) in the paint of <paramref name="car"/>'s model, turned by <paramref name="degrees"/>
    /// (where the pointer left it), else ours.</summary>
    public Texture2D? Get(string modelId, bool original, CarModelDef? car, System.Action<Texture2D>? ready = null, int degrees = 0) =>
        Get(original ? new Shot(modelId, null, Pose.Catalog, ((degrees % 360) + 360) % 360, OwnPaint(car), car?.PaintMaterial) : new Shot(modelId, null, Pose.Ours, 0), ready);

    /// <summary>A bin's picture of a part in this condition: with <paramref name="side"/> as the original shows it (from
    /// the car's left side, turned by <paramref name="degrees"/>), else as the Catalog's. A damaged part is drawn as on
    /// a car: all bare metal in its condition's colour (measured in the original's Parts Bin: a red fender of a black car
    /// shows light, the metal's colour).</summary>
    public Texture2D? GetBin(string modelId, int condition, bool side, int degrees = 0, System.Action<Texture2D>? ready = null, CarModelDef? car = null) =>
        Get(new Shot(modelId, Bare(condition), side ? Pose.Bin : Pose.Ours, side ? ((degrees % 360) + 360) % 360 : 0, OwnPaint(car), car?.PaintMaterial), ready);

    /// <summary>The colour a part of this car model is painted in its pictures (measured: a pickup's repaired hood and
    /// bed light blue, its model's own paint, while the job's pickup was green; as in the JunkYard).</summary>
    static string? OwnPaint(CarModelDef? car) => car is null ? null : car.OwnPaint is { Length: > 0 } own ? own : car.DefaultPaint;

    static int? Bare(int condition) => condition < Condition.Good ? System.Math.Max(condition, Condition.Black) : null;

    readonly Dictionary<ModelData, float> radii = [];

    /// <summary>How far a model's farthest corner is from its origin, in its pieces' own frames.</summary>
    float Radius(ModelData model)
    {
        if (radii.TryGetValue(model, out var r)) return r;
        foreach (var p in model.Pieces)
            for (int s = 0; s < p.Mesh.GetSurfaceCount(); s++)
                foreach (var v in p.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    r = Mathf.Max(r, v.Length());
        return radii[model] = r;
    }

    Texture2D? Get(Shot shot, System.Action<Texture2D>? ready)
    {
        var key = shot.Key;
        if (cache.TryGetValue(key, out var tex)) return tex;
        if (ready is not null)
        {
            if (!waiting.TryGetValue(key, out var list)) waiting[key] = list = [];
            list.Add(ready);
        }
        if (!queue.Any(q => q.Key == key)) queue.Enqueue(shot);
        if (!busy) _ = Pump();
        return null;
    }

    readonly Queue<Shot> queue2 = new();
    /// <summary>Pictures made ahead, when nothing else is waiting (the Catalog's, for the car in the WorkShop).</summary>
    readonly Queue<Shot> later = new();

    /// <summary>A Catalog picture made ahead of time, when nothing else is waiting: the original's Catalog shows a
    /// spread's pictures all at once, a tenth of a second after its pages.</summary>
    public void Prefetch(string modelId, bool original, CarModelDef? car, int degrees = 0)
    {
        var shot = original ? new Shot(modelId, null, Pose.Catalog, ((degrees % 360) + 360) % 360, OwnPaint(car), car?.PaintMaterial) : new Shot(modelId, null, Pose.Ours, 0);
        if (cache.ContainsKey(shot.Key) || later.Any(q => q.Key == shot.Key)) return;
        later.Enqueue(shot);
        if (!busy) _ = Pump();
    }

    /// <summary>Sets up the part for a shot; false when there is no model.</summary>
    bool Stage(Shot shot)
    {
        if (assets.Model(shot.Model) is not { } model) return false;
        foreach (var c in holder.GetChildren()) c.Free();
        var obj = model.Instantiate(shadows: false);
        if (shot.Bare is { } cond)
        {
            bool orig = ModelData.MeshesOf(obj).Any(mi => mi.Mesh is { } m && m.GetSurfaceCount() > 0 && OrigLook.IsOrig(m.SurfaceGetMaterial(0)));
            var metal = VehicleView.BareMetal(cond, orig);
            foreach (var mi in ModelData.MeshesOf(obj)) mi.MaterialOverride = metal;
        }
        else if (shot.Paint is { } paint && shot.PaintMaterial is { } paintName)
        {
            var colour = Conv.Hex(paint);
            foreach (var mi in ModelData.MeshesOf(obj))
            {
                if (mi.Mesh is null) continue;
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if (mi.Mesh.SurfaceGetMaterial(s) is { } mat && mat.ResourceName == paintName)
                        mi.SetSurfaceOverrideMaterial(s, mat is ShaderMaterial sm && OrigLook.IsOrig(sm)
                            ? OrigLook.Recolored(sm, colour)
                            : new StandardMaterial3D { AlbedoColor = colour, Metallic = 0.35f, Roughness = 0.38f });
            }
        }
        var box = model.Bounds;
        obj.Position = -box.GetCenter();
        var turn = new Node3D { Rotation = new Vector3(0, Mathf.DegToRad(shot.Angle), 0) };
        turn.AddChild(obj);
        holder.AddChild(turn);
        cam.Fov = 30;
        if (shot.Pose == Pose.Catalog)
        {
            // The mesh in its own frame, round its origin; the camera's view just holds the sphere of its farthest corner.
            foreach (var mi in ModelData.MeshesOf(obj)) mi.Transform = Transform3D.Identity;
            obj.Position = Vector3.Zero;
            float r = Mathf.Max(0.02f, Radius(model));
            float k = CatalogDistance;
            cam.Fov = Mathf.RadToDeg(2 * Mathf.Atan(1 / Mathf.Sqrt(k * k - 1)));
            cam.LookAtFromPosition(new Vector3(0, 0, -k * r), Vector3.Zero, Vector3.Up);
        }
        else if (shot.Pose == Pose.Bin)
        {
            // Measured: seen from the car's left side, level; the box's diagonal over the whole picture.
            float r = Mathf.Max(0.05f, box.Size.Length() / 2);
            float dist = r / Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2);
            cam.LookAtFromPosition(new Vector3(0, 0, -dist), Vector3.Zero, Vector3.Up);
        }
        else
        {
            float r = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)) * 0.62f;
            if (r <= 0) r = 0.1f;
            float dist = r / Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2);
            cam.LookAtFromPosition(new Vector3(dist * 0.7f, dist * 0.55f, dist * 0.75f), Vector3.Zero, Vector3.Up);
        }
        if (studio is not null) ModernLook.TurnStudio(studio, cam);
        return true;
    }

    async System.Threading.Tasks.Task Pump()
    {
        busy = true;
        while (queue.Count > 0 || later.Count > 0 && turning is null)
        {
            var shot = queue.Count > 0 ? queue.Dequeue() : later.Dequeue();
            var id = shot.Key;
            if (cache.ContainsKey(id)) continue;
            Texture2D? tex = null;
            if (turning is not null)
            {
                // Busy turning a part under the pointer: made when it stops.
                queue2.Enqueue(shot);
                continue;
            }
            if (Stage(shot))
            {
                // The moves reach the renderer with the next frame's process: drawn in this frame, the picture came out
                // with the last part's camera, or without the part (a request made from a deferred call, after the flush).
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                RenderTargetUpdateMode = UpdateMode.Once;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (turning is not null)
                {
                    // A part began turning meanwhile: this picture is not the shot's.
                    queue2.Enqueue(shot);
                    continue;
                }
                tex = ImageTexture.CreateFromImage(GetTexture().GetImage());
            }
            cache[id] = tex;
            if (tex is not null && waiting.Remove(id, out var cbs))
                foreach (var cb in cbs) cb(tex);
        }
        busy = false;
    }

    // ---- a part under the pointer turns ----------------------------------------------------------------

    /// <summary>How fast a bin's part under the pointer turns (measured on a film of the original: half a turn in 0.79 s).</summary>
    public const float TurnRate = 3.96f;

    Shot? turning;
    double turnAngle;

    /// <summary>The picture drawn live while a part turns under the pointer (null when none does).</summary>
    public Texture2D? Live => turning is null ? null : GetTexture();

    /// <summary>
    /// A bin's part under the pointer turns about the vertical, from where it was left (measured: the Parts Bin's picture
    /// of the part under the pointer turns, a half turn in 0.79 s; with the pointer gone it stays as it is).
    /// </summary>
    public void Turn(string modelId, int condition, double degrees, CarModelDef? car = null, Pose pose = Pose.Bin)
    {
        var shot = new Shot(modelId, pose == Pose.Catalog ? null : Bare(condition), pose, 0, OwnPaint(car), car?.PaintMaterial);
        turning = shot;
        turnAngle = degrees;
        Stage(shot with { Angle = 0 });
        SetAngle();
        RenderTargetUpdateMode = UpdateMode.Always;
    }

    /// <summary>The turning part's angle now, in degrees (null: none turns).</summary>
    public double? TurnAngle => turning is null ? null : turnAngle;

    public override void _Process(double delta)
    {
        if (turning is null) return;
        turnAngle = (turnAngle + Mathf.RadToDeg(TurnRate) * delta) % 360;
        SetAngle();
    }

    void SetAngle()
    {
        if (holder.GetChildCount() > 0 && holder.GetChild(0) is Node3D t) t.Rotation = new Vector3(0, Mathf.DegToRad((float)turnAngle), 0);
    }

    /// <summary>The part under the pointer stops turning: its picture as it stopped (the last frame drawn), kept as the
    /// bin's picture at that angle.</summary>
    public Texture2D? StopTurn()
    {
        if (turning is not { } shot) return null;
        turning = null;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        var tex = ImageTexture.CreateFromImage(GetTexture().GetImage());
        cache[(shot with { Angle = (int)System.Math.Round(turnAngle) % 360 }).Key] = tex;
        while (queue2.Count > 0) queue.Enqueue(queue2.Dequeue());
        if ((queue.Count > 0 || later.Count > 0) && !busy) _ = Pump();
        return tex;
    }
}
