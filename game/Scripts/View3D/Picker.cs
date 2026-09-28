using System.Collections.Generic;
using Godot;

namespace OpenGG.View3D;

/// <summary>What the cursor points at on a car.</summary>
public abstract record Pick;
public sealed record PartPick(string SlotId) : Pick;
public sealed record FastenerPick(string SlotId, int Index) : Pick;
public sealed record GhostPick(string SlotId) : Pick;
public sealed record BodyPick : Pick;

public readonly record struct Hit(Pick Pick, Vector3 Point, Vector3 Normal, float Distance);

/// <summary>A pickable object: its meshes (or a proxy box) and what picking it means.</summary>
public sealed class PickTarget(Node3D node, Pick pick)
{
    public Node3D Node { get; } = node;
    public Pick Pick { get; } = pick;
    /// <summary>Box in <see cref="Node"/> space used instead of the meshes (thin ghost parts, bolts).</summary>
    public Aabb? Proxy { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Ray picking against mesh triangles on the CPU (no physics bodies needed), through each mesh's
/// <see cref="MeshBvh"/>.</summary>
public static class Picker
{
    public static Hit? Cast(IEnumerable<PickTarget> targets, Vector3 origin, Vector3 dir)
    {
        dir = dir.Normalized();
        Hit? best = null;
        foreach (var t in targets)
        {
            if (!t.Enabled || !GodotObject.IsInstanceValid(t.Node) || !t.Node.IsVisibleInTree()) continue;
            if (t.Proxy is { } box)
            {
                var inv = t.Node.GlobalTransform.AffineInverse();
                var o = inv * origin;
                var d = inv.Basis * dir;
                if (RayBox(o, d, box, out float tt, out var n))
                {
                    var p = t.Node.GlobalTransform * (o + d * tt);
                    float dist = origin.DistanceTo(p);
                    if (best is null || dist < best.Value.Distance)
                        best = new Hit(t.Pick, p, (t.Node.GlobalTransform.Basis * n).Normalized(), dist);
                }
                continue;
            }
            foreach (var mi in MeshesUnder(t.Node))
            {
                if (mi.Mesh is null || !mi.IsVisibleInTree()) continue;
                var xf = mi.GlobalTransform;
                var inv = xf.AffineInverse();
                var o = inv * origin;
                var d = inv.Basis * dir;
                if (!RayBox(o, d, mi.Mesh.GetAabb().Grow(0.001f), out _, out _)) continue;
                // The world distance grows with the mesh-space t by this much (the transform may scale).
                float perT = (xf.Basis * d).Length();
                float maxT = best is { } sofar ? sofar.Distance / perT : float.PositiveInfinity;
                if (!MeshBvh.Of(mi.Mesh).Intersect(o, d, maxT, out float tt, out var a, out var b, out var c)) continue;
                var p = xf * (o + d * tt);
                var n = (xf.Basis * (b - a).Cross(c - a)).Normalized();
                if (n.Dot(dir) > 0) n = -n;
                best = new Hit(t.Pick, p, n, origin.DistanceTo(p));
            }
        }
        return best;
    }

    internal static IEnumerable<MeshInstance3D> MeshesUnder(Node node)
    {
        if (node is MeshInstance3D mi) yield return mi;
        foreach (var c in node.GetChildren())
        {
            if (c is Node3D n3 && n3.HasMeta("nopick")) continue;
            foreach (var m in MeshesUnder(c)) yield return m;
        }
    }

    sealed record PaintSurface(Vector3[] Verts, Vector2[] Uvs, Rect2 Cell);
    static readonly Dictionary<(Mesh, int), PaintSurface?> paintCache = [];

    static PaintSurface? PaintOf(Mesh mesh, int surface, string paintName)
    {
        if (paintCache.TryGetValue((mesh, surface), out var ps)) return ps;
        ps = null;
        if (mesh.SurfaceGetMaterial(surface) is { } mat && mat.ResourceName == paintName && mesh is ArrayMesh am)
        {
            var arrays = am.SurfaceGetArrays(surface);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            if (verts.Length > 0 && uvs.Length == verts.Length)
            {
                var cell = new Rect2(uvs[0], Vector2.Zero);
                foreach (var uv in uvs) cell = cell.Expand(uv);
                ps = new PaintSurface(verts, uvs, cell);
            }
        }
        paintCache[(mesh, surface)] = ps;
        return ps;
    }

    static readonly Dictionary<Mesh, int[]?> surfaceStarts = [];

    /// <summary>Where each surface's triangles start among the mesh's faces (as <see cref="Mesh.GetFaces"/> lists them,
    /// surface after surface); null when the counts don't add up to the faces.</summary>
    static int[]? SurfaceStarts(Mesh mesh)
    {
        if (surfaceStarts.TryGetValue(mesh, out var starts)) return starts;
        starts = null;
        if (mesh is ArrayMesh am)
        {
            int n = am.GetSurfaceCount();
            starts = new int[n + 1];
            for (int s = 0; s < n; s++)
            {
                int idx = am.SurfaceGetArrayIndexLen(s);
                starts[s + 1] = starts[s] + (idx > 0 ? idx : am.SurfaceGetArrayLen(s)) / 3;
            }
            if (starts[n] != MeshBvh.Of(mesh).Triangles) starts = null;
        }
        surfaceStarts[mesh] = starts;
        return starts;
    }

    /// <summary>The point of the paint picture on a mesh's triangle (its number among the mesh's faces, where on it by
    /// the weights of its second and third corners), with the square of the picture that panel uses; null when that
    /// triangle is not painted from the picture.</summary>
    public static (Vector2 Uv, Rect2 Cell)? PaintUvAt(Mesh mesh, int face, float wb, float wc, string paintName)
    {
        if (face < 0 || SurfaceStarts(mesh) is not { } starts) return null;
        int s = 0;
        while (s + 1 < starts.Length && face >= starts[s + 1]) s++;
        if (s + 1 >= starts.Length || PaintOf(mesh, s, paintName) is not { } ps) return null;
        int i = (face - starts[s]) * 3;
        if (i + 2 >= ps.Uvs.Length) return null;
        return (ps.Uvs[i] * (1 - wb - wc) + ps.Uvs[i + 1] * wb + ps.Uvs[i + 2] * wc, ps.Cell);
    }

    /// <summary>
    /// The point of the paint picture under a ray, on the paint faces of <paramref name="node"/>, with the
    /// square of the picture that panel uses. Null when the nearest paint face is not where the ray first
    /// hits something (<paramref name="maxDistance"/>).
    /// </summary>
    public static (Vector2 Uv, Rect2 Cell)? PaintUv(Node3D node, Vector3 origin, Vector3 dir, string paintName, float maxDistance)
    {
        dir = dir.Normalized();
        (Vector2 Uv, Rect2 Cell, float Dist)? best = null;
        foreach (var mi in MeshesUnder(node))
        {
            if (mi.Mesh is null || !mi.IsVisibleInTree()) continue;
            var xf = mi.GlobalTransform;
            var inv = xf.AffineInverse();
            var o = inv * origin;
            var d = inv.Basis * dir;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (PaintOf(mi.Mesh, s, paintName) is not { } ps) continue;
                for (int i = 0; i + 2 < ps.Verts.Length; i += 3)
                {
                    if (!RayTriangleBary(o, d, ps.Verts[i], ps.Verts[i + 1], ps.Verts[i + 2], out float t, out float bu, out float bv)) continue;
                    float dist = origin.DistanceTo(xf * (o + d * t));
                    if (best is not null && dist >= best.Value.Dist) continue;
                    var uv = ps.Uvs[i] * (1 - bu - bv) + ps.Uvs[i + 1] * bu + ps.Uvs[i + 2] * bv;
                    best = (uv, ps.Cell, dist);
                }
            }
        }
        if (best is not { } b || b.Dist > maxDistance + 0.01f) return null;
        return (b.Uv, b.Cell);
    }

    static bool RayTriangleBary(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
    {
        t = u = v = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        var s = o - a;
        u = s.Dot(p) * inv;
        if (u < 0 || u > 1) return false;
        var q = s.Cross(e1);
        v = d.Dot(q) * inv;
        if (v < 0 || u + v > 1) return false;
        t = e2.Dot(q) * inv;
        return t > 1e-5f;
    }

    internal static bool RayBox(Vector3 o, Vector3 d, Aabb box, out float t, out Vector3 normal)
    {
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        normal = Vector3.Up;
        var min = box.Position;
        var max = box.End;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = o[axis], da = d[axis];
            if (Mathf.Abs(da) < 1e-12f)
            {
                if (oa < min[axis] || oa > max[axis])
                {
                    t = 0;
                    return false;
                }
                continue;
            }
            float t1 = (min[axis] - oa) / da, t2 = (max[axis] - oa) / da;
            var n = Vector3.Zero;
            n[axis] = -1;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                n[axis] = 1;
            }
            if (t1 > tmin)
            {
                tmin = t1;
                normal = n;
            }
            tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax)
            {
                t = 0;
                return false;
            }
        }
        t = tmin >= 0 ? tmin : tmax;
        return tmax >= 0;
    }
}

/// <summary>
/// Many rays at the same targets (a decal's pixels, laid on the paint the original's way): what the targets are, where
/// they stand and their meshes' trees are read once, so each ray costs only the arithmetic.
/// </summary>
public sealed class RayBatch
{
    sealed class Item
    {
        public required Pick Pick;
        public Transform3D Xf, Inv;
        public Aabb? Proxy;
        public Mesh? Mesh;
        public MeshBvh? Bvh;
        public Aabb Box;
    }

    readonly List<Item> items = [];

    public RayBatch(IEnumerable<PickTarget> targets)
    {
        foreach (var t in targets)
        {
            if (!t.Enabled || !GodotObject.IsInstanceValid(t.Node) || !t.Node.IsVisibleInTree()) continue;
            if (t.Proxy is { } box)
            {
                var xf = t.Node.GlobalTransform;
                items.Add(new Item { Pick = t.Pick, Xf = xf, Inv = xf.AffineInverse(), Proxy = box });
                continue;
            }
            foreach (var mi in Picker.MeshesUnder(t.Node))
            {
                if (mi.Mesh is null || !mi.IsVisibleInTree()) continue;
                var xf = mi.GlobalTransform;
                items.Add(new Item { Pick = t.Pick, Xf = xf, Inv = xf.AffineInverse(), Mesh = mi.Mesh, Bvh = MeshBvh.Of(mi.Mesh), Box = mi.Mesh.GetAabb().Grow(0.001f) });
            }
        }
    }

    /// <summary>What the ray meets first: what it is, and on a mesh the mesh, the triangle (its number among the mesh's
    /// faces) and the weights of its second and third corners there.</summary>
    public (Pick Pick, Mesh? Mesh, int Face, float Wb, float Wc, float Distance)? Cast(Vector3 origin, Vector3 dir)
    {
        dir = dir.Normalized();
        (Pick, Mesh?, int, float, float, float)? best = null;
        float bestD = float.PositiveInfinity;
        foreach (var it in items)
        {
            var o = it.Inv * origin;
            var d = it.Inv.Basis * dir;
            float perT = (it.Xf.Basis * d).Length();
            if (it.Proxy is { } box)
            {
                if (Picker.RayBox(o, d, box, out float tb, out _) && tb * perT < bestD)
                {
                    bestD = tb * perT;
                    best = (it.Pick, null, -1, 0, 0, bestD);
                }
                continue;
            }
            if (!Picker.RayBox(o, d, it.Box, out _, out _)) continue;
            float maxT = float.IsPositiveInfinity(bestD) ? float.PositiveInfinity : bestD / perT;
            if (!it.Bvh!.Intersect(o, d, maxT, out float tt, out _, out _, out _, out int face, out float wb, out float wc)) continue;
            bestD = tt * perT;
            best = (it.Pick, it.Mesh, face, wb, wc, bestD);
        }
        return best;
    }
}
