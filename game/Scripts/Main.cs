using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Original;
using OpenGG.Core.Sim;

namespace OpenGG;

/// <summary>
/// Entry point: loads the content pack, then hands over to <see cref="App"/>.
/// Command line (after `--`): <c>--data &lt;dir&gt;</c> overrides the data folder,
/// <c>--original &lt;dir&gt;</c> plays with the copy of the original game in that folder (its content and look),
/// <c>--look original|opengg</c> picks the look (the original's, or ours drawn in code),
/// <c>--skin &lt;dir&gt;</c> uses the skin in that folder instead (see <see cref="Ui.UiSkin"/>),
/// <c>--fullscreen</c> or <c>--windowed</c> for this run (see <see cref="ScreenMode"/>),
/// <c>--view-scale &lt;k&gt;</c> draws our own look's 3D view at k times its size whatever the window, and the tour and the lab
/// save it as drawn too (<c>name_view.png</c>),
/// <c>--autoshot &lt;dir&gt;</c> runs a scripted tour and saves screenshots (for testing); with
/// <c>--mek &lt;file&gt;</c> (and the original's content) the tour shows that original save brought over instead.
/// </summary>
public partial class Main : Node
{
    App? app;

    public override void _Ready() => Boot();

    /// <summary>Loads the content (the ai pack, or the player's copy of the original on top of it) and starts the app.</summary>
    public async void Boot()
    {
        // The version is config/version in project.godot (semantic versioning; a release's tag is "v" + it).
        string version = ProjectSettings.GetSetting("application/config/version").AsString();
        DisplayServer.WindowSetTitle($"OpenGG {version}");
        GD.Print($"OpenGG {version}");
        ScreenMode.Start();
        // Money and times are always shown the same way ($1234.50), whatever the system language.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var aiDir = Paths.AiPack;
        if (!File.Exists(Path.Combine(aiDir, PackLoader.AiManifest)))
        {
            Fatal($"The content pack is missing:\n{aiDir}\n\nRun `npm install` and `npm run gen:assets` in the repository folder.");
            return;
        }
        ContentPack pack;
        AssetManifest manifest;
        try
        {
            (pack, manifest) = PackLoader.LoadDirectory(aiDir);
        }
        catch (Exception e)
        {
            Fatal($"Could not load the content pack:\n{e.Message}");
            return;
        }
        var assets = new AssetStore();
        assets.AddSource(new ManifestSource(aiDir, manifest));
        assets.AddSource(new Ui.SkinSource());
        var settings = Settings.Load();
        // The player's own copy of the original: its content (unless they chose the placeholders) and its look, both
        // read from its files in memory.
        string? original = Args.Get("original") ?? settings.OriginalFolder;
        bool useContent = Args.Get("original") is not null || settings.UseOriginal;
        bool useLook = Args.Get("look") is { } look ? look == "original" : settings.UseOriginalLook;
        string? note = null;
        OriginalArchives? archives = null;
        if (original is not null)
        {
            // Data or Data\Cars given, or the folder the game is in, will do too.
            if (OriginalGame.FindGameFolder(original) is { } found)
            {
                original = found;
                archives = OriginalArchives.Open(original);
            }
            else
            {
                note = Ui.Words.Get("folder.missing", ("folder", ShortPath(original)));
                original = null;
            }
        }
        if ((Args.Get("skin") ?? settings.SkinFolder) is { } skin) GD.Print(Ui.UiSkin.Load(skin, archives) ? $"skin: {skin}" : $"skin: none in {skin}");
        else if (archives is not null && useLook && Ui.UiSkin.LoadOriginal(archives)) GD.Print("skin: the original's look");
        else
        {
            Ui.UiSkin.Load(null);
            // The cars are there but not the archives with the screens and sounds (a partial copy): say so.
            if (original is not null && useLook) note ??= Ui.Words.Get("folder.nolook", ("folder", ShortPath(original)));
        }
        // With the original's look, its loading screens while the content is read: as its own log has it, the first
        // one, then (a few sounds later) the second, which stays up until Sign In.
        TextureRect? loading = null;
        if (Ui.UiSkin.Picture("loading.1") is { } firstScreen)
        {
            loading = new TextureRect { Texture = firstScreen, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Position = Vector2.Zero, Size = new Vector2(640, 480) };
            AddChild(loading);
            await Frames(2);
            if (Ui.UiSkin.Picture("loading.2") is { } secondScreen) loading.Texture = secondScreen;
            await Frames(2);
            if (Args.Get("autoshot") is { } shots)
            {
                Directory.CreateDirectory(shots);
                Shot(shots, "00_loading");
            }
        }
        OriginalGame? origGame = null;
        if (original is not null && useContent)
        {
            try
            {
                var og = OriginalGame.Load(original, pack, SkinPhrases(), archives);
                foreach (var p in og.Problems) GD.Print($"original: {p}");
                if (og.Pack.Phrases is { } book) GD.Print($"phrases: {book.Sentences.Count} sentences, {book.Customers.Count} customers");
                pack = og.Pack;
                origGame = og;
                assets.AddSource(new OriginalSource(og), first: true);
            }
            catch (Exception e)
            {
                note = $"Could not read the original game: {e.Message}";
            }
        }
        foreach (var err in Validation.Validate(pack)) GD.PushWarning($"content: {err}");
        app = new App(new ContentIndex(pack), assets, pack.Id) { Reboot = Reboot, StartupNote = note, Original = origGame };
        AddChild(app);
        loading?.QueueFree();
        // Lab: a skin's 3D scene as the game places it, to a JSON file (--export-scene <id> <file>), then quit.
        if (Args.Get("export-scene") is { } sceneId && Args.Get("export-to") is { } sceneFile)
        {
            if (Ui.UiSkin.Scene(sceneId) is { } model) File.WriteAllText(sceneFile, model.ToJson());
            GD.Print(Ui.UiSkin.Scene(sceneId) is null ? $"export: no scene {sceneId}" : $"export: {sceneId} -> {sceneFile}");
            GetTree().Quit();
            return;
        }
        if (Args.Get("lab") is { } lab) Report(Lab(lab, Args.Get("autoshot") ?? Path.GetDirectoryName(lab) ?? "."));
        else if (Args.Get("replay") is { } scenario) Report(Replay(scenario, Args.Get("autoshot") ?? Path.ChangeExtension(scenario, null)));
        else if (Args.Get("soundcheck") is { } check) Report(SoundCheck(check));
        else if (Args.Get("partscheck") is { } parts) Report(PartsCheck(parts));
        else if (Args.Get("feelcheck") is { } feel) Report(FeelCheck(feel));
        else if (Args.Get("p2check") is { } p2) Report(P2Check(p2));
        else if (Args.Get("playtest") is { } play) Report(PlayTest(play));
        else if (Args.Get("sheetshot") is { } sheet) Report(SheetShot(sheet));
        else if (Args.Get("autoshot") is { } dir) Report(Tour(dir));
    }

    /// <summary>A lab or tour run's failure, to the log (they run on their own, nobody awaits them).</summary>
    static async void Report(Task run)
    {
        try
        {
            await run;
        }
        catch (Exception e)
        {
            GD.PrintErr($"lab/tour failed: {e}");
        }
    }

    /// <summary>
    /// Lab (for fitting the look to the original's screenshots): a car in the WorkShop, then one screenshot per
    /// entry of a JSON script. The script: <c>{"car": id, "wear": [black, red, yellow, green] (the parts' chances),
    /// "park": [car ids] (more cars in the Car Lot),
    /// "paint": "#rrggbb", "job": n (the n-th Jobs Mode job
    /// instead of a car), "mek": file (instead: the car in the WorkShop of an original's save, as saved),
    /// "remove": [part numbers], "condition": {"part number": 0..3}, "shots": [{"name", "yaw", "pitch", "dist",
    /// "target": [x,y,z] | "frame": true | "angles": [yaw, pitch] (the rig places the camera) | "orbit":
    /// "up|down|left|right" (as a held arrow key, for "seconds"), "condition": {...}, "export": file (the car's
    /// triangles), "region": "Complete|Engine|Body|RunningGear", "ambient", "diffuse", "light", "specular":
    /// [r,g,b], "power", "pan": metres (the JunkYard's camera along its path), "hover": the JunkYard item under the pointer, "xray": Show Condition held, "pick": [x, y] (logs what a click there would pick), "model": id (that model alone where the car stands, the camera on its middle), "boltSlot": slot id (bolt mode on it)}], "screen": "lot" (shoot the Car Lot at its
    /// first bay instead) | "auction" (the WorkShop car on the Auction's stage) | "junkyard" (with "area":
    /// "Engine|Body|RunningGear" and "bare": true for the scene without its parts)}</c>. Values left out keep the previous
    /// ones.
    /// </summary>
    async Task Lab(string file, string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        await Frames(10);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        var g = Game.Create(a.CI, "Lab", 7);
        if (root.TryGetProperty("mek", out var mek) && a.Original is { } og)
        {
            g = MekImport.Import(og, a.CI, MekFile.Read(mek.GetString()!), 7).Game;
            g.State.Job = null;
            g.State.Offer = null;
        }
        else if (root.TryGetProperty("job", out var job) && job.GetInt32() is int n and > 0)
        {
            g.State.CompletedJobs.AddRange(g.JobChain.Take(n - 1).Select(j => j.Id));
            g.RequestJob();
            g.AcceptJob();
        }
        else
        {
            g.State.FreePlay = true;
            var car = root.GetProperty("car").GetString()!;
            // "wear": the chances of black, red, yellow and green parts (a new car without it).
            double[] wear = root.TryGetProperty("wear", out var we) ? we.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [0, 0, 0, 1];
            var v = g.NewVehicle(a.CI.Car(car), Core.Sim.Owner.Player, wear);
            if (root.TryGetProperty("paint", out var paint)) v.Paint = paint.GetString()!;
            g.State.Lot.Add(v.Id);
            g.BringToWorkshop(v.Id);
            // "park": more cars in the Car Lot, by model.
            if (root.TryGetProperty("park", out var park))
                foreach (var p in park.EnumerateArray())
                    g.State.Lot.Add(g.NewVehicle(a.CI.Car(p.GetString()!), Core.Sim.Owner.Player, wear).Id);
        }
        // The WorkShop car's place of a part, by the part's number in its .car file.
        string? SlotOf(string number) => g.WorkshopVehicle()?.Slots.FirstOrDefault(kv => kv.Value.Part?.PartId.EndsWith("." + number, StringComparison.Ordinal) == true).Key;
        void SetConditions(System.Text.Json.JsonElement e)
        {
            foreach (var p in e.EnumerateObject())
                if (SlotOf(p.Name) is { } id) g.WorkshopVehicle()!.Slots[id].Part!.Condition = p.Value.GetInt32();
        }
        if (root.TryGetProperty("remove", out var remove))
            foreach (var p in remove.EnumerateArray())
                if (SlotOf(p.ToString()) is { } id) g.WorkshopVehicle()!.Slots[id] = VehicleRules.EmptySlot();
        if (root.TryGetProperty("condition", out var cond)) SetConditions(cond);
        a.StartDemo(g);
        await Frames(30);
        // "screen": "lot" shoots the Car Lot (its first bay) instead of the WorkShop; "auction" puts the WorkShop car
        // on the Auction's stage.
        if (root.TryGetProperty("screen", out var scr) && scr.GetString() is "lot" or "auction" or "junkyard" or "catalog")
        {
            if (scr.GetString() == "lot") a.Go(Screen.Lot);
            else if (scr.GetString() == "catalog") a.Go(Screen.Catalog);
            else if (scr.GetString() == "junkyard")
            {
                if (root.TryGetProperty("junkPoses", out var poses))
                    View3D.JunkyardScene.LabPoses = poses.EnumerateObject().ToDictionary(p => p.Name, p => (p.Value[0].GetSingle(), p.Value[1].GetSingle(), p.Value[2].GetSingle()));
                a.DemoJunk(root.TryGetProperty("area", out var ar) ? Enum.Parse<Core.Content.Region>(ar.GetString()!) : Core.Content.Region.Engine,
                    root.TryGetProperty("bare", out var bare) && bare.GetBoolean(),
                    root.TryGetProperty("junkFor", out var jf) ? jf.GetString() : null,
                    root.TryGetProperty("junkRemove", out var jr) ? jr.EnumerateArray().Select(e => e.GetString()!).ToList() : null,
                    root.TryGetProperty("junkLast", out var jl) ? jl.EnumerateArray().Select(e => e.GetString()!).ToList() : null);
            }
            else a.DemoAuctionOwnCar();
            await Frames(30);
        }
        static Vector3 V3(System.Text.Json.JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        foreach (var s in root.GetProperty("shots").EnumerateArray())
        {
            if (s.TryGetProperty("condition", out var sc))
            {
                SetConditions(sc);
                a.DemoSync();
                await Frames(2);
            }
            if (s.TryGetProperty("region", out var region)) a.SetRegion(Enum.Parse<View>(region.GetString()!));
            // "boltSlot": bolt mode on that slot (its bolts shown, the camera on it).
            if (s.TryGetProperty("boltSlot", out var bs)) a.DemoBoltSlot(bs.GetString()!);
            // "model": one model on its own where the car stands; the camera then looks at its middle.
            Vector3? modelAt = s.TryGetProperty("model", out var model) ? a.DemoModel(model.GetString()!) : null;
            if (s.TryGetProperty("pan", out var pan)) a.DemoJunkPan(pan.GetSingle());
            // "section": the Catalog's section shown ("engine", "body", "running_gear", "decals"), its pictures made.
            if (s.TryGetProperty("section", out var sec))
            {
                a.DemoCatalogSection(sec.GetString()!);
                await Frames(120);
            }
            if (s.TryGetProperty("hover", out var hover)) a.DemoJunkHover(hover.ValueKind == System.Text.Json.JsonValueKind.Null ? null : hover.GetString());
            if (s.TryGetProperty("conditionColours", out var cc))
                View3D.OrigLook.SetConditionColours(cc.EnumerateArray().Select(c => V3(c) / 255f).ToArray());
            if (s.TryGetProperty("xray", out var xr)) a.SetXray(xr.GetBoolean());
            if (s.TryGetProperty("ambient", out var e)) View3D.OrigLook.Ambient = V3(e);
            if (s.TryGetProperty("diffuse", out e)) View3D.OrigLook.Diffuse = V3(e);
            if (s.TryGetProperty("light", out e)) View3D.OrigLook.Light = V3(e);
            if (s.TryGetProperty("specular", out e)) View3D.OrigLook.Specular = V3(e);
            if (s.TryGetProperty("power", out e)) View3D.OrigLook.Power = e.GetSingle();
            if (s.TryGetProperty("specLight", out e)) View3D.OrigLook.SpecLight = V3(e);
            View3D.OrigLook.Debug = s.TryGetProperty("debug", out e) ? e.GetSingle() : 0;
            View3D.OrigLook.Apply();
            await Frames(2);
            if (s.TryGetProperty("frame", out var fr) && fr.GetBoolean()) a.DemoFrame();
            // The view's angles, the WorkShop's own rig placing the camera.
            if (s.TryGetProperty("angles", out var an)) a.DemoAngles(an[0].GetSingle(), an[1].GetSingle());
            if (s.TryGetProperty("orbit", out var orbit))
            {
                // As a held arrow key turns the view (for "seconds"), through the WorkShop's own code.
                var keys = orbit.GetString()!;
                float dy = keys == "left" ? -1 : keys == "right" ? 1 : 0, dp = keys == "up" ? 1 : keys == "down" ? -1 : 0;
                a.DemoTurn(dy, dp, s.TryGetProperty("seconds", out e) ? e.GetDouble() : 0.07);
            }
            else
            {
                var o = a.DemoOrbit;
                float yaw = s.TryGetProperty("yaw", out e) ? e.GetSingle() : o.Yaw;
                float pitch = s.TryGetProperty("pitch", out e) ? e.GetSingle() : o.Pitch;
                float dist = s.TryGetProperty("dist", out e) ? e.GetSingle() : o.Distance;
                var target = s.TryGetProperty("target", out e) ? V3(e) : modelAt ?? o.Target;
                if (s.TryGetProperty("yaw", out _) || s.TryGetProperty("pitch", out _) || s.TryGetProperty("dist", out _) || s.TryGetProperty("target", out _) || modelAt is not null)
                    a.DemoCamera(yaw, pitch, dist, target);
            }
            await Frames(3);
            if (s.TryGetProperty("export", out var ex)) a.DemoExport(Path.Combine(dir, ex.GetString()!));
            if (s.TryGetProperty("exportJunk", out var ej)) a.DemoExportJunk(Path.Combine(dir, ej.GetString()!));
            if (s.TryGetProperty("pick", out var pk)) GD.Print($"lab: pick {pk[0].GetSingle()},{pk[1].GetSingle()} -> {a.DemoPick(new Vector2(pk[0].GetSingle(), pk[1].GetSingle()))}");
            if (s.TryGetProperty("region", out _) || s.TryGetProperty("angles", out _))
                GD.Print($"lab: {s.GetProperty("name").GetString()} framing centre {a.DemoFraming.Centre} level {a.DemoFraming.Level:0.####}");
            Shot(dir, s.GetProperty("name").GetString()!);
        }
        if (a.Screen == Screen.Auction) GD.Print("lab: auction camera " + a.DemoAuctionGeometry);
        if (a.Screen == Screen.Junkyard) GD.Print("lab: junkyard camera " + a.DemoJunkGeometry);
        var fin = a.DemoOrbit;
        GD.Print($"lab: camera yaw {fin.Yaw:0.####} pitch {fin.Pitch:0.####} dist {fin.Distance:0.####} target {fin.Target}");
        GD.Print($"lab: framing centre {a.DemoFraming.Centre} level {a.DemoFraming.Level:0.####}");
        GetTree().Quit();
    }

    /// <summary>A skin's own words for random jobs (see <see cref="Ui.UiSkin.JobPhrases"/>), in place of the original's;
    /// their faces are served as "skin:" pictures.</summary>
    static JobPhraseBook? SkinPhrases()
    {
        if (Ui.UiSkin.JobPhrases is not { } rel || Ui.UiSkin.ReadFile(rel) is not { } bytes) return null;
        var dir = Path.GetDirectoryName(rel)?.Replace('\\', '/') is { Length: > 0 } d ? d + "/" : "";
        var problems = new List<string>();
        var book = PhraseFile.Parse(System.Text.Encoding.Latin1.GetString(bytes), face => $"skin:{dir}{face}", problems);
        foreach (var p in problems) GD.Print($"phrases: skipped {p}");
        return book;
    }

    /// <summary>Reload the content (after the player picked the original game's folder).</summary>
    void Reboot()
    {
        if (app is not null)
        {
            RemoveChild(app);
            app.QueueFree();
            app = null;
        }
        Callable.From(Boot).CallDeferred();
    }

    void Fatal(string message)
    {
        var label = new Label { Text = message, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(label);
        GD.PushError(message);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    void Shot(string dir, string name)
    {
        ScreenMode.GameImage(GetViewport(), gameSize: false).SavePng(Path.Combine(dir, name + ".png"));
        // --view-scale: the 3D view drawn bigger than the window shows it, saved as drawn.
        if (Args.Get("view-scale") is not null && app is not null) app.ViewImage().SavePng(Path.Combine(dir, name + "_view.png"));
        GD.Print($"autoshot: {name}");
    }

    /// <summary>
    /// The tour with an original's save, part two: what was checked against the original (docs/FIDELITY.md), a
    /// screenshot each (and the offers' words in the log): random offers of each kind, a Job Update, a job with
    /// nothing wrong, the Auction, the view held on the real arrow keys on every tab, the Parts Bin with and
    /// without a car.
    /// </summary>
    async Task Checks(App a, OriginalGame og, string mek, string dir)
    {
        Game Fresh(uint seed)
        {
            var g = MekImport.Import(og, a.CI, MekFile.Read(mek), seed).Game;
            g.State.CompletedJobs.AddRange(a.CI.Pack.Jobs.Where(j => !j.Phrased).Select(j => j.Id));
            g.State.FreePlay = true;
            return g;
        }
        Game EmptyWorkshop(uint seed)
        {
            var g = Fresh(seed);
            if (g.State.Workshop is not null) g.PutCarInLot();
            return g;
        }
        await Settle(a);
        (string Kind, Func<Job, bool> Is)[] kinds =
        [
            ("two", j => j.Reqs.Count == 2 && j.Text.Contains("  ")),
            ("part", j => j.Reqs.Count > 0 && j.Reqs.All(r => r.SlotIds.Count == 1) && j.Fee > 0),
            ("region", j => j.Reqs.Count == 1 && j.Reqs[0].SlotIds.Count >= 3),
            ("nothing", j => j.Reqs.Count == 0),
        ];
        foreach (var (kind, test) in kinds)
            for (uint seed = 1; seed < 3000; seed++)
            {
                var g = EmptyWorkshop(seed);
                if (g.RequestJob().Data is not { } offer || !test(offer)) continue;
                GD.Print($"check offer {kind}: {offer.Difficulty} {Game.Money(offer.Fee)} | {offer.Text} | update: {offer.Nag}");
                if (kind == "nothing")
                {
                    // A Novice whose next pay takes them up to Handy: Skill Advance and Available Cars after the job.
                    g.State.Skill = 1;
                    g.State.BestCash = 15_500;
                    g.State.Stats.CarsRepaired = Math.Max(g.State.Stats.CarsRepaired, 9);
                }
                a.StartDemo(g);
                await Frames(20);
                _ = a.DemoJob(); // the offer waiting
                await Frames(30);
                Shot(dir, $"c1_offer_{kind}");
                if (kind == "two")
                {
                    a.DemoConfirmDialog();
                    await Frames(40);
                    _ = a.DemoJobHelp();
                    await Frames(30);
                    Shot(dir, "c2_job_update");
                    // Measured: the Job Help panel is gone while Job Update is up, and back after OK.
                    a.DemoConfirmDialog();
                    await Frames(20);
                    Shot(dir, "c2b_job_update_ok");
                }
                else if (kind == "nothing")
                {
                    // Measured: taken, it is done at once (the view circles the car, then Job Complete).
                    a.DemoConfirmDialog();
                    await Frames(40);
                    Shot(dir, "c3_nothing_spin");
                    for (int i = 0; i < 400 && !a.DemoDialogOpen; i++) await Frames(1);
                    await Frames(10);
                    Shot(dir, "c3_nothing_done");
                    // Measured: after the job's OK, Skill Advance over a blank WorkShop, then Available Cars.
                    a.DemoConfirmDialog();
                    await Frames(20);
                    Shot(dir, "c10_skill_advance");
                    a.DemoConfirmDialog();
                    await Frames(20);
                    Shot(dir, "c10_available_cars");
                    a.DemoConfirmDialog();
                    await Frames(20);
                    Shot(dir, "c10_after");
                    GD.Print($"check skill: {g.SkillName}");
                }
                else a.DemoCloseDialog();
                await Settle(a);
                break;
            }

        // The Auction: the opening, a few seconds in, near the end; the bids in the log.
        var ag = EmptyWorkshop(1);
        a.StartDemo(ag);
        await Frames(20);
        a.Go(Screen.Auction);
        await Frames(12);
        Shot(dir, "c4_auction_start");
        await Seconds(6);
        Shot(dir, "c4_auction_6s");
        await Seconds(12);
        Shot(dir, "c4_auction_18s");
        if (ag.State.Auction is { } au)
            GD.Print($"check auction: step {au.Step}, bids " + string.Join(" ", au.History.Select(b => $"{b.Amount}@{b.At:0.00}")));
        a.Go(Screen.Workshop);

        // The view on the arrow keys, held for real, on every tab: to the top, to the bottom, then round.
        var tg = Fresh(1);
        if (tg.State.Workshop is null)
        {
            // The mechanic sold every car: a cady79 of our own, the car the original's sweeps were taken on.
            var carId = a.CI.HasCar("orig.cady79") ? "orig.cady79" : a.CI.Pack.Cars[0].Id;
            var own = tg.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0.1, 0.3, 0.3, 0.3]);
            tg.State.Lot.Add(own.Id);
            tg.BringToWorkshop(own.Id);
        }
        a.StartDemo(tg);
        await Frames(30);
        foreach (var tab in new[] { View.Complete, View.Engine, View.Body, View.RunningGear })
        {
            a.SetRegion(tab);
            await Frames(50);
            await Hold(Key.Up, 1.2);
            await Frames(5);
            Shot(dir, $"c5_{tab.ToString().ToLowerInvariant()}_up");
            await Hold(Key.Down, 2.4);
            await Frames(5);
            Shot(dir, $"c5_{tab.ToString().ToLowerInvariant()}_down");
            await Hold(Key.Up, 0.49);
            GD.Print($"check tilt {tab}: pitch {a.DemoOrbit.Pitch * 180 / Mathf.Pi:0.0}");
        }
        a.SetRegion(View.Complete);
        await Frames(30);
        await Hold(Key.Left, 0.3);
        await Frames(5);
        Shot(dir, "c6_left");

        // The Parts Bin: the WorkShop car's model's parts; none with the WorkShop empty.
        var model = tg.WorkshopVehicle()!.ModelId;
        tg.BuyPart(a.CI.Car(model).Slots.First(s => s.DefaultPart is not null).DefaultPart!);
        var other = a.CI.Pack.Cars.First(c => c.Id != model && c.Slots.All(s => a.CI.Car(model).Slots.All(t => t.SlotType != s.SlotType)));
        tg.BuyPart(other.Slots.First(s => s.DefaultPart is not null).DefaultPart!);
        await Frames(10);
        GD.Print($"check bin: {tg.State.Bin.Count} parts in the bins, {tg.BinFor(model).Count()} shown for {model}");
        Shot(dir, "c7_bin_car");
        a.DemoPutCarInLot();
        await Frames(20);
        Shot(dir, "c7_bin_empty");

        // The Car Lot: cars numbered as they were bought, parked from the first bay on; the lot opens on the first;
        // the held arrow keys glide the camera along the lane, the figures and the marker follow the car nearest the
        // middle of the view; a click on it brings it into the WorkShop.
        VehicleState Buy(Game g)
        {
            g.State.Cash = Math.Max(g.State.Cash, 100_000);
            var au = g.GoToAuction().Data!;
            au.RivalMax = 0;
            g.PlaceBid();
            for (int t = 0; t < 30 && au.Open; t++) g.Tick(1);
            g.LeaveAuction();
            var v = g.Vehicle(au.VehicleId);
            g.PutCarInLot();
            return v;
        }
        void LotLine(string what)
        {
            var (pos, choice, marker) = a.DemoLot;
            var g = a.Game;
            string car = choice is { } c && c.Index < g.State.Lot.Count ? $"Number {g.Vehicle(g.State.Lot[c.Index]).Number} at x {c.At.X:0}" : "none";
            GD.Print($"check lot {what}: camera at bay {pos:0.000}, figures for {car}, marker {marker:0}");
        }
        var lg = EmptyWorkshop(2);
        for (int i = 0; i < 4; i++) Buy(lg);
        GD.Print($"check lot numbers: {string.Join(" ", lg.State.Lot.Select(id => lg.Vehicle(id).Number))}");
        a.StartDemo(lg);
        await Frames(20);
        a.Go(Screen.Lot);
        await Frames(30);
        LotLine("open");
        Shot(dir, "c8_lot_open");
        // The original's clicks on ◄ as the lot opened: where its camera was (fitted by the bay lines, in bays) and where
        // its marker was, then over to the second car at the twelfth click.
        (float Pos, int Marker)[] seen =
        [
            (0, 315), (0.0302f, 324), (0.0589f, 332), (0.0887f, 341), (0.1174f, 350), (0.1469f, 360), (0.1908f, 374),
            (0.2371f, 391), (0.2690f, 401), (0.3249f, 422), (0.3530f, 434), (0.3845f, 446), (0.4155f, 182), (0.4454f, 187),
        ];
        var clicks = new List<string>();
        foreach (var (pos, marker) in seen)
        {
            a.DemoLotPos(pos);
            await Frames(1);
            var (_, ch, mk) = a.DemoLot;
            clicks.Add($"{(ch is { } cc ? lg.Vehicle(lg.State.Lot[cc.Index]).Number : -1)}:{mk:0}/{marker}");
        }
        GD.Print("check lot clicks (ours/original): " + string.Join(" ", clicks));
        a.DemoLotPos(0);
        await Frames(2);
        await Hold(Key.Right, 0.3);
        LotLine("right at the first bay");
        await Hold(Key.Left, 0.075);
        LotLine("a 75 ms press");
        Shot(dir, "c8_lot_click");
        await Hold(Key.Left, 1.0);
        LotLine("held 1 s more");
        Shot(dir, "c8_lot_glide");
        await Hold(Key.Left, 8);
        LotLine("held to the end");
        Shot(dir, "c8_lot_end");
        a.DemoLotClickChoice();
        await Frames(30);
        GD.Print($"check lot click: the WorkShop has Number {lg.WorkshopVehicle()?.Number}, the lot {string.Join(" ", lg.State.Lot.Select(id => lg.Vehicle(id).Number))}");
        Shot(dir, "c8_lot_clicked");

        // Twelve cars: Go To Auction says the lot is full.
        var fg = EmptyWorkshop(3);
        while (fg.CarsOwned < Game.LotBays) Buy(fg);
        a.StartDemo(fg);
        await Frames(20);
        a.DemoGoToAuction();
        await Frames(20);
        Shot(dir, "c9_lot_full");
        GD.Print($"check lot full: {fg.CarsOwned} cars, auction {(fg.State.Auction is null ? "closed" : "open")}");
        await Settle(a);
        a.Go(Screen.Lot);
        await Frames(30);
        Shot(dir, "c9_lot_twelve");
    }

    /// <summary>Answers every dialog that comes up until none has for three seconds (a job's thanks come up a
    /// while after it ends).</summary>
    async Task Settle(App a)
    {
        for (double quiet = 0; quiet < 3;)
        {
            if (a.DemoDialogOpen)
            {
                a.DemoConfirmDialog();
                quiet = 0;
                await Frames(5);
                continue;
            }
            await Seconds(0.25);
            quiet += 0.25;
        }
    }

    /// <summary>Holds a key down for a while, as the player would (the game reads it from Input).</summary>
    async Task Hold(Key key, double seconds)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Seconds(seconds);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(2);
    }

    /// <summary>A folder as the sign-in's one line has room for: its last two names when it is long.</summary>
    static string ShortPath(string path)
    {
        if (path.Length <= 44) return path;
        var sep = Path.DirectorySeparatorChar;
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 ? $"...{sep}{parts[^2]}{sep}{parts[^1]}" : path;
    }

    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    /// <summary>A scripted walk through the screens, saving a screenshot of each.</summary>
    // ---- sound check -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>--soundcheck &lt;dir&gt;</c>: the original's measuring session done again on OpenGG, with the mouse (as input
    /// events) at the places it was clicked in the original: every action goes to <c>&lt;dir&gt;/actions.jsonl</c> with its
    /// time (Unix seconds), so a loopback recording made meanwhile can be matched against the sounds each should make.
    /// A Free Play mechanic with a car of the content's in the WorkShop.
    /// </summary>
    async Task SoundCheck(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "actions.jsonl");
        File.WriteAllText(log, "");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        void Log(string label)
        {
            double t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            File.AppendAllText(log, "{\"t\": " + t.ToString("0.000", inv) + ", \"label\": \"" + label + "\", \"screen\": \"" + a.Screen + "\"}\n");
            GD.Print($"soundcheck: {label} [{a.Screen}]");
        }
        async Task Wait(int ms) => await ToSignal(GetTree().CreateTimer(ms / 1000.0), SceneTreeTimer.SignalName.Timeout);
        void Mouse(Vector2 place, bool? press)
        {
            // The game's place in the window (it may be scaled, with bars beside it).
            var at = GetViewport().GetFinalTransform() * place;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            if (press is { } p)
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = p, ButtonMask = p ? MouseButtonMask.Left : 0 });
        }
        async Task Hold(float x, float y, string label, int holdMs = 80, int afterMs = 1500)
        {
            var at = new Vector2(x, y);
            Mouse(at, null);
            await Frames(3);
            Log(label);
            Mouse(at, true);
            await Wait(holdMs);
            Mouse(at, false);
            await Wait(afterMs);
        }
        Task Click(float x, float y, string label, int afterMs = 1500) => Hold(x, y, label, 80, afterMs);

        await Wait(1000);
        Log("sign-in music");
        await Wait(3000);
        // The sign-in sheet's boxes (run it with --user <a scratch folder>: it makes and deletes a mechanic there).
        if (Args.Get("user") is not null)
        {
            await Click(103, 172, "sign-in NEW", 1500);
            Shot(dir, "signin_new_box");
            foreach (char ch in "Tester")
            {
                var k = new InputEventKey { Pressed = true, Keycode = (Key)char.ToUpperInvariant(ch), Unicode = ch };
                Input.ParseInputEvent(k);
                await Frames(2);
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = (Key)char.ToUpperInvariant(ch) });
                await Frames(2);
            }
            await Click(262, 280, "Create New Mechanic OK", 2000);
            Shot(dir, "signin_new_row");
            await Click(44, 172, "sign-in DELETE", 1500);
            Shot(dir, "signin_delete_box");
            await Click(262, 280, "DELETE OK", 2000);
            Shot(dir, "signin_after_delete");
            await Click(492, 95, "sign-in EXIT", 1500);
            Shot(dir, "signin_exit_box");
            await Click(389, 280, "EXIT CANCEL", 1500);
        }
        // The full screen, by its keys (--screentest: it takes the screen for a few seconds).
        if (Args.Has("screentest"))
        {
            async Task Press(Key key, bool alt, string label)
            {
                Log(label);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, AltPressed = alt, Pressed = true });
                await Frames(2);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, AltPressed = alt, Pressed = false });
                await Wait(1500);
                GD.Print($"soundcheck: full screen {ScreenMode.Fullscreen}, window {GetTree().Root.Size}, scale {ScreenMode.Scale:0.###}");
            }
            await Press(Key.F11, false, "F11 (full screen)");
            Shot(dir, "fullscreen_signin");
            await Press(Key.Enter, true, "Alt+Enter (a window again)");
            Shot(dir, "windowed_again");
        }
        var g = Game.Create(a.CI, "Tester", 7);
        g.State.FreePlay = true;
        g.State.Cash = 100_000;
        // A car in good order (its engine starts), with bolts on an engine part: the original's Escort if it is there.
        var carId = Args.Get("car") ?? (a.CI.HasCar("orig.escort") ? "orig.escort" : a.CI.Pack.Cars[0].Id);
        var v = g.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0, 0, 0, 1]);
        v.Number = g.State.Stats.CarsBought++;
        g.State.Lot.Add(v.Id);
        g.BringToWorkshop(v.Id);
        Log("start demo");
        a.StartDemo(g);
        await Wait(2000);
        // COMPLETE: the pointer on the car lights its region; a click goes to that region's tab.
        Mouse(new Vector2(200, 205), null);
        await Wait(800);
        Shot(dir, "complete_hover_body");
        Mouse(new Vector2(140, 250), null);
        await Wait(800);
        Shot(dir, "complete_hover_wheel");
        await Click(200, 205, "COMPLETE click on the body", 2500);
        Shot(dir, "complete_click_body");
        Mouse(new Vector2(450, 330), null);
        await Wait(300);
        await Click(62, 61, "tab COMPLETE (back)", 1500);
        // The view's tabs, twice the same one.
        await Click(185, 61, "tab ENGINE");
        await Click(185, 61, "tab ENGINE again (same)");
        await Click(415, 61, "tab RUNNING GEAR");
        await Click(280, 61, "tab BODY");
        // Body Paint, held on the car.
        await Click(448, 190, "Body Paint tool select");
        await Hold(195, 215, "paint spray HOLD 1500ms", 1500, 2000);
        await Click(62, 61, "tab COMPLETE");
        // The tools.
        await Click(445, 190, "Camera tool select");
        await Click(445, 190, "Camera tool select again (same)");
        await Click(200, 205, "Camera: a snapshot of the car", 1500);
        await Click(447, 120, "Impact Wrench tool select");
        await Click(30, 330, "Impact Wrench click on nothing");
        // Show Condition and Start Engine, held.
        await Hold(575, 118, "Show Condition HOLD 800ms", 800, 1500);
        // Bolt mode (as the tour opens it: the engine part with the most bolts, its first bolt out), a click beside
        // the bolts, then CANCEL.
        await Click(185, 61, "tab ENGINE (2)");
        // Start Engine let go early, then held 1.5 s with a frame every half second (the engine shakes, its crank turns).
        await Hold(447, 318, "Start Engine HOLD 300ms", 300, 2500);
        var key = new Vector2(447, 318);
        Mouse(key, null);
        await Frames(3);
        Log("Start Engine HOLD 1500ms");
        Mouse(key, true);
        for (int i = 1; i <= 12; i++)
        {
            await Wait(500);
            if (i == 3) Mouse(key, false);
            Shot(dir, $"engine_{i * 500:0000}ms");
        }
        Log("bolt mode opened, a bolt out");
        a.DemoBoltMode();
        await Wait(2000);
        await Click(30, 330, "bolt mode click on nothing");
        await Click(515, 242, "bolt mode CANCEL");
        // The Catalog: open, a section's tab, back.
        await Click(575, 294, "Catalog");
        await Click(599, 177, "Catalog ENGINE tab");
        await Click(415, 64, "Catalog Go Back To WorkShop");
        // The JunkYard: the arrow held, the plank shown and another, back.
        await Click(575, 386, "Go To JunkYard", 2500);
        await Hold(489, 331, "JunkYard right arrow HOLD 1500ms", 1500, 1500);
        await Click(555, 222, "JunkYard RUNNING GEAR plank (the area shown)");
        await Click(552, 188, "JunkYard BODY plank");
        await Click(415, 64, "JunkYard Go Back To WorkShop", 2500);
        // A job offered and put off, the car offered to the auction and not.
        await Click(575, 206, "Get A Job", 2500);
        await Click(410, 366, "Job Request CANCEL");
        await Click(575, 250, "Auction Car", 2500);
        await Click(381, 281, "Auction Car CANCEL");
        // The Auction for a while: a bid, the next car, then back.
        await Click(575, 430, "Go To Auction", 6000);
        await Click(574, 355, "Auction Place Bid", 2500);
        await Click(575, 417, "Auction Skip Car", 5000);
        Shot(dir, "auction");
        await Click(415, 64, "Auction Go Back To WorkShop", 2500);
        Shot(dir, "back_from_auction");
        // Put Car In Lot: the Car Lot comes up (the run's first visit: its crow and departing car, 21 and 31 s in); back.
        await Click(575, 162, "Put Car In Lot", 33000);
        Shot(dir, "put_car_in_lot");
        // The camera walks along the lane while ◄ is held (a footstep every 0.42 s or so).
        await Hold(48, 368, "Car Lot left arrow HOLD 2000ms", 2000, 1500);
        await Click(415, 64, "Car Lot Go Back To WorkShop", 2500);
        // A job done (the tutorial's first, as the tour does it): the car starts up and leaves to a fanfare, then
        // Job Complete! with the till, the tutorial's two tips, and the next customer.
        g.State.FreePlay = false;
        await Click(575, 206, "Get A Job", 2500);
        await Click(293, 366, "Job Request OK", 3000);
        Log("job finished (the last part goes on)");
        a.DemoFinishJob();
        await Wait(6000);
        Shot(dir, "job_complete");
        await Click(293, 366, "Job Complete OK", 2000);
        await Click(325, 281, "Congratulations OK", 2000);
        await Click(325, 281, "The Future OK", 3000);
        Shot(dir, "next_job_request");
        await Click(293, 366, "Job Request OK (next)", 3000);
        await Click(610, 466, "WorkShop Exit (job active)", 2000);
        Shot(dir, "job_active_box");
        await Click(263, 281, "Job Active OK (leave)", 4000);
        Shot(dir, "exit");
        Log("end");
        await Wait(500);
        GetTree().Quit();
    }

    /// <summary>
    /// <c>--p2check &lt;dir&gt;</c>: what session 7 measured, on OpenGG: the Auction's cars changing places after Skip Car
    /// (a shot every tenth of a second: skip_0100 ...), a car won (the box with no Go Back To WorkShop and no Skip Car),
    /// and a Catalog card's part turning under the pointer, left turned, and still turned on the next visit.
    /// </summary>
    async Task P2Check(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        async Task Wait(int ms) => await ToSignal(GetTree().CreateTimer(ms / 1000.0), SceneTreeTimer.SignalName.Timeout);
        void Mouse(Vector2 place, bool? press)
        {
            var at = GetViewport().GetFinalTransform() * place;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = press == true ? MouseButtonMask.Left : 0 });
            if (press is { } pr)
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pr, ButtonMask = pr ? MouseButtonMask.Left : 0 });
        }
        async Task Click(float x, float y, int afterMs = 800)
        {
            Mouse(new Vector2(x, y), null);
            await Frames(3);
            Mouse(new Vector2(x, y), true);
            await Wait(70);
            Mouse(new Vector2(x, y), false);
            await Wait(afterMs);
        }
        var g = Game.Create(a.CI, "Tester", 7);
        g.State.CompletedJobs.AddRange(g.JobChain.Select(j => j.Id));
        g.State.FreePlay = true;
        g.State.Cash = 50000;
        a.StartDemo(g);
        await Frames(20);
        Mouse(new Vector2(450, 470), null);
        a.Go(Screen.Auction);
        await Wait(1500);
        Shot(dir, "auc_before_skip");
        await Click(575, 418, 0);
        for (int ms = 100; ms <= 1000; ms += 100)
        {
            await Wait(100);
            Shot(dir, $"skip_{ms:0000}");
        }
        GD.Print($"p2check: after Skip Car the car drives on for {Game.AuctionSwap} s; now arriving {g.State.Auction?.Arriving:0.00}");
        // Won: nobody else bids, the clock run down.
        g.State.Auction!.RivalMax = 0;
        await Click(575, 355, 300);
        g.State.Auction!.Elapsed = g.State.Auction.Duration - 0.3;
        await Wait(1200);
        Shot(dir, "won_box");
        a.DemoConfirmDialog();
        await Wait(1500);
        // A flag aimed at the car (BODY, Body Paint, DECALS, the first decal, DONE): the checkerboard preview.
        if (a.CI.Pack.Decals.FirstOrDefault(d => !d.Tint) is { } flag)
        {
            g.State.DecalUses[flag.Id] = 10;
            await Click(280, 61, 1000);
            await Click(448, 190, 700);
            await Click(446, 349, 1200);
            await Click(168, 187, 900);
            await Click(359, 367, 900);
            Mouse(new Vector2(200, 205), null);
            await Wait(500);
            Shot(dir, "decal_aim");
            Mouse(new Vector2(160, 190), null);
            await Wait(400);
            Shot(dir, "decal_aim_2");
            Mouse(new Vector2(462, 300), null);
            a.SetRegion(View.Complete);
            await Wait(500);
        }
        // A snapshot with the Camera (Shot0.jpg and its 100 x 75 Shot0.bmp).
        await Click(448, 190, 700);
        await Click(200, 205, 900);
        GD.Print($"p2check: snapshots {string.Join(", ", Directory.Exists(Paths.Snapshots) ? Directory.GetFiles(Paths.Snapshots, "Shot*", SearchOption.AllDirectories) : [])}");
        // The Catalog: the first card's part turning under the pointer.
        await Click(575, 294, 3000);
        Mouse(new Vector2(450, 470), null);
        await Wait(300);
        Shot(dir, "cat_open");
        Mouse(new Vector2(124, 150), null);
        for (int ms = 200; ms <= 1200; ms += 200)
        {
            await Wait(200);
            Shot(dir, $"cat_turn_{ms:0000}");
        }
        Mouse(new Vector2(450, 470), null);
        await Wait(500);
        Shot(dir, "cat_left_turned");
        await Click(415, 64, 1500);
        await Click(575, 294, 2500);
        Mouse(new Vector2(450, 470), null);
        await Wait(300);
        Shot(dir, "cat_reopened");
        GD.Print("p2check: done");
        GetTree().Quit();
    }

    /// <summary>
    /// <c>--feelcheck &lt;dir&gt;</c>: session 10's films of the original's screens done again on OpenGG, with the mouse (as
    /// input events) at the same places and times, and every frame saved as the game's 640 x 480 picture
    /// (<c>&lt;tag&gt;/&lt;ms from the action&gt;.png</c>), for a comparison frame by frame: the tools, Show Condition held, a
    /// button pressed and let go off it, the Catalog (in, a section, a page, out), the JunkYard, the Car Lot and the
    /// Auction in and out, a Job Request opened and put off.
    /// </summary>
    async Task FeelCheck(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        async Task Wait(int ms) => await ToSignal(GetTree().CreateTimer(ms / 1000.0), SceneTreeTimer.SignalName.Timeout);
        void Mouse(Vector2 place, bool? press)
        {
            var at = GetViewport().GetFinalTransform() * place;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = press == true || Input.IsMouseButtonPressed(MouseButton.Left) ? MouseButtonMask.Left : 0 });
            if (press is { } p)
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = p, ButtonMask = p ? MouseButtonMask.Left : 0 });
        }
        // As the relay clicks: down, 70 ms, up.
        async Task Click(float x, float y)
        {
            Mouse(new Vector2(x, y), true);
            await Wait(70);
            Mouse(new Vector2(x, y), false);
        }
        async Task Hold(float x, float y, int ms)
        {
            Mouse(new Vector2(x, y), true);
            await Wait(ms);
            Mouse(new Vector2(x, y), false);
        }
        // Every frame drawn from 0.3 s before the action to the given seconds after it.
        async Task Film(string tag, float x, float y, double seconds, Func<Task> act)
        {
            Mouse(new Vector2(x, y), null);
            await Frames(3);
            var frames = new List<(long Us, Image Img)>();
            bool on = true;
            async Task Grab()
            {
                while (on)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var img = ScreenMode.GameImage(GetViewport(), gameSize: false);
                    if (img.GetSize() != new Vector2I(ScreenMode.Width, ScreenMode.Height)) img.Resize(ScreenMode.Width, ScreenMode.Height, Image.Interpolation.Nearest);
                    frames.Add(((long)Time.GetTicksUsec(), img));
                }
            }
            var grab = Grab();
            await Wait(300);
            long t0 = (long)Time.GetTicksUsec();
            await act();
            while ((long)Time.GetTicksUsec() - t0 < seconds * 1e6) await Frames(1);
            on = false;
            await grab;
            var sub = Path.Combine(dir, tag);
            Directory.CreateDirectory(sub);
            foreach (var (us, img) in frames) img.SavePng(Path.Combine(sub, $"{(us - t0) / 1000:+00000;-00000}.png"));
            GD.Print($"feel: {tag} {frames.Count} frames");
        }

        var g = Game.Create(a.CI, "Tester", 8);
        g.State.FreePlay = true;
        g.State.Cash = 100000;
        var carId = a.CI.HasCar("orig.41ford") ? "orig.41ford" : a.CI.Pack.Cars[0].Id;
        var own = g.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0.2, 0.3, 0.3, 0.2]);
        g.State.Lot.Add(own.Id);
        g.BringToWorkshop(own.Id);
        a.StartDemo(g);
        await Frames(30);
        await Film("f_tool_camera", 447, 190, 1.0, () => Click(447, 190));
        await Film("f_tool_wrench", 447, 118, 1.0, () => Click(447, 118));
        await Film("f_showcond", 578, 118, 1.8, () => Hold(578, 118, 1000));
        Mouse(new Vector2(470, 250), null);
        await Frames(5);
        await Film("f_hover_catalog", 470, 250, 1.0, async () => { Mouse(new Vector2(578, 295), null); await Frames(1); });
        Mouse(new Vector2(578, 295), true);
        await Frames(6);
        Shot(dir, "pressed_catalog");
        Mouse(new Vector2(470, 300), null);
        await Frames(6);
        Shot(dir, "pressed_moved_off");
        Mouse(new Vector2(470, 300), false);
        await Wait(1000);
        Shot(dir, "released_off");
        await Film("f_catalog_in", 578, 295, 2.5, () => Click(578, 295));
        await Film("f_cat_section_engine", 605, 170, 1.2, () => Click(605, 170));
        await Film("f_cat_turn", 552, 443, 1.5, () => Click(552, 443));
        await Film("f_catalog_out", 416, 64, 2.5, () => Click(416, 64));
        await Film("f_junk_in", 578, 387, 3.0, () => Click(578, 387));
        await Film("f_junk_out", 416, 64, 3.0, () => Click(416, 64));
        await Film("f_lot_in", 578, 343, 3.0, () => Click(578, 343));
        await Film("f_lot_out", 416, 64, 3.0, () => Click(416, 64));
        await Film("f_auction_in", 578, 430, 3.5, () => Click(578, 430));
        await Film("f_auction_out", 416, 64, 3.0, () => Click(416, 64));
        await Wait(500);
        await Film("f_jobreq_open", 578, 207, 1.5, () => Click(578, 207));
        await Film("f_jobreq_cancel", 408, 366, 1.2, () => Click(408, 366));
        // Held a second: does it act as the button goes down or as it comes up?
        await Film("f_hold_tool_camera", 447, 190, 1.8, () => Hold(447, 190, 1000));
        await Film("f_hold_tool_wrench", 447, 118, 1.8, () => Hold(447, 118, 1000));
        await Film("f_hold_tab_body", 290, 61, 1.8, () => Hold(290, 61, 1000));
        await Film("f_hold_tab_complete", 63, 61, 1.8, () => Hold(63, 61, 1000));
        await Film("f_hold_getajob", 578, 207, 1.8, () => Hold(578, 207, 1000));
        await Film("f_hold_cancel", 408, 366, 1.8, () => Hold(408, 366, 1000));
        GetTree().Quit();
    }

    /// <summary>
    /// <c>--partscheck &lt;dir&gt;</c>: the original's measuring session of the Parts Bin and the job's end (session 6) done
    /// again on OpenGG, with the mouse (as input events) at the places it went in the original, a screenshot at each of
    /// its captures (same names) and the job's end filmed: <c>end_&lt;ms&gt;</c> every tenth of a second from the drop.
    /// The tutorial's first job (the original's first car if it is there), then a wheel put on and taken off.
    /// </summary>
    async Task PartsCheck(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        async Task Wait(int ms) => await ToSignal(GetTree().CreateTimer(ms / 1000.0), SceneTreeTimer.SignalName.Timeout);
        void Mouse(Vector2 place, bool? press)
        {
            var at = GetViewport().GetFinalTransform() * place;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = press == true || Input.IsMouseButtonPressed(MouseButton.Left) ? MouseButtonMask.Left : 0 });
            if (press is { } p)
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = p, ButtonMask = p ? MouseButtonMask.Left : 0 });
        }
        async Task Click(float x, float y, int afterMs = 800)
        {
            Mouse(new Vector2(x, y), null);
            await Frames(3);
            Mouse(new Vector2(x, y), true);
            await Wait(70);
            Mouse(new Vector2(x, y), false);
            await Wait(afterMs);
        }
        // Pressed at the first place, moved through the others (a few frames each), let go at the last.
        async Task Drag(params Vector2[] path)
        {
            Mouse(path[0], null);
            await Frames(3);
            Mouse(path[0], true);
            await Wait(150);
            for (int i = 1; i < path.Length; i++)
            {
                Mouse(path[i], null);
                await Wait(80);
            }
            await Wait(300);
            Mouse(path[^1], false);
        }
        var bin = new Vector2(55, 392);
        var repair = new Vector2(452, 396);
        var car = new Vector2(200, 205);
        // Where a part shows in the view, as a point of the screen (the view's pick works in its own pixels).
        Vector2? OnView(string slotId)
        {
            for (int y = 4; y < 254; y += 5)
                for (int x = 4; x < 377; x += 5)
                    if (a.DemoPick(new Vector2(x, y)) is var p && p.StartsWith("PartPick") && p.Contains($"SlotId = {slotId} }}"))
                        return new Vector2(x, y) + L.View.Position;
            return null;
        }

        // The tutorial's first job, its damaged body part taken off (as the tour does).
        var g = Game.Create(a.CI, "Tester", 7);
        a.StartDemo(g);
        await Frames(20);
        _ = a.DemoJob();
        await Wait(1500);
        Shot(dir, "test_signin");
        a.DemoConfirmDialog();
        await Wait(1500);
        Shot(dir, "job1_ws");
        await Click(280, 61, 1000);
        await Hold(Key.Right, 0.315);
        await Wait(500);
        a.DemoTakeOffDamaged();
        Mouse(new Vector2(462, 300), null);
        await Wait(800);
        Shot(dir, "job1_fender_off");
        // The slot pressed and let go without moving; then carried over REPAIR, the car, the command column, let go there.
        Mouse(bin, null);
        await Wait(400);
        Shot(dir, "bin_hover");
        // The part under the pointer turns (the original: a half turn in 0.79 s), and stays as it is when it leaves.
        for (int k = 1; k <= 8; k++)
        {
            await Wait(200);
            Shot(dir, $"bin_turn_{k}");
        }
        Mouse(new Vector2(462, 300), null);
        await Wait(500);
        Shot(dir, "bin_turned_left");
        Mouse(bin, null);
        await Wait(400);
        Mouse(bin, true);
        await Wait(500);
        Shot(dir, "bin_pressed_still");
        Mouse(bin, false);
        await Wait(300);
        Shot(dir, "bin_released_still");
        Mouse(bin, true);
        await Wait(150);
        foreach (var p in new[] { new Vector2(150, 395), new Vector2(300, 396), repair })
        {
            Mouse(p, null);
            await Wait(80);
        }
        await Wait(400);
        Shot(dir, "drag_over_repair");
        Mouse(new Vector2(200, 200), null);
        await Wait(400);
        Shot(dir, "drag_over_car");
        Mouse(new Vector2(280, 222), null);
        await Wait(400);
        Shot(dir, "drag_over_car2");
        Mouse(new Vector2(575, 200), null);
        await Wait(300);
        Shot(dir, "drag_over_column");
        Mouse(new Vector2(575, 200), false);
        await Wait(400);
        Shot(dir, "released_over_column");
        // Repaired, then put back: the job's end, filmed.
        await Drag(bin, new Vector2(150, 396), new Vector2(300, 396), repair);
        await Wait(900);
        Shot(dir, "repair_dialog");
        a.DemoConfirmDialog();
        await Wait(1000);
        Mouse(new Vector2(462, 300), null);
        await Wait(300);
        Shot(dir, "repaired");
        Mouse(bin, null);
        await Frames(3);
        Mouse(bin, true);
        await Wait(150);
        foreach (var p in new[] { new Vector2(100, 330), new Vector2(160, 260), car })
        {
            Mouse(p, null);
            await Wait(80);
        }
        await Wait(300);
        Mouse(car, false);
        // The first frame after every tenth of a second, kept in memory (saving takes longer than a frame).
        var t0 = Time.GetTicksMsec();
        var film = new List<(int Ms, Image Img)>();
        for (int ms = 0; ms <= 3300; ms += 100)
        {
            while (Time.GetTicksMsec() - t0 < (ulong)ms) await Frames(1);
            film.Add((ms, ScreenMode.GameImage(GetViewport(), gameSize: false)));
        }
        foreach (var (ms, img) in film) img.SavePng(Path.Combine(dir, $"end_{ms:0000}.png"));
        for (int i = 0; i < 300 && !a.DemoDialogOpen; i++) await Frames(1);
        await Wait(500);
        Shot(dir, "jobcomplete_box");
        a.DemoConfirmDialog();
        await Wait(1200);
        Shot(dir, "congratulations");
        a.DemoConfirmDialog();
        await Wait(1200);
        Shot(dir, "the_future");
        a.DemoConfirmDialog();
        await Wait(2000);
        Shot(dir, "next_request_job2");
        GD.Print($"check: next job's view yaw {a.DemoOrbit.Yaw * 180 / Mathf.Pi % 360:0.0} pitch {a.DemoOrbit.Pitch * 180 / Mathf.Pi:0.0}");
        while (a.DemoDialogOpen)
        {
            a.DemoCloseDialog();
            await Frames(10);
        }

        // A wheel of the car put on and off: the Impact Wrench's box to attach it, the bin keeping it meanwhile, CANCEL
        // putting it back; then bolted on, and another taken off by its bolts.
        var og = Game.Create(a.CI, "Tester", 8);
        og.State.FreePlay = true;
        og.State.Skill = 1; // Free Play comes after the tutorial, at Novice (else its first income brings a Skill Advance)
        var carId = a.CI.HasCar("orig.mustang") ? "orig.mustang" : a.CI.Pack.Cars[0].Id;
        var own = og.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0, 0, 0, 1]);
        og.State.Lot.Add(own.Id);
        og.BringToWorkshop(own.Id);
        var wheels = a.CI.Car(carId).Slots.Where(s => s.Region == Core.Content.Region.RunningGear && own.Slots[s.Id].Part is { } p && a.CI.Part(p.PartId).Fasteners.Count >= 3
            && s.Name.Contains("Wheel", StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).Take(2).ToList();
        a.StartDemo(og);
        await Frames(20);
        a.SetRegion(View.RunningGear);
        await Wait(800);
        if (wheels.Count == 2)
        {
            og.UseAllFasteners(wheels[0], "ratchet", remove: true);
            og.RemovePart(wheels[0]);
            await Wait(600);
            Shot(dir, "wheel_in_bin");
            await Drag(bin, new Vector2(120, 330), new Vector2(180, 260), car);
            await Wait(1200);
            Shot(dir, "attach_box");
            // Session 9: in bolt mode the pointer on the part lights nothing (only a bolt or a hole, yellow).
            if (OnView(wheels[0]) is { } onPart)
            {
                Mouse(onPart, null);
                await Frames(4);
                Shot(dir, "attach_pointer_on_part");
            }
            await Click(515, 242, 1000);
            Shot(dir, "attach_cancelled");
            GD.Print($"check: after CANCEL the wheel is {(own.Slots[wheels[0]].Part is null ? "off the car" : "on the car")}, the bin holds {og.State.Bin.Count}");
            await Drag(bin, new Vector2(120, 330), new Vector2(180, 260), car);
            await Wait(1200);
            // The user's play of the original: putting a part on, the bolts only go in (a bolt in clicked again stays in,
            // and only the holes take the pointer).
            a.DemoBoltClick(wheels[0], 0);
            a.DemoBoltClick(wheels[0], 0);
            await Frames(2);
            // Turned round until the wheel's face (its bolts) is in sight.
            int holesSeen = 0;
            for (int k = 0; k < 14 && holesSeen == 0; k++)
            {
                holesSeen = a.DemoBoltPoints(wheels[0], wantIn: false).Count;
                if (holesSeen > 0) break;
                a.DemoTurn(1, 0, 0.1);
                await Frames(3);
            }
            GD.Print($"check: attach mode: a bolt in clicked again is {(own.Slots[wheels[0]].Fasteners[0] ? "still in" : "out again")}, bolts in that take the pointer {a.DemoBoltPoints(wheels[0], wantIn: true).Count}, holes {holesSeen}");
            // Session 12: the car in top condition for the first time at the last bolt: Car Complete, at once.
            own.Completed = false;
            for (int i = 0; i < own.Slots[wheels[0]].Fasteners.Count; i++) a.DemoBoltClick(wheels[0], i);
            await Frames(6);
            Shot(dir, "car_complete");
            GD.Print($"check: the last bolt in: {(a.DemoDialogOpen ? "a box, " + a.DemoDialogTitle : "no box")}, the car's clock {(own.Completed ? "stopped" : "running")}");
            a.DemoCloseDialog();
            await Wait(1200);
            Shot(dir, "attached");
            GD.Print($"check: bolted on: the wheel is {(own.Slots[wheels[0]].Part is null ? "off the car" : "on the car")}, bolt mode {(a.DemoBoltOpen ? "open" : "closed")}, the bin holds {og.State.Bin.Count}");
            // Session 9: out of bolt mode the part under the pointer is lit, its condition's colour added onto what is
            // behind it.
            if (OnView(wheels[0]) is { } lit)
            {
                Mouse(lit, null);
                await Frames(4);
                Shot(dir, "pointer_on_part");
                Mouse(new Vector2(560, 470), null);
                await Frames(4);
            }
            // Session 9: another tab's framing is reached in about four frames, most of the way in the first (ENGINE
            // seen once first: a view's first frame can be long while its materials are made ready).
            a.SetRegion(View.Engine);
            await Wait(800);
            a.SetRegion(View.RunningGear);
            await Wait(800);
            var from = a.DemoOrbit.Position;
            a.SetRegion(View.Engine);
            var path = new List<Vector3>();
            var ms = new List<double>();
            ulong start = Time.GetTicksUsec();
            for (int f = 0; f < 10; f++)
            {
                await Frames(1);
                path.Add(a.DemoOrbit.Position);
                ms.Add((Time.GetTicksUsec() - start) / 1000.0);
            }
            float whole = from.DistanceTo(path[^1]);
            GD.Print($"check: to ENGINE ({whole:0.00} away), the way gone each frame: {string.Join(" ", path.Select(p => whole > 0 ? (from.DistanceTo(p) / whole).ToString("0.00", CultureInfo.InvariantCulture) : "-"))}"
                + $" (at {string.Join(" ", ms.Select(m => m.ToString("0", CultureInfo.InvariantCulture)))} ms)");
            a.SetRegion(View.RunningGear);
            await Wait(800);
            a.DemoBoltSlot(wheels[1]);
            await Wait(800);
            // Taking a part off, the bolts only come out.
            a.DemoBoltClick(wheels[1], 0);
            a.DemoBoltClick(wheels[1], 0);
            await Frames(2);
            int boltsSeen = 0;
            for (int k = 0; k < 14 && boltsSeen == 0; k++)
            {
                boltsSeen = a.DemoBoltPoints(wheels[1], wantIn: true).Count;
                if (boltsSeen > 0) break;
                a.DemoTurn(1, 0, 0.1);
                await Frames(3);
            }
            GD.Print($"check: unscrew mode: a bolt out clicked again is {(own.Slots[wheels[1]].Fasteners[0] ? "in again" : "still out")}, bolts in that take the pointer {boltsSeen}, holes {a.DemoBoltPoints(wheels[1], wantIn: false).Count}");
            // Session 11: while the box is up a tab takes no click; CANCEL puts the bolts taken out back in, and out of
            // bolt mode no bolt is drawn.
            var regionWas = a.Region;
            await Click(64, 61, 600);
            GD.Print($"check: bolt mode: the COMPLETE tab clicked: {(a.DemoBoltOpen ? "the box still up" : "the box gone")}, the view {(a.Region == regionWas ? "as it was" : "changed to " + a.Region)}");
            // Session 12: the bolts go back one after another, a bolt's sound each; the box stays up till the last.
            int outNow = own.Slots[wheels[1]].Fasteners.Count(x => !x);
            a.DemoCancelBolts();
            await Frames(2);
            GD.Print($"check: CANCEL with {outNow} bolt(s) out: at once the box {(a.DemoBoltOpen ? "still up" : "gone")}, bolts in {own.Slots[wheels[1]].Fasteners.Count(x => x)}");
            await Wait(outNow * 375 + 300);
            GD.Print($"check: CANCEL while taking a part off: bolts in {own.Slots[wheels[1]].Fasteners.Count(x => x)} of {own.Slots[wheels[1]].Fasteners.Count}, bolt mode {(a.DemoBoltOpen ? "open" : "closed")}, bolts drawn {a.DemoBoltPoints(wheels[1], wantIn: true).Count + a.DemoBoltPoints(wheels[1], wantIn: false).Count}");
            a.DemoBoltSlot(wheels[1]);
            await Wait(800);
            for (int i = 0; i < own.Slots[wheels[1]].Fasteners.Count; i++) a.DemoBoltClick(wheels[1], i);
            await Wait(1200);
            Shot(dir, "unscrewed");
            GD.Print($"check: unscrewed: the other wheel is {(own.Slots[wheels[1]].Part is null ? "off the car" : "on the car")}, bolt mode {(a.DemoBoltOpen ? "open" : "closed")}, the bin holds {og.State.Bin.Count}");
            // Session 9: a black part does not go back on ("Assembly Error!"); it stays in the bin.
            if (og.State.Bin.Count > 0)
            {
                og.State.Bin[0].Part.Condition = Condition.Black;
                await Drag(bin, new Vector2(120, 330), new Vector2(180, 260), car);
                await Wait(800);
                Shot(dir, "black_dropped");
                GD.Print($"check: a black wheel let go over the car: {(a.DemoDialogOpen ? "a box, " + a.DemoDialogTitle : "no box")}, the wheel is {(own.Slots[wheels[1]].Part is null ? "off the car" : "on the car")}, the bin holds {og.State.Bin.Count}");
                a.DemoCloseDialog();
                await Frames(10);
            }
        }
        // The user's play of the original: a part bought in the JunkYard leaves the others where they lie.
        a.Go(Screen.Junkyard);
        await Wait(1500);
        var lying = a.DemoJunkPoints();
        if (lying.Count > 1)
        {
            var before = a.DemoJunkPlaces();
            var (buyId, buyAt) = lying[lying.Count / 2];
            Shot(dir, "junk_before");
            await Click(buyAt.X, buyAt.Y, 900);
            Mouse(new Vector2(560, 470), null);
            await Frames(4);
            Shot(dir, "junk_bought");
            var after = a.DemoJunkPlaces();
            int moved = after.Count(p => before.TryGetValue(p.Key, out var was) && was.DistanceTo(p.Value) > 0.001f);
            GD.Print($"check: JunkYard: {og.State.PurchaseBin.Count} part bought ({(after.ContainsKey(buyId) ? "still in the yard" : "gone from the yard")}); of the other {after.Count} parts {moved} moved");
            // Session 11: another area looked at and back, the places stay as they were (the bought one's empty).
            var home = a.DemoJunkArea;
            Vector2 Plank(Core.Content.Region r) => r switch { Core.Content.Region.Engine => new(556, 157), Core.Content.Region.Body => new(556, 191), _ => new(553, 225) };
            var away = Plank(home == Core.Content.Region.Body ? Core.Content.Region.Engine : Core.Content.Region.Body);
            await Click(away.X, away.Y, 1000);
            await Click(Plank(home).X, Plank(home).Y, 1000);
            Mouse(new Vector2(560, 470), null);
            await Frames(4);
            var again = a.DemoJunkPlaces();
            GD.Print($"check: JunkYard: BODY and back: the bought part {(again.ContainsKey(buyId) ? "back in the yard" : "still gone")}, {again.Count(p => before.TryGetValue(p.Key, out var was) && was.DistanceTo(p.Value) > 0.001f)} of {again.Count} parts moved");
            await Click(61, 398, 900);
            Mouse(new Vector2(560, 470), null);
            await Frames(4);
            Shot(dir, "junk_given_back");
            var back = a.DemoJunkPlaces();
            GD.Print($"check: JunkYard: given back ({og.State.PurchaseBin.Count} left in the Purchase Bin), the part lies {(!back.TryGetValue(buyId, out var at) ? "nowhere" : at.DistanceTo(before[buyId]) < 0.001f ? "in its place" : "elsewhere")}, {back.Count(p => before.TryGetValue(p.Key, out var was) && was.DistanceTo(p.Value) > 0.001f)} parts moved");
            // The next visit lays the row out afresh from the shelf, where the part given back went last (then what the
            // visit brings).
            a.Go(Screen.Workshop);
            await Wait(600);
            a.Go(Screen.Junkyard);
            await Wait(1500);
            var next = a.DemoJunkPlaces();
            if (next.TryGetValue(buyId, out var nx))
                GD.Print($"check: JunkYard: next visit: the part given back lies past {next.Count(p => p.Key != buyId && before.ContainsKey(p.Key) && p.Value.X < nx.X)} of the {next.Count(p => p.Key != buyId && before.ContainsKey(p.Key))} parts that were there before it");
            else GD.Print("check: JunkYard: next visit: the part given back went with the visit's change");
        }
        // Session 14: a Novice whose save's cash is past two bars signs in: Skill Advance and Available Cars for Handy,
        // then for Expert, over the WorkShop's tabs with no car and no commands; the car comes in after the last OK.
        var sg = Game.Create(a.CI, "Tester", 9);
        foreach (var j in sg.JobChain) sg.State.CompletedJobs.Add(j.Id);
        sg.State.FreePlay = true;
        sg.State.Skill = 1;
        sg.State.Cash = sg.State.BestCash = 50_000;
        var signCar = sg.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0, 0, 0, 1]);
        sg.State.Lot.Add(signCar.Id);
        sg.BringToWorkshop(signCar.Id);
        signCar.Completed = true;
        a.StartDemo(sg, skillCheck: true);
        var boxes = new List<string>();
        for (int k = 0; k < 6; k++)
        {
            await Frames(20);
            if (!a.DemoDialogOpen) break;
            boxes.Add(a.DemoDialogTitle + (a.DemoCarHeld > 0 ? "" : " (the car in view)"));
            Shot(dir, $"signin_box_{k}");
            a.DemoConfirmDialog();
        }
        await Frames(20);
        Shot(dir, "signin_after");
        GD.Print($"check: signing in as a Novice with $50,000: {string.Join(", ", boxes)}; then {sg.SkillName}, the car {(a.DemoCarHeld == 0 ? "in view" : "still kept out")}");
        a.DemoSaveThumbs(dir);
        GetTree().Quit();
    }

    /// <summary>
    /// <c>--playtest &lt;dir&gt;</c>: a whole game played through with the mouse (as input events at the original's
    /// places): a new mechanic, the nine tutorial jobs, Free Play's shops and jobs, the Auction bought and sold at, the
    /// Car Lot, paint and decals, leaving a job, the sign-in sheet's boxes and IMPORT MECHANICS. A screenshot at every
    /// step (named as the original's captures where there is one to compare with) and "check:" lines in the log.
    /// Run with <c>--user &lt;an empty folder&gt;</c>: it makes mechanics there.
    /// </summary>
    async Task PlayTest(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        async Task Wait(int ms) => await ToSignal(GetTree().CreateTimer(ms / 1000.0), SceneTreeTimer.SignalName.Timeout);
        void Mouse(Vector2 place, bool? press)
        {
            var at = GetViewport().GetFinalTransform() * place;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = press == true || Input.IsMouseButtonPressed(MouseButton.Left) ? MouseButtonMask.Left : 0 });
            if (press is { } p)
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = p, ButtonMask = p ? MouseButtonMask.Left : 0 });
        }
        async Task Click(float x, float y, int afterMs = 700)
        {
            Mouse(new Vector2(x, y), null);
            await Frames(3);
            Mouse(new Vector2(x, y), true);
            await Wait(70);
            Mouse(new Vector2(x, y), false);
            await Wait(afterMs);
        }
        async Task HoldAt(float x, float y, int ms, int afterMs = 500)
        {
            Mouse(new Vector2(x, y), null);
            await Frames(3);
            Mouse(new Vector2(x, y), true);
            await Wait(ms);
            Mouse(new Vector2(x, y), false);
            await Wait(afterMs);
        }
        async Task Drag(params Vector2[] path)
        {
            Mouse(path[0], null);
            await Frames(3);
            Mouse(path[0], true);
            await Wait(150);
            for (int i = 1; i < path.Length; i++)
            {
                Mouse(path[i], null);
                await Wait(80);
            }
            await Wait(300);
            Mouse(path[^1], false);
            await Wait(600);
        }
        async Task Away()
        {
            Mouse(new Vector2(462, 300), null);
            await Wait(300);
        }
        async Task<bool> WaitDialog(int ms = 6000)
        {
            for (int i = 0; i < ms / 50 && !a.DemoDialogOpen; i++) await Wait(50);
            return a.DemoDialogOpen;
        }
        async Task Ok(int afterMs = 900)
        {
            if (a.DemoDialogOpen) a.DemoConfirmDialog();
            await Wait(afterMs);
        }
        void Check(string what) => GD.Print($"check: {what}");
        var g = () => a.Game;
        var CI = () => a.CI;
        string Money(decimal m) => Game.Money(m);
        var bin1 = new Vector2(53, 397);
        var bin2 = new Vector2(147, 397);
        var repair = new Vector2(458, 396);
        var scrap = new Vector2(458, 450);
        var car = new Vector2(200, 205);
        int phase = 0;
        async Task Phase(string name, Func<Task> body)
        {
            phase++;
            GD.Print($"playtest: {phase} {name}");
            try { await body(); }
            catch (Exception e) { GD.PrintErr($"playtest: {name} failed: {e}"); }
        }

        // ---- the sign-in sheet: NEW, the name typed, the row, signing in ----
        await Phase("sign-in", async () =>
        {
            await Wait(1500);
            Shot(dir, "s_signin_empty");
            await Click(103, 172, 900);
            Shot(dir, "s_new_box");
            foreach (char ch in "Tester")
            {
                Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = (Key)char.ToUpperInvariant(ch), Unicode = ch });
                await Frames(2);
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = (Key)char.ToUpperInvariant(ch) });
                await Frames(2);
            }
            Shot(dir, "s_new_typed");
            await Click(262, 280, 1200);
            Shot(dir, "s_signin_row");
            await Click(216, 172, 2500);
            Shot(dir, "s_request_job1");
            Check($"sign-in: cash {Money(g().State.Cash)}, jobs mode {g().InJobsMode}, offer {g().State.Offer?.Fee}");
        });

        // ---- the tutorial's jobs, done as a player would; the first two step by step as in the original's session ----
        int unexpected = 0;
        async Task ClearBoxes(string ctx)
        {
            for (int i = 0; i < 6 && a.DemoDialogOpen; i++)
            {
                unexpected++;
                Shot(dir, $"unexpected_{unexpected:00}");
                Check($"{ctx}: UNEXPECTED box '{a.DemoDialogTitle}' (closed)");
                a.DemoConfirmDialog();
                await Wait(900);
            }
        }
        async Task FinishJob(string tag)
        {
            var err = a.DemoSolveJob();
            if (err.Length > 0) Check($"{tag}: solve: {err}");
            if (await WaitDialog(7000))
            {
                if (a.DemoDialogTitle != "Job Complete!")
                {
                    await ClearBoxes(tag);
                    await WaitDialog(7000);
                }
                Shot(dir, $"{tag}_complete");
                Check($"{tag}: '{a.DemoDialogTitle}' up, cash {Money(g().State.Cash)}, bin {g().State.Bin.Count}");
            }
            else Check($"{tag}: NO Job Complete box (job {(g().State.Job is null ? "gone" : "still on")})");
            await Ok(1200);
        }
        async Task<bool> AcceptRequest(string tag)
        {
            if (!await WaitDialog(6000)) { Check($"{tag}: NO request box"); return false; }
            if (a.DemoDialogTitle != "Job Request") { await ClearBoxes(tag); if (!await WaitDialog(6000)) return false; }
            Shot(dir, $"{tag}_request");
            Check($"{tag}: offer {g().State.Offer?.TemplateId}, fee {g().State.Offer?.Fee}, text: {g().State.Offer?.Text}");
            await Click(293, 366, 1500);
            Shot(dir, $"{tag}_ws");
            var j = g().State.Job;
            Check($"{tag}: job {j?.TemplateId}, tabs {(j?.Tabs is { } t ? string.Join("/", t) : "all")}, car {g().WorkshopVehicle()?.ModelId}");
            return j is not null;
        }
        for (int n = 1; n <= 12 && g().InJobsMode; n++)
        {
            int job = n;
            await Phase($"tutorial job {job}", async () =>
            {
                if (!await AcceptRequest($"t{job}")) return;
                var v = g().WorkshopVehicle()!;
                var tpl = g().State.Job!.TemplateId;
                if (tpl.EndsWith(".0", StringComparison.Ordinal))
                {
                    // The first job as the original's session did it: BODY, the view turned, the fender clicked off (its
                    // name under the view), Job Help, REPAIR, back on the car, the end filmed.
                    await Click(280, 61, 900);
                    Shot(dir, "t1_body");
                    await Hold(Key.Right, 0.315);
                    await Wait(400);
                    Shot(dir, "t1_body_right90");
                    var slot = v.Slots.First(kv => kv.Value.Part is { Condition: < Condition.Green }).Key;
                    if (a.DemoSlotPoint(slot) is { } at)
                    {
                        Mouse(at, null);
                        await Wait(400);
                        Shot(dir, "t1_hover_fender");
                        await Click(at.X, at.Y, 800);
                    }
                    else Check("t1: no click point for the damaged part");
                    await Away();
                    Shot(dir, "t1_fender_off");
                    Check($"t1: bin {g().State.Bin.Count} after the click");
                    await Click(581, 206, 1200);
                    Shot(dir, "t1_job_update");
                    await Click(293, 366, 800);
                    await Drag(bin1, new Vector2(150, 396), new Vector2(300, 396), repair);
                    Shot(dir, "t1_repair_dialog");
                    await Click(263, 281, 900);
                    await Away();
                    Shot(dir, "t1_repaired");
                    Check($"t1: budget {Money(g().State.Job?.Budget ?? 0)} after the repair");
                    await Drag(bin1, new Vector2(100, 330), new Vector2(160, 260), car);
                    await Wait(1200);
                    Shot(dir, "t1_end_spin");
                    if (await WaitDialog(4000)) Shot(dir, "t1_complete");
                    Check($"t1: done: '{a.DemoDialogTitle}', cash {Money(g().State.Cash)} (5000 + 100 + what was left)");
                    await Ok(900);
                    Shot(dir, "t1_congratulations");
                    Check($"t1: '{a.DemoDialogTitle}'");
                    await Ok(900);
                    Shot(dir, "t1_future");
                    Check($"t1: '{a.DemoDialogTitle}'");
                    await Ok(1500);
                    return;
                }
                if (tpl.EndsWith(".1", StringComparison.Ordinal))
                {
                    await Click(280, 61, 900);
                    Shot(dir, "t2_body");
                    foreach (var slot in v.Slots.Where(kv => kv.Value.Part is { Condition: < Condition.Green } && kv.Value.Fasteners.Count == 0).Select(kv => kv.Key).ToList())
                    {
                        if (a.DemoSlotPoint(slot) is { } at) await Click(at.X, at.Y, 800);
                        else
                        {
                            await Hold(Key.Left, 0.63);
                            await Wait(300);
                            if (a.DemoSlotPoint(slot) is { } at2) await Click(at2.X, at2.Y, 800);
                            else Check($"t2: no click point for {slot}");
                        }
                    }
                    await Away();
                    Shot(dir, "t2_both_off");
                    Check($"t2: bin {g().State.Bin.Count}");
                    await Drag(bin1, new Vector2(150, 396), new Vector2(300, 396), repair);
                    await Click(263, 281, 900);
                    await Drag(bin2, new Vector2(200, 396), new Vector2(350, 396), repair);
                    Shot(dir, "t2_repair_dialog2");
                    await Click(263, 281, 900);
                    await Away();
                    Shot(dir, "t2_both_repaired");
                    await Drag(bin1, new Vector2(100, 330), new Vector2(160, 260), car);
                    await Wait(800);
                    Shot(dir, "t2_one_back");
                    await Drag(bin1, new Vector2(100, 330), new Vector2(160, 260), car);
                    if (await WaitDialog(5000))
                    {
                        Shot(dir, "t2_complete");
                        Check($"t2: '{a.DemoDialogTitle}', cash {Money(g().State.Cash)}");
                        await Ok(1500);
                    }
                    else
                    {
                        Check("t2: no complete box after the drops; solving the rest");
                        await FinishJob("t2");
                    }
                    return;
                }
                if (tpl.EndsWith(".3", StringComparison.Ordinal))
                {
                    // The wheel: bolt mode by a click on it, its bolts by clicks, the part in the bin.
                    await Click(415, 61, 900);
                    var slot = v.Slots.First(kv => kv.Value.Part is { Condition: < Condition.Green }).Key;
                    for (int turn = 0; turn < 4 && a.DemoSlotPoint(slot) is null; turn++) { await Hold(Key.Left, 0.315); await Wait(300); }
                    if (a.DemoSlotPoint(slot) is { } at)
                    {
                        Mouse(at, null);
                        await Wait(400);
                        Shot(dir, "t4_hover_wheel");
                        await Click(at.X, at.Y, 1200);
                        await Away();
                        Shot(dir, "t4_boltmode");
                        Check($"t4: bolt mode {a.DemoBoltOpen} after the click; box '{a.DemoDialogTitle}'");
                        for (int tries = 0; tries < 14 && a.DemoBoltOpen; tries++)
                        {
                            var pts = a.DemoBoltPoints(slot, wantIn: true);
                            if (pts.Count == 0) { await Hold(Key.Up, 0.15); await Wait(200); await Hold(Key.Left, 0.1); await Wait(200); continue; }
                            foreach (var p in pts) { if (!a.DemoBoltOpen) break; await Click(p.X, p.Y, 400); }
                        }
                        await Away();
                        Shot(dir, "t4_wheel_off");
                        Check($"t4: after the bolts: bolt mode {a.DemoBoltOpen}, bin {g().State.Bin.Count}, wheel on the car {v.Slots[slot].Part is not null}");
                        // Repaired and back on: the attach box, its bolts clicked in.
                        if (g().State.Bin.Count > 0)
                        {
                            await Drag(bin1, new Vector2(150, 396), new Vector2(300, 396), repair);
                            await Click(263, 281, 900);
                            await Drag(bin1, new Vector2(100, 330), new Vector2(160, 260), car);
                            await Wait(800);
                            Shot(dir, "t4_attach_box");
                            Check($"t4: attach: bolt mode {a.DemoBoltOpen}, bin shows {g().State.Bin.Count}");
                            for (int tries = 0; tries < 14 && a.DemoBoltOpen; tries++)
                            {
                                var pts = a.DemoBoltPoints(slot, wantIn: false);
                                if (pts.Count == 0) { await Hold(Key.Up, 0.15); await Wait(200); await Hold(Key.Left, 0.1); await Wait(200); continue; }
                                foreach (var p in pts) { if (!a.DemoBoltOpen) break; await Click(p.X, p.Y, 400); }
                            }
                            Check($"t4: bolted: bolt mode {a.DemoBoltOpen}, job {(g().State.Job is null ? "done" : "on")}");
                        }
                    }
                    else Check("t4: the wheel could not be clicked");
                }
                if (tpl.EndsWith(".4", StringComparison.Ordinal))
                {
                    await Click(185, 61, 900);
                    await Away();
                    Shot(dir, "t5_engine");
                }
                await FinishJob($"t{job}");
                if (!g().InJobsMode)
                {
                    // Skill Advance (Learning -> Novice) over a blank WorkShop; no Available Cars for this one.
                    if (await WaitDialog(3000)) Shot(dir, "t_skill_advance");
                    Check($"tutorial over: '{a.DemoDialogTitle}', skill {g().SkillName}, free play {g().State.FreePlay}, cash {Money(g().State.Cash)}");
                    await Ok(1200);
                    await ClearBoxes("after the tutorial");
                    Shot(dir, "fp_workshop_empty");
                }
            });
        }

        // ---- Free Play: Get A Job, CANCEL, the same again, Job Help, the Catalog and the JunkYard looked at, Exit's box ----
        await Phase("free play job", async () =>
        {
            if (g().InJobsMode) { Check("fp: still in Jobs Mode, skipped"); return; }
            await Click(575, 206, 1500);
            Shot(dir, "fp_request");
            var first = g().State.Offer?.Text;
            await Click(410, 366, 900);
            await Click(575, 206, 1500);
            Check($"fp: the same offer after CANCEL: {g().State.Offer?.Text == first}");
            await Click(293, 366, 1500);
            Shot(dir, "fp_job_ws");
            await Click(581, 206, 1200);
            Shot(dir, "fp_job_update");
            await Click(293, 366, 800);
            // The Catalog from the job: opens at the tab's section; a dear part asked for and refused (Bummer).
            await Click(575, 294, 1500);
            Shot(dir, "fp_catalog");
            await Click(599, 177, 900);
            Shot(dir, "fp_catalog_engine");
            await Click(124, 144, 900);
            Shot(dir, "fp_buy_part");
            Check($"fp: first card: box '{a.DemoDialogTitle}'");
            await Click(263, 281, 900);
            Shot(dir, "fp_bummer");
            Check($"fp: after OK: box '{a.DemoDialogTitle}' (the original: Bummer when the budget is short)");
            await Ok(700);
            await Click(415, 64, 1500);
            await ClearBoxes("fp back from catalog");
            // The JunkYard from the job: its area, a part under the pointer; back without buying.
            await Click(575, 386, 2500);
            Shot(dir, "fp_junkyard");
            var pts = a.DemoJunkPoints();
            Check($"fp: junk parts clickable in view: {pts.Count} of {g().Junk.Count}");
            if (pts.Count > 0)
            {
                Mouse(pts[0].At, null);
                await Wait(500);
                Shot(dir, "fp_junk_hover");
            }
            await Click(415, 64, 1500);
            await ClearBoxes("fp back from junk");
            // Exit during the job: the box, CANCEL; then done.
            await Click(610, 466, 1200);
            Shot(dir, "fp_job_active");
            await Click(381, 281, 900);
            Check($"fp: still on the job after CANCEL: {g().State.Job is not null}");
            await FinishJob("fp");
            await ClearBoxes("fp after the job");
            Shot(dir, "fp_after_job");
        });

        // ---- the Auction: a car bought; the WorkShop with your own car ----
        await Phase("auction buy", async () =>
        {
            await ClearBoxes("before the auction");
            if (g().InJobsMode || g().State.Job is not null) { Check("au: not in Free Play without a job, skipped"); return; }
            await Click(575, 430, 2500);
            Shot(dir, "au_start");
            await Wait(3000);
            Shot(dir, "au_3s");
            if (g().State.Auction is { } au)
            {
                au.RivalMax = 0;
                await Click(574, 356, 1500);
                Shot(dir, "au_my_bid");
                Check($"au: leader {au.Leader}, current {au.CurrentBid}, asking {au.Asking}, step {au.Step}");
                for (int i = 0; i < 60 && !a.DemoDialogOpen; i++) await Wait(500);
                Shot(dir, "au_winning_bid");
                Check($"au: '{a.DemoDialogTitle}': bought for {au.Result?.Price}, cash {Money(g().State.Cash)}");
                await Ok(1500);
                Shot(dir, "own_ws");
                Check($"own: workshop {g().WorkshopVehicle()?.ModelId}, owner {g().WorkshopVehicle()?.Owner}, number {g().WorkshopVehicle()?.Number}, orig cost {g().WorkshopVehicle()?.Stats.OrigCost}");
            }
        });

        // ---- your own car: the shops with your own cash, Show Condition, the Camera, Body Paint, decals, Start Engine ----
        await Phase("own car", async () =>
        {
            if (g().WorkshopVehicle() is not { Owner: Core.Sim.Owner.Player } own) { Check("own: no car of yours in the WorkShop, skipped"); return; }
            // The Catalog: a part bought, a page turned, a decal bought.
            await Click(575, 294, 1500);
            Shot(dir, "own_catalog");
            await Click(599, 177, 900);
            var cash0 = g().State.Cash;
            if (a.DemoCatalogCard(cash0) is { } card)
            {
                await Click(card.At.X, card.At.Y, 900);
                Shot(dir, "own_buy_part");
                await Click(263, 281, 900);
                Check($"own: bought {card.Name} ({Money(card.Price)}): cash {Money(cash0)} -> {Money(g().State.Cash)}, bin {g().State.Bin.Count}, repair cost {g().WorkshopVehicle()?.Stats.RepairCost}");
            }
            await Click(554, 442, 700);
            Shot(dir, "own_catalog_page2");
            await Click(40, 178, 900);
            Shot(dir, "own_catalog_decals");
            await Click(99, 149, 900);
            Shot(dir, "own_buy_decal");
            await Click(263, 281, 900);
            Check($"own: decal uses {string.Join(",", g().State.DecalUses.Select(u => u.Key + "=" + u.Value))}");
            await Click(415, 64, 1500);
            await ClearBoxes("own back from catalog");
            Shot(dir, "own_back_from_catalog");
            // The JunkYard: hover, buy, give back, buy again; the planks; the arrow held; back with the part.
            await Click(575, 386, 2500);
            Shot(dir, "own_junkyard");
            var pts = a.DemoJunkPoints();
            Check($"own: junk parts clickable in view: {pts.Count} of {g().Junk.Count}");
            if (pts.Count > 0)
            {
                var (id, at) = pts[0];
                var price = g().Junk.Find(j => j.Id == id) is { } ji ? Economy.JunkPrice(CI(), ji.Part) : 0;
                Mouse(at, null);
                await Wait(500);
                Shot(dir, "own_junk_hover");
                cash0 = g().State.Cash;
                await Click(at.X, at.Y, 900);
                await Away();
                Shot(dir, "own_junk_bought");
                Check($"own: junk bought for {Money(price)}: purchase bin {g().State.PurchaseBin.Count}, cash {Money(cash0)} -> {Money(g().State.Cash)}");
                Mouse(new Vector2(61, 398), null);
                await Wait(400);
                Shot(dir, "own_junk_pbin_hover");
                await Click(61, 398, 900);
                Shot(dir, "own_junk_returned");
                Check($"own: after the return: purchase bin {g().State.PurchaseBin.Count}, cash {Money(g().State.Cash)}");
                var again = a.DemoJunkPoints().FirstOrDefault(p => p.Id == id);
                if (again.Id is not null) await Click(again.At.X, again.At.Y, 900);
            }
            await Click(556, 191, 1200);
            Shot(dir, "own_junk_body");
            await Click(553, 225, 1200);
            Shot(dir, "own_junk_rgear");
            await HoldAt(489, 331, 1500, 500);
            Shot(dir, "own_junk_panned");
            await Click(556, 157, 1200);
            Shot(dir, "own_junk_engine_again");
            await Click(415, 64, 1500);
            await ClearBoxes("own back from junk");
            Shot(dir, "own_back_from_junk");
            Check($"own: bin {g().State.Bin.Count} after the JunkYard, cash {Money(g().State.Cash)}, repair cost {g().WorkshopVehicle()?.Stats.RepairCost}");
            // The junk part carried to REPAIR (its box) and then to SCRAP (its box).
            await Click(62, 61, 900);
            var junkItem = g().State.Bin.FirstOrDefault(b => b.From == "junkyard");
            if (junkItem is not null)
            {
                Vector2 SlotOf(string uid)
                {
                    var shownItems = g().BinFor(g().WorkshopVehicle()?.ModelId).Select(b => b.Part.Uid).ToList();
                    int idx = Math.Max(0, shownItems.IndexOf(uid));
                    return new Vector2(53 + (idx % 4) * 93.7f, 397 + (idx / 4) * 50);
                }
                await Drag(SlotOf(junkItem.Part.Uid), new Vector2(300, 400), repair);
                Shot(dir, "own_repair_dialog");
                Check($"own: REPAIR on a {junkItem.Part.Condition switch { 0 => "black", 1 => "red", 2 => "yellow", _ => "green" }} part: box '{a.DemoDialogTitle}'");
                await Click(263, 281, 900);
                await ClearBoxes("own repair");
                await Drag(SlotOf(junkItem.Part.Uid), new Vector2(300, 420), scrap);
                Shot(dir, "own_scrap_dialog");
                cash0 = g().State.Cash;
                await Click(263, 281, 900);
                Check($"own: scrapped: bin {g().State.Bin.Count}, cash {Money(cash0)} -> {Money(g().State.Cash)}");
            }
            // Show Condition held; the Camera; Body Paint; decals; Start Engine.
            await HoldAt(575, 118, 800, 300);
            Shot(dir, "own_show_condition");
            await Click(448, 190, 700);
            int shots0 = Directory.Exists(Paths.Snapshots) ? Directory.GetFiles(Paths.Snapshots, "*.jpg", SearchOption.AllDirectories).Length : 0;
            await Click(200, 205, 900);
            int shots1 = Directory.Exists(Paths.Snapshots) ? Directory.GetFiles(Paths.Snapshots, "*.jpg", SearchOption.AllDirectories).Length : 0;
            Check($"own: camera snapshots {shots0} -> {shots1}");
            await Click(280, 61, 900);
            await Click(448, 190, 700);
            await Away();
            Shot(dir, "own_paint_tools");
            await Click(407, 278, 300);
            await HoldAt(195, 215, 1000, 300);
            await Away();
            Shot(dir, "own_sprayed");
            await Click(482, 241, 300);
            await Click(200, 205, 700);
            await Away();
            Shot(dir, "own_panel_painted");
            Check($"own: paint picture {g().Canvas(own) is not null}, cash {Money(g().State.Cash)} (painting is free)");
            await Click(446, 349, 1200);
            Shot(dir, "own_decal_browser");
            await Click(168, 187, 900);
            Shot(dir, "own_decal_modify");
            await Click(320, 231, 400);
            await Click(396, 166, 400);
            Shot(dir, "own_decal_bigger_colour");
            await Click(359, 367, 900);
            Mouse(car, null);
            await Wait(600);
            Shot(dir, "own_decal_preview");
            await Click(200, 205, 900);
            await Away();
            Shot(dir, "own_decal_placed");
            Check($"own: decals on the car {own.Decals.Count} (none when the panel clicked is in bare metal: measured, the use goes all the same), uses left {string.Join(",", g().State.DecalUses.Select(u => u.Key + "=" + u.Value))}");
            await Click(446, 349, 1200);
            Shot(dir, "own_decal_browser_again");
            await Click(465, 367, 700);
            await Click(62, 61, 900);
            Mouse(new Vector2(447, 318), null);
            await Frames(3);
            Mouse(new Vector2(447, 318), true);
            await Wait(1000);
            Shot(dir, "own_engine_running");
            await Wait(600);
            Mouse(new Vector2(447, 318), false);
            await Wait(1500);
            await ClearBoxes("own end");
        });

        // ---- the Car Lot: parked, the arrows, back in; then sold at the Auction ----
        await Phase("car lot and sale", async () =>
        {
            if (g().WorkshopVehicle() is not { Owner: Core.Sim.Owner.Player }) { Check("lot: no car of yours in the WorkShop, skipped"); return; }
            await Click(575, 162, 2500);
            Shot(dir, "lot_parked");
            Check($"lot: cars {g().State.Lot.Count}, workshop {g().State.Workshop}");
            await HoldAt(583, 368, 1000, 300);
            Shot(dir, "lot_glided");
            await HoldAt(48, 368, 1500, 300);
            // Back to the first bay, so the car is well inside the view (a long one sat at its edge after the glide).
            await HoldAt(583, 368, 1800, 300);
            if (a.DemoLotCarPoint() is { } cp)
            {
                await Click(cp.X, cp.Y, 1500);
                Shot(dir, "lot_car_back_in");
                Check($"lot: back in the workshop {g().State.Workshop is not null}");
            }
            else Check("lot: the parked car could not be clicked");
            if (g().WorkshopVehicle() is null) return;
            await Click(575, 250, 1200);
            Shot(dir, "sell_ask");
            await Click(263, 281, 2000);
            Shot(dir, "sell_auction");
            for (int i = 0; i < 70 && !a.DemoDialogOpen; i++) await Wait(500);
            Shot(dir, "sell_sold");
            Check($"sell: result {g().State.Auction?.Result?.Buyer} at {g().State.Auction?.Result?.Price}, profit {g().State.Auction?.Result?.Profit}, cash {Money(g().State.Cash)}");
            await Ok(1500);
            Shot(dir, "sell_after");
            Check($"sell: workshop {g().State.Workshop}, lot {g().State.Lot.Count}, cars owned {g().CarsOwned}");
            await Click(575, 342, 2000);
            Shot(dir, "lot_empty");
            await Click(415, 64, 1200);
        });

        // ---- leaving a job, the sheet's boxes, IMPORT MECHANICS ----
        await Phase("exit and sheet", async () =>
        {
            await Click(575, 206, 1500);
            var offer = g().State.Offer?.Text;
            await Click(293, 366, 1500);
            await Click(610, 466, 1200);
            Shot(dir, "x_job_active");
            await Click(263, 281, 2500);
            Shot(dir, "x_signin_after_exit");
            Check($"exit: profile saved with job {(a.Game.State.Job is null ? "none" : "on")}");
            await Click(216, 172, 2500);
            Shot(dir, "x_ws_after_signin");
            await Click(575, 206, 1500);
            Check($"exit: the offer after leaving is the same one: {g().State.Offer?.Text == offer}");
            await Click(410, 366, 900);
            await Click(610, 466, 2000);
            await Click(44, 172, 1200);
            Shot(dir, "x_delete_box");
            await Click(390, 280, 900);
            await Click(492, 95, 1200);
            Shot(dir, "x_exit_box");
            await Click(390, 280, 900);
            await Click(492, 63, 1500);
            Shot(dir, "x_credits");
            await Click(555, 460, 1500);
            if (a.DemoPressSignIn("IMPORT"))
            {
                await Wait(1200);
                Shot(dir, "x_import_ask");
                await Ok(4000);
                Shot(dir, "x_signin_imported");
            }
            else Check("exit: no IMPORT MECHANICS button");
        });
        GD.Print("playtest: done");
        GetTree().Quit();
    }

    /// <summary><c>--sheetshot &lt;dir&gt;</c>: the sign-in sheet as the game starts (with <c>--user</c>, the mechanics saved there), then quit.</summary>
    async Task SheetShot(string dir)
    {
        Directory.CreateDirectory(dir);
        await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
        Shot(dir, "sheet_again");
        var a = app!;
        // The first mechanic signed in: what the WorkShop shows after a reload.
        Input.ParseInputEvent(new InputEventMouseMotion { Position = GetViewport().GetFinalTransform() * new Vector2(216, 172) });
        await Frames(3);
        foreach (bool p in new[] { true, false })
        {
            var at = GetViewport().GetFinalTransform() * new Vector2(216, 172);
            Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = p, ButtonMask = p ? MouseButtonMask.Left : 0 });
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        }
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        Shot(dir, "ws_again");
        GD.Print($"check: reloaded: cash {Game.Money(a.Game.State.Cash)}, skill {a.Game.SkillName}, workshop {a.Game.State.Workshop}, lot {a.Game.State.Lot.Count}, bin {a.Game.State.Bin.Count}, decals {string.Join(",", a.Game.State.DecalUses.Select(u => u.Key + "=" + u.Value))}, shelves {a.Game.State.Shelves.Count}, job {(a.Game.State.Job is null ? "none" : "on")}, offer {(a.Game.State.Offer is null ? "none" : "waiting")}");
        GetTree().Quit();
    }

    async Task Tour(string dir)
    {
        var a = app!;
        Directory.CreateDirectory(dir);
        await Frames(30);
        Shot(dir, "01_signin");
        if (Args.Get("mek") is { } mek && a.Original is { } og)
        {
            // An original's mechanic brought over: its WorkShop, Parts Bin and Car Lot.
            var (imported, problems) = MekImport.Import(og, a.CI, MekFile.Read(mek), 7);
            foreach (var p in problems) GD.Print($"import: {p}");
            a.StartDemo(imported);
            await Frames(60);
            Shot(dir, "m1_workshop");
            a.SetRegion(View.Body);
            await Frames(60);
            Shot(dir, "m2_body");
            // The Decal Browser with the decals brought over: the list, the first one to modify, BIGGER in red.
            a.DemoDecalBrowser();
            await Frames(10);
            Shot(dir, "m2b_decals_choose");
            a.DemoBrowse("first");
            await Frames(10);
            Shot(dir, "m2c_decals_modify");
            a.DemoBrowse("size:0");
            if (a.CI.Pack.Paints.ElementAtOrDefault(6) is { } red) a.DemoBrowse("colour:" + red.Color);
            await Frames(10);
            Shot(dir, "m2d_decals_bigger_red");
            a.DemoBrowse("cancel");
            // Our own look's browser doesn't take these presses: close it as a dialog.
            if (a.DemoDialogOpen) a.DemoCloseDialog();
            await Frames(10);
            a.Go(Screen.Junkyard);
            await Frames(240);
            Shot(dir, "m3_junkyard");
            a.Go(Screen.Lot);
            await Frames(60);
            Shot(dir, "m4_carlot");
            GD.Print("lot geometry: " + a.DemoLotGeometry);
            a.Go(Screen.Workshop);
            // A random job (the car packs' jobs set aside) whose parts come straight off, done start to finish.
            Game? jobGame = null;
            for (uint seed = 1; seed < 300 && jobGame is null; seed++)
            {
                var tryG = MekImport.Import(og, a.CI, MekFile.Read(mek), seed).Game;
                tryG.State.CompletedJobs.AddRange(a.CI.Pack.Jobs.Where(j => !j.Phrased).Select(j => j.Id));
                tryG.State.FreePlay = true;
                if (tryG.RequestJob().Data is not { } offer || !a.CI.Pack.Jobs.Exists(j => j.Id == offer.TemplateId && j.Phrased)) continue;
                var v = tryG.Vehicle(offer.VehicleId);
                bool Straight(string id) => v.Slots[id].Part is { Condition: > Core.Content.Condition.Black } p
                    ? a.CI.Children(v.ModelId, id).All(c => v.Slots[c.Id].Part is null)
                      && a.CI.Slot(v.ModelId, id).BlockedBy.All(b => !v.Slots.TryGetValue(b, out var s) || s.Part is null)
                      && (a.CI.Part(p.PartId).RemoveAfter ?? []).All(b => !v.Slots.TryGetValue(b, out var s) || s.Part is null)
                    : v.Slots[id].Part is null && VehicleRules.InstallCheck(a.CI, v, id, a.CI.Slot(v.ModelId, id).DefaultPart!).Ok;
                if (offer.Reqs.SelectMany(r => r.SlotIds).All(Straight)) jobGame = tryG;
            }
            if (jobGame is not null)
            {
                a.StartDemo(jobGame);
                await Frames(30);
                _ = a.DemoJob();
                await Frames(30);
                Shot(dir, "m5_job_request");
                a.DemoConfirmDialog();
                await Frames(60);
                Shot(dir, "m6_job_started");
                a.DemoFixJob();
                await Frames(200);
                Shot(dir, "m7_job_complete");
            }
            await Checks(a, og, mek, dir);
            GetTree().Quit();
            return;
        }
        var g = Game.Create(a.CI, "Tester", 7);
        g.State.FreePlay = true;
        var carIds = Args.Get("car") is { } c ? [c] : a.CI.Pack.Cars.Take(2).Select(x => x.Id).ToArray();
        foreach (var carId in carIds)
        {
            var v = g.NewVehicle(a.CI.Car(carId), Core.Sim.Owner.Player, [0.05, 0.2, 0.25, 0.5]);
            v.Number = g.State.Stats.CarsBought++;
            g.State.Lot.Add(v.Id);
        }
        g.BringToWorkshop(g.State.Lot[0]);
        a.StartDemo(g);
        await Frames(60);
        Shot(dir, "02_workshop_complete");
        a.SetRegion(View.Body);
        await Frames(60);
        Shot(dir, "03_workshop_body");
        a.DemoPaint();
        await Frames(20);
        Shot(dir, "03b_body_paint");
        // No decals yet: the Decal Browser says so.
        a.DemoDecalBrowser();
        await Frames(10);
        Shot(dir, "03c_decals_none");
        a.DemoCloseDialog();
        a.SetRegion(View.Engine);
        await Frames(60);
        Shot(dir, "04_workshop_engine");
        a.SetXray(true);
        await Frames(10);
        Shot(dir, "05_engine_xray");
        a.SetXray(false);
        a.SetRegion(View.RunningGear);
        await Frames(60);
        a.DemoBoltMode();
        await Frames(90);
        Shot(dir, "06_running_gear_bolts");
        a.Go(Screen.Catalog);
        await Frames(240);
        Shot(dir, "07_catalog");
        a.Go(Screen.Junkyard);
        await Frames(240);
        Shot(dir, "08_junkyard");
        a.Go(Screen.Auction);
        await Frames(90);
        Shot(dir, "09_auction");
        a.Go(Screen.Lot);
        await Frames(60);
        Shot(dir, "10_carlot");
        a.Go(Screen.Workshop);
        g.PutCarInLot();
        a.Go(Screen.Lot);
        await Frames(90);
        Shot(dir, "10b_carlot_car");
        a.Go(Screen.Workshop);
        g.State.FreePlay = false;
        await Frames(10);
        _ = a.DemoJob();
        await Frames(30);
        Shot(dir, "11_job_request");
        a.DemoConfirmDialog();  // OK (our own jobs' box has a CANCEL too)
        await Frames(30);
        Shot(dir, "12_job_started");
        a.DemoFinishJob();
        // The view circles the finished car until Job Complete! (3 s after the last part).
        await Seconds(1.2);
        Shot(dir, "13_job_done_spin");
        await Seconds(2.5);
        Shot(dir, "14_job_complete");
        a.DemoCloseDialog();
        await Frames(20);
        Shot(dir, "15_congratulations");
        a.DemoCloseDialog();
        await Frames(20);
        Shot(dir, "16_the_future");
        a.DemoCloseDialog();
        await Frames(40);
        Shot(dir, "17_next_job");
        GetTree().Quit();
    }
}
