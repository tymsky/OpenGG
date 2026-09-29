using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;

namespace OpenGG.View3D;

/// <summary>
/// 3D view of one vehicle. Mirrors the vehicle state (our own look animates the differences). It reconciles
/// against the state instead of replaying events, so it cannot drift out of sync.
/// </summary>
public partial class VehicleView : Node3D
{
    /// <summary>Render layer of paintable body meshes (decals project only onto these).</summary>
    public const uint BodyLayer = 2;

    sealed class SlotView
    {
        public required SlotDef Def;
        public required Node3D Holder;
        public required Node3D Pivot;
        public Node3D? Model;
        public string? PartUid;
        public PickTarget? PartTarget;
        public Node3D?[] Fasteners = [];
        public PickTarget?[] FastenerTargets = [];
        public Vector3[] FastenerHome = [];
        public Vector3[] FastenerDir = [];
        public bool[] FastenerIn = [];
        public bool[] FastenerGhost = [];
        public bool Open;
        public Node3D? Ghost;
        public PickTarget? GhostTarget;
        public string Color = "";
        /// <summary>Drawn as bare metal (a damaged part): its condition, or -1 for a good part.</summary>
        public int Rust = -1;
    }

    /// <summary>Measured in the original: a damaged part is drawn bare, without its paint or textures, in one
    /// flat colour by its condition: yellow a pinkish grey, red an olive grey, black a dark brown (fitted on the
    /// same hood, black and then yellow, from the same view, and on a red door). It gets its paint and textures
    /// back once repaired.</summary>
    static readonly Color[] RustColors = [new(0.246f, 0.176f, 0.123f), new(0.303f, 0.284f, 0.233f), new(0.570f, 0.494f, 0.478f)];
    static readonly StandardMaterial3D?[] rustMats = new StandardMaterial3D?[3];
    static readonly ShaderMaterial?[] rustOrig = new ShaderMaterial?[3];
    static StandardMaterial3D RustMat(int level) => rustMats[level] ??=
        (ModernLook.On ? ModernLook.BareMetal(RustColors[level]) : null) ?? new() { AlbedoColor = RustColors[level], Roughness = 0.95f };
    /// <summary>The same for the original's cars, lit their way (the colours fitted to its screenshots).</summary>
    static ShaderMaterial RustOrig(int level) =>
        rustOrig[level] ??= OrigLook.Material($"rust{level}", RustColors[level], null, OrigLook.Blend.Opaque, flat: false, shiny: false);

    Material? BaseOverride(SlotView sv) => sv.Rust >= 0 ? BareMetal(sv.Rust, OrigLookCar) : null;

    /// <summary>A damaged part's bare metal, by its condition (black, red, yellow), for the original's look or ours.</summary>
    public static Material BareMetal(int condition, bool origLook) => origLook ? RustOrig(Math.Clamp(condition, 0, 2)) : RustMat(Math.Clamp(condition, 0, 2));

    bool? origLookCar;
    /// <summary>Is the car drawn with the original's lighting (its models come with <see cref="OrigLook"/> materials)?
    /// Known once a part's model is there.</summary>
    bool OrigLookCar
    {
        get
        {
            if (origLookCar is { } known) return known;
            var mesh = slots.Values.Where(s => s.Model is not null).SelectMany(s => ModelData.MeshesOf(s.Model!))
                .Select(mi => mi.Mesh).FirstOrDefault(m => m is not null && m.GetSurfaceCount() > 0);
            if (mesh is null) return false;
            origLookCar = OrigLook.IsOrig(mesh.SurfaceGetMaterial(0));
            return origLookCar.Value;
        }
    }

    static readonly StandardMaterial3D GhostMat = Flat(new Color(0.6f, 0.82f, 1f, 0.28f));
    static readonly StandardMaterial3D GhostOkMat = Flat(new Color(0.36f, 1f, 0.54f, 0.35f));
    static readonly StandardMaterial3D GhostBlockedMat = Flat(new Color(0.67f, 0.67f, 0.67f, 0.18f));
    static readonly StandardMaterial3D HiliteSolid = new() { AlbedoColor = new Color(1f, 0.82f, 0.23f), EmissionEnabled = true, Emission = new Color(0.42f, 0.29f, 0f), Metallic = 0.4f, Roughness = 0.4f };
    static readonly StandardMaterial3D HiliteOverlay = new()
    {
        AlbedoColor = new Color(1f, 0.8f, 0.2f, 0.22f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    static readonly StandardMaterial3D[] XrayMats = Condition.Colors.Select(c => Flat(Color.FromHtml(c) with { A = 0.62f })).ToArray();
    /// <summary>The part under the pointer lights up in its condition colour (measured in the original).</summary>
    static readonly StandardMaterial3D[] HoverMats = Condition.Colors.Select(c => new StandardMaterial3D
    {
        AlbedoColor = Color.FromHtml(c).Lightened(0.25f),
        EmissionEnabled = true,
        Emission = Color.FromHtml(c) * 0.35f,
        Roughness = 0.6f,
    }).ToArray();

    static StandardMaterial3D Flat(Color c) => new()
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    public string VehicleId { get; }
    readonly Game game;
    readonly AssetStore assets;
    readonly bool full;
    readonly bool animate;
    readonly Dictionary<string, SlotView> slots = [];
    readonly Dictionary<(string Color, Material Template), Material> paintMats = [];
    readonly Node3D carNode = new() { Name = "Car" };
    readonly Node3D decalsRoot = new() { Name = "Decals" };
    readonly List<PickTarget> targets = [];
    readonly List<(Node3D Node, Region Region)> statics = [];
    Node3D? body;
    PickTarget? bodyTarget;
    string paint = "";
    string decalKey = "";
    Node3D? hovered;
    Pick? hoveredPick;
    View view = View.Complete;
    bool xray;
    string? boltSlot;
    Decal? preview;
    readonly Dictionary<(Texture2D, bool, bool), Texture2D> flipped = [];

    /// <summary>
    /// The part in "bolt mode" (the original's Impact Wrench Tool): its bolts are shown, and no others (measured: out of
    /// bolt mode no bolt or hole is drawn).
    /// </summary>
    public string? BoltSlot
    {
        get => boltSlot;
        set
        {
            boltSlot = value;
            ApplyVisibility();
        }
    }

    /// <summary>Bolt mode is doing the part's bolts up (it was just put on) rather than undoing them.</summary>
    public bool BoltAttach { get; set; }

    /// <summary>
    /// Can the pointer pick this? Measured: in bolt mode the bolts go one way only (taking a part off, out; putting one
    /// on, in), and a bolt already done neither lights up under the pointer nor takes a click.
    /// </summary>
    public bool Pickable(PickTarget t) =>
        t.Pick is not FastenerPick f || f.SlotId != boltSlot || !slots.TryGetValue(f.SlotId, out var sv)
        || f.Index >= sv.FastenerIn.Length || sv.FastenerIn[f.Index] != BoltAttach;

    /// <param name="full">Workshop detail (bolts, and in our own look animations); false for the Auction and Car Lot.</param>
    public VehicleView(Game game, AssetStore assets, string vehicleId, bool full)
    {
        this.game = game;
        this.assets = assets;
        this.full = full;
        // Measured: the original puts a part on and takes it off at once (the next frame has it there or gone), and a
        // bolt too. Our own look slides them.
        animate = full && ModernLook.On;
        VehicleId = vehicleId;
        Name = $"Vehicle_{vehicleId}";
    }

    VehicleState? Vehicle => game.State.Vehicles.GetValueOrDefault(VehicleId);
    CarModelDef Car => game.CI.Car(Vehicle!.ModelId);

    public IEnumerable<PickTarget> Targets => targets;

    /// <summary>Car space (content coordinates) in the world: decals and hits are stored in it.</summary>
    public Transform3D CarTransform => carNode.GlobalTransform;

    public override void _Ready()
    {
        AddChild(carNode);
        carNode.AddChild(decalsRoot);
        var v = Vehicle;
        if (v is null) return;
        var car = Car;
        if (car.BodyModel.Length > 0 && assets.Model(car.BodyModel) is { } shell)
        {
            body = shell.Instantiate();
            body.Name = "Body";
            carNode.AddChild(body);
            foreach (var mi in ModelData.MeshesOf(body)) mi.Layers = 1 | BodyLayer;
            bodyTarget = new PickTarget(body, new BodyPick());
            targets.Add(bodyTarget);
        }
        foreach (var s in car.Statics)
        {
            if (assets.Model(s.Model) is not { } m) continue;
            var node = m.Instantiate();
            node.Name = "Static";
            carNode.AddChild(node);
            statics.Add((node, s.Region));
            if (s.Region == Region.Body)
            {
                foreach (var mi in ModelData.MeshesOf(node)) mi.Layers = 1 | BodyLayer;
                targets.Add(new PickTarget(node, new BodyPick()));
            }
        }
        foreach (var def in car.Slots)
        {
            var holder = new Node3D { Name = def.Id, Position = def.Pos.V(), Basis = def.Rot.EulerXyz() };
            var pivot = new Node3D { Name = "pivot" };
            holder.AddChild(pivot);
            carNode.AddChild(holder);
            slots[def.Id] = new SlotView { Def = def, Holder = holder, Pivot = pivot };
        }
        carNode.Position = new Vector3(0, GroundOffset(car), 0);
        Sync(animateChanges: false);
    }

    /// <summary>A car model's stock shape: how far to lift it onto the ground, and its footprint (x, z in car space).</summary>
    static readonly Dictionary<string, (float Ground, Rect2 Foot)> shapes = [];

    /// <summary>
    /// A car model's stock parts, measured on their meshes, not on their boxes (the box of a long part turned in its
    /// place reaches far below it: the 57 Chevy's chrome trim lifted that car more than a metre off the ground).
    /// </summary>
    (float Ground, Rect2 Foot) Shape(CarModelDef car)
    {
        if (shapes.TryGetValue(car.Id, out var known)) return known;
        float minY = float.MaxValue, x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        void Take(ModelData? m, Transform3D xf)
        {
            if (m is null) return;
            foreach (var piece in m.Pieces)
            {
                var t = xf * piece.Transform;
                foreach (var f in piece.Mesh.GetFaces())
                {
                    var p = t * f;
                    minY = Mathf.Min(minY, p.Y);
                    x0 = Mathf.Min(x0, p.X);
                    x1 = Mathf.Max(x1, p.X);
                    z0 = Mathf.Min(z0, p.Z);
                    z1 = Mathf.Max(z1, p.Z);
                }
            }
        }
        foreach (var s in car.Statics) Take(assets.Model(s.Model), Transform3D.Identity);
        foreach (var def in car.Slots)
            if (def.DefaultPart is { } dp)
                Take(assets.Model(game.CI.Part(dp).Model), new Transform3D(def.Rot.EulerXyz(), def.Pos.V()));
        float ground = minY < -0.02f && minY != float.MaxValue ? -minY : 0;
        var foot = x0 <= x1 ? new Rect2(x0, z0, x1 - x0, z1 - z0) : new Rect2(-2, -1, 4, 2);
        return shapes[car.Id] = (ground, foot);
    }

    /// <summary>Lifts a car whose origin is not on the ground (the original's cars) onto the lowest point of its stock parts.</summary>
    float GroundOffset(CarModelDef car) => Shape(car).Ground;

    /// <summary>Darkens what is under it by a factor of the colour numbers on screen, as the original's engine did
    /// (a multiply in linear light would darken dark ground more).</summary>
    static readonly Shader ShadowShader = new()
    {
        Code = $$"""
            shader_type spatial;
            render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
            uniform sampler2D under_tex : hint_screen_texture, filter_nearest;
            uniform float keep = 0.46;
            {{OrigLook.ColourSpace}}
            void fragment() {
                ALBEDO = to_linear(to_srgb(texture(under_tex, SCREEN_UV).rgb) * keep);
                ALPHA = 1.0;
            }
            """,
    };

    /// <summary>
    /// The original's shadow under a parked car: a flat dark rectangle on the ground, the car's footprint (its stock
    /// parts, open doors and all), with sharp edges, that leaves <paramref name="keep"/> of the ground's brightness
    /// (measured on the Car Lot: 0.46, and the rectangle fitted the footprint once the lot's camera was fitted).
    /// </summary>
    public void AddShadow(float keep)
    {
        if (Vehicle is null) return;
        var foot = Shape(Car).Foot;
        var mat = new ShaderMaterial { Shader = ShadowShader };
        mat.SetShaderParameter("keep", keep);
        // On the ground under the car (the view's origin is on the ground; the car itself is lifted onto it).
        AddChild(new MeshInstance3D
        {
            Name = "Shadow",
            Mesh = new PlaneMesh { Size = foot.Size },
            Position = new Vector3(foot.GetCenter().X, 0.01f, foot.GetCenter().Y),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>Unscrew direction for a bolt the content gives no direction for: the normal of the part's nearest face.</summary>
    static Vector3 AutoDir(ModelData? model, Vector3 p)
    {
        if (model is null) return Vector3.Up;
        var center = model.Bounds.GetCenter();
        float best = float.MaxValue;
        var dir = Vector3.Up;
        foreach (var piece in model.Pieces)
        {
            var faces = piece.Mesh.GetFaces();
            for (int i = 0; i + 2 < faces.Length; i += 3)
            {
                var a = piece.Transform * faces[i];
                var b = piece.Transform * faces[i + 1];
                var c = piece.Transform * faces[i + 2];
                float d = p.DistanceSquaredTo((a + b + c) / 3);
                if (d >= best) continue;
                var n = (b - a).Cross(c - a);
                if (n.LengthSquared() < 1e-12f) continue;
                best = d;
                dir = n.Normalized();
            }
        }
        if (dir.Dot(p - center) < 0) dir = -dir;
        return dir;
    }

    // ---- region views and x-ray ----------------------------------------------------------

    /// <summary>Which parts are visible and which can be worked on in each tab.</summary>
    public void SetView(View v)
    {
        view = v;
        ApplyVisibility();
    }

    /// <summary>Can the player work on this slot in the current tab?</summary>
    public bool Interactive(string slotId) =>
        slots.TryGetValue(slotId, out var sv) && view != View.Complete && sv.Def.Region.ToView() == view;

    /// <summary>Regions a job lets you work on. Show Condition on the COMPLETE tab only shows these (measured).</summary>
    public HashSet<Region>? JobRegions { get; set; }

    /// <summary>Measured in the original: each work view shows its own region only (BODY has no wheels,
    /// RUNNING GEAR no engine); COMPLETE shows everything.</summary>
    bool VisibleIn(Region region) => view == View.Complete
        ? !xray || JobRegions is null || JobRegions.Contains(region)
        : region.ToView() == view;

    void ApplyVisibility()
    {
        bool showBody = (view is View.Complete or View.Body) && VisibleIn(Region.Body);
        if (body is not null) body.Visible = showBody;
        // Show Condition draws the parts only: the fixed pieces have no condition, and the decals go with the paint.
        decalsRoot.Visible = showBody && !xray;
        foreach (var (node, region) in statics) node.Visible = VisibleIn(region) && !xray;
        foreach (var sv in slots.Values)
        {
            sv.Holder.Visible = VisibleIn(sv.Def.Region);
            bool active = view != View.Complete && sv.Def.Region.ToView() == view;
            // Measured: only the part in bolt mode shows its bolts; out of it none are drawn.
            bool show = active && !xray && sv.Def.Id == boltSlot;
            for (int i = 0; i < sv.Fasteners.Length; i++)
            {
                if (sv.Fasteners[i] is not { } f) continue;
                f.Visible = show;
                if (i < sv.FastenerTargets.Length && sv.FastenerTargets[i] is { } t) t.Enabled = show;
            }
        }
    }

    public void SetXray(bool on)
    {
        xray = on;
        foreach (var sv in slots.Values) ApplyXray(sv);
        ApplyVisibility();
    }

    /// <summary>The region lit by COMPLETE's hover (null: none).</summary>
    Region? litRegion;

    /// <summary>
    /// COMPLETE's hover, measured: the pointer on the car lights the whole region of the part under it (the body, the
    /// running gear), each part in its condition's colour as Show Condition draws it; the rest of the car stays as it is.
    /// </summary>
    public void LightRegion(Region? region)
    {
        if (region == litRegion) return;
        var was = litRegion;
        litRegion = region;
        foreach (var sv in slots.Values)
            if (sv.Def.Region == was || sv.Def.Region == region) ApplyXray(sv);
    }

    void ApplyXray(SlotView sv)
    {
        if (sv.Model is null) return;
        var st = Vehicle?.Slots.GetValueOrDefault(sv.Def.Id);
        bool on = xray || sv.Def.Region == litRegion;
        // The original's cars: each surface in the condition's colour added onto what is behind (OrigLook.Condition),
        // lit and highlighted as that surface is; black parts keep their own look.
        bool orig = OrigLookCar && on && st?.Part is { Condition: > 0 };
        if (orig)
            foreach (var mi in ModelData.MeshesOf(sv.Model))
                for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
                    mi.SetSurfaceOverrideMaterial(i, OrigLook.ConditionFor(mi.Mesh?.SurfaceGetMaterial(i), st!.Part!.Condition));
        else RestoreSurfaces(sv.Model, sv.Color);
        foreach (var mi in ModelData.MeshesOf(sv.Model))
            mi.MaterialOverride = orig ? null : on && !OrigLookCar && st?.Part is { } p ? XrayMats[p.Condition] : BaseOverride(sv);
    }

    /// <summary>A part's surfaces back as they are drawn: their own materials, the paint on the paint's (which is kept
    /// per surface, so clearing the surfaces for a highlight takes it off too).</summary>
    void RestoreSurfaces(Node3D model, string color)
    {
        foreach (var mi in ModelData.MeshesOf(model))
            for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++) mi.SetSurfaceOverrideMaterial(i, null);
        if (color.Length > 0) PaintNode(model, color);
    }

    // ---- sync ------------------------------------------------------------------------------

    /// <summary>Bring the 3D view in line with the vehicle state. Safe to call often.</summary>
    public void Sync(bool animateChanges = true)
    {
        var v = Vehicle;
        if (v is null || !IsInsideTree()) return;
        bool anim = animateChanges && animate;
        foreach (var sv in slots.Values) SyncSlot(v, sv, anim);
        if (v.Paint != paint)
        {
            paint = v.Paint;
            if (body is not null) PaintNode(body, v.Paint);
            foreach (var (node, _) in statics) PaintNode(node, v.Paint);
        }
        foreach (var sv in slots.Values)
        {
            string color = v.Slots[sv.Def.Id].Part?.Color ?? v.Paint;
            if (sv.Model is not null && sv.Color != color)
            {
                sv.Color = color;
                PaintNode(sv.Model, color);
            }
            int rust = v.Slots[sv.Def.Id].Part is { Condition: < Condition.Good } damaged ? Math.Max(damaged.Condition, Condition.Black) : -1;
            if (sv.Model is not null && (sv.Rust != rust || xray))
            {
                sv.Rust = rust;
                ApplyXray(sv);
            }
        }
        string key = string.Join(";", v.Decals.Select(d => $"{d.DecalId},{d.Pos},{d.Normal},{d.Angle:0.###},{d.Size:0.###},{d.Color}"))
            + "|" + string.Join(",", slots.Values.Where(s => s.Def.Region == Region.Body).Select(s => s.PartUid ?? "-"));
        if (key != decalKey)
        {
            decalKey = key;
            BuildDecals(v);
        }
        ApplyVisibility();
    }

    void SyncSlot(VehicleState v, SlotView sv, bool anim)
    {
        var st = v.Slots[sv.Def.Id];
        string? want = st.Part?.Uid;
        if (sv.PartUid != want)
        {
            var old = sv.Model;
            if (sv.PartTarget is not null) targets.Remove(sv.PartTarget);
            foreach (var ft in sv.FastenerTargets)
                if (ft is not null) targets.Remove(ft);
            foreach (var f in sv.Fasteners) f?.QueueFree();
            sv.Model = null;
            sv.Rust = -1;
            sv.PartTarget = null;
            sv.Fasteners = [];
            sv.FastenerTargets = [];
            sv.PartUid = want;
            sv.Color = "";
            if (hovered == old) hovered = null;
            if (old is not null)
            {
                if (anim)
                {
                    var dir = LocalDir(sv, sv.Def.RemoveDir.V());
                    var start = old.Position;
                    var tw = old.CreateTween();
                    tw.TweenMethod(Callable.From<float>(k =>
                    {
                        old.Position = start + dir * k * 0.6f;
                        old.Scale = Vector3.One * (1 - k * 0.6f);
                    }), 0f, 1f, 0.45);
                    tw.TweenCallback(Callable.From(old.QueueFree));
                }
                else old.QueueFree();
            }
            if (want is not null && st.Part is { } part)
            {
                var def = game.CI.Part(part.PartId);
                var model = assets.Model(def.Model)?.Instantiate() ?? Placeholder();
                model.Name = "part";
                sv.Pivot.AddChild(model);
                sv.Model = model;
                if (sv.Def.Region == Region.Body)
                    foreach (var mi in ModelData.MeshesOf(model)) mi.Layers = 1 | BodyLayer;
                sv.PartTarget = new PickTarget(model, new PartPick(sv.Def.Id));
                targets.Add(sv.PartTarget);
                if (anim)
                {
                    var dir = LocalDir(sv, sv.Def.RemoveDir.V());
                    model.Position = dir * 0.4f;
                    model.CreateTween().TweenProperty(model, "position", Vector3.Zero, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                }
                int n = def.Fasteners.Count;
                sv.Fasteners = new Node3D?[n];
                sv.FastenerTargets = new PickTarget?[n];
                sv.FastenerHome = new Vector3[n];
                sv.FastenerDir = new Vector3[n];
                sv.FastenerIn = Enumerable.Repeat(true, n).ToArray();
                sv.FastenerGhost = new bool[n];
                if (full)
                    for (int i = 0; i < n; i++)
                    {
                        var f = def.Fasteners[i];
                        var kind = game.CI.Kind(f.Kind);
                        if (kind.Visual == FastenerVisual.None) continue;
                        var bolt = assets.Model($"fastener.{kind.Visual.ToString().ToLowerInvariant()}")?.Instantiate(shadows: false) ?? Placeholder();
                        float size = (float)kind.Size;
                        var dir = f.Dir.V().Normalized();
                        if (dir.LengthSquared() < 0.5f) dir = AutoDir(assets.Model(def.Model), f.Pos.V());
                        bolt.Position = f.Pos.V();
                        bolt.Basis = Conv.FromUp(dir).Scaled(Vector3.One * size);
                        sv.Pivot.AddChild(bolt);
                        sv.Fasteners[i] = bolt;
                        sv.FastenerHome[i] = bolt.Position;
                        sv.FastenerDir[i] = dir;
                        var target = new PickTarget(bolt, new FastenerPick(sv.Def.Id, i)) { Proxy = new Aabb(new Vector3(-1.6f, -1.6f, -1.6f), new Vector3(3.2f, 3.2f, 3.2f)) };
                        sv.FastenerTargets[i] = target;
                        targets.Add(target);
                    }
                SyncFasteners(v, sv, anim: false);
            }
        }
        if (sv.Model is not null) SyncFasteners(v, sv, anim: false);
        if (sv.Def.Openable is { } op && st.Open != sv.Open && st.Part is not null)
        {
            sv.Open = st.Open;
            var to = new Quaternion(op.Axis.V().Normalized(), st.Open ? (float)op.Angle : 0f);
            if (anim)
            {
                var from = sv.Pivot.Quaternion;
                sv.Pivot.CreateTween().TweenMethod(Callable.From<float>(k => sv.Pivot.Quaternion = from.Slerp(to, k)), 0f, 1f, 0.6)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
            else sv.Pivot.Quaternion = to;
        }
        if (st.Part is null && sv.Def.Openable is not null && sv.Open)
        {
            sv.Open = false;
            sv.Pivot.Quaternion = Quaternion.Identity;
        }
    }

    void SyncFasteners(VehicleState v, SlotView sv, bool anim)
    {
        var st = v.Slots[sv.Def.Id];
        if (st.Part is null || sv.Model is null) return;
        for (int i = 0; i < sv.Fasteners.Length; i++)
        {
            var obj = sv.Fasteners[i];
            if (obj is null || i >= st.Fasteners.Count) continue;
            bool want = st.Fasteners[i];
            if (sv.FastenerIn[i] == want && sv.FastenerGhost[i] == !want) continue;
            sv.FastenerIn[i] = want;
            var home = sv.FastenerHome[i];
            var dir = sv.FastenerDir[i];
            var baseBasis = obj.Basis;
            var scale = baseBasis.Scale;
            var rest = Conv.FromUp(dir);
            void Spin(float a) => obj.Basis = (rest * new Basis(Vector3.Up, a)).Scaled(scale);
            if (!anim)
            {
                obj.Position = home;
                Spin(0);
                SetFastenerGhost(sv, i, !want);
                continue;
            }
            SetFastenerGhost(sv, i, false);
            var tw = obj.CreateTween();
            if (!want)
            {
                tw.TweenMethod(Callable.From<float>(k =>
                {
                    obj.Position = home + dir * k * 0.035f;
                    Spin(k * Mathf.Pi * 6);
                }), 0f, 1f, 0.35);
                int idx = i;
                tw.TweenCallback(Callable.From(() =>
                {
                    obj.Position = home;
                    Spin(0);
                    SetFastenerGhost(sv, idx, true);
                }));
            }
            else
            {
                tw.TweenMethod(Callable.From<float>(k =>
                {
                    obj.Position = home + dir * (1 - k) * 0.035f;
                    Spin((1 - k) * Mathf.Pi * 6);
                }), 0f, 1f, 0.35);
            }
        }
    }

    /// <summary>A bolt that is out leaves a black hole (as in the original).</summary>
    static readonly StandardMaterial3D HoleMat = new() { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 1 };

    void SetFastenerGhost(SlotView sv, int i, bool ghost)
    {
        sv.FastenerGhost[i] = ghost;
        if (sv.Fasteners[i] is { } obj)
            foreach (var mi in ModelData.MeshesOf(obj)) mi.MaterialOverride = ghost ? HoleMat : null;
    }

    /// <summary>A part's bolts in world space, for the BOLT labels of bolt mode.</summary>
    public IEnumerable<(int Index, Vector3 Global, bool In)> Bolts(string slotId)
    {
        if (!slots.TryGetValue(slotId, out var sv)) yield break;
        for (int i = 0; i < sv.Fasteners.Length; i++)
            if (sv.Fasteners[i] is { } f && f.IsInsideTree() && f.Visible) yield return (i, f.GlobalPosition, sv.FastenerIn[i]);
    }

    Vector3 LocalDir(SlotView sv, Vector3 dir) => (sv.Holder.Basis.Inverse() * dir).Normalized();

    static Node3D Placeholder()
    {
        var n = new Node3D();
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.1f) } });
        return n;
    }

    // ---- paint ------------------------------------------------------------------------------

    ImageTexture? canvasTex;
    StandardMaterial3D? canvasMat;
    readonly Dictionary<Material, ShaderMaterial> canvasOrig = [];
    int canvasVersion = -1;

    /// <summary>Cars with a paint picture draw their paint from it (see <see cref="PaintCanvas"/>); the original's
    /// cars light it their way (the template is the paint surface's own material).</summary>
    Material? CanvasMaterial(Material? template = null)
    {
        if (Vehicle is not { } v || game.Canvas(v) is not { } canvas) return null;
        if (canvasTex is null || canvas.Version != canvasVersion)
        {
            var img = Image.CreateFromData(PaintCanvas.Size, PaintCanvas.Size, false, Image.Format.Rgb8, canvas.Pixels);
            if (canvasTex is null) canvasTex = ImageTexture.CreateFromImage(img);
            else canvasTex.Update(img);
            canvasVersion = canvas.Version;
        }
        if (template is ShaderMaterial sm && OrigLook.IsOrig(sm))
        {
            if (!canvasOrig.TryGetValue(sm, out var om)) canvasOrig[sm] = om = OrigLook.WithTexture(sm, canvasTex);
            return om;
        }
        return canvasMat ??= new StandardMaterial3D
        {
            ResourceName = Car.PaintMaterial,
            AlbedoTexture = canvasTex,
            Metallic = 0.35f,
            Roughness = 0.38f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
    }

    /// <summary>After paint went on: show the new paint picture.</summary>
    public void RefreshPaint() => CanvasMaterial();

    /// <summary>The model of the part in a slot (for painting it).</summary>
    public Node3D? ModelOf(string slotId) => slots.TryGetValue(slotId, out var sv) ? sv.Model : null;

    /// <summary>The fixed body shell.</summary>
    public IEnumerable<Node3D> BodyNodes()
    {
        if (body is not null) yield return body;
        foreach (var (node, region) in statics)
            if (region == Region.Body) yield return node;
    }

    public string PaintMaterialName => Car.PaintMaterial;

    void PaintNode(Node3D node, string color)
    {
        string paintName = Car.PaintMaterial;
        foreach (var mi in ModelData.MeshesOf(node))
        {
            if (mi.Mesh is null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.Mesh.SurfaceGetMaterial(s);
                if (mat is null || mat.ResourceName != paintName) continue;
                mi.SetSurfaceOverrideMaterial(s, CanvasMaterial(mat) ?? PaintMaterial(mat, color));
            }
        }
    }

    Material PaintMaterial(Material template, string color)
    {
        if (paintMats.TryGetValue((color, template), out var m)) return m;
        if (template is ShaderMaterial sm && OrigLook.IsOrig(sm)) m = OrigLook.Recolored(sm, Conv.Hex(color));
        else
        {
            var std = template is StandardMaterial3D t ? (StandardMaterial3D)t.Duplicate() : new StandardMaterial3D();
            std.AlbedoColor = Conv.Hex(color);
            std.Metallic = Mathf.Max(std.Metallic, 0.35f);
            std.Roughness = Mathf.Min(std.Roughness, 0.38f);
            m = std;
        }
        m.ResourceName = Car.PaintMaterial;
        paintMats[(color, template)] = m;
        return m;
    }

    // ---- decals -------------------------------------------------------------------------------

    void BuildDecals(VehicleState v)
    {
        foreach (var c in decalsRoot.GetChildren()) c.QueueFree();
        foreach (var d in v.Decals)
        {
            var def = game.CI.Pack.Decals.Find(x => x.Id == d.DecalId);
            if (def is null || assets.Texture(def.Texture) is not { } tex) continue;
            var n = d.Normal.V().Normalized();
            var decal = new Decal
            {
                TextureAlbedo = Flipped(tex, d.FlipX, d.FlipY),
                Size = new Vector3((float)(d.Size * def.Aspect), 0.3f, (float)d.Size),
                CullMask = BodyLayer,
                NormalFade = 0.35f,
                UpperFade = 0.1f,
                LowerFade = 0.1f,
                Position = d.Pos.V(),
                Basis = Conv.FromUp(n) * new Basis(Vector3.Up, (float)d.Angle),
                // A decal that takes a colour is drawn in it (white turns into the colour).
                Modulate = d.Color is { } c ? Color.FromHtml(c) : Colors.White,
            };
            decalsRoot.AddChild(decal);
        }
    }

    Texture2D Flipped(Texture2D tex, bool fx, bool fy)
    {
        if (!fx && !fy) return tex;
        if (flipped.TryGetValue((tex, fx, fy), out var t)) return t;
        var img = tex.GetImage();
        if (img.IsCompressed()) img.Decompress();
        if (fx) img.FlipX();
        if (fy) img.FlipY();
        return flipped[(tex, fx, fy)] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The decal that follows the pointer while you aim. With the original's look as the original draws it (measured on
    /// its captures: a star, a flag): the picture flat where the pointer is, lying on the surface there, in its colours
    /// unlit and its pure black left out, every other pixel of the view in a checkerboard, over whatever is behind (the
    /// star's arm went on past the car's edge). Our own look projects it onto the car, see-through.
    /// </summary>
    public void ShowDecalPreview(Texture2D tex, Vector3 worldPos, Vector3 worldNormal, float angle, float size, float aspect, bool fx, bool fy, Color? tint = null)
    {
        var inv = carNode.GlobalTransform.AffineInverse();
        var basis = Conv.FromUp((inv.Basis * worldNormal).Normalized()) * new Basis(Vector3.Up, angle);
        if (!ModernLook.On)
        {
            if (stipple is null)
            {
                stipple = new MeshInstance3D { Mesh = new QuadMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = new ShaderMaterial { Shader = StippleShader } };
                carNode.AddChild(stipple);
            }
            ((QuadMesh)stipple.Mesh).Size = new Vector2(size * aspect, size);
            var m = (ShaderMaterial)stipple.MaterialOverride;
            m.SetShaderParameter("tex", Flipped(tex, fx, fy));
            m.SetShaderParameter("tint", tint ?? Colors.White);
            // The quad's face turned to lie in the decal's plane (its up is the picture's top, as the decal's -Z).
            stipple.Transform = new Transform3D(basis * new Basis(Vector3.Right, -Mathf.Pi / 2), inv * worldPos);
            stipple.Visible = true;
            return;
        }
        if (preview is null)
        {
            preview = new Decal { CullMask = BodyLayer, NormalFade = 0.35f, UpperFade = 0.1f, LowerFade = 0.1f, Modulate = new Color(1, 1, 1, 0.55f) };
            carNode.AddChild(preview);
        }
        preview.TextureAlbedo = Flipped(tex, fx, fy);
        preview.Size = new Vector3(size * aspect, 0.3f, size);
        preview.Position = inv * worldPos;
        preview.Basis = basis;
        preview.Modulate = (tint ?? Colors.White) with { A = 0.55f };
        preview.Visible = true;
    }

    public void HideDecalPreview()
    {
        if (preview is not null) preview.Visible = false;
        if (stipple is not null) stipple.Visible = false;
    }

    MeshInstance3D? stipple;

    static Shader? stippleShader;
    static Shader StippleShader => stippleShader ??= new Shader
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled;

            uniform sampler2D tex : source_color, filter_nearest;
            uniform vec4 tint : source_color = vec4(1.0);

            void fragment() {
            	vec4 c = texture(tex, UV);
            	// The picture's ground (clear, or pure black as the original's pictures have it) is not drawn.
            	if (c.a < 0.5 || max(c.r, max(c.g, c.b)) < 0.012) discard;
            	// Every other pixel of the view.
            	if (((int(FRAGCOORD.x) + int(FRAGCOORD.y)) & 1) == 0) discard;
            	ALBEDO = c.rgb * tint.rgb;
            }
            """,
    };

    // ---- ghosts (install preview) ------------------------------------------------------------

    public void ShowGhosts(string partId, IEnumerable<(string Id, bool Ok)> slotList)
    {
        ClearGhosts();
        var def = game.CI.Part(partId);
        foreach (var (id, ok) in slotList)
        {
            if (!slots.TryGetValue(id, out var sv) || assets.Model(def.Model) is not { } model) continue;
            var g = model.Instantiate(shadows: false);
            foreach (var mi in ModelData.MeshesOf(g)) mi.MaterialOverride = ok ? GhostOkMat : GhostBlockedMat;
            sv.Pivot.AddChild(g);
            sv.Ghost = g;
            // An invisible box around the outline, so thin parts are easy to click.
            sv.GhostTarget = new PickTarget(g, new GhostPick(id)) { Proxy = model.Bounds.Grow(0.02f) };
            targets.Add(sv.GhostTarget);
        }
    }

    public void ClearGhosts()
    {
        foreach (var sv in slots.Values)
        {
            if (sv.Ghost is null) continue;
            if (hovered == sv.Ghost) hovered = null;
            sv.Ghost.QueueFree();
            sv.Ghost = null;
            if (sv.GhostTarget is not null) targets.Remove(sv.GhostTarget);
            sv.GhostTarget = null;
        }
    }

    public IEnumerable<string> GhostSlots => slots.Values.Where(s => s.Ghost is not null).Select(s => s.Def.Id);

    // ---- highlight / focus ----------------------------------------------------------------------

    public void Hover(Pick? pick)
    {
        var obj = pick is null ? null : ObjectFor(pick);
        if (obj == hovered && pick == hoveredPick) return;
        if (hovered is not null && IsInstanceValid(hovered)) Unhighlight(hovered, hoveredPick);
        hovered = obj;
        hoveredPick = pick;
        if (obj is not null && pick is not null) Highlight(obj, pick);
    }

    void Highlight(Node3D obj, Pick pick)
    {
        if (pick is FastenerPick fp)
        {
            bool ghost = slots.TryGetValue(fp.SlotId, out var sv) && fp.Index < sv.FastenerGhost.Length && sv.FastenerGhost[fp.Index];
            foreach (var mi in ModelData.MeshesOf(obj)) mi.MaterialOverride = HiliteSolid;
            return;
        }
        if (pick is GhostPick || xray) return;
        if (pick is PartPick pp && Vehicle?.Slots.GetValueOrDefault(pp.SlotId)?.Part is { } part)
        {
            foreach (var mi in ModelData.MeshesOf(obj))
            {
                if (OrigLookCar)
                {
                    mi.MaterialOverride = null;
                    for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
                        mi.SetSurfaceOverrideMaterial(i, OrigLook.HoverFor(mi.Mesh?.SurfaceGetMaterial(i), part.Condition));
                }
                else mi.MaterialOverride = HoverMats[Math.Clamp(part.Condition, 0, 3)];
            }
            return;
        }
        foreach (var mi in ModelData.MeshesOf(obj)) mi.MaterialOverlay = HiliteOverlay;
    }

    void Unhighlight(Node3D obj, Pick? pick)
    {
        if (pick is FastenerPick fp)
        {
            bool ghost = slots.TryGetValue(fp.SlotId, out var sv) && fp.Index < sv.FastenerGhost.Length && sv.FastenerGhost[fp.Index];
            foreach (var mi in ModelData.MeshesOf(obj)) mi.MaterialOverride = ghost ? HoleMat : null;
            return;
        }
        SlotView? psv = pick is PartPick pp && slots.TryGetValue(pp.SlotId, out var found) ? found : null;
        foreach (var mi in ModelData.MeshesOf(obj))
        {
            mi.MaterialOverlay = null;
            if (psv is not null) mi.MaterialOverride = BaseOverride(psv);
        }
        // The part's own surfaces (and paint) back, or its condition's colour if Show Condition is on for it.
        if (psv is not null) ApplyXray(psv);
    }

    public Node3D? ObjectFor(Pick pick) => pick switch
    {
        PartPick p => slots.GetValueOrDefault(p.SlotId)?.Model,
        GhostPick g => slots.GetValueOrDefault(g.SlotId)?.Ghost,
        FastenerPick f => slots.TryGetValue(f.SlotId, out var sv) && f.Index < sv.Fasteners.Length ? sv.Fasteners[f.Index] : null,
        _ => null,
    };

    /// <summary>World-space bounds of a slot's part, if it is on.</summary>
    public Aabb? SlotBounds(string slotId) => slots.TryGetValue(slotId, out var sv) && sv.Model is { } m ? WorldBounds(m) : null;

    /// <summary>The region a slot belongs to.</summary>
    public Region? SlotRegion(string slotId) => slots.TryGetValue(slotId, out var sv) ? sv.Def.Region : null;

    /// <summary>The middle of a part's bolts in world space (what bolt mode looks at), or null without any drawn.</summary>
    public Vector3? BoltCentre(string slotId)
    {
        if (!slots.TryGetValue(slotId, out var sv)) return null;
        var pts = sv.Fasteners.Where(f => f is not null && f.IsInsideTree()).Select(f => f!.GlobalPosition).ToList();
        return pts.Count == 0 ? null : pts.Aggregate(Vector3.Zero, (a, b) => a + b) / pts.Count;
    }

    /// <summary>World-space center of a slot's part (or of the empty slot).</summary>
    public Vector3 SlotCenter(string slotId)
    {
        if (!slots.TryGetValue(slotId, out var sv)) return GlobalPosition;
        var target = sv.Model ?? sv.Ghost;
        if (target is not null && WorldBounds(target) is { } box) return box.GetCenter();
        return sv.Holder.GlobalPosition;
    }

    /// <summary>World-space bounds of the parts shown for a tab (measured: the original frames the parts that are
    /// on the car, zooming in when one comes off); where none is on, the places they go.</summary>
    public Aabb? Bounds(View forView)
    {
        Aabb? box = null, places = null;
        void Add(Aabb b) => box = box is { } x ? x.Merge(b) : b;
        foreach (var sv in slots.Values)
        {
            if (forView != View.Complete && sv.Def.Region.ToView() != forView) continue;
            if (sv.Model is not null && WorldBounds(sv.Model) is { } b) Add(b);
            else
            {
                var p = new Aabb(sv.Holder.GlobalPosition, Vector3.Zero);
                places = places is { } x ? x.Merge(p) : p;
            }
        }
        if (forView == View.Complete && body is not null && WorldBounds(body) is { } bb) Add(bb);
        foreach (var (node, region) in statics)
            if ((forView == View.Complete || region.ToView() == forView) && WorldBounds(node) is { } sb) Add(sb);
        return box ?? places;
    }

    // ---- the engine running --------------------------------------------------------------------------

    readonly List<(Node3D Mesh, Transform3D Home)> spinning = [];
    float spinAngle;

    /// <summary>Two turns a second: the original's speed was not measured (its frames only show the parts turned).</summary>
    const float SpinSpeed = Mathf.Tau * 2;

    /// <summary>
    /// The engine runs: the parts that turn with it (the original's spinners: crankshaft, flywheel, fan) turn about
    /// their own axis (measured: in the frames of a start only the crank's pulley changed, besides the whole engine
    /// shaking). In the original's cars every spinner's mesh lies centred on its own Z axis (the crankshafts long along
    /// it, the flywheels and fans flat across it), along the car in a V8 and across it in the Escort's transverse four.
    /// </summary>
    public void Spin(double delta)
    {
        if (spinning.Count == 0 && Vehicle is { } v)
            foreach (var sv in slots.Values)
                if (sv.Model is { } m && v.Slots[sv.Def.Id].Part is { } part && game.CI.Part(part.PartId).Spins)
                    foreach (var mi in ModelData.MeshesOf(m)) spinning.Add((mi, mi.Transform));
        spinAngle = (spinAngle + (float)delta * SpinSpeed) % Mathf.Tau;
        var turn = new Transform3D(new Basis(Vector3.Back, spinAngle), Vector3.Zero);
        foreach (var (mesh, home) in spinning)
            if (IsInstanceValid(mesh)) mesh.Transform = home * turn;
    }

    /// <summary>The engine has stopped: the turning parts back as they were.</summary>
    public void StopSpin()
    {
        foreach (var (mesh, home) in spinning)
            if (IsInstanceValid(mesh)) mesh.Transform = home;
        spinning.Clear();
        spinAngle = 0;
    }

    /// <summary>Lab: every triangle of the car as it is drawn now, in world space, part by part (for fitting
    /// cameras to the original's screenshots by the car's outline). A text file: "g name region" starts a part,
    /// then one triangle per line, nine numbers.</summary>
    public void ExportTriangles(string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var w = new System.IO.StreamWriter(path);
        void Write(string name, string region, Node3D node)
        {
            if (!node.IsVisibleInTree()) return;
            w.WriteLine($"g {name} {region}");
            foreach (var mi in ModelData.MeshesOf(node))
            {
                if (mi.Mesh is null || !mi.IsVisibleInTree()) continue;
                var xf = mi.GlobalTransform;
                var f = mi.Mesh.GetFaces();
                for (int i = 0; i + 2 < f.Length; i += 3)
                {
                    var a = xf * f[i];
                    var b = xf * f[i + 1];
                    var c = xf * f[i + 2];
                    w.WriteLine(string.Format(inv, "{0:0.#####} {1:0.#####} {2:0.#####} {3:0.#####} {4:0.#####} {5:0.#####} {6:0.#####} {7:0.#####} {8:0.#####}",
                        a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z));
                }
            }
        }
        if (body is not null) Write("body", "Body", body);
        foreach (var (node, region) in statics) Write("static", region.ToString(), node);
        foreach (var sv in slots.Values)
            if (sv.Model is not null) Write(sv.Def.Id, sv.Def.Region.ToString(), sv.Model);
    }

    /// <summary>Our own look's contact shadows: the wheels on the car and shown, with their bounds in the world.</summary>
    public IEnumerable<(string SlotId, Aabb Box)> ShownWheels()
    {
        foreach (var (id, sv) in slots)
            if (sv.Model is { } m && m.IsVisibleInTree() && sv.Def.Family.Equals("wheel", StringComparison.OrdinalIgnoreCase) && WorldBounds(m) is { } b)
                yield return (id, b);
    }

    static Aabb? WorldBounds(Node3D node)
    {
        Aabb? box = null;
        foreach (var mi in ModelData.MeshesOf(node))
        {
            if (mi.Mesh is null) continue;
            var b = mi.GlobalTransform * mi.Mesh.GetAabb();
            box = box is { } x ? x.Merge(b) : b;
        }
        return box;
    }
}
