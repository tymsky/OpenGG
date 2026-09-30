using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using OpenGG.Ui;
using OpenGG.View3D;

namespace OpenGG;

// The WorkShop screen: view tabs, the tool column, the command column, the Parts Bin, and the 3D input.
public partial class App
{
    static readonly (View View, string Label)[] TabList =
        [(View.Complete, "COMPLETE"), (View.Engine, "ENGINE"), (View.Body, "BODY"), (View.RunningGear, "RUNNING GEAR")];

    const int BinPerPage = 8;

    ToolDef CurrentTool => CI.Tool(tool);

    /// <summary>The tabs the last car in the WorkShop had (null before any).</summary>
    List<View>? lastTabs;

    /// <summary>Jobs can hide tabs; the original shows an empty plate in their place.</summary>
    bool TabAllowed(View v) => v == View.Complete || Game.State.Job?.Tabs is not { } tabs || tabs.Contains(v);

    /// <summary>
    /// The WorkShop is blank: no tabs, no commands, not even Exit, nothing in the view. Measured: under Skill Advance and
    /// Available Cars, and in Jobs Mode between jobs (the next customer's Job Request comes over a blank WorkShop, after
    /// the sign-in too).
    /// </summary>
    bool WorkshopBlank => advancing || (Game.InJobsMode && Game.State.Job is null && finishing is null && Game.WorkshopVehicle() is null);

    void RenderWorkshop()
    {
        var v = Game.WorkshopVehicle();
        bool blank = WorkshopBlank;
        // Measured: with the WorkShop empty the plates stay as the last car had them (the last job's tabs), on
        // COMPLETE, and do nothing.
        if (v is not null) lastTabs = (Game.State.Job?.Tabs ?? [.. TabList.Select(x => x.View)]).ToList();
        for (int i = 0; i < 4; i++)
        {
            var (view, label) = TabList[i];
            var t = tabs[i];
            t.Label = label;
            t.SkinScreen = Skinned ? "workshop" : null;
            var tr = Skinned ? L.Orig.Tabs[i] : L.Tabs[i];
            t.Position = tr.Position;
            t.Size = tr.Size;
            // While a finished job's car is on show, the tabs stay (on COMPLETE).
            bool present = v is not null || finishing is not null;
            t.Blank = blank || (!present
                ? lastTabs is null || (view != View.Complete && !lastTabs.Contains(view))
                : finishing?.Tabs is { } ft ? view != View.Complete && !ft.Contains(view) : !TabAllowed(view));
            t.Active = !t.Blank && Region == view;
            t.Disabled = t.Blank || !present;
            t.MouseDefaultCursorShape = t.Disabled ? CursorShape.Arrow : CursorShape.PointingHand;
            t.QueueRedraw();
        }
        // Measured: the finished job's car keeps its (green) tag while it is on show; a part being unscrewed is still on.
        if (carHeld == 0 && (v ?? finishingCar) is { } tv && VehicleRules.AssembledCondition(CI, tv, Region.ToRegion(), attaching is null ? boltSlot : null) is { } cond)
        {
            assembledTag.Visible = true;
            assembledTag.Tint = U.CondColor(cond);
            assembledTag.Condition = cond;
            assembledTag.QueueRedraw();
        }
        if (v is null && finishing is null && !blank)
        {
            // Measured: a black box across the view, the words centred in the screen's white letters.
            var empty = Words.Get("workshop.empty");
            var box = Put(screenUi, new Panel(), Skinned ? L.R(21, 165, 348, 97) : L.R(20, 165, 350, 98));
            box.AddThemeStyleboxOverride("panel", Look.Flat(Colors.Black));
            if (Skinned && DialogLayer.SkinText("text", empty, L.R(0, 0, 348, 97)) is { } words) box.AddChild(words);
            else Text(box, L.R(8, 0, 334, 98), empty, 13, Look.White, align: HorizontalAlignment.Center, wrap: true);
        }
        partName.Text = boltSlot is not null && v is not null && CI.HasSlot(v.ModelId, boltSlot) ? CI.Slot(v.ModelId, boltSlot).Name : hoverLabel ?? note ?? "";
        // Measured: the finished job's car keeps the tools while it is on show.
        // Measured: under the boxes a car came in with, the tool column is empty too.
        RenderTools(carHeld > 0 ? null : v ?? finishingCar);
        RenderCommands();
        RenderBin();
        RenderBoltBox(v);
    }

    // ---- tool column -------------------------------------------------------------------------------

    void RenderTools(VehicleState? v)
    {
        U.Clear(toolLayer);
        if (v is null) return;
        // Measured: a job's car gets only the Impact Wrench and Start Engine (the Camera and Body Paint are for your own).
        bool job = Game.State.Job is not null || finishing is not null;
        var list = CI.Pack.Tools.Where(t => t.Regions.Contains(Region) && !(job && t.Action is ToolAction.Camera or ToolAction.Paint)).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var id = t.Id;
            bool engine = t.Action == ToolAction.StartEngine;
            float y = engine ? L.ToolY[2] : L.ToolY[Math.Min(i, 1)];
            var place = engine ? 2 : Math.Min(i, 1);
            var b = Put(toolLayer, new ToolIcon { Label = t.Name, Icon = Icon(t.Icon, 48), Selected = tool == t.Id && !engine, TooltipText = Tip($"{t.Name} [{t.Hotkey}]") },
                Skinned ? L.Orig.Tools[place] : L.R(L.ToolX - 55, y - 2, 110, 58));
            if (Skinned)
            {
                // The camera has pictures of its own; the others are cut from the WorkShop's.
                if (t.Action == ToolAction.Camera && UiSkin.Picture("camera.up") is { } cu) { b.SkinUp = cu; b.SkinDown = UiSkin.Picture("camera.down"); }
                else b.SkinScreen = "workshop";
            }
            // Measured: the tools make sounds of their own, not the buttons' click; a tool is picked as its button goes
            // down (held a second, the ring moved in the first frames).
            b.Quiet = true;
            b.ActionMode = BaseButton.ActionModeEnum.Press;
            if (engine) b.ButtonDown += () => EngineDown();
            else b.Pressed += () => PickTool(id);
        }
        if (CurrentTool.Action == ToolAction.Paint && Region == View.Body) RenderPaintTools();
    }

    void RenderPaintTools()
    {
        var box = L.Brushes;
        (Brush B, string Tip)[] list = [(Brush.Small, "Small brush"), (Brush.Medium, "Medium brush"), (Brush.Large, "Large brush"), (Brush.Panel, "Panel: paints a whole part")];
        string[] brushPics = ["brush.small", "brush.medium", "brush.large", "brush.part"];
        for (int i = 0; i < list.Length; i++)
        {
            var (b, tip) = list[i];
            var pic = Skinned ? UiSkin.Picture(brushPics[i]) : null;
            var btn = Put(toolLayer, new BrushButton { Brush = (int)b, Selected = brush == b && !placingDecal, TooltipText = Tip(tip), Picture = pic, Quiet = true },
                pic is not null ? L.R(L.Orig.Brushes.X + i * 25, L.Orig.Brushes.Y, 24, 24) : L.R(box.Position.X + i * 25, box.Position.Y, 22, 22));
            btn.Pressed += () =>
            {
                brush = b;
                placingDecal = false;
                Refresh();
            };
        }
        var pal = L.Palette;
        var paints = CI.Pack.Paints;
        float sw = pal.Size.X / 3, sh = pal.Size.Y / 9;
        for (int i = 0; i < paints.Count && i < 27; i++)
        {
            var p = paints[i];
            var id = p.Id;
            var cell = L.Orig.PaletteCell;
            var s = Put(toolLayer, new Swatch { Color = Color.FromHtml(p.Color), Selected = paintId == p.Id, TooltipText = Tip(p.Name), Classic = Skinned, Quiet = true },
                Skinned ? new Rect2(cell.Position + new Vector2(i % 3, i / 3) * L.Orig.PalettePitch, cell.Size)
                    : L.R(pal.Position.X + (i % 3) * sw, pal.Position.Y + (i / 3) * sh, sw, sh));
            s.Pressed += () =>
            {
                paintId = id;
                placingDecal = false;
                Refresh();
            };
        }
        DrawnButton decals = Skinned && UiSkin.Picture("decals.up") is { } du
            ? Put(toolLayer, new PictureButton { SkinUp = du, SkinDown = UiSkin.Picture("decals.down") }, L.Orig.Decals)
            : Put(toolLayer, new BlackButton { Label = "DECALS", FontSize = 11 }, L.DecalsButton);
        decals.Pressed += () => _ = DecalBrowser();
    }

    // ---- bolt mode ------------------------------------------------------------------------------------

    void RenderBoltBox(VehicleState? v)
    {
        if (v is null || boltSlot is null || !v.Slots.TryGetValue(boltSlot, out var st) || st.Part is null) return;
        // Measured: while the box is up only the view (its bolts, the arrow keys) and CANCEL do anything; the tabs, the
        // command plates left in sight, Start Engine and the Parts Bin take no click and make no sound.
        var view = new Rect2(viewArea.Position, viewArea.Size);
        Rect2[] around =
        [
            new(0, 0, 640, view.Position.Y),
            new(0, view.End.Y, 640, 480 - view.End.Y),
            new(0, view.Position.Y, view.Position.X, view.Size.Y),
            new(view.End.X, view.Position.Y, 640 - view.End.X, view.Size.Y),
        ];
        foreach (var r in around) Put(boltLayer, new Control { MouseFilter = MouseFilterEnum.Stop }, r);
        // Measured: one sentence while taking a part off, another once a part has been put on loose.
        string text = Words.Get(boltAttach ? "bolts.do" : "bolts.undo");
        if (UiSkin.Picture("dialog.frame") is { } pic && DialogLayer.SkinText("dialog", text, L.R(13, 30, 223, 96)) is { } words)
        {
            // The original's box, over the tools and the commands (measured at 390, 95).
            var box = Put(boltLayer, new DialogFrame { Title = CurrentTool.Name + " Tool", Picture = pic }, L.R(390, 95, 247, 171));
            box.AddChild(words);
            var cancelPic = UiSkin.Picture("dialog.cancel.up");
            // Measured: a lone button at 71, as in the other boxes.
            var skCancel = Put(box, new PictureButton { SkinUp = cancelPic, SkinDown = UiSkin.Picture("dialog.cancel.down") }, L.R(71, 132, 108, 30));
            skCancel.Pressed += CancelBolts;
            return;
        }
        var frame = Put(boltLayer, new DialogFrame { Title = CurrentTool.Name + " Tool" }, L.BoltBox);
        Text(frame, L.R(14, 40, L.BoltBox.Size.X - 28, 70), text, 13, Look.DialogText, align: HorizontalAlignment.Center, wrap: true);
        var cancel = Put(frame, new BlackButton { Label = "CANCEL" }, L.R((L.BoltBox.Size.X - 106) / 2, 132, 106, 28));
        cancel.Pressed += CancelBolts;
    }

    /// <summary>
    /// The Impact Wrench box's CANCEL (and our Esc). Measured: while a part is being taken off, the bolts taken out go back
    /// in (the box comes up again on the part with all its bolts); while one is being put on, the part goes back to the
    /// Parts Bin (see <see cref="SetBoltSlot"/>).
    /// </summary>
    void CancelBolts()
    {
        if (boltsGoingBack) return;
        if (boltSlot is { } id && !boltAttach && Game.WorkshopVehicle() is { } v && v.Slots.TryGetValue(id, out var st) && st.Fasteners.Count(x => !x) is > 0 and var n)
            _ = PutBoltsBack(id, n);
        else SetBoltSlot(null);
    }

    /// <summary>CANCEL is putting a part's bolts back: the box stays up and nothing answers meanwhile.</summary>
    bool boltsGoingBack;

    /// <summary>
    /// Measured: CANCEL puts the bolts taken out back one after another, from the button's release, a bolt's sound each and
    /// back to back (three bolts: at 0.09, 0.47 and 0.85 s after the press, the sound 0.375 s long); the box and the black
    /// holes stay as they were until the last has sounded, then the box goes (two bolts: 0.85 s after the release).
    /// </summary>
    async Task PutBoltsBack(string slotId, int count)
    {
        boltsGoingBack = true;
        var sound = UiSkin.HasSound("snd.bolt_out") ? "snd.bolt_in" : CI.Tool(tool).Sound;
        double each = Audio.Length(sound) is > 0 and var len ? len : 0.375;
        for (int i = 0; i < count; i++)
        {
            Audio.Play(sound, 0.7f);
            await ToSignal(GetTree().CreateTimer(each), SceneTreeTimer.SignalName.Timeout);
        }
        boltsGoingBack = false;
        if (boltSlot != slotId) return;
        quietBolts = true;
        Game.UseAllFasteners(slotId, tool, remove: false);
        quietBolts = false;
        SetBoltSlot(null);
    }

    /// <summary>The bolts going back after CANCEL have sounded already.</summary>
    bool quietBolts;

    /// <summary>Bolt mode is doing bolts up (the part was just put on) rather than undoing them.</summary>
    bool boltAttach;

    /// <summary>Keeps the BOLT labels on the bolts still to do as the view turns.</summary>
    void UpdateBoltLabels()
    {
        if (Screen != Screen.Workshop || boltSlot is null || workshop.View is not { } view || xray)
        {
            if (boltLabels.Points.Length > 0)
            {
                boltLabels.Points = [];
                boltLabels.QueueRedraw();
            }
            return;
        }
        var cam = workshop.Camera;
        var pts = view.Bolts(boltSlot)
            .Where(b => b.In != boltAttach && !cam.IsPositionBehind(b.Global))
            .Select(b => cam.UnprojectPosition(b.Global))
            .ToArray();
        boltLabels.Points = pts;
        boltLabels.QueueRedraw();
    }

    // ---- command column ------------------------------------------------------------------------------

    void RenderCommands()
    {
        var v = Game.WorkshopVehicle();
        var job = Game.State.Job ?? finishing;
        // The column has fixed places; where a command isn't available the original shows an empty slot.
        var slots = new (string Label, string Icon, Action Act, bool Arrow)?[8];
        if (WorkshopBlank || carHeld > 0)
        {
            // Measured: every place is empty, Exit's too.
            for (int i = 0; i < 8; i++)
                Put(cmdLayer, new BlankPlate { Skinned = Skinned }, Skinned ? L.R(L.Orig.CmdX, L.Orig.CmdY[i], L.Orig.CmdW, L.Orig.CmdH) : L.R(L.CmdX, L.CmdY[i], L.CmdW, L.CmdH));
            Put(cmdLayer, new BlankPlate { Skinned = Skinned }, Skinned ? L.Orig.Exit : L.Exit);
            return;
        }
        if (v is not null || finishing is not null) slots[0] = ("Show Condition", "", () => { }, false);
        if (job is not null)
        {
            // While the finished job's car is on show the plates stay (measured) but lead nowhere.
            bool live = finishing is null;
            slots[4] = ("Catalog", "icon.nav.catalog", () => { if (live) Go(Screen.Catalog); }, false);
            slots[6] = ("Go To JunkYard", "", () => { if (live) Go(Screen.Junkyard); }, true);
        }
        else if (Game.InJobsMode)
        {
            slots[2] = ("Get A Job", "icon.nav.job", () => _ = GetJob(), false);
        }
        else
        {
            if (v is not null)
            {
                slots[1] = ("Put Car In Lot", "icon.nav.lot", PutCarInLot, false);
                slots[3] = ("Auction Car", "icon.nav.auction", () => _ = AuctionOwnCar(), false);
                slots[4] = ("Catalog", "icon.nav.catalog", () => Go(Screen.Catalog), false);
                slots[6] = ("Go To JunkYard", "", () => Go(Screen.Junkyard), true);
            }
            slots[2] = ("Get A Job", "icon.nav.job", () => _ = GetJob(), false);
            slots[5] = ("Go To Car Lot", "", () => Go(Screen.Lot), true);
            slots[7] = ("Go To Auction", "", GoToAuction, true);
        }
        // Measured: the Job Help panel is gone once Job Complete! has come up (empty places there).
        bool help = job is not null && !jobCompleteUp;
        for (int i = 0; i < 8; i++)
        {
            if (help && i is 1 or 2 or 3) continue; // the Job Help panel sits there
            var r = Skinned ? L.R(L.Orig.CmdX, L.Orig.CmdY[i], L.Orig.CmdW, L.Orig.CmdH) : L.R(L.CmdX, L.CmdY[i], L.CmdW, L.CmdH);
            if (slots[i] is not { } s)
            {
                Put(cmdLayer, new BlankPlate { Skinned = Skinned }, r);
                continue;
            }
            var plate = Put(cmdLayer, new GoldPlate { Label = s.Label, Icon = s.Icon.Length > 0 ? Icon(s.Icon, 32) : null, Arrow = s.Arrow, Chips = i == 0, SkinScreen = Skinned ? "workshop" : null }, r);
            if (i == 0)
            {
                plate.Lit = xray;
                plate.Quiet = true; // measured: its own sounds, pressed and let go (see SetXray)
                plate.ButtonDown += () =>
                {
                    xrayMouse = true;
                    SetXray(true);
                };
                plate.ButtonUp += () => SetXray(false);
                plate.TooltipText = Tip("Hold to see the condition of every part (or hold C)");
            }
            else
            {
                var act = s.Act;
                plate.Pressed += act;
            }
        }
        // Measured: while Job Update is up its panel is gone, the column's empty places showing there.
        if (help && !jobUpdateUp)
        {
            var pic = finishing is { } f ? f.ThanksPortrait ?? f.Portrait : job!.Portrait;
            var panel = Put(cmdLayer, new JobHelpPanel { Portrait = Icon(pic, 96), SkinUp = Skinned ? UiSkin.Picture("jobhelp") : null }, Skinned ? L.Orig.JobHelp : L.JobHelp);
            panel.Pressed += () => _ = JobHelp();
        }
        var exit = Put(cmdLayer, new GoldPlate { Label = "Exit", Arrow = true, FontSize = 15, SkinScreen = Skinned ? "workshop" : null }, Skinned ? L.Orig.Exit : L.Exit);
        exit.Pressed += () => _ = Exit();
    }

    // ---- Parts Bin ----------------------------------------------------------------------------------------

    void RenderBin()
    {
        // Measured: the bin of the car's model; empty with no car, the finished job's while it is on show. A part
        // being bolted on still shows at its place until its last bolt is in.
        var all = Game.State.Bin.ToList();
        if (attaching is { } at && boltAttach && boltSlot == at.SlotId) all.Insert(Math.Clamp(at.Index, 0, all.Count), at.Item);
        var model = Game.WorkshopVehicle()?.ModelId ?? finishingModel;
        var binCar = model is not null && CI.HasCar(model) ? CI.Car(model) : null;
        var items = all.Where(b => Game.FitsCar(b.Part, model))
            .Where(b => Region == View.Complete || PartRegion(CI.Part(b.Part.PartId)).ToView() == Region)
            .Select(b => b.Part).ToList();
        if (all.Count > binCount) binPage = int.MaxValue; // show the newest part
        binCount = all.Count;
        int pages = Math.Max(1, (items.Count + BinPerPage - 1) / BinPerPage);
        binPage = Math.Clamp(binPage, 0, pages - 1);
        Put(binLayer, new BinTab { Label = "Parts Bin", Skinned = Skinned }, L.BinTab);
        var bin = Put(binLayer, new ColorRect { Color = Look.Bin, MouseFilter = MouseFilterEnum.Ignore, Visible = !Skinned }, L.Bin);
        var shown = items.Skip(binPage * BinPerPage).Take(BinPerPage).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var part = shown[i];
            var def = CI.Part(part.PartId);
            bool bolting = attaching is { } a && a.Item.Part.Uid == part.Uid;
            var slot = Put(binLayer, new PartSlot
            {
                // The part being bolted on is only shown there (it is on the car).
                Uid = bolting ? "" : part.Uid,
                Label = def.Name,
                Condition = part.Condition,
                Selected = pickedItem == part.Uid,
                TooltipText = Tip(def.Name),
                Classic = Skinned,
            }, Skinned ? L.Orig.BinSlot(i) : L.R(L.Bin.Position.X + 2 + (i % 4) * L.BinSlot.X, L.Bin.Position.Y + 1 + (i / 4) * L.BinSlot.Y, L.BinSlot.X - 1, L.BinSlot.Y - 1));
            var uid = part.Uid;
            var partModel = def.Model;
            int cond = part.Condition;
            slot.Thumb = thumbs.GetBin(partModel, cond, Skinned, BinAngle(uid), t =>
            {
                if (IsInstanceValid(slot) && turningUid != uid)
                {
                    slot.Thumb = t;
                    slot.QueueRedraw();
                }
            }, binCar);
            slot.Grabbed = at => StartCarry(slot, at);
            if (Skinned && !bolting)
            {
                // Measured: the part under the pointer turns in its slot, and stays as it is when the pointer leaves.
                slot.MouseEntered += () => StartTurn(slot, uid, partModel, cond);
                slot.MouseExited += () => StopTurn(slot, uid);
                if (turningUid == uid) slot.Live = thumbs.Live;
            }
        }
        // The part turning under the pointer is gone from the bin (repaired parts keep their place).
        if (turningUid is not null && !shown.Any(p => p.Uid == turningUid)) StopTurn(null, turningUid);
        _ = bin;
        var sk = Skinned ? "workshop" : null;
        var up = Put(binLayer, new ArrowButton { Up = true, Disabled = binPage == 0, SkinScreen = sk }, Skinned ? L.Orig.PageUp : L.PageUp);
        up.Pressed += () => { binPage--; Refresh(); };
        var num = Text(binLayer, Skinned ? L.Orig.PageNum : L.PageNum, $"{binPage + 1}", 12, Look.White, align: HorizontalAlignment.Center);
        if (Skinned && UiSkin.Font("tiny") is { } tiny)
        {
            num.AddThemeFontOverride("font", tiny);
            num.AddThemeFontSizeOverride("font_size", UiSkin.FontSize(tiny));
        }
        var down = Put(binLayer, new ArrowButton { Up = false, Disabled = binPage >= pages - 1, SkinScreen = sk }, Skinned ? L.Orig.PageDown : L.PageDown);
        down.Pressed += () => { binPage++; Refresh(); };
        repairPlate = Put(binLayer, new DropPlate { Label = "REPAIR", Icon = Icon("icon.ui.repair", 64), Skinned = Skinned }, L.Repair);
        scrapPlate = Put(binLayer, new DropPlate { Label = "SCRAP", Icon = Icon("icon.ui.scrap", 64), Skinned = Skinned }, L.Scrap);
    }

    // ---- a bin's part turning under the pointer --------------------------------------------------------------

    /// <summary>Where each bin part's picture was left turned (degrees); the part under the pointer turns from there.</summary>
    readonly Dictionary<string, double> binAngles = [];
    string? turningUid;

    int BinAngle(string uid) => (int)Math.Round(binAngles.GetValueOrDefault(uid)) % 360;

    void StartTurn(PartSlot slot, string uid, string model, int condition)
    {
        if (carry is not null || dialogs.IsOpen || turningUid == uid) return;
        if (turningUid is not null) StopTurn(null, turningUid);
        turningUid = uid;
        var carModel = Game.WorkshopVehicle()?.ModelId ?? finishingModel;
        thumbs.Turn(model, condition, binAngles.GetValueOrDefault(uid), carModel is not null && CI.HasCar(carModel) ? CI.Car(carModel) : null);
        slot.Live = thumbs.Live;
    }

    void StopTurn(PartSlot? slot, string uid)
    {
        // A slot drawn anew leaves its old one under the pointer: that one going is no reason to stop.
        if (turningUid != uid || (slot is not null && IsInstanceValid(slot) && slot.IsQueuedForDeletion())) return;
        if (thumbs.TurnAngle is { } a) binAngles[uid] = a;
        turningUid = null;
        var still = thumbs.StopTurn();
        if (slot is not null && IsInstanceValid(slot))
        {
            slot.Live = null;
            if (still is not null) slot.Thumb = still;
            slot.QueueRedraw();
        }
    }

    // ---- a part carried from the Parts Bin -----------------------------------------------------------------

    /// <summary>A part picked up from the Parts Bin: its uid, where its slot was taken hold of, the slot's copy that goes
    /// along with the pointer.</summary>
    sealed record Carry(string Uid, Vector2 Grab, PartSlot Copy);

    Carry? carry;
    /// <summary>The part whose bin slot shows it picked up (outlined): while it is carried, and while the box it was let
    /// go on (Repair Part, Scrap Part) is up.</summary>
    string? pickedItem;
    /// <summary>A part put on the car whose bolts are being done up: the bin item as it was and its place there.</summary>
    (BinItem Item, int Index, string SlotId)? attaching;
    DropPlate? repairPlate, scrapPlate;

    /// <summary>
    /// A part picked up from the Parts Bin (the button went down on its slot). Measured: the slot itself goes along with
    /// the pointer, held where it was taken (its picture, name and condition triangle, outlined as a picked part), over
    /// everything; the slot in the bin stays, outlined; the pointer stays the hand, and nothing under it lights up. Let
    /// go anywhere over the 3D view, the part goes on the car; over REPAIR or SCRAP, their boxes come up; anywhere else
    /// nothing happens.
    /// </summary>
    void StartCarry(PartSlot slot, Vector2 at)
    {
        if (Screen != Screen.Workshop || carry is not null || dialogs.IsOpen || slot.Uid.Length == 0 || attaching is not null) return;
        // Measured: a carried part does not turn.
        if (turningUid == slot.Uid) StopTurn(slot, slot.Uid);
        var copy = new PartSlot
        {
            Label = slot.Label,
            Condition = slot.Condition,
            Thumb = slot.Thumb,
            Classic = slot.Classic,
            Selected = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Put(carryLayer, copy, new Rect2(slot.GlobalPosition, slot.Size));
        carry = new Carry(slot.Uid, at, copy);
        pickedItem = slot.Uid;
        slot.Selected = true;
        slot.QueueRedraw();
        ClearHover();
        // Our own look shows on the car where the part can go (the original shows nothing).
        if (ModernLook.On && workshop.View is { } view && Game.WorkshopVehicle() is { } v && Game.Item(slot.Uid) is { } item)
        {
            var slots = VehicleRules.FittingSlots(CI, v, item.Part.PartId, ignoreAccess: true)
                .Select(id => (id, VehicleRules.InstallCheck(CI, v, id, item.Part.PartId).Ok)).ToList();
            if (slots.Count > 0) view.ShowGhosts(item.Part.PartId, slots);
        }
    }

    /// <summary>While a part is carried its slot follows the pointer, and letting go drops it where the pointer is. The
    /// events are read here: the pointer stays with the slot it was picked up from until the button is let go.</summary>
    public override void _Input(InputEvent e)
    {
        if (carry is null) return;
        if (e is InputEventMouseMotion mm)
        {
            var p = ((InputEventMouseMotion)MakeInputLocal(mm)).Position;
            carry.Copy.Position = p - carry.Grab;
            if (repairPlate is not null && IsInstanceValid(repairPlate)) repairPlate.CarriedOver = L.Repair.HasPoint(p);
            if (scrapPlate is not null && IsInstanceValid(scrapPlate)) scrapPlate.CarriedOver = L.Scrap.HasPoint(p);
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb)
            DropCarry(((InputEventMouseButton)MakeInputLocal(mb)).Position);
    }

    /// <summary>The carried part let go at <paramref name="at"/> (null: put back, nothing done).</summary>
    void DropCarry(Vector2? at)
    {
        if (carry is not { } c) return;
        carry = null;
        c.Copy.QueueFree();
        if (repairPlate is not null && IsInstanceValid(repairPlate)) repairPlate.CarriedOver = false;
        if (scrapPlate is not null && IsInstanceValid(scrapPlate)) scrapPlate.CarriedOver = false;
        workshop.View?.ClearGhosts();
        pickedItem = null;
        if (at is { } p && Screen == Screen.Workshop && !dialogs.IsOpen)
        {
            // The Repair Part and Scrap Part boxes keep the part picked while they are up.
            if (L.Repair.HasPoint(p)) _ = Repair(c.Uid);
            else if (L.Scrap.HasPoint(p)) _ = Scrap(c.Uid);
            else if (new Rect2(viewArea.Position, viewArea.Size).HasPoint(p)) OnPartDropped(c.Uid, p - viewArea.Position);
        }
        Refresh();
    }

    // ---- state changes ------------------------------------------------------------------------------

    /// <summary>A tool picked in the column or by its key; measured: a sound for a new tool, none for the one in hand.</summary>
    void PickTool(string id)
    {
        if (id != tool) Audio.Play("snd.tool");
        SetTool(id);
    }

    void SetTool(string id)
    {
        tool = id;
        if (CurrentTool.Action != ToolAction.Paint)
        {
            placingDecal = false;
            workshop.View?.HideDecalPreview();
        }
        SetBoltSlot(null);
        Refresh();
    }

    /// <summary>A view tab clicked; measured: each tab has its sound, the tab already shown none.</summary>
    void ClickTab(View r)
    {
        if (r != Region && TabAllowed(r) && Game.WorkshopVehicle() is not null) Audio.Play(TabSound(r));
        SetRegion(r);
    }

    static string TabSound(View r) => r switch
    {
        View.Engine => "snd.tab.engine",
        View.Body => "snd.tab.body",
        View.RunningGear => "snd.tab.running_gear",
        _ => "snd.tab.complete",
    };

    public void SetRegion(View r)
    {
        if (!TabAllowed(r) || Game.WorkshopVehicle() is null) return;
        workshop.View?.LightRegion(null);
        Region = r;
        workshop.SetRegion(r);
        if (!CurrentTool.Regions.Contains(r)) tool = "ratchet";
        if (!CI.Pack.Tools.Any(t => t.Id == tool)) tool = CI.Pack.Tools[0].Id;
        placingDecal = false;
        SetBoltSlot(null);
        ClearHover();
        Refresh();
    }

    /// <summary>Show Condition is held with the mouse (rather than the C key).</summary>
    bool xrayMouse;

    /// <summary>Show Condition on or off; measured: a sound as it comes on and another as it goes off.</summary>
    public void SetXray(bool on)
    {
        if (xray == on) return;
        xray = on;
        Audio.Play(on ? "snd.condition_on" : "snd.condition_off");
        if (workshop.View is { } view)
            view.JobRegions = Game.State.Job?.Tabs is { } t ? t.Select(x => x.ToRegion()).OfType<Core.Content.Region>().ToHashSet() : null;
        workshop.View?.SetXray(on);
        workshop.FrameXray(on ? workshop.View?.JobRegions : null);
        Refresh();
    }

    void SetBoltSlot(string? slotId, bool attach = false)
    {
        // Measured: the Impact Wrench's box comes up with the dialogs' sound, and to take bolts out with a magnet's
        // too (a part put on has just made its own); it goes silently.
        bool entering = slotId is not null && slotId != boltSlot;
        if (entering)
        {
            if (!attach) Audio.Play("snd.bolt_mode");
            Audio.Play("snd.dialog");
        }
        bool leaving = boltSlot is not null && slotId is null;
        var was = boltSlot;
        // The box is the Impact Wrench's: a part put on with another tool in hand gets it.
        if (slotId is not null && CurrentTool.Action != ToolAction.Fasten && CI.Pack.Tools.FirstOrDefault(t => t.Action == ToolAction.Fasten) is { } wrench)
            tool = wrench.Id;
        boltSlot = slotId;
        boltAttach = slotId is not null && attach;
        if (workshop.View is not null)
        {
            workshop.View.BoltAttach = boltAttach;
            workshop.View.BoltSlot = slotId;
        }
        // Measured: bolt mode zooms onto the part, to a sound of its own when the view comes in closer (a wheel; the
        // engine's parts, already near, don't zoom); leaving it, the view goes back to the region, framed on the
        // parts that are on the car now (it zooms in once a big part is off).
        if (slotId is not null && workshop.FocusSlot(slotId) && entering && !attach) Audio.Play("snd.zoom");
        else if (leaving) Reframe();
        // Measured: leaving the box before the last bolt of a part being put on is in (CANCEL) takes the part off the
        // car and back to its place in the Parts Bin.
        if (attaching is { } at && at.SlotId == was && slotId != was)
        {
            attaching = null;
            if (Game.WorkshopVehicle() is { } v && v.Slots.TryGetValue(at.SlotId, out var st) && st.Part?.Uid == at.Item.Part.Uid && st.Fasteners.Any(x => !x))
                Run(Game.CancelAttach(at.SlotId, at.Index, at.Item.From));
        }
        Refresh();
    }

    /// <summary>The view framed again on the region's parts on the car, once the car is redrawn. Measured: the original
    /// does so whenever a part comes off or goes on (a filter off, the view in closer; back on, out again).</summary>
    void Reframe() => Callable.From(() => { if (boltSlot is null) workshop.Frame(); }).CallDeferred();

    /// <summary>
    /// A click on a bolt with the Impact Wrench's box up. Measured: the bolts go one way only, out while a part is being
    /// taken off and in while one is being put on; a click on a bolt already done is the wrench in the air, as beside
    /// the bolts, and changes nothing.
    /// </summary>
    void BoltClick(string slotId, int index)
    {
        if (!BoltWorks(slotId, index)) AirWrench();
        else if (Run(Game.UseFastener(slotId, index, tool))) AfterBolt(slotId);
    }

    bool BoltWorks(string slotId, int index) =>
        boltSlot == slotId && Game.WorkshopVehicle() is { } v && v.Slots.TryGetValue(slotId, out var st)
        && index < st.Fasteners.Count && st.Fasteners[index] != boltAttach;

    /// <summary>
    /// A bolt went in or out in bolt mode. Measured: with the last bolt out the part drops into the Parts Bin (its sound
    /// and the bolt's together); with the last bolt in the part is on; either way the box goes, silently, and the view
    /// goes back to the region.
    /// </summary>
    void AfterBolt(string slotId)
    {
        if (boltSlot != slotId || Game.WorkshopVehicle() is not { } v || !v.Slots.TryGetValue(slotId, out var st) || st.Part is null) return;
        if (boltAttach && st.Fasteners.All(x => x))
        {
            attaching = null;
            SetBoltSlot(null);
        }
        else if (!boltAttach && st.Fasteners.All(x => !x))
        {
            SetBoltSlot(null);
            Run(Game.RemovePart(slotId));
        }
    }

    // ---- 3D input ----------------------------------------------------------------------------------------

    /// <summary>How far a right-button drag turns the view, radians a pixel (fitted to the original's frames).</summary>
    const float DragTurn = 3.4f * Mathf.Pi / 180;

    Vector2 downPos, mousePos;
    bool leftDown, dragged, hoverQueued, spraying;
    AudioStreamPlayer? sprayLoop;

    /// <summary>Side of the small, medium and large brushes' square dab, in paint picture pixels.</summary>
    static int BrushSide(Brush b) => b switch { Brush.Small => 2, Brush.Medium => 5, _ => 10 };

    /// <summary>Freehand Body Paint: paint goes on for as long as the button is held.</summary>
    void SprayAt(Vector2 pos)
    {
        if (!leftDown || Screen != Screen.Workshop || CurrentTool.Action != ToolAction.Paint || placingDecal || brush == Brush.Panel)
        {
            spraying = false;
            return;
        }
        if (workshop.PaintPoint(pos) is not { } p) return;
        Game.Spray(p.Uv.X, p.Uv.Y, BrushSide(brush), paintId, (p.Cell.Position.X, p.Cell.Position.Y, p.Cell.End.X, p.Cell.End.Y), p.SlotId);
    }
    Hit? lastHit;

    void OnViewInput(InputEvent e)
    {
        var scene = current;
        switch (e)
        {
            case InputEventMouseButton mb:
                if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                {
                    float f = mb.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f;
                    if (scene == workshop) workshop.Zoom(f);
                    else scene?.Orbit.Zoom(f);
                }
                else if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        leftDown = true;
                        dragged = false;
                        downPos = mb.Position;
                        mousePos = mb.Position;
                        if (Screen == Screen.Workshop && CurrentTool.Action == ToolAction.Camera && Game.WorkshopVehicle() is not null) TakePhoto(viewInput.GlobalPosition + mb.Position);
                        if (Screen == Screen.Workshop && CurrentTool.Action == ToolAction.Paint && !placingDecal && Game.WorkshopVehicle() is { } pv)
                        {
                            // Measured: the spray sounds for as long as the button is held, whatever the brush.
                            AudioBank.StopLoop(sprayLoop);
                            sprayLoop = Audio.Loop("snd.spray");
                            spraying = brush != Brush.Panel && Game.Canvas(pv) is not null;
                        }
                    }
                    else
                    {
                        leftDown = false;
                        AudioBank.StopLoop(sprayLoop);
                        sprayLoop = null;
                        if (spraying)
                        {
                            spraying = false;
                            break;
                        }
                        if (!dragged && Screen == Screen.Workshop) Click(mb.Position, mb.ShiftPressed);
                        else if (!dragged && Screen == Screen.Lot) LotClick();
                        else if (!dragged && Screen == Screen.Junkyard) JunkClick(mb.Position);
                    }
                }
                break;
            case InputEventMouseMotion mm:
                mousePos = mm.Position;
                // Measured: dragging with the right button turns the WorkShop's view as if the car were held: to the
                // right, the eye goes round to the left (the car's nose follows the pointer); down, the eye rises
                // towards the roof. About 3.4 degrees a pixel of the 640 x 480 screen either way, to the keys' tilt limit.
                if ((mm.ButtonMask & MouseButtonMask.Right) != 0)
                {
                    if (Screen == Screen.Workshop && !workshop.ShowcaseOn) workshop.Turn(-mm.Relative.X * DragTurn, mm.Relative.Y * DragTurn);
                    ClearHover();
                }
                else hoverQueued = true;
                break;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || dialogs.IsOpen || Screen == Screen.SignIn) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (Screen == Screen.Lot && k.Keycode is Key.Left or Key.Right)
        {
            GetViewport().SetInputAsHandled(); // held keys glide the camera, see GlideLot
            return;
        }
        if (Screen == Screen.Junkyard && k.Keycode is Key.Left or Key.Right)
        {
            GetViewport().SetInputAsHandled(); // held keys glide the camera, see GlideJunk
            return;
        }
        if (Screen != Screen.Workshop) return;
        // With the Impact Wrench's box up only the arrow keys turn the view (and our Esc is its CANCEL).
        if (boltSlot is not null && k.Pressed && k.Keycode is not (Key.Escape or Key.Left or Key.Right or Key.Up or Key.Down or Key.Kp2 or Key.Kp4 or Key.Kp6 or Key.Kp8))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (k.Keycode == Key.C)
        {
            if (k.Pressed && !k.Echo) SetXray(true);
            else if (!k.Pressed) SetXray(false);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!k.Pressed) return;
        switch (k.Keycode)
        {
            case Key.Escape:
                if (placingDecal) placingDecal = false;
                else if (boltSlot is not null) CancelBolts();
                workshop.View?.HideDecalPreview();
                Refresh();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Kp2 or Key.Kp4 or Key.Kp6 or Key.Kp8:
                break; // held keys turn the view, see _Process
            default:
                var key = ((char)k.Unicode).ToString();
                var t = CI.Pack.Tools.FirstOrDefault(t => t.Hotkey == key && t.Regions.Contains(Region));
                if (t is null) return;
                if (t.Action == ToolAction.StartEngine)
                {
                    if (!k.Echo) EngineDown(k.Keycode);
                }
                else PickTool(t.Id);
                break;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (hoverQueued)
        {
            hoverQueued = false;
            if (Screen == Screen.Workshop) Hover(mousePos);
            else if (Screen == Screen.Junkyard) JunkHover(mousePos);
        }
    }

    void ClearHover()
    {
        if (hoverLabel is null) return;
        hoverLabel = null;
        partName.Text = "";
        if (Screen == Screen.Junkyard) Refresh();
    }

    // ---- decals, the original's way ------------------------------------------------------------------------

    /// <summary>The decal being aimed on a car with a paint picture (see <see cref="DecalFrame"/>).</summary>
    DecalAim? decalAim;
    string decalAimKey = "";

    /// <summary>A car with a paint picture takes decals into it, the original's way; our own cars keep them apart.</summary>
    bool StampsDecals => Game.WorkshopVehicle() is { } v && Game.Canvas(v) is not null;

    /// <summary>BIGGER, Normal, smaller: which of the Decal Browser's sizes is chosen.</summary>
    int DecalSizeIndex => decalSize > 0.46f ? 0 : decalSize < 0.44f ? 2 : 1;

    /// <summary>The decal as it is to be laid on the screen, made again when its choice changes.</summary>
    DecalFrame? CurrentDecalFrame()
    {
        if (decalAim is null || decalId is null || CI.Pack.Decals.Find(d => d.Id == decalId) is not { } def) return null;
        var tint = def.Tint && decalColor is { } dc ? Color.FromHtml(dc) : Colors.White;
        string key = $"{decalId}|{decalAngle}|{decalFlipX}|{decalFlipY}|{DecalSizeIndex}|{tint.ToHtml()}|{ModernLook.On}";
        if (key != decalAimKey)
        {
            decalAimKey = key;
            decalAim.SetFrame(DecalFrame.Build(Assets.Texture(def.Texture), decalAngle, decalFlipX, decalFlipY, DecalFrame.Scale(DecalSizeIndex), tint), dots: !ModernLook.On);
        }
        return decalAim.Frame;
    }

    /// <summary>With a decal to stick on a car with a paint picture, the picture follows the pointer over the view
    /// (measured: in place of the pointer, over the car or not).</summary>
    void UpdateDecalAim(Vector2 pos)
    {
        bool aiming = placingDecal && Screen == Screen.Workshop && !dialogs.IsOpen && carry is null && StampsDecals && CurrentDecalFrame() is not null;
        if (aiming) decalAim!.Place(pos);
        else decalAim?.Hide();
        SetCursor(ToolCursor());
    }

    /// <summary>
    /// A decal stuck on a car with a paint picture, the original's way (measured): each of its pixels as it lies on the
    /// screen paints the pixel of the paint picture on the body panel seen there, whichever panel it is (a Star across
    /// the hood's edge went onto the hood and the fender). Where the picture is finer than the screen the original leaves
    /// dots; OpenGG fills between them (see <see cref="DecalStamp"/>). Silent.
    /// </summary>
    void StampDecal(Vector2 pos)
    {
        if (decalId is null || CurrentDecalFrame() is not { } f) return;
        var origin = f.Origin(pos);
        var bounds = viewInput.Size;
        var points = new List<Vector2>();
        var cells = new List<int>();
        for (int j = 0; j < f.Height; j++)
            for (int i = 0; i < f.Width; i++)
            {
                if (f.Pixels[j * f.Width + i] is null) continue;
                var p = origin + new Vector2(i + 0.5f, j + 0.5f);
                if (p.X < 0 || p.Y < 0 || p.X >= bounds.X || p.Y >= bounds.Y) continue;
                points.Add(p);
                cells.Add(j * f.Width + i);
            }
        static byte B(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x * 255), 0, 255);
        var grid = new DecalSample?[f.Width * f.Height];
        var surfaces = new Dictionary<Rect2, int>();
        foreach (var (index, uv, cell, slot) in workshop.PaintTexels(points))
        {
            int at = cells[index];
            var c = f.Pixels[at]!.Value;
            if (!surfaces.TryGetValue(cell, out int surface)) surfaces[cell] = surface = surfaces.Count;
            grid[at] = new DecalSample(uv.X * PaintCanvas.Size, uv.Y * PaintCanvas.Size, new Rgb(B(c.R), B(c.G), B(c.B)), slot, surface);
        }
        var texels = DecalStamp.Texels(f.Width, f.Height, grid);
        if (!Run(Game.StampDecal(decalId, texels))) return;
        placingDecal = false;
        lastDecal = decalId;
        UpdateDecalAim(pos);
    }

    /// <summary>The part under the pointer: lit up in its condition colour, its name under the view.</summary>
    void Hover(Vector2 pos)
    {
        UpdateDecalAim(pos);
        if (leftDown || dialogs.IsOpen || workshop.View is null || carry is not null)
        {
            ClearHover();
            return;
        }
        var hit = workshop.Pick(pos);
        lastHit = hit;
        var v = Game.WorkshopVehicle();
        if (CompleteRegion(hit?.Pick) is { } lit)
        {
            // Measured: in COMPLETE the pointer lights the whole region of what is under it, and names nothing.
            workshop.View.Hover(null);
            workshop.View.LightRegion(lit);
            if (hoverLabel is not null)
            {
                hoverLabel = null;
                partName.Text = note ?? "";
            }
            return;
        }
        workshop.View.LightRegion(null);
        // COMPLETE's parts are only there to light their region (above): nothing else to show or name on them.
        if (Region == View.Complete && hit?.Pick is PartPick or FastenerPick) hit = null;
        // Measured: with Body Paint in hand (spraying, or aiming a decal) the original neither lights nor names the part
        // under the pointer; only the decal's preview follows it.
        bool painting = CurrentTool.Action == ToolAction.Paint;
        // Measured: with the Impact Wrench's box up only the bolt or hole under the pointer lights up (yellow); no part
        // does, the one being bolted included; nor a bolt already done (a hole while taking a part off, a bolt in while
        // putting one on), which is not picked at all.
        workshop.View.Hover(hit?.Pick is BodyPick || painting || boltSlot is not null && !(hit?.Pick is FastenerPick hf && hf.SlotId == boltSlot) ? null : hit?.Pick);
        if (placingDecal && !StampsDecals && hit is { } dh && decalId is not null && IsPaintable(dh.Pick) && CI.Pack.Decals.Find(d => d.Id == decalId) is { } ddef && Assets.Texture(ddef.Texture) is { } dtex)
            workshop.View.ShowDecalPreview(dtex, dh.Point, dh.Normal, decalAngle, decalSize, (float)ddef.Aspect, decalFlipX, decalFlipY, ddef.Tint && decalColor is { } dc ? Color.FromHtml(dc) : null);
        else workshop.View.HideDecalPreview();
        string? name = hit?.Pick switch
        {
            _ when painting => null,
            FastenerPick => null,
            PartPick pp when v is not null => CI.Slot(v.ModelId, pp.SlotId).Name,
            GhostPick g when v is not null => CI.Slot(v.ModelId, g.SlotId).Name,
            _ => null,
        };
        if (boltSlot is not null && v is not null) name = CI.Slot(v.ModelId, boltSlot).Name;
        if (hoverLabel != name)
        {
            hoverLabel = name;
            partName.Text = name ?? note ?? "";
        }
    }

    /// <summary>COMPLETE with the Impact Wrench in hand (no part carried from the bin): the region of what is under the
    /// pointer, which the original lights on hover and goes to on a click; null otherwise.</summary>
    Core.Content.Region? CompleteRegion(Pick? pick)
    {
        if (Region != View.Complete || CurrentTool.Action != ToolAction.Fasten || carry is not null || boltSlot is not null
            || Game.WorkshopVehicle() is not { } v)
            return null;
        return pick switch
        {
            PartPick pp => CI.Slot(v.ModelId, pp.SlotId).Region,
            FastenerPick fp => CI.Slot(v.ModelId, fp.SlotId).Region,
            BodyPick => Core.Content.Region.Body,
            _ => null,
        };
    }

    bool IsPaintable(Pick p) => p is BodyPick || (p is PartPick pp && Game.WorkshopVehicle() is { } v && CI.Slot(v.ModelId, pp.SlotId).Region == Core.Content.Region.Body);

    void Click(Vector2 pos, bool shift)
    {
        if (boltsGoingBack) return;
        var hit = workshop.Pick(pos);
        var v = Game.WorkshopVehicle();
        var t = CurrentTool;
        if (t.Action == ToolAction.Camera) return;
        if (hit is not { } h || v is null)
        {
            if (t.Action == ToolAction.Fasten && v is not null) AirWrench();
            return;
        }
        var p = h.Pick;
        if (t.Action == ToolAction.Paint)
        {
            if (placingDecal)
            {
                if (StampsDecals) StampDecal(pos);
                else if (decalId is not null && IsPaintable(p))
                {
                    var (lp, ln) = workshop.ToCarSpace(h);
                    var tinted = CI.Pack.Decals.Find(d => d.Id == decalId) is { Tint: true } ? decalColor : null;
                    // Measured: sticking it on is silent.
                    if (Run(Game.AddDecal(decalId, lp, ln, decalAngle, decalSize, decalFlipX, decalFlipY, tinted, (p as PartPick)?.SlotId)))
                    {
                        placingDecal = false;
                        lastDecal = decalId;
                    }
                }
            }
            else if (brush == Brush.Panel && Game.Canvas(v) is not null)
            {
                // The panel brush fills the panel's whole square of the paint picture.
                if (workshop.PaintPoint(pos) is { } pt) Run(Game.PaintArea(pt.Cell.Position.X, pt.Cell.Position.Y, pt.Cell.End.X, pt.Cell.End.Y, paintId, pt.SlotId));
            }
            else if (brush == Brush.Panel && p is PartPick pp) Run(Game.PaintPart(pp.SlotId, paintId));
            Hover(pos);
            return;
        }
        if (t.Action != ToolAction.Fasten) return;
        // Measured: in COMPLETE a click on the car takes you to its region's tab, with the tab's sound.
        if (CompleteRegion(p) is { } region)
        {
            workshop.View?.LightRegion(null);
            ClickTab(region.ToView());
            Hover(pos);
            return;
        }
        if (Region == View.Complete && p is PartPick or FastenerPick) return; // COMPLETE's parts are for looking at
        switch (p)
        {
            case FastenerPick f:
                BoltClick(f.SlotId, f.Index);
                break;
            case PartPick pp:
            {
                if (boltSlot is not null)
                {
                    AirWrench(); // bolt mode: only the part's bolts do anything
                    break;
                }
                var st = v.Slots[pp.SlotId];
                var slot = CI.Slot(v.ModelId, pp.SlotId);
                int n = st.Fasteners.Count, tight = st.Fasteners.Count(x => x);
                var reach = VehicleRules.Reach(CI, v, pp.SlotId);
                if (!reach.Ok)
                {
                    Run(Result.From<Unit>(reach));
                    break;
                }
                if (slot.Openable is not null && tight == n)
                {
                    Run(Game.ToggleOpen(pp.SlotId));
                    break;
                }
                if (tight > 0)
                {
                    // The original checks what has to come off first before going into bolt mode.
                    var r = VehicleRules.RemoveCheck(CI, v, pp.SlotId);
                    if (!r.Ok && r.Code != "fastened") Run(Result.From<Unit>(r));
                    // Ours: Shift takes the part off, bolts and all (a part never stays half undone).
                    else if (shift)
                    {
                        if (Run(Game.UseAllFasteners(pp.SlotId, t.Id, remove: true))) Run(Game.RemovePart(pp.SlotId));
                    }
                    else SetBoltSlot(pp.SlotId);
                    break;
                }
                Run(Game.RemovePart(pp.SlotId));
                break;
            }
            default:
                AirWrench();
                break;
        }
        Hover(pos);
    }

    /// <summary>Measured: an Impact Wrench click that does nothing (on nothing, beside the bolts in bolt mode) makes
    /// the wrench's sound in the air.</summary>
    void AirWrench() => Audio.Play("snd.wrench_air");

    /// <summary>A part from the Parts Bin let go over the 3D view. Measured: anywhere in the view (an empty corner too) it
    /// goes on the car, in its own place.</summary>
    void OnPartDropped(string uid, Vector2 pos)
    {
        if (Screen != Screen.Workshop) return;
        var v = Game.WorkshopVehicle();
        var item = Game.Item(uid);
        if (v is null || item is null) return;
        // Measured: a black part gets "Assembly Error!" wherever it would go, and stays in the bin.
        if (VehicleRules.PartCheck(item.Part) is { Ok: false } worn)
        {
            Run(Result.From<Unit>(worn), installing: true);
            return;
        }
        string? slotId = null;
        // Our own look's ghost parts: the one under the pointer, where a part fits more than one place.
        if (workshop.Pick(pos) is { Pick: GhostPick g }) slotId = g.SlotId;
        else
        {
            var ok = VehicleRules.FittingSlots(CI, v, item.Part.PartId);
            if (ok.Count >= 1) slotId = ok[0];
            else
            {
                // Measured: a part whose place is taken (the Sports Exhaust with the stock Exhaust on) gets "That part
                // replaces the Exhaust, which is already attached."
                var type = CI.Part(item.Part.PartId).SlotType;
                var at = VehicleRules.FittingSlots(CI, v, item.Part.PartId, ignoreAccess: true).FirstOrDefault()
                    ?? CI.Car(v.ModelId).Slots.FirstOrDefault(s => s.SlotType == type)?.Id;
                Run(at is not null ? Result.From<Unit>(VehicleRules.InstallCheck(CI, v, at, item.Part.PartId))
                    : Result.Fail($"The {CI.Part(item.Part.PartId).Name} doesn't go on this car."), installing: true);
                return;
            }
        }
        int index = Game.State.Bin.IndexOf(item);
        if (!Run(Game.InstallPart(slotId, uid), installing: true)) return;
        // Measured: a part with bolts goes on loose and the Impact Wrench comes up to attach them; the bin keeps showing
        // the part until they are all in (the box's CANCEL puts it back there). A job may have finished meanwhile.
        if (Game.WorkshopVehicle() is { } now && now.Slots[slotId].Part?.Uid == uid && now.Slots[slotId].Fasteners.Count > 0)
        {
            attaching = (item, index, slotId);
            SetBoltSlot(slotId, attach: true);
        }
    }

    // ---- camera -----------------------------------------------------------------------------------------

    /// <summary>The Camera saves the whole screen, per mechanic (Shot0.jpg, Shot1.jpg...), the pointer on it too (measured:
    /// the original's snapshots show its camera pointer where it was clicked).</summary>
    /// <param name="at">Where the click was, on the 640 x 480 screen (the pointer is drawn there).</param>
    void TakePhoto(Vector2 at)
    {
        workshop.View?.Hover(null);
        // The game's 640 x 480, without the bars of a wider screen.
        var img = ScreenMode.GameImage(GetViewport(), gameSize: true);
        if (UiSkin.CursorPicture(ToolCursor()) is { } pointer)
        {
            img.Convert(Image.Format.Rgba8);
            var pic = (Image)pointer.Picture.Duplicate();
            pic.Convert(Image.Format.Rgba8);
            img.BlendRect(pic, new Rect2I(Vector2I.Zero, pic.GetSize()), (Vector2I)at.Floor() - pointer.Hotspot);
            img.Convert(Image.Format.Rgb8);
        }
        var dir = Path.Combine(Paths.Snapshots, $"Shots_{Safe(Game.State.Mechanic)}");
        Directory.CreateDirectory(dir);
        int n = 0;
        while (File.Exists(Path.Combine(dir, $"Shot{n}.jpg"))) n++;
        img.SaveJpg(Path.Combine(dir, $"Shot{n}.jpg"), 1f); // the original's are at the highest quality
        // Measured: next to it a 100 x 75 picture of the same, Shot<n>.bmp (the original's is 8-bit; ours is 24-bit).
        var thumb = (Image)img.Duplicate();
        thumb.Resize(100, 75, Image.Interpolation.Bilinear);
        SaveBmp(thumb, Path.Combine(dir, $"Shot{n}.bmp"));
        Audio.Play("snd.camera");
    }

    /// <summary>An uncompressed 24-bit Windows bitmap (Godot writes no BMP).</summary>
    static void SaveBmp(Image img, string path)
    {
        img.Convert(Image.Format.Rgb8);
        int w = img.GetWidth(), h = img.GetHeight(), row = (w * 3 + 3) & ~3;
        var px = img.GetData();
        using var f = new BinaryWriter(File.Create(path));
        f.Write((byte)'B');
        f.Write((byte)'M');
        f.Write(54 + row * h); // file size
        f.Write(0);
        f.Write(54); // where the pixels start
        f.Write(40); // the info header
        f.Write(w);
        f.Write(h); // rows bottom up
        f.Write((short)1);
        f.Write((short)24);
        f.Write(0); // no compression
        f.Write(row * h);
        f.Write(2835); // 72 dpi
        f.Write(2835);
        f.Write(0);
        f.Write(0);
        var line = new byte[row];
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                line[x * 3] = px[i + 2];
                line[x * 3 + 1] = px[i + 1];
                line[x * 3 + 2] = px[i];
            }
            f.Write(line);
        }
    }

    static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));

    // ---- actions ---------------------------------------------------------------------------------------

    /// <summary>The job dialogs: portrait, Difficulty and Fee on the left, the customer's words on the right.</summary>
    Task<string> JobDialog(string title, Job job, string text, string portrait, string footer, DialogButton[] buttons)
    {
        if (UiSkin.Picture("jobframe") is { } frame)
        {
            // The original's job box, measured: the customer at (14, 34), the words from (102, 54), Difficulty
            // and Fee under the picture, the footer above OK, which sits at (66, 262), RESTART or CANCEL at (181, 262).
            var sk = new Control { MouseFilter = MouseFilterEnum.Ignore };
            Put(sk, new PortraitFrame { Picture = Icon(portrait, 96), Border = false }, L.R(14, 34, 75, 75));
            void Add(Label? l) { if (l is not null) sk.AddChild(l); }
            Add(DialogLayer.SkinText("text", text.Replace("*", ""), L.R(102, 54, 184, 150), HorizontalAlignment.Left, VerticalAlignment.Top));
            Add(DialogLayer.SkinText("text", job.Difficulty.ToString(), L.R(14, 142, 75, 16), wrap: false));
            Add(DialogLayer.SkinText("text", U.Money(job.Fee), L.R(14, 200, 75, 16), wrap: false));
            // Measured: the footer is centred in its box, so two lines sit 8 px higher than one ("Hit OK to begin the
            // job." at y 327 of the screen, "Hit OK to continue or CANCEL to / give up on this job." at 318 and 335).
            Add(DialogLayer.SkinText("text", footer, L.R(10, 226, 280, 34), valign: VerticalAlignment.Center));
            return dialogs.ShowPicture(title, frame, new Vector2(175, 90), sk, buttons, [new Vector2(66, 262), new Vector2(181, 262)]);
        }
        var rect = L.R(176, 92, 296, 294);
        var body = new Control { MouseFilter = MouseFilterEnum.Ignore };
        Put(body, new PortraitFrame { Picture = Icon(portrait, 96) }, L.R(14, 11, 75, 75));
        Text(body, L.R(10, 94, 83, 18), "Difficulty", 14, Look.Orange, align: HorizontalAlignment.Center, shadow: true);
        Put(body, new ValuePlate { Label = job.Difficulty.ToString(), Color = Look.White }, L.R(14, 114, 75, 24));
        Text(body, L.R(10, 154, 83, 18), "Fee", 14, Look.Orange, align: HorizontalAlignment.Center, shadow: true);
        Put(body, new ValuePlate { Label = U.Money(job.Fee), Color = Look.MoneyGreen }, L.R(14, 172, 75, 24));
        var words = Put(body, new TextPanel(), L.R(97, 23, 192, 174));
        Text(words, L.R(6, 4, 180, 166), text.Replace("*", ""), 13, Look.White, align: HorizontalAlignment.Left, shadow: true, wrap: true).VerticalAlignment = VerticalAlignment.Top;
        Text(body, L.R(8, 200, 280, 34), footer, 13, Look.White, align: HorizontalAlignment.Center, shadow: true, wrap: true);
        return dialogs.ShowCustom(title, rect, body, buttons, 258, wrench: true);
    }

    async Task GetJob()
    {
        var r = Game.RequestJob();
        if (!Run(r)) return;
        var job = r.Data!;
        // Measured: the Job Request comes up with the phone's sound instead of the dialogs'.
        dialogs.NextOpenSound = "snd.phone";
        // Jobs Mode's requests only have OK; Free Play's can be put off with CANCEL (the same job waits).
        DialogButton[] buttons = Game.InJobsMode ? [new("OK", "ok", true)] : [new("OK", "ok", true), new("CANCEL", "cancel")];
        var answer = await JobDialog("Job Request", job, job.Text, job.Portrait, Words.Get("job.begin"), buttons);
        if (answer != "ok")
        {
            Game.DeclineJob();
            return;
        }
        if (!Run(Game.AcceptJob())) return;
        Screen = Screen.Workshop;
        ShowScene(workshop);
        workshop.SetVehicle(job.VehicleId);
        Region = View.Complete;
        workshop.SetRegion(Region, animate: false);
        Refresh();
        if (job.Reqs.Count == 0) Game.FinishJobIfDone();
    }

    async Task JobDone(JobDoneEvent j)
    {
        // Measured: the till rings 2.7 s after the last part went on (instead of the dialogs' sound), and "Job Complete!"
        // shows 3.0 s after it (four films); the view stops circling the car as it comes up, and the Job Help panel goes.
        await ToSignal(GetTree().CreateTimer(2.7), SceneTreeTimer.SignalName.Timeout);
        if (finishing != j.Job) return; // left meanwhile (Exit)
        Audio.Play("snd.cash");
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        if (finishing != j.Job) return;
        workshop.StopShowcase();
        jobCompleteUp = true;
        Refresh();
        dialogs.NextOpenSound = "";
        await JobDialog("Job Complete!", j.Job, j.Job.Thanks, j.Job.ThanksPortrait ?? j.Job.Portrait, "", [new("OK", "ok", true)]);
        // After the very first job the original explains what comes next, over the WorkShop as the job left it.
        if (Game.JobChain.FirstOrDefault()?.Id == j.Job.TemplateId && Game.State.CompletedJobs.Count == 1)
        {
            await dialogs.Alert("Congratulations", Words.Get("tip.first"));
            await dialogs.Alert("The Future", Words.Get("tip.future"));
        }
        // Then the WorkShop is cleared: the car is gone, the money box shows your cash.
        EndFinishing();
        Refresh();
        // Jobs Mode: the next customer is already on the phone.
        if (Game.InJobsMode && Game.State.Job is null && Screen == Screen.Workshop) QueueDialog(GetJob);
    }

    /// <summary>A finished job's car is gone from the WorkShop.</summary>
    void EndFinishing()
    {
        finishing = null;
        finishingModel = null;
        finishingCar = null;
        jobCompleteUp = false;
        workshop.EndShowcase();
    }

    bool jobUpdateUp;

    async Task JobHelp()
    {
        if (Game.State.Job is not { } job) return;
        var hint = Game.NextHint();
        bool chain = Game.InJobsMode;
        // Measured: Job Help comes up with a sound of its own instead of the dialogs'.
        dialogs.NextOpenSound = "snd.job_help";
        jobUpdateUp = true;
        Refresh();
        string answer;
        try
        {
            answer = await JobDialog("Job Update", job, hint, job.Portrait, Words.Get(chain ? "job.update.restart" : "job.update.cancel"),
                [new("OK", "ok", true), new(chain ? "RESTART" : "CANCEL", "other")]);
        }
        finally
        {
            jobUpdateUp = false;
            Refresh();
        }
        if (answer != "other") return;
        SetBoltSlot(null);
        if (chain)
        {
            if (Run(Game.RestartJob())) await GetJob();
        }
        else Run(Game.QuitJob());
    }

    /// <summary>A part let go on REPAIR. Measured: its bin slot stays picked while the Repair Part box is up.</summary>
    async Task Repair(string uid)
    {
        if (Game.Item(uid) is not { } it) return;
        var def = CI.Part(it.Part.PartId);
        if (it.Part.Condition is Condition.Black or Condition.Green)
        {
            Run(Game.RepairItem(uid));
            return;
        }
        var cost = Economy.RepairCost(CI, it.Part);
        pickedItem = uid;
        Refresh();
        bool ok = await dialogs.Confirm("Repair Part", Words.Get("repair.ask", ("cost", U.Money(cost)), ("part", def.Name)));
        pickedItem = null;
        if (ok && Run(Game.RepairItem(uid)))
        {
            Audio.Play(Audio.Has("snd.repair") ? "snd.repair" : "snd.ratchet");
            // Measured: the repaired part's picture is shown from the side again, however it was left turned.
            binAngles.Remove(uid);
        }
        Refresh();
    }

    /// <summary>A part let go on SCRAP; its bin slot stays picked while the Scrap Part box is up.</summary>
    async Task Scrap(string uid)
    {
        if (Game.Item(uid) is not { } it) return;
        var def = CI.Part(it.Part.PartId);
        pickedItem = uid;
        Refresh();
        bool ok = await dialogs.Confirm("Scrap Part", Words.Get("scrap.ask", ("price", U.Money(Economy.ScrapValue(CI, it.Part))), ("part", def.Name)));
        pickedItem = null;
        if (ok && Run(Game.ScrapItem(uid))) Audio.Play("snd.scrap");
        Refresh();
    }

    /// <summary>Put Car In Lot; measured: the Car Lot screen comes up with the car parked.</summary>
    void PutCarInLot()
    {
        if (Run(Game.PutCarInLot())) Go(Screen.Lot);
    }

    /// <summary>
    /// Exit. Measured: during a job the original warns that leaving means doing the job over; after OK the job was gone
    /// (signed in again, the WorkShop was empty and Get A Job offered another job). The job goes as Job Help's
    /// RESTART or CANCEL would take it: in Jobs Mode it comes back from the start.
    /// </summary>
    async Task Exit()
    {
        if (Game.State.Job is not null)
        {
            // Measured: leaving closes the box without its sound.
            dialogs.NextQuietAnswer = "ok";
            if (!await dialogs.Confirm("Job Active", Words.Get("job.active"))) return;
            SetBoltSlot(null);
            Run(Game.LeaveJob());
        }
        EndFinishing();
        ExitToSignIn();
    }

    async Task AuctionOwnCar()
    {
        if (Game.WorkshopVehicle() is not { } v) return;
        if (!await dialogs.Confirm("Auction Car", Words.Get("auction.car"))) return;
        if (Run(Game.AuctionOwnCar(v.Id))) Go(Screen.Auction);
    }

    // ---- Start Engine ----------------------------------------------------------------------------------

    /// <summary>Start Engine held down: for how long (null: not held), by which key (none: the mouse button), what it
    /// found, the start's sounds playing and how long the longest runs, whether the engine caught.</summary>
    double? engineHeld;
    Key engineKey = Key.None;
    Diagnosis? engineTry;
    readonly List<AudioStreamPlayer> engineSounds = [];
    double engineRun;
    bool engineCaught;

    /// <summary>Held this long the engine catches (measured: let go at 0.45 s the start stopped, at 0.50 s it went on).</summary>
    const double CatchTime = 0.5;
    /// <summary>How long a caught engine shakes and turns without a sound to go by (the start's sound runs 5 s).</summary>
    const double RunTime = 5.0;
    /// <summary>How long an engine cranks without a sound to go by (the original's crank runs 1.57 s).</summary>
    const double CrankTime = 1.57;

    /// <summary>
    /// Start Engine pressed. Measured on the original: the button (or its key) has to be held. The start's sound begins
    /// at once; let go within half a second and it stops there, the engine unstarted; held longer, the engine catches:
    /// the whole sound plays out (let go or not) while the engine shakes and its crank turns, for as long as the sound
    /// runs. An engine that won't start makes its one sound either way: a click without a working starter; cranking,
    /// the engine shaking and its fan turning meanwhile, without a part it needs or with its block worn out. A car with
    /// sounds of its own (in the original's .car files: a starter's, an engine's,
    /// an exhaust's) plays all of those on the car together instead of the start's sound, and runs as long as the
    /// longest of them. An accessory's sound on the car plays along with them when the engine is to start (not with a
    /// click or a crank). Our own look also writes the outcome under the view; the original's writes nothing.
    /// </summary>
    void EngineDown(Key key = Key.None)
    {
        if (engineHeld is not null || Game.StartEngine() is not { Ok: true, Data: { } d } || Game.WorkshopVehicle() is not { } v) return;
        engineHeld = 0;
        engineKey = key;
        engineCaught = false;
        engineSounds.Clear();
        if (d.Outcome is not (EngineOutcome.Runs or EngineOutcome.Rough or EngineOutcome.Loud))
        {
            engineTry = null;
            Audio.Play(d.Sound);
            // Measured: cranking, the engine shakes and its fan turns for as long as the crank's sound; a click, nothing.
            if (d.Outcome == EngineOutcome.NoStart) workshop.RunEngine(Audio.Length(d.Sound) is > 0 and var crank ? crank : CrankTime, across: 1);
            if (!Skinned) ShowNote(d.Message);
            return;
        }
        engineTry = d;
        var own = OwnSounds(v);
        List<string?> sounds = own.Count > 0 ? [.. own] : [d.Sound];
        engineRun = sounds.Max(s => Audio.Length(s)) is > 0 and var len ? len : RunTime;
        // Measured: an accessory's own sound (a C Cab's horn) plays with the start's, from the press, and is cut with it.
        foreach (var s in sounds.Concat(AccessorySounds(v)))
            if (Audio.Play(s) is { } p) engineSounds.Add(p);
    }

    /// <summary>Start Engine let go: before the engine caught, the start stops where it is.</summary>
    void EngineUp()
    {
        if (engineHeld is null) return;
        if (engineTry is not null && !engineCaught)
            foreach (var p in engineSounds)
                if (IsInstanceValid(p)) p.Stop();
        engineHeld = null;
        engineTry = null;
        engineSounds.Clear();
    }

    /// <summary>Each frame while Start Engine is held (the button may be drawn anew meanwhile, so its state is read
    /// from the mouse or the key): past half a second the engine catches.</summary>
    void EngineHold(double dt)
    {
        if (engineHeld is not { } held) return;
        bool down = Screen == Screen.Workshop && (engineKey == Key.None ? Input.IsMouseButtonPressed(MouseButton.Left) : Input.IsKeyPressed(engineKey));
        if (!down)
        {
            EngineUp();
            return;
        }
        engineHeld = held + dt;
        if (engineCaught || engineTry is not { } d || engineHeld < CatchTime || Game.WorkshopVehicle() is not { } v) return;
        engineCaught = true;
        workshop.RunEngine(engineRun - CatchTime);
        if (!Skinned) ShowNote(d.Message);
    }

    /// <summary>The sounds of the parts on the car that start with it (the original's cars that have their own: a
    /// starter's, an engine's, an exhaust's; not an accessory's).</summary>
    List<string> OwnSounds(VehicleState v) => PartSounds(v, accessories: false);

    /// <summary>The sounds of the accessories on the car (a horn, a rocket booster), which play along with the start's.</summary>
    List<string> AccessorySounds(VehicleState v) => PartSounds(v, accessories: true);

    List<string> PartSounds(VehicleState v, bool accessories) =>
        v.Slots.Values.Where(s => s.Part is not null).Select(s => CI.Part(s.Part!.PartId))
            .Where(p => p.Sound is not null && (p.SoundKind == PartSound.Accessory) == accessories).Select(p => p.Sound!).Distinct().ToList();

    /// <summary>A line under the view for a few seconds (where the part names show).</summary>
    void ShowNote(string text)
    {
        note = text;
        partName.Text = text;
        var id = ++noteId;
        GetTree().CreateTimer(4).Timeout += () =>
        {
            if (noteId != id) return;
            note = null;
            partName.Text = hoverLabel ?? "";
        };
    }

    string? note;
    int noteId;
}

/// <summary>A Parts Bin slot: the part's picture, its name under it and the condition triangle.</summary>
public partial class PartSlot : BinSlot
{
    public string Label { get; set; } = "";
    public int Condition { get; set; }
    public bool Selected { get; set; }
    /// <summary>With a skin: the name in the original's small white letters.</summary>
    public bool Classic { get; set; }
    /// <summary>The picture of the part turning under the pointer, drawn anew every frame (null: the still one).</summary>
    public Texture2D? Live { get; set; }
    bool over;

    public override void _Process(double delta)
    {
        if (Live is not null) QueueRedraw();
    }

    public PartSlot()
    {
        Flat = true;
        FocusMode = FocusModeEnum.None;
        MouseEntered += () => { over = true; QueueRedraw(); };
        MouseExited += () => { over = false; QueueRedraw(); };
        AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>The original's condition triangle (measured: red 173, 8, 8; yellow 173, 174, 8; green 8, 113, 8; black
    /// 57, 56, 57, a dark grey) and the outline of a part under the pointer (red 189, 12, 8; yellow 189, 190, 8; green 8,
    /// 125, 8; black 57, 60, 57).</summary>
    static readonly Color[] ClassicTriangle = [Color.Color8(57, 56, 57), Color.Color8(173, 8, 8), Color.Color8(173, 174, 8), Color.Color8(8, 113, 8)];
    static readonly Color[] ClassicOutline = [Color.Color8(57, 60, 57), Color.Color8(189, 12, 8), Color.Color8(189, 190, 8), Color.Color8(8, 125, 8)];
    /// <summary>The original's triangle row by row from the slot's corner (measured: 13 wide, 10 rows, inside the outline).</summary>
    static readonly int[] TriangleRows = [13, 11, 10, 9, 7, 6, 5, 3, 2, 1];

    public override void _Draw()
    {
        int cond = Math.Clamp(Condition, 0, 3);
        if (Classic)
        {
            // Measured: the slot's own black ground (a slot carried over the car hides what is behind it), the picture,
            // the name's glyph tops 40 below the slot's top, the triangle inside the one-pixel outline.
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
            // Measured: the part's box diagonal 50 pixels, in the slot's middle; its name over it (a long part, the
            // Belts' or the Waterpump's, reaches under the letters).
            DrawThumb(new Rect2((Size.X - 50) / 2, 0, 50, 50), Live ?? Thumb);
            if (UiSkin.Font("tiny") is { } tiny)
            {
                // A name too long for the place goes on two lines, as the Catalog's do (ours: not seen in a bin).
                int ts = UiSkin.FontSize(tiny);
                var rows = CatalogItem.Wrap(tiny, ts, Label, Size.X);
                for (int r = 0; r < rows.Count; r++)
                    DrawString(tiny, new Vector2(0, 40 - (rows.Count - 1 - r) * 9 + tiny.GetAscent(ts)), rows[r], HorizontalAlignment.Center, Size.X, ts);
            }
            if (Selected || over) DrawRect(new Rect2(Vector2.Zero, Size).Grow(-0.5f), ClassicOutline[cond], filled: false, width: 1);
            for (int r = 0; r < TriangleRows.Length; r++) DrawRect(new Rect2(1, 1 + r, TriangleRows[r], 1), ClassicTriangle[cond]);
            return;
        }
        if (Selected || over) DrawRect(new Rect2(Vector2.Zero, Size), Selected ? Color.FromHtml("#d6aa4a") : Color.FromHtml("#6b696b"), filled: false, width: 1);
        DrawThumb(new Rect2(14, 2, Size.X - 28, Size.Y - 16), Thumb);
        DrawString(Look.CondensedBold, new Vector2(0, Size.Y - 3), Label, HorizontalAlignment.Center, Size.X, 10, Look.White);
        DrawColoredPolygon([new Vector2(0, 0), new Vector2(12, 0), new Vector2(0, 12)], U.CondColor(Condition));
    }

    void DrawThumb(Rect2 box, Texture2D? pic)
    {
        if (pic is null) return;
        var ts = pic.GetSize();
        float s = Mathf.Min(box.Size.X / ts.X, box.Size.Y / ts.Y);
        var sz = ts * s;
        DrawTextureRect(pic, new Rect2(box.Position + (box.Size - sz) / 2, sz), false);
    }
}

/// <summary>A brush button of Body Paint: dot, small square, big square, door.</summary>
public partial class BrushButton : DrawnButton
{
    public int Brush { get; set; }
    public bool Selected { get; set; }
    /// <summary>With a skin: the original's picture of the brush (a yellow frame when chosen).</summary>
    public Texture2D? Picture { get; set; }

    public override void _Draw()
    {
        if (Picture is not null)
        {
            DrawTexture(Picture, Vector2.Zero);
            if (Selected) DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(1, 1, 0), filled: false, width: 2);
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Colors.Black);
        DrawRect(r.Grow(-1), Selected ? Color.FromHtml("#e0c040") : Color.FromHtml("#8a8a8a"), filled: false, width: Selected ? 2 : 1);
        var c = Size / 2;
        var w = Colors.White;
        switch (Brush)
        {
            case 0: DrawRect(new Rect2(c - new Vector2(1, 1), new Vector2(2, 2)), w); break;
            case 1: DrawRect(new Rect2(c - new Vector2(3, 3), new Vector2(6, 6)), w); break;
            case 2: DrawRect(new Rect2(c - new Vector2(6, 6), new Vector2(12, 12)), w); break;
            default:
                DrawRect(new Rect2(c - new Vector2(7, 5), new Vector2(14, 11)), w);
                DrawRect(new Rect2(c - new Vector2(5, 3), new Vector2(7, 4)), Colors.Black);
                break;
        }
    }
}

/// <summary>A colour of the Body Paint palette.</summary>
public partial class Swatch : DrawnButton
{
    public Color Color { get; set; }
    public bool Selected { get; set; }
    /// <summary>The original's look: the chosen colour is a thin bar on white.</summary>
    public bool Classic { get; set; }

    public override void _Draw()
    {
        if (Classic)
        {
            var box = new Rect2(Vector2.Zero, Size);
            if (!Selected) DrawRect(box, Color);
            else
            {
                DrawRect(box, Colors.White);
                DrawRect(box.Grow(-2), Color);
            }
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r.Grow(-0.5f), Color);
        if (Selected) DrawRect(r.Grow(-1), Colors.White, filled: false, width: 2);
    }
}

/// <summary>The Job Help button: the customer's picture under a "Job Help" caption.</summary>
public partial class JobHelpPanel : DrawnButton
{
    public Texture2D? Portrait { get; set; }

    public override void _Draw()
    {
        if (SkinUp is not null)
        {
            DrawTexture(SkinUp, Vector2.Zero);
            if (Portrait is not null) DrawTextureRect(Portrait, L.Orig.JobHelpPortrait, false);
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GoldTexture, new Rect2(0, 0, Size.X, 24), tile: true, modulate: Down ? new Color(0.8f, 0.8f, 0.8f) : Colors.White);
        DrawRect(new Rect2(0, 0, Size.X, 24), Colors.Black, filled: false, width: 1);
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(24, 17), "Job Help", HorizontalAlignment.Left, -1, 14, Look.GoldText);
        DrawLine(new Vector2(6, 16), new Vector2(15, 7), Look.GoldText, 3);
        var pr = new Rect2(4, 27, Size.X - 8, Size.Y - 31);
        DrawRect(pr, Colors.Black);
        if (Portrait is not null) DrawTextureRect(Portrait, pr.Grow(-2), false);
        DrawRect(r, Colors.Black, filled: false, width: 1);
    }
}

/// <summary>The customer's picture in the job dialogs.</summary>
public partial class PortraitFrame : Control
{
    public Texture2D? Picture { get; set; }
    /// <summary>Our black edge (the original's box has its own).</summary>
    public bool Border { get; set; } = true;

    public PortraitFrame() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        if (!Border)
        {
            if (Picture is not null) DrawTextureRect(Picture, r, false);
            return;
        }
        DrawRect(r, Colors.Black);
        if (Picture is not null) DrawTextureRect(Picture, r.Grow(-1), false);
    }
}

/// <summary>A grey plate with a value in it (Difficulty, Fee).</summary>
public partial class ValuePlate : Control
{
    public string Label { get; set; } = "";
    public Color Color { get; set; } = Colors.White;

    public ValuePlate() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GreyTexture, r, tile: true, modulate: new Color(1.3f, 1.3f, 1.3f));
        Bevel.Draw(this, r, new Color(1, 1, 1, 0.35f), new Color(0, 0, 0, 0.7f));
        var f = Look.CondensedBold;
        const int size = 13;
        var pos = new Vector2(0, (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size));
        DrawString(f, pos + Vector2.One, Label, HorizontalAlignment.Center, Size.X, size, new Color(0, 0, 0, 0.9f));
        DrawString(f, pos, Label, HorizontalAlignment.Center, Size.X, size, Color);
    }
}

/// <summary>The grey panel the customer's words are written on.</summary>
public partial class TextPanel : Control
{
    public TextPanel() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GreyTexture, r, tile: true, modulate: new Color(1.45f, 1.45f, 1.45f));
        Bevel.Draw(this, r, new Color(0, 0, 0, 0.6f), new Color(1, 1, 1, 0.25f));
    }
}
