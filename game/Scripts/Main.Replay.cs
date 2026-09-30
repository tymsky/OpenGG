using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using OpenGG.Core.Sim;

namespace OpenGG;

/// <summary>
/// Replay (for comparing behaviour with the original): <c>--replay &lt;scenario.jsonl&gt; --autoshot &lt;dir&gt;</c>
/// plays a scenario as a player would, with the mouse and the keyboard on the 640 × 480 screen, the same scenario a
/// script plays in the original. With <c>--mechanics &lt;folder&gt;</c> the original's saves there (*.mek, in name
/// order) are brought over first, so the sign-in sheet has the same rows as the original's.
/// A scenario is one JSON object a line ("#" lines are comments), each a step:
/// <c>{"do": "click", "x", "y", "ms"?}</c>, <c>"hold"</c> (x, y, ms), <c>"drag"</c> and <c>"rdrag"</c> (x0, y0, x1,
/// y1, ms: the left or the right button), <c>"move"</c> (x, y), <c>"key"</c> (key: a Godot key name such as "Left",
/// "Escape", "Enter", "C"; ms), <c>"type"</c> (text), <c>"wait"</c> (s), <c>"shot"</c> (name), <c>"film"</c> (name, s,
/// box: [x, y, w, h]; filmed while the next steps go on, for s seconds from the next press), <c>"state"</c> (name: the game's state, see
/// <see cref="StateDump"/>). A first line with "scenario" names it. Run it with Godot's <c>--fixed-fps 60</c>: times are
/// the game's own (see <see cref="gameClock"/>). Out: shots/, films/&lt;name&gt;/ (frames and their
/// times), states/, and replay.jsonl (each step's time; "press" is when its button or key went down).
/// </summary>
public partial class Main
{
    /// <summary>The game's own time (its frames' deltas): with Godot's --fixed-fps 60 every frame is a 60th of a second
    /// however long filming it takes, so films show the motion as it runs, frame by frame.</summary>
    double gameClock;

    public override void _Process(double delta) => gameClock += delta;

    async Task Replay(string file, string dir)
    {
        try
        {
            await ReplayRun(file, dir);
        }
        finally
        {
            GetTree().Quit();
        }
    }

    async Task ReplayRun(string file, string dir)
    {
        var a = app!;
        foreach (var sub in new[] { "shots", "films", "states" }) Directory.CreateDirectory(Path.Combine(dir, sub));
        var steps = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => JsonDocument.Parse(l).RootElement).ToList();
        if (Args.Get("mechanics") is { } meks)
            a.ReplayImport(Directory.GetFiles(meks, "*.mek").Order(StringComparer.OrdinalIgnoreCase));
        await Frames(30);
        var log = new StreamWriter(Path.Combine(dir, "replay.jsonl"));
        double start = gameClock;
        double Now() => gameClock - start;
        var films = new List<Task>();
        var pressed = new List<double>();
        Vector2 pointer = new(320, 240);

        async Task Wait(double s)
        {
            if (s > 0) await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
        }
        void Send(InputEvent e) => Input.ParseInputEvent(e);
        void Move(Vector2 to, MouseButtonMask held)
        {
            var at = GetViewport().GetFinalTransform() * to;
            Send(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - GetViewport().GetFinalTransform() * pointer, ButtonMask = held });
            pointer = to;
        }
        void Button(MouseButton b, bool down)
        {
            var at = GetViewport().GetFinalTransform() * pointer;
            var mask = b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
            Send(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = b, Pressed = down, ButtonMask = down ? mask : 0 });
        }
        void KeyEvent(Key k, bool down, long unicode = 0) =>
            Send(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down, Unicode = down ? unicode : 0 });
        async Task Drag(MouseButton b, Vector2 from, Vector2 to, double s)
        {
            var mask = b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
            Move(from, 0);
            await Frames(3);
            Button(b, true);
            Log(new() { ["press"] = Now() });
            int n = Math.Max(2, (int)(s / 0.05));
            for (int i = 1; i <= n; i++)
            {
                await Wait(s / n);
                Move(from + (to - from) * i / n, mask);
            }
            await Wait(0.1);
            Button(b, false);
        }
        void Log(Dictionary<string, object> extra)
        {
            if (extra.TryGetValue("press", out var pr)) pressed.Add((double)pr);
            extra["t"] = Math.Round(Now(), 3);
            log.WriteLine(JsonSerializer.Serialize(extra));
            log.Flush();
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.TryGetProperty("scenario", out _)) continue;
            string what = s.GetProperty("do").GetString()!;
            float F(string k, float or = 0) => s.TryGetProperty(k, out var v) ? v.GetSingle() : or;
            string S(string k) => s.GetProperty(k).GetString()!;
            Log(new() { ["i"] = i, ["do"] = what, ["step"] = s.GetRawText() });
            switch (what)
            {
                case "move":
                    Move(new Vector2(F("x"), F("y")), 0);
                    await Frames(2);
                    break;
                case "click":
                case "hold":
                    Move(new Vector2(F("x"), F("y")), 0);
                    await Frames(3);
                    Button(MouseButton.Left, true);
                    Log(new() { ["press"] = Now() });
                    await Wait(F("ms", 70) / 1000);
                    Button(MouseButton.Left, false);
                    await Frames(2);
                    break;
                case "drag":
                case "rdrag":
                    await Drag(what == "drag" ? MouseButton.Left : MouseButton.Right, new Vector2(F("x0"), F("y0")), new Vector2(F("x1"), F("y1")), F("ms", 500) / 1000);
                    break;
                case "key":
                    var k = Enum.Parse<Key>(S("key"), ignoreCase: true);
                    KeyEvent(k, true);
                    Log(new() { ["press"] = Now() });
                    await Wait(F("ms", 60) / 1000);
                    KeyEvent(k, false);
                    await Frames(2);
                    break;
                case "type":
                    foreach (char c in S("text"))
                    {
                        var ck = c == ' ' ? Godot.Key.Space : Enum.Parse<Key>(char.ToUpperInvariant(c).ToString(CultureInfo.InvariantCulture), ignoreCase: true);
                        KeyEvent(ck, true, c);
                        await Wait(0.06);
                        KeyEvent(ck, false);
                        await Wait(0.08);
                    }
                    break;
                case "wait":
                    await Wait(F("s"));
                    break;
                case "shot":
                    ScreenMode.GameImage(GetViewport(), gameSize: true).SavePng(Path.Combine(dir, "shots", S("name") + ".png"));
                    break;
                case "film":
                    var box = s.TryGetProperty("box", out var bx) ? bx.EnumerateArray().Select(v => v.GetInt32()).ToArray() : [0, 0, 640, 480];
                    films.Add(Film(Path.Combine(dir, "films", S("name")), F("s", 3), new Rect2I(box[0], box[1], box[2], box[3]), Now, pressed));
                    break;
                case "state":
                    File.WriteAllText(Path.Combine(dir, "states", S("name") + ".state.json"), StateDump.Of(a.Game.State, a.CI));
                    break;
                default:
                    GD.PrintErr($"replay: unknown step {what}");
                    break;
            }
        }
        await Task.WhenAll(films);
        Log(new() { ["do"] = "end" });
        log.Close();
        GD.Print($"replay: {steps.Count} steps done");
    }

    /// <summary>Every frame drawn for <paramref name="seconds"/>: the box of the 640 × 480 screen, with its time since the
    /// replay's start (kept in memory, written at the end).</summary>
    async Task Film(string dir, float seconds, Rect2I box, Func<double> now, List<double> pressed)
    {
        Directory.CreateDirectory(dir);
        var frames = new List<(double T, Image Img)>();
        double start = now();
        // For the seconds given from the next press.
        double End() => (pressed.Where(p => p >= start).Cast<double?>().FirstOrDefault() ?? start) + seconds;
        while (now() < End())
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = ScreenMode.GameImage(GetViewport(), gameSize: false);
            float k = img.GetWidth() / (float)ScreenMode.Width;
            var crop = img.GetRegion(new Rect2I((Vector2I)(new Vector2(box.Position.X, box.Position.Y) * k).Round(), (Vector2I)(new Vector2(box.Size.X, box.Size.Y) * k).Round()));
            if (k != 1) crop.Resize(box.Size.X, box.Size.Y, Image.Interpolation.Bilinear);
            frames.Add((now(), crop));
        }
        for (int i = 0; i < frames.Count; i++) frames[i].Img.SavePng(Path.Combine(dir, $"{i:D4}.png"));
        File.WriteAllText(Path.Combine(dir, "times.json"), JsonSerializer.Serialize(new { box = new[] { box.Position.X, box.Position.Y, box.Size.X, box.Size.Y }, t = frames.Select(f => Math.Round(f.T, 4)) }));
    }
}
