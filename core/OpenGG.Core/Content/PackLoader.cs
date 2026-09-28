namespace OpenGG.Core.Content;

/// <summary>Loads a content pack described by a manifest (used for the bundled "ai" pack).</summary>
public static class PackLoader
{
    public const string AiManifest = "ai_manifest.json";

    public static (ContentPack Pack, AssetManifest Manifest) LoadDirectory(string dir, string manifestFile = AiManifest) =>
        Load(manifestFile, file => File.ReadAllText(Path.Combine(dir, file)));

    /// <param name="read">Returns the text of a file named in the manifest.</param>
    public static (ContentPack Pack, AssetManifest Manifest) Load(string manifestFile, Func<string, string> read)
    {
        var manifest = Json.Parse<AssetManifest>(read(manifestFile));
        T Get<T>(string key)
        {
            if (!manifest.Data.TryGetValue(key, out var file))
                throw new InvalidDataException($"The manifest has no data file for \"{key}\".");
            return Json.Parse<T>(read(file));
        }

        var pack = new ContentPack
        {
            Id = manifest.Pack,
            Name = manifest.Name,
            Tools = Get<List<ToolDef>>("tools"),
            FastenerKinds = Get<List<FastenerKindDef>>("fasteners"),
            Parts = Get<List<PartDef>>("parts"),
            Cars = Get<List<CarModelDef>>("cars"),
            Jobs = Get<List<JobTemplateDef>>("jobs"),
            Names = Get<NameLists>("names"),
            Portraits = Get<List<string>>("portraits"),
            Decals = Get<List<DecalDef>>("decals"),
            Paints = Get<List<PaintDef>>("paints"),
            Rules = Get<RulesDef>("rules"),
        };
        pack.Normalize();
        return (pack, manifest);
    }
}
