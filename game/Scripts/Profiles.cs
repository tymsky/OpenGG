using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using CoreJson = OpenGG.Core.Content.Json;

namespace OpenGG;

public sealed class ProfileMeta
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Up to 10 mechanics per content pack, saved as JSON files in the user folder.</summary>
public sealed class Profiles
{
    /// <summary>Measured: the sign-in sheet has nine rows (the 1999 manual said ten).</summary>
    public const int Max = 9;
    readonly string dir;

    public Profiles(string packId)
    {
        dir = Path.Combine(Paths.User, "mechanics", packId);
        Directory.CreateDirectory(dir);
    }

    string IndexFile => Path.Combine(dir, "index.json");
    string SaveFile(string id) => Path.Combine(dir, $"{id}.json");

    public List<ProfileMeta> List()
    {
        try
        {
            return File.Exists(IndexFile) ? CoreJson.Parse<List<ProfileMeta>>(File.ReadAllText(IndexFile)) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    void WriteIndex(List<ProfileMeta> list) => WriteAtomic(IndexFile, CoreJson.Write(list, indented: true));

    static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    public (ProfileMeta Meta, Game Game)? Create(ContentIndex ci, string name)
    {
        var clean = name.Trim().Length > 0 ? name.Trim() : "Mechanic";
        var game = Game.Create(ci, clean);
        return Add(clean, game) is { } meta ? (meta, game) : null;
    }

    /// <summary>A game made elsewhere (e.g. brought over from the original's saves) as a new mechanic.</summary>
    public ProfileMeta? Add(string name, Game game)
    {
        var list = List();
        if (list.Count >= Max) return null;
        var id = $"m{DateTime.UtcNow.Ticks:x}";
        while (list.Any(m => m.Id == id)) id += "x";
        var meta = new ProfileMeta { Id = id, Name = name };
        list.Add(meta);
        WriteIndex(list);
        Save(meta.Id, game);
        return meta;
    }

    public Game? Load(ContentIndex ci, string id)
    {
        try
        {
            if (!File.Exists(SaveFile(id))) return null;
            var game = Game.Load(ci, File.ReadAllText(SaveFile(id)));
            foreach (var p in game.Problems) Godot.GD.Print($"mechanic {id}: {p}");
            return game;
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning($"Could not load mechanic {id}: {e.Message}");
            return null;
        }
    }

    public bool Save(string id, Game game)
    {
        try
        {
            WriteAtomic(SaveFile(id), game.Save());
            return true;
        }
        catch (Exception e)
        {
            Godot.GD.PushError($"Could not save mechanic {id}: {e.Message}");
            return false;
        }
    }

    public void Delete(string id)
    {
        WriteIndex(List().Where(m => m.Id != id).ToList());
        if (File.Exists(SaveFile(id))) File.Delete(SaveFile(id));
    }
}
