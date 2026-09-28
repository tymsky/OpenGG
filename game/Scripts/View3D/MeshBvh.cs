using System;
using System.Collections.Generic;
using Godot;

namespace OpenGG.View3D;

/// <summary>
/// A bounding volume hierarchy over a mesh's triangles, in the mesh's own space, so a ray finds the triangle it hits
/// after testing a few boxes instead of every triangle: picking costs next to nothing however detailed the models are
/// (the original's biggest car has 154,000 triangles). Built once per mesh, the first time a ray reaches it.
/// </summary>
public sealed class MeshBvh
{
    const int LeafSize = 4;

    readonly Vector3[] faces;
    /// <summary>Triangles in tree order (each an index into <see cref="faces"/> / 3).</summary>
    readonly int[] order;
    readonly List<Node> nodes = [];

    struct Node
    {
        public Vector3 Min, Max;
        /// <summary>Leaf: its first triangle in <see cref="order"/>. Inner node: its left child.</summary>
        public int Start;
        /// <summary>Leaf: how many triangles. Inner node: 0.</summary>
        public int Count;
        /// <summary>Inner node: its right child, and the axis it was split along.</summary>
        public int Right, Axis;
    }

    static readonly Dictionary<Mesh, MeshBvh> cache = [];

    /// <summary>How many triangles the mesh has.</summary>
    public int Triangles => faces.Length / 3;

    public static MeshBvh Of(Mesh mesh)
    {
        if (!cache.TryGetValue(mesh, out var bvh)) cache[mesh] = bvh = new MeshBvh(mesh.GetFaces());
        return bvh;
    }

    MeshBvh(Vector3[] faces)
    {
        this.faces = faces;
        int n = faces.Length / 3;
        order = new int[n];
        var centres = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
            centres[i] = (faces[i * 3] + faces[i * 3 + 1] + faces[i * 3 + 2]) / 3;
        }
        if (n > 0) Build(0, n, centres);
    }

    /// <summary>Makes the node for triangles order[start..end) and returns its index.</summary>
    int Build(int start, int end, Vector3[] centres)
    {
        var node = new Node { Min = Vector3.Inf, Max = -Vector3.Inf };
        var cMin = Vector3.Inf;
        var cMax = -Vector3.Inf;
        for (int i = start; i < end; i++)
        {
            int t = order[i];
            for (int k = 0; k < 3; k++)
            {
                node.Min = node.Min.Min(faces[t * 3 + k]);
                node.Max = node.Max.Max(faces[t * 3 + k]);
            }
            cMin = cMin.Min(centres[t]);
            cMax = cMax.Max(centres[t]);
        }
        int index = nodes.Count;
        nodes.Add(node);
        var size = cMax - cMin;
        int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        if (end - start <= LeafSize || size[axis] <= 1e-9f)
        {
            node.Start = start;
            node.Count = end - start;
            nodes[index] = node;
            return index;
        }
        // Split at the middle of the centres' spread; if everything falls on one side, at the median.
        float mid = (cMin[axis] + cMax[axis]) / 2;
        int split = start;
        for (int j = start; j < end; j++)
            if (centres[order[j]][axis] < mid)
            {
                (order[split], order[j]) = (order[j], order[split]);
                split++;
            }
        if (split == start || split == end)
        {
            Array.Sort(order, start, end - start, Comparer<int>.Create((a, b) => centres[a][axis].CompareTo(centres[b][axis])));
            split = (start + end) / 2;
        }
        node.Start = Build(start, split, centres);
        node.Right = Build(split, end, centres);
        node.Axis = axis;
        nodes[index] = node;
        return index;
    }

    /// <summary>The nearest triangle the ray o + d·t hits (both sides count) with 0 &lt; t &lt; <paramref name="maxT"/>:
    /// its t and the triangle's corners.</summary>
    public bool Intersect(Vector3 o, Vector3 d, float maxT, out float hitT, out Vector3 a, out Vector3 b, out Vector3 c) =>
        Intersect(o, d, maxT, out hitT, out a, out b, out c, out _, out _, out _);

    /// <summary>As above, with the triangle's number in the mesh's faces (surface after surface, as
    /// <see cref="Mesh.GetFaces"/> gives them) and where on it the ray went through (the weights of its second and
    /// third corners).</summary>
    public bool Intersect(Vector3 o, Vector3 d, float maxT, out float hitT, out Vector3 a, out Vector3 b, out Vector3 c,
        out int face, out float wb, out float wc)
    {
        hitT = maxT;
        a = b = c = Vector3.Zero;
        face = -1;
        wb = wc = 0;
        if (nodes.Count == 0) return false;
        var inv = new Vector3(1 / d.X, 1 / d.Y, 1 / d.Z);
        bool found = false;
        Span<int> stack = stackalloc int[96];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            var node = nodes[stack[--sp]];
            if (!HitsBox(o, inv, node.Min, node.Max, hitT)) continue;
            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int t = order[i];
                    var p0 = faces[t * 3];
                    var p1 = faces[t * 3 + 1];
                    var p2 = faces[t * 3 + 2];
                    if (RayTriangle(o, d, p0, p1, p2, out float tt, out float tu, out float tv) && tt < hitT)
                    {
                        hitT = tt;
                        (a, b, c) = (p0, p1, p2);
                        (face, wb, wc) = (t, tu, tv);
                        found = true;
                    }
                }
                continue;
            }
            if (sp + 2 > stack.Length) continue;
            // The nearer child last, so it is looked at first and the farther one can often be skipped.
            bool leftFirst = d[node.Axis] >= 0;
            stack[sp++] = leftFirst ? node.Right : node.Start;
            stack[sp++] = leftFirst ? node.Start : node.Right;
        }
        return found;
    }

    static bool HitsBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max, float maxT)
    {
        float t1 = (min.X - o.X) * inv.X, t2 = (max.X - o.X) * inv.X;
        float tmin = Math.Min(t1, t2), tmax = Math.Max(t1, t2);
        t1 = (min.Y - o.Y) * inv.Y;
        t2 = (max.Y - o.Y) * inv.Y;
        tmin = Math.Max(tmin, Math.Min(t1, t2));
        tmax = Math.Min(tmax, Math.Max(t1, t2));
        t1 = (min.Z - o.Z) * inv.Z;
        t2 = (max.Z - o.Z) * inv.Z;
        tmin = Math.Max(tmin, Math.Min(t1, t2));
        tmax = Math.Min(tmax, Math.Max(t1, t2));
        return tmax >= Math.Max(tmin, 0) && tmin <= maxT;
    }

    /// <summary>Möller–Trumbore, both sides.</summary>
    static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
    {
        t = u = v = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Math.Abs(det) < 1e-12f) return false;
        float invDet = 1f / det;
        var s = o - a;
        u = s.Dot(p) * invDet;
        if (u < 0 || u > 1) return false;
        var q = s.Cross(e1);
        v = d.Dot(q) * invDet;
        if (v < 0 || u + v > 1) return false;
        t = e2.Dot(q) * invDet;
        return t > 1e-5f;
    }
}
