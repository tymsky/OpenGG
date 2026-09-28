using System;
using System.Collections.Generic;
using Godot;

namespace OpenGG.Assets;

/// <summary>Fire-and-forget sound effects from a small pool of players.</summary>
public partial class AudioBank : Node
{
    readonly List<AudioStreamPlayer> players = [];
    readonly RandomNumberGenerator rng = new();
    int next;

    public AssetStore? Assets { get; set; }
    public float Volume { get; set; } = 0.6f;
    public bool Muted { get; set; }

    readonly AudioStreamPlayer music = new() { Name = "Music" };
    string? musicId;

    public override void _Ready()
    {
        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            players.Add(p);
        }
        AddChild(music);
    }

    public bool Has(string id) => Stream(id) is not null;

    /// <summary>Loops a piece of music (null stops it); nothing happens if it is already playing.</summary>
    public void PlayMusic(string? id, float volume = 0.45f)
    {
        if (id == musicId) return;
        musicId = id;
        music.Stop();
        if (Muted || id is null || Stream(id) is not AudioStreamWav wav) return;
        music.Stream = Looped(wav);
        music.VolumeDb = Level(id, volume);
        music.Play();
    }

    /// <summary>A sound played over and over, seamlessly, until <see cref="StopLoop"/> (the spray while the button is
    /// held, the crowd at the Auction).</summary>
    public AudioStreamPlayer? Loop(string? id, float volume = 1f)
    {
        if (Muted || string.IsNullOrEmpty(id) || Stream(id) is not AudioStreamWav wav) return null;
        var p = new AudioStreamPlayer { Stream = Looped(wav), VolumeDb = Level(id, volume) };
        AddChild(p);
        p.Play();
        return p;
    }

    public static void StopLoop(AudioStreamPlayer? p)
    {
        if (p is null || !IsInstanceValid(p)) return;
        p.Stop();
        p.QueueFree();
    }

    static AudioStreamWav Looped(AudioStreamWav wav)
    {
        var loop = (AudioStreamWav)wav.Duplicate();
        loop.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        loop.LoopBegin = 0;
        int frame = (loop.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1) * (loop.Stereo ? 2 : 1);
        loop.LoopEnd = loop.Data.Length / frame;
        return loop;
    }

    /// <summary>Length of a sound in seconds (0 if unknown).</summary>
    public double Length(string? id) => string.IsNullOrEmpty(id) || Stream(id) is not { } s ? 0 : s.GetLength();

    /// <summary>The skin's sound when the skin says what plays for this id (an empty name: nothing, its game is
    /// silent there), else the content's.</summary>
    AudioStream? Stream(string id) => Ui.UiSkin.HasSound(id) ? Ui.UiSkin.Sound(id) : Assets?.Sound(id);

    /// <summary>A skin's sounds play as its game plays them: the original's, music too, all at one level (measured on a
    /// recording of it). OpenGG's own sounds at the level asked for.</summary>
    float Level(string id, float volume) => Mathf.LinearToDb(Mathf.Max(0.0001f, Volume * (Ui.UiSkin.HasSound(id) ? 1f : volume)));

    /// <summary>Plays a sound after <paramref name="delay"/> seconds, faded out and stopped after
    /// <paramref name="maxSeconds"/> (long engine recordings).</summary>
    public void PlayLater(string? id, double delay, double maxSeconds = 0, float volume = 1f)
    {
        if (Muted || string.IsNullOrEmpty(id)) return;
        var t = GetTree().CreateTimer(delay);
        t.Timeout += () =>
        {
            var p = Play(id, volume);
            if (p is null || maxSeconds <= 0) return;
            var tw = p.CreateTween();
            tw.TweenInterval(Math.Max(0, maxSeconds - 0.6));
            tw.TweenProperty(p, "volume_db", -40f, 0.6);
            tw.TweenCallback(Callable.From(p.Stop));
        };
    }

    public AudioStreamPlayer? Play(string? id, float volume = 1f, float rate = 1f)
    {
        if (Muted || string.IsNullOrEmpty(id) || Stream(id) is not { } stream) return null;
        AudioStreamPlayer? player = null;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[(next + i) % players.Count];
            if (!p.Playing)
            {
                player = p;
                break;
            }
        }
        player ??= players[next % players.Count];
        next = (players.IndexOf(player) + 1) % players.Count;
        player.Stream = stream;
        player.VolumeDb = Level(id, volume);
        // OpenGG's own sounds vary a little; the original plays each at its own pitch every time (measured), its cars'
        // own sounds too.
        bool own = !(Ui.UiSkin.IsOriginal || Ui.UiSkin.HasSound(id) || id.StartsWith("orig-", StringComparison.Ordinal));
        player.PitchScale = own ? rate * rng.RandfRange(0.94f, 1.06f) : rate;
        player.Play();
        return player;
    }
}
