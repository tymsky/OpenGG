using System.Collections.Generic;
using Godot;

namespace OpenGG.Assets;

/// <summary>One mesh of a model, placed relative to the model origin.</summary>
public sealed record ModelPiece(Mesh Mesh, Transform3D Transform);

/// <summary>
/// A loaded model: meshes (shared resources) and their placement. Instantiate it as many times as
/// needed; instances share meshes and materials, so tint them with surface override materials.
/// </summary>
public sealed class ModelData
{
    public List<ModelPiece> Pieces { get; } = [];
    public Aabb Bounds { get; private set; }

    public void Add(Mesh mesh, Transform3D transform)
    {
        Pieces.Add(new ModelPiece(mesh, transform));
        var box = transform * mesh.GetAabb();
        Bounds = Pieces.Count == 1 ? box : Bounds.Merge(box);
    }

    /// <summary>A node with one MeshInstance3D per piece.</summary>
    public Node3D Instantiate(bool shadows = true)
    {
        var root = new Node3D();
        foreach (var p in Pieces)
        {
            var mi = new MeshInstance3D
            {
                Mesh = p.Mesh,
                Transform = p.Transform,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(mi);
        }
        return root;
    }

    public static IEnumerable<MeshInstance3D> MeshesOf(Node node)
    {
        if (node is MeshInstance3D mi) yield return mi;
        foreach (var child in node.GetChildren())
            foreach (var m in MeshesOf(child))
                yield return m;
    }
}
