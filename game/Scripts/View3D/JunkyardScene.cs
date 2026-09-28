using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using OpenGG.Ui;

namespace OpenGG.View3D;

/// <summary>A part lying in the JunkYard.</summary>
public sealed record JunkPick(string ItemId) : Pick;

/// <summary>
/// The JunkYard as in the original: an outdoor yard where the car model's used parts lie in a long row
/// (engine parts on wooden shelves, body and running gear parts on concrete slabs). The camera slides
/// along the row; the part under the pointer lights up in its condition colour.
/// </summary>
public partial class JunkyardScene : StageScene
{
    const float Gap = 0.35f;
    const float ShelfY = 0.75f;

    readonly Node3D row = new() { Name = "Row" };
    readonly List<PickTarget> targets = [];
    readonly Dictionary<string, (Node3D Node, int Condition)> items = [];
    /// <summary>Each part's own look under the pointer's highlight: its bare metal, or null (painted).</summary>
    readonly Dictionary<string, Material?> looks = [];
    string? hovered;
    float rowEnd;
    public Game Game { get; set; }
    public Region Area { get; private set; } = Region.Engine;
    /// <summary>Where the camera is along its path (metres from the start).</summary>
    public float Pan { get; private set; }
    /// <summary>How far the camera can go: the original's yard to the end of its path (play_02), ours to the row's end.</summary>
    public float PanMax => area is { } oa && OriginalYard ? oa.Length : Mathf.Max(0, rowEnd - 1.44f);

    /// <summary>
    /// How fast the camera glides along while ◄ or ► (or an arrow key) is held, in metres a second. Measured on the
    /// original's yard: a click moved it 15 of the scene's units (one frame), a key held for 0.7 s about 315, and a
    /// held ► stopped at the path's end.
    /// </summary>
    public const float GlideSpeed = 450 * (float)Core.Original.OrigSpace.MetersPerUnit;

    public JunkyardScene(AssetStore assets, Game game) : base(assets)
    {
        Game = game;
        Name = "JunkYard";
        Camera.Fov = 40;
    }

    /// <summary>An area of the original's yard: its scene, the boards or slabs the parts go on (in the
    /// scene's space), and the camera's path (from play_01 towards play_02, looking at CamFocus).</summary>
    sealed class OrigArea
    {
        public required Node3D Node;
        public List<Aabb> Shelves = [];
        public Vector3 Eye, Focus;
        public float Length;
    }

    readonly Dictionary<Region, OrigArea> origAreas = [];
    OrigArea? area;

    OrigArea? Orig(Region r)
    {
        if (origAreas.TryGetValue(r, out var a)) return a;
        var id = r switch { Region.Engine => "junk.engine", Region.Body => "junk.body", _ => "junk.running_gear" };
        if (UiSkin.Scene(id) is not { } m) return null;
        static bool Helper(string n) => n.StartsWith("play_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("CamFocus", StringComparison.OrdinalIgnoreCase);
        var node = m.Build(ShowroomScene.FromScene, name => UiSkin.SceneTexture(id, name), mesh => !Helper(mesh.Name), light: YardLight);
        node.Visible = false;
        AddChild(node);
        a = new OrigArea { Node = node };
        foreach (var s in new[] { "Shelf01", "Shelf02" })
            if (m.Find(s) is { } shelf) a.Shelves.Add(shelf.Bounds());
        var p1 = m.Find("play_01")?.Bounds().GetCenter() ?? Vector3.Zero;
        var p2 = m.Find("play_02")?.Bounds().GetCenter() ?? p1;
        a.Eye = p1;
        a.Focus = m.Find("CamFocus")?.Bounds().GetCenter() ?? p1 + new Vector3(0, 100, -50);
        a.Length = (ShowroomScene.FromScene(p2) - ShowroomScene.FromScene(p1)).X;
        origAreas[r] = a;
        return a;
    }

    /// <summary>With a skin that has the original's yard: its three scenes instead of ours.</summary>
    bool OriginalYard => UiSkin.Scene("junk.engine") is not null;

    /// <summary>
    /// How the original lights its yard, fitted on its captures against the colours it shows: the scene's surfaces (the
    /// ground, the slabs, the shelf's boards and frame) on twelve captures, and the parts of two cars' shelves put where
    /// the original put them (7 views, 41,000 pixels). One light for both, from the side of the rows' ends and from
    /// below the ground, so nothing facing up or towards the camera catches it; the scene takes ambient 0.590 and 0.648
    /// of it. The scenes have no lamps.
    /// </summary>
    static readonly Vector3 TowardsYardLight = new(0.606f, -0.483f, 0.632f);
    static readonly Model3ds.SceneLight YardLight = new(0.590f, 0.648f, TowardsYardLight);

    /// <summary>
    /// The parts lying in the yard: the same light, in the cars' doubled terms ambient 0.307 and 0.276 of it (about half
    /// the scene's, as on the Car Lot; 5.4 off on average in the colour numbers, 17 with the scene's light halved from
    /// the side it seemed to come from), turned into the camera's space, and without the cars' highlight (a painted
    /// part shows its paint's own colour: a red hood 156 of red and nothing of green or blue).
    /// </summary>
    static readonly Model3ds.SceneLight PartsLight = new(0.614f, 0.552f, TowardsYardLight);

    public void LightParts()
    {
        if (!OriginalYard || area is null) OrigLook.UseWorkshop();
        else OrigLook.UseScene(PartsLight, Camera.GlobalTransform.Basis.Inverse() * PartsLight.TowardsLight, highlight: false);
    }

    public override void _Ready()
    {
        if (OriginalYard)
        {
            AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(0.55f, 0.68f, 0.85f),
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = Colors.White,
                    AmbientLightEnergy = 0.6f,
                },
            });
            AddChild(new DirectionalLight3D { LightEnergy = 0.9f, Transform = Transform3D.Identity.LookingAt(new Vector3(-0.4f, -1, -0.5f), Vector3.Up) });
            // As everywhere in the original's engine: the view 45° tall.
            Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            Camera.Fov = 45;
            Camera.Far = 200;
            Orbit.MinDistance = 0.01f;
            Orbit.MaxDistance = 1000;
            Orbit.MinPitch = -1.5f;
            Orbit.MaxPitch = 1.5f;
            Orbit.MinHeight = -100;
            AddChild(row);
            return;
        }
        AddChild(MakeEnvironment(new Color(0.55f, 0.68f, 0.85f), new Color(0.85f, 0.87f, 0.9f), 0.8f, sky: true, fogDensity: 0.01f));
        var sun = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.95f, 0.85f),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 25,
            Transform = Transform3D.Identity.LookingAt(new Vector3(-3, -8, -5), Vector3.Up).Translated(new Vector3(3, 8, 5)),
        };
        if (ModernLook.On) ModernLook.Soften(sun);
        AddChild(sun);
        // Packed dirt, and a chain-link fence behind the row.
        var dirt = ModernLook.On ? ModernLook.Surface("Ground109", 3f, new Vector2(400, 60)) : null;
        if (dirt is null)
        {
            dirt = TexturedMat("tex.concrete", new Vector2(60, 10), 1f);
            dirt.AlbedoColor = new Color(0.72f, 0.6f, 0.46f);
        }
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 60) }, MaterialOverride = dirt, Position = new Vector3(150, 0, 0) });
        var fence = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.41f, 0.4f), Metallic = 0.5f, Roughness = 0.7f };
        for (int i = -4; i < 120; i++)
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 2.4f }, Position = new Vector3(i * 3f, 1.2f, -4.5f), MaterialOverride = fence });
        // Our own look: the fence's wire mesh; else a see-through grey sheet.
        if (ModernLook.On && ModernLook.ChainLink(new Vector2(370, 2.2f), 1.1f) is { } mesh)
            AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(370, 2.2f) }, Position = new Vector3(170, 1.2f, -4.5f), MaterialOverride = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        else
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(370, 2.2f, 0.02f) },
                Position = new Vector3(170, 1.2f, -4.5f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.36f, 0.35f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.9f },
            });
        AddChild(row);
        Look(animate: false);
    }

    /// <summary>
    /// An area's row as laid out in this visit to the yard: where each part lies, and where the next one would go (how
    /// far along, and on which board or slab of the original's yard). Measured: for the whole visit the parts keep their
    /// places, another area looked at and back again included; a part bought leaves its place empty and one given back
    /// goes back to it. The next visit lays the rows out afresh from the shelf.
    /// </summary>
    sealed class Layout
    {
        public readonly Dictionary<string, Transform3D> Laid = [];
        public float NextX;
        public int NextShelf;
    }

    readonly Dictionary<Region, Layout> layouts = [];
    Layout lay = new();

    /// <summary>A new visit to the yard: its rows are laid out afresh from the shelf.</summary>
    public void NewVisit() => layouts.Clear();

    /// <summary>Lays out the parts of one area of the yard.</summary>
    public void Show(IEnumerable<JunkItem> stock, Region area)
    {
        Area = area;
        LightParts();
        foreach (var c in row.GetChildren()) c.QueueFree();
        targets.Clear();
        items.Clear();
        looks.Clear();
        hovered = null;
        bool fresh = !layouts.TryGetValue(area, out var known);
        lay = known ?? (layouts[area] = new Layout());
        if (OriginalYard && Orig(area) is { } oa)
        {
            ShowOriginal(stock, oa, fresh);
            return;
        }
        this.area = null;
        bool shelf = area == Region.Engine;
        var wood = (ModernLook.On ? ModernLook.Surface("Planks037A", 1.2f, mean: new Color(0.5f, 0.36f, 0.22f), triplanar: true) : null)
            ?? new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.36f, 0.22f), Roughness = 0.9f };
        var slab = (ModernLook.On ? ModernLook.Surface("Asphalt031", 1.5f, mean: new Color(0.62f, 0.61f, 0.58f), triplanar: true, bumps: 0.5f) : null)
            ?? new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.61f, 0.58f), Roughness = 0.95f };
        foreach (var it in stock) Lay(it);
        rowEnd = lay.NextX;
        // The shelf or the slab the row lies on.
        var under = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Mathf.Max(3, rowEnd + 1), shelf ? 0.06f : 0.04f, 1.4f) },
            Position = new Vector3(rowEnd / 2 - 0.3f, shelf ? ShelfY - 0.03f : 0.02f, 0),
            MaterialOverride = shelf ? wood : slab,
        };
        row.AddChild(under);
        if (shelf)
            for (float lx = -0.3f; lx <= rowEnd + 0.4f; lx += 1.6f)
                foreach (float lz in new[] { -0.62f, 0.62f })
                    row.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.07f, ShelfY, 0.07f) }, Position = new Vector3(lx, ShelfY / 2, lz), MaterialOverride = wood });
        Pan = Mathf.Clamp(Pan, 0, PanMax);
        Look(animate: false);
    }

    /// <summary>
    /// The original's yard, as measured on its captures (two cars' shelves, all three areas): the parts in the shelf's
    /// order along the first board or slab, each turned so that its longest side runs back across the board (a half
    /// turn, and a quarter more for a part longer than it is wide), centred across the board and lying on it. From one
    /// part's middle to the next the row moves on by 0.25 m and half the next part's length (the part before does not
    /// count), starting 0.2 m in from the board's end.
    /// </summary>
    const float RowStart = 0.2f, RowGap = 0.25f;

    /// <summary>Lab: parts placed by hand (item id → turn in degrees, and where the middle of its box goes across and in
    /// depth), to compare them with the original's captures where it put them.</summary>
    public static Dictionary<string, (float Yaw, float X, float Z)>? LabPoses;

    void ShowOriginal(IEnumerable<JunkItem> stock, OrigArea oa, bool fresh)
    {
        foreach (var a in origAreas.Values) a.Node.Visible = a == oa;
        area = oa;
        Pan = Mathf.Clamp(Pan, 0, PanMax);
        Look(animate: false);
        LightParts();
        if (fresh) lay.NextX = oa.Shelves.Count > 0 ? ShowroomScene.FromScene(oa.Shelves[0].Position).X + RowStart : 0;
        foreach (var it in stock) Lay(it);
        Pan = Mathf.Clamp(Pan, 0, PanMax);
        Look(animate: false);
    }

    /// <summary>
    /// The area's parts changed while it is on show. Measured: a part bought leaves the others where they lie (its
    /// place stays empty), and one given back from the Purchase Bin goes back to its place.
    /// </summary>
    public void Restock(IEnumerable<JunkItem> stock)
    {
        var now = stock.ToList();
        var keep = now.Select(j => j.Id).ToHashSet();
        foreach (var id in items.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            var (node, _) = items[id];
            if (hovered == id) hovered = null;
            targets.RemoveAll(t => t.Pick is JunkPick jp && jp.ItemId == id);
            items.Remove(id);
            looks.Remove(id);
            node.QueueFree();
        }
        foreach (var it in now.Where(j => !items.ContainsKey(j.Id))) Lay(it);
    }

    /// <summary>Puts a part in the yard: back in its place if it lay there in this visit, else next along the row.</summary>
    void Lay(JunkItem it)
    {
        var def = Game.CI.Part(it.Part.PartId);
        if (Assets.Model(def.Model) is not { } model) return;
        Node3D node;
        if (OriginalYard && area is { } oa)
        {
            if (oa.Shelves.Count == 0) return;
            node = model.Instantiate(shadows: false);
            if (lay.Laid.TryGetValue(it.Id, out var was)) node.Transform = was;
            else
            {
                Vector3 Start(int i) => ShowroomScene.FromScene(oa.Shelves[i].Position);
                Vector3 End(int i) => ShowroomScene.FromScene(oa.Shelves[i].End);
                var b = model.Bounds;
                var turn = new Basis(Vector3.Up, b.Size.X > b.Size.Z ? Mathf.Pi * 1.5f : Mathf.Pi);
                float length = Mathf.Max(b.Size.X, b.Size.Z);
                if (lay.NextX + RowGap + length / 2 > End(lay.NextShelf).X && lay.NextShelf + 1 < oa.Shelves.Count)
                {
                    lay.NextShelf++;
                    lay.NextX = Start(lay.NextShelf).X + RowStart;
                }
                lay.NextX += RowGap + length / 2;
                var s0 = Start(lay.NextShelf);
                var s1 = End(lay.NextShelf);
                var mid = turn * b.GetCenter();
                node.Transform = new Transform3D(turn, new Vector3(lay.NextX - mid.X, s0.Y - b.Position.Y, (s0.Z + s1.Z) / 2 - mid.Z));
                if (LabPoses is not null && LabPoses.TryGetValue(it.Id, out var pose))
                {
                    var t = new Basis(Vector3.Up, Mathf.DegToRad(pose.Yaw));
                    var m = t * b.GetCenter();
                    node.Transform = new Transform3D(t, new Vector3(pose.X - m.X, s0.Y - b.Position.Y, pose.Z - m.Z));
                }
            }
        }
        else
        {
            node = model.Instantiate(shadows: true);
            if (lay.Laid.TryGetValue(it.Id, out var was)) node.Transform = was;
            else
            {
                var b = model.Bounds;
                // Lie the part down with its longest side along the row, as if thrown there.
                float w = b.Size.X, d = b.Size.Z;
                if (d > w * 1.3f && d > 0.8f)
                {
                    node.RotateY(Mathf.Pi / 2);
                    (w, d) = (d, w);
                    b = new Aabb(new Vector3(b.Position.Z, b.Position.Y, -b.End.X), new Vector3(b.Size.Z, b.Size.Y, b.Size.X));
                }
                float baseY = Area == Region.Engine ? ShelfY : 0.02f;
                node.Position = new Vector3(lay.NextX - b.Position.X, baseY - b.Position.Y, -b.GetCenter().Z);
                lay.NextX += w + Gap;
            }
        }
        lay.Laid[it.Id] = node.Transform;
        row.AddChild(node);
        Dress(it.Id, node, it.Part);
        items[it.Id] = (node, it.Part.Condition);
        targets.Add(new PickTarget(node, new JunkPick(it.Id)));
    }

    /// <summary>Moves the camera along for a frame of a held arrow; false when it was at the end already.</summary>
    public bool Glide(int dir, double dt)
    {
        float to = Mathf.Clamp(Pan + dir * GlideSpeed * (float)dt, 0, PanMax);
        if (Mathf.IsEqualApprox(to, Pan)) return false;
        Pan = to;
        Look(animate: false);
        return true;
    }

    /// <summary>Lab: the camera this far along its path (metres).</summary>
    public void SetPan(float metres)
    {
        Pan = Mathf.Clamp(metres, 0, PanMax);
        Look(animate: false);
    }

    /// <summary>Lab: every part in the yard, "g" and its item id, part id and where it stands (its placement as 12 numbers,
    /// basis columns then origin), then its triangles in its own model's space.</summary>
    public void ExportItems(string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var w = new System.IO.StreamWriter(path);
        foreach (var (id, (node, _)) in items)
        {
            if (!IsInstanceValid(node)) continue;
            var partId = Game.Junk.Find(j => j.Id == id)?.Part.PartId ?? "?";
            var t = node.GlobalTransform;
            w.WriteLine(string.Format(inv, "g {0} {1} {2} {3} {4} {5} {6} {7} {8} {9} {10} {11} {12} {13}", id, partId,
                t.Basis.Column0.X, t.Basis.Column0.Y, t.Basis.Column0.Z, t.Basis.Column1.X, t.Basis.Column1.Y, t.Basis.Column1.Z,
                t.Basis.Column2.X, t.Basis.Column2.Y, t.Basis.Column2.Z, t.Origin.X, t.Origin.Y, t.Origin.Z));
            var toLocal = t.AffineInverse();
            foreach (var mi in ModelData.MeshesOf(node))
            {
                if (mi.Mesh is null) continue;
                var xf = toLocal * mi.GlobalTransform;
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
    }

    /// <summary>Lab: the camera (origin, basis rows, field of view).</summary>
    public string Geometry()
    {
        static string V(Vector3 v) => $"{v.X:0.0000},{v.Y:0.0000},{v.Z:0.0000}";
        var t = Camera.GlobalTransform;
        return $"cam {V(t.Origin)} x {V(t.Basis.X)} y {V(t.Basis.Y)} z {V(t.Basis.Z)} fov {Camera.Fov:0.00} keep {Camera.KeepAspect} pan {Pan:0.000}";
    }

    public void ResetPan()
    {
        Pan = 0;
        Look(animate: false);
    }

    void Look(bool animate)
    {
        if (OriginalYard && area is { } oa)
        {
            var along = new Vector3(Pan, 0, 0);
            Orbit.MoveTo(ShowroomScene.FromScene(oa.Eye) + along, ShowroomScene.FromScene(oa.Focus) + along, animate);
            return;
        }
        float cx = Pan + 0.9f;
        float y = Area == Region.Engine ? ShelfY + 0.15f : 0.2f;
        Orbit.MoveTo(new Vector3(cx, y + 0.85f, 2.1f), new Vector3(cx, y, 0), animate);
    }

    /// <summary>Checks: each part on show and a point of the view where a click picks it (the box's middle or near it).</summary>
    public List<(string Id, Vector2 At)> ItemPoints()
    {
        var list = new List<(string, Vector2)>();
        Vector2[] offsets = [Vector2.Zero, new(5, 0), new(-5, 0), new(0, 5), new(0, -5), new(10, 5), new(-10, -5), new(10, -5), new(-10, 5), new(0, 12), new(0, -12)];
        foreach (var (id, (node, _)) in items)
        {
            if (!IsInstanceValid(node) || !node.IsInsideTree()) continue;
            Aabb? box = null;
            foreach (var mi in ModelData.MeshesOf(node))
            {
                var b = mi.GlobalTransform * mi.GetAabb();
                box = box is { } x ? x.Merge(b) : b;
            }
            var world = box?.GetCenter() ?? node.GlobalPosition;
            if (Camera.IsPositionBehind(world)) continue;
            var p = Camera.UnprojectPosition(world);
            foreach (var d in offsets)
                if (Pick(p + d) == id)
                {
                    list.Add((id, p + d));
                    break;
                }
        }
        return list;
    }

    /// <summary>Checks: where each part on show lies.</summary>
    public Dictionary<string, Vector3> Places() =>
        items.Where(x => IsInstanceValid(x.Value.Node)).ToDictionary(x => x.Key, x => x.Value.Node.GlobalPosition);

    public string? Pick(Vector2 viewportPos)
    {
        var hit = Picker.Cast(targets, Camera.ProjectRayOrigin(viewportPos), Camera.ProjectRayNormal(viewportPos));
        return hit?.Pick is JunkPick jp ? jp.ItemId : null;
    }

    /// <summary>
    /// A part in the yard looks as it would on a car in the WorkShop (measured on the original's yard): damaged, all
    /// bare metal in its condition's colour; good, its own materials with its paint in the colour the car model itself
    /// is painted (the original's cars: their paint materials; a pickup's blue fender, a Cadillac's red hood).
    /// </summary>
    void Dress(string itemId, Node3D node, PartInstance part)
    {
        Material? look = null;
        bool orig = ModelData.MeshesOf(node).Any(mi => mi.Mesh is { } m && m.GetSurfaceCount() > 0 && OrigLook.IsOrig(m.SurfaceGetMaterial(0)));
        if (part.Condition < Condition.Good) look = VehicleView.BareMetal(part.Condition, orig);
        else if (Game.State.JunkFor is { } carId && Game.CI.HasCar(carId))
        {
            var car = Game.CI.Car(carId);
            var colour = Conv.Hex(car.OwnPaint is { Length: > 0 } own ? own : car.DefaultPaint);
            foreach (var mi in ModelData.MeshesOf(node))
            {
                if (mi.Mesh is null) continue;
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if (mi.Mesh.SurfaceGetMaterial(s) is { } mat && mat.ResourceName == car.PaintMaterial)
                        mi.SetSurfaceOverrideMaterial(s, mat is ShaderMaterial sm && OrigLook.IsOrig(sm)
                            ? OrigLook.Recolored(sm, colour)
                            : new StandardMaterial3D { AlbedoColor = colour, Metallic = 0.35f, Roughness = 0.38f });
            }
        }
        looks[itemId] = look;
        foreach (var mi in ModelData.MeshesOf(node)) mi.MaterialOverride = look;
    }

    /// <summary>
    /// The part under the pointer in its condition's colour, as the original draws it: the colour added onto what is
    /// behind it, from both sides of every face (a closed part adds about twice a single sheet), lit like the yard's
    /// parts (<see cref="OrigLook.Condition"/>; fitted on 14 hovered parts of the pickup).
    /// </summary>
    public void Hover(string? itemId)
    {
        if (itemId == hovered) return;
        if (hovered is not null && items.TryGetValue(hovered, out var old) && IsInstanceValid(old.Node))
        {
            foreach (var mi in ModelData.MeshesOf(old.Node))
                for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++) mi.SetSurfaceOverrideMaterial(i, null);
            // Back to its own look: a good part's paint is on its surfaces, so it is dressed again.
            if (Game.Junk.Find(j => j.Id == hovered) is { } it) Dress(hovered, old.Node, it.Part);
            else
                foreach (var mi in ModelData.MeshesOf(old.Node)) mi.MaterialOverride = looks.GetValueOrDefault(hovered);
        }
        hovered = itemId;
        if (itemId is not null && items.TryGetValue(itemId, out var cur))
            foreach (var mi in ModelData.MeshesOf(cur.Node))
            {
                mi.MaterialOverride = null;
                for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
                    mi.SetSurfaceOverrideMaterial(i, OrigLook.YardConditionFor(mi.Mesh?.SurfaceGetMaterial(i), cur.Condition));
            }
    }
}
