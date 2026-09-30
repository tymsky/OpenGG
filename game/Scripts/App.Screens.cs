using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using OpenGG.Ui;
using OpenGG.View3D;

namespace OpenGG;

// Catalog, JunkYard, Auction, Car Lot, Sign In, Credits and the Decal Browser.
public partial class App
{
    /// <summary>Region a part belongs to on the WorkShop car (or on any car).</summary>
    Core.Content.Region PartRegion(PartDef p)
    {
        // The original's parts carry their own region: one that shares a place with a part of another region (a '41
        // Rod's Blower Carb 1 and its Scoop, one or the other) stays in its own section, as the original's Catalog has it.
        foreach (var r in new[] { Core.Content.Region.Engine, Core.Content.Region.Body, Core.Content.Region.RunningGear })
            if (p.Category == r.Label()) return r;
        var v = Game.WorkshopVehicle();
        var cars = v is not null ? [CI.Car(v.ModelId)] : CI.Pack.Cars;
        foreach (var c in cars)
            if (c.Slots.Find(x => x.SlotType == p.SlotType) is { } s) return s.Region;
        return Core.Content.Region.Engine;
    }

    /// <summary>The screen's prompt and "Go Back To WorkShop", as on every shop screen.</summary>
    void ShopHeader(string prompt)
    {
        if (Skinned)
        {
            // The skin's screen has the prompt; the plate is cut from its pictures.
            var sback = Put(screenUi, new GoldPlate { Label = "Go Back To WorkShop", Arrow = true, SkinScreen = SkinScreenId }, L.Orig.GoBack);
            sback.Pressed += () => Go(Screen.Workshop);
            return;
        }
        Text(screenUi, L.Prompt, prompt, 14, Color.FromHtml("#cfcfcf"), shadow: true);
        var back = Put(screenUi, new GoldPlate { Label = "Go Back To WorkShop", Arrow = true, FontSize = 14 }, L.GoBack);
        back.Pressed += () => Go(Screen.Workshop);
    }

    // ---- Catalog (a ring binder) --------------------------------------------------------------------

    /// <summary>
    /// The parts in the Catalog's section shown. Measured: every part of the car in the section, in the order of the
    /// car's file, the custom ones too (a T-Bird's chop top and fender skirts on a job that needed none of them); but in
    /// Jobs Mode not the custom ones (the tutorial's pickup and Mustang showed none: no tinted glass, no Performance Hood).
    /// </summary>
    List<PartDef> CatalogParts(CarModelDef? car)
    {
        var region = catalogTab switch { "body" => Core.Content.Region.Body, "running_gear" => Core.Content.Region.RunningGear, _ => Core.Content.Region.Engine };
        return CI.Pack.Parts
            .Where(p => car is null || car.Slots.Any(s => s.SlotType == p.SlotType))
            .Where(p => PartRegion(p) == region && !(p.Custom && Game.InJobsMode))
            .ToList();
    }

    /// <summary>The Catalog's pictures for the car in the WorkShop, made ahead while nothing else is being drawn (the
    /// section shown last first), so that its spreads come up with every picture at once, as the original's do.</summary>
    void PrefetchCatalog()
    {
        if (Game.WorkshopVehicle() is not { } v || !CI.HasCar(v.ModelId)) return;
        var car = CI.Car(v.ModelId);
        var first = catalogTab switch { "body" => Core.Content.Region.Body, "running_gear" => Core.Content.Region.RunningGear, _ => Core.Content.Region.Engine };
        foreach (var p in CI.Pack.Parts
                     .Where(p => car.Slots.Any(s => s.SlotType == p.SlotType) && !(p.Custom && Game.InJobsMode))
                     .OrderBy(p => PartRegion(p) == first ? 0 : 1))
            thumbs.Prefetch(p.Model, Skinned, car, CatalogAngle(p.Id));
    }

    /// <summary>Where each Catalog card's part was left turned (degrees), by part.</summary>
    readonly Dictionary<string, double> catalogAngles = [];
    string? turningCard;

    int CatalogAngle(string partId) => (int)Math.Round(catalogAngles.GetValueOrDefault(partId)) % 360;

    void StartCardTurn(CatalogItem card, PartDef p, CarModelDef? car)
    {
        if (dialogs.IsOpen || turningCard == p.Id) return;
        if (turningCard is not null) StopCardTurn(null, turningCard);
        if (turningUid is not null) StopTurn(null, turningUid);
        turningCard = p.Id;
        thumbs.Turn(p.Model, Condition.Green, catalogAngles.GetValueOrDefault(p.Id), car, Thumbnails.Pose.Catalog);
        card.Live = thumbs.Live;
        card.QueueRedraw();
    }

    void StopCardTurn(CatalogItem? card, string partId)
    {
        // A card drawn anew leaves its old one under the pointer: that one going is no reason to stop.
        if (turningCard != partId || (card is not null && IsInstanceValid(card) && card.IsQueuedForDeletion())) return;
        if (thumbs.TurnAngle is { } a) catalogAngles[partId] = a;
        turningCard = null;
        var still = thumbs.StopTurn();
        if (card is not null && IsInstanceValid(card))
        {
            card.Live = null;
            if (still is not null) card.Picture = still;
            card.QueueRedraw();
        }
    }

    void RenderCatalog()
    {
        ShopHeader(Words.Get("prompt.part"));
        var v = Game.WorkshopVehicle();
        var car = v is null ? null : CI.Car(v.ModelId);
        bool decals = catalogTab == "decals";
        if (!Skinned) Put(screenUi, new Binder { Dark = decals }, L.Book);
        (string Id, string Label, Color Color, Rect2 Rect)[] tabDefs =
        [
            ("decals", "DECALS", Color.FromHtml("#6b9a5a"), L.R(18, 122, 28, 106)),
            ("engine", "ENGINE", Color.FromHtml("#b86a6a"), L.R(588, 122, 26, 108)),
            ("body", "BODY", Color.FromHtml("#c8b870"), L.R(588, 240, 26, 72)),
            ("running_gear", "R GEAR", Color.FromHtml("#7a8ac0"), L.R(588, 322, 26, 100)),
        ];
        for (int ti = 0; ti < tabDefs.Length; ti++)
        {
            var (id, label, color, rect) = tabDefs[ti];
            var t = Put(screenUi, new BookTab { Label = label, Tint = color, Active = catalogTab == id, SkinScreen = Skinned ? SkinScreenId : null, Quiet = true }, Skinned ? L.Orig.BookTabs[ti] : rect);
            t.Pressed += () =>
            {
                // Measured: a section's tab sounds as the section opens (no button click).
                if (catalogTab != id) Audio.Play("snd.catalog_tab");
                catalogSpreads[catalogTab] = catalogSpread;
                catalogTab = id;
                catalogSpread = catalogSpreads.GetValueOrDefault(id);
                Refresh();
            };
        }
        int perPage = decals ? 16 : 10, perSpread = perPage * 2;
        int count;
        if (decals)
        {
            var list = CI.Pack.Decals;
            count = list.Count;
            for (int page = 0; page < 2; page++)
            {
                var area = page == 0 ? L.LeftPage : L.RightPage;
                var shown = list.Skip(catalogSpread * perSpread + page * perPage).Take(perPage).ToList();
                for (int i = 0; i < shown.Count; i++)
                {
                    var d = shown[i];
                    // Measured: four columns 56 apart, rows 80 apart, a 50-pixel picture over the name.
                    var cell = Skinned
                        ? L.R(L.Orig.DecalColumns[page * 4 + i % 4] - 3, L.Orig.DecalTop + (i / 4) * L.Orig.DecalPitch, 56, 70)
                        : L.R(area.Position.X + (i % 4) * (area.Size.X / 4), area.Position.Y + 2 + (i / 4) * 80, area.Size.X / 4, 78);
                    var b = Put(screenUi, new CatalogItem { Label = d.Name, Picture = Assets.Texture(d.Texture), Dark = true, TooltipText = Tip(d.Name), Classic = Skinned, Quiet = true }, cell);
                    b.Pressed += () => _ = BuyDecal(d);
                }
            }
        }
        else
        {
            var parts = CatalogParts(car);
            count = parts.Count;
            // Measured: the original shows a spread's part pictures all at once, a tenth of a second after the pages; ours
            // are made one after another, so those not made yet are held back until the last one is there.
            var late = new List<(CatalogItem Card, PartDef Part)>();
            int left = 0;
            bool waited = false;
            // The pictures made so far onto their cards; the others stay held back and come as they are made.
            void ShowLate() => late.RemoveAll(l =>
            {
                if (!IsInstanceValid(l.Card)) return true;
                if (turningCard == l.Part.Id || thumbs.Get(l.Part.Model, Skinned, car, null, CatalogAngle(l.Part.Id)) is not { } pic) return false;
                l.Card.Picture = pic;
                l.Card.QueueRedraw();
                return true;
            });
            for (int page = 0; page < 2; page++)
            {
                var area = page == 0 ? L.LeftPage : L.RightPage;
                var shown = parts.Skip(catalogSpread * perSpread + page * perPage).Take(perPage).ToList();
                Rect2 Card(int i) => Skinned
                    ? new Rect2(L.Orig.PartColumns[page * 2 + i % 2], L.Orig.PartTop + (i / 2) * L.Orig.PartPitch, L.Orig.PartCard.X, L.Orig.PartCard.Y)
                    : L.R(area.Position.X + 12 + (i % 2) * 117, area.Position.Y + 10 + (i / 2) * 63, 102, 48);
                // The original shows every card of the page, empty ones too.
                if (Skinned)
                    for (int i = shown.Count; i < perPage; i++)
                        Put(screenUi, new CatalogItem { Classic = true, Disabled = true, MouseFilter = MouseFilterEnum.Ignore }, Card(i));
                for (int i = 0; i < shown.Count; i++)
                {
                    var p = shown[i];
                    var cell = Card(i);
                    var b = Put(screenUi, new CatalogItem { Label = p.Name, TooltipText = Tip(p.Name), Classic = Skinned, Quiet = true }, cell);
                    b.Picture = thumbs.Get(p.Model, Skinned, car, _ =>
                    {
                        if (--left <= 0 || waited) ShowLate();
                    }, CatalogAngle(p.Id));
                    if (b.Picture is null)
                    {
                        late.Add((b, p));
                        left++;
                    }
                    b.Pressed += () => _ = BuyPart(p);
                    if (Skinned)
                    {
                        // Measured: a card's part turns while the pointer is over it (a pointer on its way across turns it
                        // too) and stays as it is when the pointer leaves, on the next visit as well; the bin's own pictures
                        // are not turned with it.
                        b.MouseEntered += () => StartCardTurn(b, p, car);
                        b.MouseExited += () => StopCardTurn(b, p.Id);
                        if (turningCard == p.Id) b.Live = thumbs.Live;
                    }
                }
            }
            // A picture that could not be made must not keep the others back: after a while each comes as it is made.
            if (late.Count > 0) GetTree().CreateTimer(3).Timeout += () =>
            {
                waited = true;
                ShowLate();
            };
        }
        int spreads = Math.Max(1, (count + perSpread - 1) / perSpread);
        catalogSpread = Math.Clamp(catalogSpread, 0, spreads - 1);
        catalogSpreads[catalogTab] = catalogSpread;
        if (catalogSpread > 0)
        {
            var prev = Put(screenUi, new PageTurn { Label = "TURN PAGE", Left = true, Dark = decals, SkinScreen = Skinned ? SkinScreenId : null, Quiet = true }, Skinned ? L.Orig.TurnLeft : L.R(44, 434, 100, 16));
            prev.Pressed += () => { catalogSpread--; Audio.Play("snd.page"); Refresh(); };
        }
        if (catalogSpread < spreads - 1)
        {
            var next = Put(screenUi, new PageTurn { Label = "TURN PAGE", Left = false, Dark = decals, SkinScreen = Skinned ? SkinScreenId : null, Quiet = true }, Skinned ? L.Orig.TurnRight : L.R(488, 434, 100, 16));
            next.Pressed += () => { catalogSpread++; Audio.Play("snd.page"); Refresh(); };
        }
    }

    async Task BuyPart(PartDef p)
    {
        if (!await dialogs.Confirm("Buy Part", Words.Get("buy.part", ("part", p.Name), ("price", U.Money(p.Price))))) return;
        if (Run(Game.BuyPart(p.Id))) Audio.Play("snd.cash");
    }

    async Task BuyDecal(DecalDef d)
    {
        if (!await dialogs.Confirm("Buy Decal", Words.Get("buy.decal", ("uses", d.Uses.ToString()), ("name", d.Name), ("price", U.Money(d.Price))))) return;
        if (Run(Game.BuyDecal(d.Id))) Audio.Play("snd.cash");
    }

    // ---- JunkYard ---------------------------------------------------------------------------------------

    string junkKey = "";

    /// <summary>The yard in 3D (see <see cref="JunkyardScene"/>), the signpost and the Purchase Bin.</summary>
    void RenderJunkyard()
    {
        ShopHeader(Words.Get("prompt.part"));
        var items = JunkItems();
        string key = $"{junkArea}|{string.Join(",", items.Select(j => j.Id))}";
        if (key != junkKey)
        {
            // Measured: a part bought leaves the others where they lie. The row is laid out afresh from the shelf as
            // the yard or another of its areas comes up (ours: not seen).
            bool sameArea = junkKey.StartsWith($"{junkArea}|", StringComparison.Ordinal);
            junkKey = key;
            bool newArea = junkScene.Area != junkArea;
            if (sameArea) junkScene.Restock(items);
            else junkScene.Show(items, junkArea);
            // Measured: each area's camera stays where it was left while you look at the others.
            if (newArea) junkScene.SetPan(junkPans.GetValueOrDefault(junkArea));
        }
        var sk = Skinned ? SkinScreenId : null;
        if (!Skinned) Put(screenUi, new ViewFrame(), L.JunkView.Grow(2));
        junkName = Skinned && DialogLayer.SkinText("text", "", L.Orig.JunkName, HorizontalAlignment.Left, VerticalAlignment.Top, wrap: false) is { } jn
            ? Put(screenUi, jn, L.Orig.JunkName)
            : Text(screenUi, L.JunkName, "", 13, Look.White, shadow: true, align: HorizontalAlignment.Center);
        RenderJunkName();
        // Measured: ◄ ► glide the camera along for as long as they are held (a click: a short way), to the path's ends,
        // and look the same there.
        var left = Put(screenUi, new ArrowButton { Horizontal = true, Up = true, SkinScreen = sk }, Skinned ? L.Orig.JunkLeft : L.JunkLeft);
        left.ButtonDown += () => junkHeld = -1;
        left.ButtonUp += () => junkHeld = 0;
        var right = Put(screenUi, new ArrowButton { Horizontal = true, Up = false, SkinScreen = sk }, Skinned ? L.Orig.JunkRight : L.JunkRight);
        right.ButtonDown += () => junkHeld = 1;
        right.ButtonUp += () => junkHeld = 0;
        // The signpost.
        var sign = Skinned ? screenUi : Put(screenUi, new SignPost(), L.JunkSign);
        (Core.Content.Region Area, string Label)[] areas = [(Core.Content.Region.Engine, "ENGINE"), (Core.Content.Region.Body, "BODY"), (Core.Content.Region.RunningGear, "RUNNING GEAR")];
        for (int i = 0; i < areas.Length; i++)
        {
            var (area, label) = areas[i];
            // Measured: the plank of the area shown does nothing, not even click.
            var plank = Put(sign, new SignPlank { Label = label, Active = junkArea == area, SkinScreen = sk, Quiet = junkArea == area }, Skinned ? L.Orig.Planks[i] : L.R(4, 34 + i * 38, 118, 30));
            plank.Pressed += () =>
            {
                if (junkArea == area) return;
                junkPans[junkArea] = junkScene.Pan;
                junkArea = area;
                Refresh();
            };
        }
        // The Purchase Bin: click a part to give it back.
        Put(screenUi, new BinTab { Label = "Purchase Bin", Skinned = Skinned }, L.PurchaseTab);
        if (!Skinned) Put(screenUi, new ColorRect { Color = Look.Bin, MouseFilter = MouseFilterEnum.Ignore }, L.PurchaseBin);
        var bought = Game.State.PurchaseBin;
        var binArea = Skinned ? L.Orig.PurchaseBin : L.PurchaseBin;
        int perRow = Skinned ? 6 : 4;
        // Measured: the strip has a pager like the Parts Bin's (six places a page with the original's look).
        int purchasePages = Math.Max(1, (bought.Count + perRow - 1) / perRow);
        purchasePage = Math.Clamp(purchasePage, 0, purchasePages - 1);
        var pageUp = Put(screenUi, new ArrowButton { Up = true, Disabled = purchasePage == 0, SkinScreen = sk }, Skinned ? L.Orig.PurchaseUp : L.PageUp);
        pageUp.Pressed += () => { purchasePage--; Refresh(); };
        var pageDown = Put(screenUi, new ArrowButton { Up = false, Disabled = purchasePage >= purchasePages - 1, SkinScreen = sk }, Skinned ? L.Orig.PurchaseDown : L.PageDown);
        pageDown.Pressed += () => { purchasePage++; Refresh(); };
        for (int i = 0; i < perRow && purchasePage * perRow + i < bought.Count; i++)
        {
            var j = bought[purchasePage * perRow + i];
            var def = CI.Part(j.Part.PartId);
            var slot = Put(screenUi, new PartSlot { Label = def.Name, Condition = j.Part.Condition, TooltipText = Tip(def.Name), Classic = Skinned },
                Skinned ? L.Orig.PurchaseSlot(i) : L.R(binArea.Position.X + 2 + i * L.BinSlot.X, binArea.Position.Y + 2, L.BinSlot.X - 1, L.BinSlot.Y - 1));
            slot.Thumb = thumbs.GetBin(def.Model, j.Part.Condition, Skinned, 0, t =>
            {
                if (IsInstanceValid(slot))
                {
                    slot.Thumb = t;
                    slot.QueueRedraw();
                }
            }, Game.State.JunkFor is { } jf && CI.HasCar(jf) ? CI.Car(jf) : null);
            var id = j.Id;
            slot.Pressed += () => Run(Game.ReturnJunk(id));
        }
    }

    Label? junkName;

    void RenderJunkName()
    {
        if (junkName is not null && IsInstanceValid(junkName)) junkName.Text = junkHint;
    }

    /// <summary>Where each area's camera was left in this visit to the JunkYard.</summary>
    readonly Dictionary<Core.Content.Region, float> junkPans = [];

    /// <summary>Time to the next footstep while the JunkYard's camera glides (null: it is not gliding).</summary>
    double? junkStep;

    const double StepEvery = 0.42, FirstStep = 0.21;

    List<JunkItem> JunkItems() => Game.Junk.Where(j => PartRegion(CI.Part(j.Part.PartId)) == junkArea).ToList();

    /// <summary>The JunkYard's camera glides while ◄ or ► (on the screen or the keyboard) is held down.</summary>
    void GlideJunk(double dt)
    {
        if (junkHeld != 0 && !Input.IsMouseButtonPressed(MouseButton.Left)) junkHeld = 0;
        int dir = Math.Clamp(junkHeld + (Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) ? 1 : 0), -1, 1);
        if (dir == 0 || !junkScene.Glide(dir, dt))
        {
            junkStep = null;
            return;
        }
        // Measured: the camera walks: a footstep every 0.42 s while it glides, the first 0.21 s in.
        junkStep = (junkStep ?? FirstStep) - dt;
        if (junkStep <= 0)
        {
            Audio.Play("snd.footstep");
            junkStep += StepEvery;
        }
        // The part under a pointer resting on the view changes as the yard slides by.
        var p = viewInput.GetLocalMousePosition();
        if (new Rect2(Vector2.Zero, viewInput.Size).HasPoint(p)) JunkHover(p);
    }

    /// <summary>Measured: a click buys the part at once, no questions.</summary>
    void JunkClick(Vector2 pos)
    {
        if (junkScene.Pick(pos) is not { } id) return;
        junkScene.Hover(null);
        if (Run(Game.BuyJunk(id))) Audio.Play("snd.part_off"); // measured: the sound of a part coming off
        junkHint = "";
        RenderJunkName();
    }

    /// <summary>"Name ($price)" under the view for the part under the pointer, which lights up in its condition colour.</summary>
    void JunkHover(Vector2 pos)
    {
        var id = leftDown || dialogs.IsOpen ? null : junkScene.Pick(pos);
        junkScene.Hover(id);
        var it = id is null ? null : Game.Junk.Find(j => j.Id == id);
        junkHint = it is null ? "" : $"{CI.Part(it.Part.PartId).Name} ({U.Money(Economy.JunkPrice(CI, it.Part))})";
        RenderJunkName();
    }

    // ---- Auction ------------------------------------------------------------------------------------------

    /// <summary>The Auction's figures as the column shows them.</summary>
    readonly record struct AuctionFigures(decimal Current, decimal Asking, decimal Yours, bool Lead);
    /// <summary>What the column and the clock show: measured, the car before's figures and clock stay up while the next
    /// car drives on (null when the Auction is opened).</summary>
    AuctionFigures? shownFigures;
    float shownClock;
    /// <summary>Until when (engine seconds) the column is blank: measured on 60 fps films, every change of a figure (a bid,
    /// a drop, the next car) draws the whole column anew, the three figures gone for a frame or two, Your Bid too.</summary>
    double columnBlankUntil;
    const double ColumnBlink = 0.033;

    void RenderAuction()
    {
        var a = Game.State.Auction;
        bool selling = a is { Mode: AuctionMode.Sell, Closed: false };
        // Measured: while "Winning Bid!" is up there is no Go Back To WorkShop and no Skip Car (Place Bid stays).
        bool won = a is { Closed: true, Result.Buyer: Bidder.Player };
        var sk = Skinned ? SkinScreenId : null;
        if (!Skinned) Text(screenUi, L.Prompt, Words.Get("prompt.bid"), 14, Color.FromHtml("#cfcfcf"), shadow: true);
        if (!selling && !won)
        {
            var back = Put(screenUi, new GoldPlate { Label = "Go Back To WorkShop", Arrow = true, FontSize = 14, SkinScreen = sk }, Skinned ? L.Orig.AuctionGoBack : L.GoBack);
            back.Pressed += () => Go(Screen.Workshop);
        }
        if (!Skinned) Put(screenUi, new ViewFrame(), L.AuctionView.Grow(2));
        if (a is null) return;
        var figures = new AuctionFigures(a.CurrentBid, a.Asking, a.History.LastOrDefault(b => b.Who == Bidder.Player)?.Amount ?? 0, a.Leader == Bidder.Player);
        bool arriving = a.Arriving > 0 && shownFigures is not null;
        var f = arriving ? shownFigures!.Value : figures;
        if (!arriving) shownClock = a.Closed ? 0 : (float)(a.TimeLeft / Math.Max(0.1, a.Duration));
        double clockNow = Time.GetTicksMsec() / 1000.0;
        if (shownFigures is { } before && before != f) columnBlankUntil = clockNow + ColumnBlink;
        shownFigures = f;
        bool blank = clockNow < columnBlankUntil;
        int row = 0;
        void Value(float y, string label, string value, bool lead = false)
        {
            if (Skinned)
            {
                // The screen has the titles and the boxes; the figures are the money font's.
                var at = new Vector2(L.Orig.AuctionValueRight, L.Orig.AuctionValueTop[row++]);
                Put(screenUi, new AuctionValue { Value = value, Lead = lead, SkinAt = at, SkinLeadAt = new Vector2(L.Orig.AuctionLead.X, L.Orig.AuctionLead.Y) }, L.R(518, y + 27, 116, 26));
                return;
            }
            Text(screenUi, L.R(514, y, 124, 22), label, 16, Look.Orange, shadow: true, align: HorizontalAlignment.Center);
            Put(screenUi, new AuctionValue { Value = value, Lead = lead }, L.R(518, y + 27, 116, 26));
        }
        string Whole(decimal m) => blank ? "" : U.Money(m).Replace(".00", "");
        Value(96, "Current Bid", Whole(f.Current), f.Lead && !blank);
        Value(176, "Asking Price", Whole(f.Asking));
        Value(256, "Your Bid", Whole(f.Yours));
        if (!a.Closed || won)
        {
            // While the next car drives on (and under "Winning Bid!") the buttons do nothing.
            var bid = Put(screenUi, new GoldPlate { Label = "Place Bid at this\nAsking Price", FontSize = 14, SkinScreen = sk }, Skinned ? L.Orig.Bid : L.R(518, 328, 116, 50));
            bid.Pressed += () => { if (Game.State.Auction is { Closed: false, Arriving: <= 0 }) Run(Game.PlaceBid()); };
            if (a.Mode == AuctionMode.Buy && !a.Closed)
            {
                var skip = Put(screenUi, new GoldPlate { Label = "- Skip Car -", FontSize = 14, SkinScreen = sk }, Skinned ? L.Orig.Skip : L.R(518, 392, 116, 30));
                skip.Pressed += () => { if (Game.State.Auction is { Arriving: <= 0 }) Run(Game.SkipCar()); };
            }
        }
        var faces = Skinned ? UiSkin.Picture("timer") : null;
        Put(screenUi, new PieClock { Fraction = shownClock, Faces = faces },
            faces is not null ? new Rect2(L.Orig.AuctionClock, new Vector2(faces.GetHeight(), faces.GetHeight())) : L.AuctionClock);
    }

    // ---- Car Lot ---------------------------------------------------------------------------------------------

    void RenderLot()
    {
        ShopHeader(Words.Get("prompt.car"));
        var sk = Skinned ? SkinScreenId : null;
        if (!Skinned) Put(screenUi, new ViewFrame(), L.LotView.Grow(2));
        var lot = Game.State.Lot;
        var choice = lotScene.LotChoice();
        lotChoice = choice?.Index ?? -1;
        // Measured: ◄ ► move the camera along the lane for as long as they are held (a click: a short way); with
        // nowhere to go they do nothing but look the same.
        var left = Put(screenUi, new ArrowButton { Horizontal = true, Up = true, SkinScreen = sk }, Skinned ? L.Orig.LotLeft : L.LotLeft);
        left.ButtonDown += () => lotHeld = 1;
        left.ButtonUp += () => lotHeld = 0;
        var right = Put(screenUi, new ArrowButton { Horizontal = true, Up = false, SkinScreen = sk }, Skinned ? L.Orig.LotRight : L.LotRight);
        right.ButtonDown += () => lotHeld = -1;
        right.ButtonUp += () => lotHeld = 0;
        lotBar = Put(screenUi, new LotBar { Skinned = Skinned }, Skinned ? L.Orig.LotBar : L.LotBar.Grow(6));
        lotBar.MarkerX = LotMarker(choice, lotBar);
        var v = lotChoice >= 0 && lotChoice < lot.Count ? Game.Vehicle(lot[lotChoice]) : null;
        (string Label, string Value, float X)[] cols =
        [
            ("Number", v?.Number?.ToString() ?? "", 110),
            ("Orig Cost", v is null ? "" : U.Money(v.Stats.OrigCost), 245),
            ("Repair Time", v is null ? "" : U.Hms(v.Stats.RepairTime), 385),
            ("Repair Cost", v is null ? "" : U.Money(v.Stats.RepairCost), 522),
        ];
        for (int ci = 0; ci < cols.Length; ci++)
        {
            var (label, value, x) = cols[ci];
            // Measured: the money figures for Number and the costs, the screen's smaller ones for the time.
            if (Skinned && ci != 2 && UiSkin.Font("cash") is not null)
            {
                Put(screenUi, new SkinFigures { Value = value }, L.R(L.Orig.LotValueX[ci] - 60, L.Orig.LotCashTop, 120, 24));
                continue;
            }
            if (Skinned && DialogLayer.SkinText("text", value, L.R(L.Orig.LotValueX[ci] - 60, L.Orig.LotValueTop, 120, 18), valign: VerticalAlignment.Top, wrap: false) is { } vl)
            {
                screenUi.AddChild(vl);
                continue;
            }
            Text(screenUi, L.R(x - 66, 396, 132, 22), label, 17, Look.Orange, shadow: true, align: HorizontalAlignment.Center);
            Put(screenUi, new ValuePlate { Label = value, Color = Look.MoneyGreen }, L.R(x - 52, 424, 104, 24));
        }
    }

    /// <summary>The gold marker sits under the chosen car (measured: under its middle, as it moves across the view).</summary>
    float? LotMarker((int Index, Vector2 At)? choice, Control bar) =>
        choice is { } c ? viewArea.Position.X + c.At.X - bar.Position.X : null;

    /// <summary>Click a car to bring it into the WorkShop.</summary>
    /// <summary>A click in the Car Lot's view. Measured: wherever it is (twice on empty asphalt), it brings the car the
    /// figures are about, the one nearest the view's middle, into the WorkShop.</summary>
    void LotClick()
    {
        if (lotScene.LotChoice() is not { } c || Game.State.Lot.ElementAtOrDefault(c.Index) is not { } id) return;
        if (Run(Game.BringToWorkshop(id))) Go(Screen.Workshop);
    }

    // ---- Decal Browser ----------------------------------------------------------------------------------------

    async Task DecalBrowser()
    {
        if (Skinned && UiSkin.Screen("decals.choose") is { } choosePics && UiSkin.Screen("decals.modify") is { } modifyPics)
        {
            await ClassicDecalBrowser(choosePics, modifyPics);
            return;
        }
        var rect = L.R(120, 90, 398, 298);
        // In the order you bought them, as the original lists them.
        var owned = Game.State.DecalUses.Where(u => u.Value > 0).Select(u => CI.Pack.Decals.Find(d => d.Id == u.Key)).OfType<DecalDef>().ToList();
        if (owned.Count == 0)
        {
            var empty = new Control { MouseFilter = MouseFilterEnum.Ignore };
            Text(empty, L.R(14, 4, 200, 20), "Choose a Decal:", 13, Look.White, shadow: true);
            var well = Put(empty, new TextPanel(), L.R(8, 26, 382, 208));
            Text(well, L.R(20, 0, 340, 208), Words.Get("decals.none"), 13, Look.White, shadow: true, align: HorizontalAlignment.Center, wrap: true);
            await dialogs.ShowCustom("Decal Browser", rect, empty, [new DialogButton("CANCEL", "cancel", true)], 262, buttonWidth: 94);
            return;
        }
        // Choose.
        var choose = new Control { MouseFilter = MouseFilterEnum.Ignore };
        Text(choose, L.R(14, 4, 200, 20), "Choose a Decal:", 13, Look.White, shadow: true);
        Put(choose, new TextPanel(), L.R(8, 26, 360, 208));
        string? picked = null;
        var tcs = new TaskCompletionSource<bool>();
        for (int i = 0; i < owned.Count && i < 15; i++)
        {
            var d = owned[i];
            var cell = Put(choose, new CatalogItem { Label = d.Name, Picture = Assets.Texture(d.Texture), Dark = true, Count = Game.DecalUses(d.Id) }, L.R(12 + (i % 5) * 70, 30 + (i / 5) * 66, 68, 64));
            cell.Pressed += () =>
            {
                picked = d.Id;
                tcs.TrySetResult(true);
            };
        }
        var chooseTask = dialogs.ShowCustom("Decal Browser", rect, choose, [new DialogButton("CANCEL", "cancel", true)], 262, buttonWidth: 94);
        var first = await Task.WhenAny(chooseTask, tcs.Task);
        if (first == chooseTask || picked is null) return;
        dialogs.CloseTop();
        // Modify: turn, flip, size and colour, then DONE to place it (the colour starts white each time, as measured).
        decalId = picked;
        decalColor = null;
        var def = CI.Pack.Decals.First(d => d.Id == picked);
        var modify = new Control { MouseFilter = MouseFilterEnum.Ignore };
        Text(modify, L.R(14, 4, 200, 20), "Modify Decal:", 13, Look.White, shadow: true);
        var preview = Put(modify, new DecalPreview { Picture = Assets.Texture(def.Texture) }, L.R(44, 62, 128, 124));
        void Upd()
        {
            preview.Angle = decalAngle;
            preview.FlipX = decalFlipX;
            preview.FlipY = decalFlipY;
            preview.Tint = decalColor is null ? Colors.White : Color.FromHtml(decalColor);
            preview.SizeFactor = decalSize / 0.45f;
            preview.QueueRedraw();
        }
        Upd();
        void Btn(string label, Rect2 r, Action act)
        {
            var b = Put(modify, new BlackButton { Label = label, FontSize = 10 }, r);
            b.Pressed += () => { act(); Upd(); };
        }
        // Measured: a quarter turn a click.
        Btn("TURN ↺", L.R(20, 34, 48, 26), () => decalAngle += Mathf.Pi / 2);
        Btn("TURN ↻", L.R(150, 34, 48, 26), () => decalAngle -= Mathf.Pi / 2);
        Btn("FLIP", L.R(16, 100, 24, 60), () => decalFlipY = !decalFlipY);
        Btn("◄ FLIP ►", L.R(64, 190, 88, 20), () => decalFlipX = !decalFlipX);
        Btn("BIGGER", L.R(176, 104, 52, 20), () => decalSize = 0.45f * 1.5f);
        Btn("Normal", L.R(176, 146, 52, 20), () => decalSize = 0.45f);
        Btn("smaller", L.R(176, 188, 52, 20), () => decalSize = 0.45f / 1.5f);
        if (def.Tint)
        {
            var paints = CI.Pack.Paints;
            for (int i = 0; i < paints.Count && i < 27; i++)
            {
                var p = paints[i];
                var sw = Put(modify, new Swatch { Color = Color.FromHtml(p.Color), Quiet = true }, L.R(256 + (i % 3) * 40, 44 + (i / 3) * 17, 40, 17));
                sw.Pressed += () => { decalColor = p.Color; Upd(); };
            }
        }
        var answer = await dialogs.ShowCustom("Decal Browser", rect, modify, [new DialogButton("DONE", "done", true), new DialogButton("CANCEL", "cancel")], 262, buttonWidth: 94);
        if (answer != "done") return;
        placingDecal = true;
        Refresh();
    }

    /// <summary>The decal placed last since the game started (the Decal Browser's Last Used).</summary>
    string? lastDecal;
    /// <summary>The open Decal Browser's buttons, by what they do (for the tour).</summary>
    Action<string>? browse;

    /// <summary>
    /// The original's Decal Browser: one panel, two pages. "Choose a Decal:" lists the decals you have (five across,
    /// two rows, scrolled a row at a time), with Last Used once you have placed one; "Modify Decal:" turns, flips,
    /// sizes and colours it, with Back to the list. Its buttons are cut out of the pages' pictures.
    /// </summary>
    async Task ClassicDecalBrowser(UiSkin.ScreenPictures choosePics, UiSkin.ScreenPictures modifyPics)
    {
        // Measured: the decals you have, in the order you bought them, a page of six across and two down at a time;
        // the arrows turn a whole page.
        var owned = Game.State.DecalUses.Where(u => u.Value > 0).Select(u => CI.Pack.Decals.Find(d => d.Id == u.Key)).OfType<DecalDef>().ToList();
        const int perPage = L.Orig.BrowserColumns * L.Orig.BrowserRows;
        int pages = Math.Max(1, (owned.Count + perPage - 1) / perPage), page = 0;
        var panel = new Control { MouseFilter = MouseFilterEnum.Stop, Position = L.Orig.Browser.Position, Size = L.Orig.Browser.Size };
        var done = dialogs.ShowPanel(panel, "cancel", null, out var answer);
        float[] sizes = [0.45f * 1.5f, 0.45f, 0.45f / 1.5f];

        void Button(UiSkin.ScreenPictures pics, Rect2 r, string what, bool lit = false)
        {
            var b = PictureButton.Cut(pics, r);
            b.Lit = lit;
            b.Pressed += () => browse?.Invoke(what);
            panel.AddChild(b);
        }

        void Choose()
        {
            U.Clear(panel);
            Put(panel, new TextureRect { Texture = choosePics.Base, MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 400, 300));
            Button(choosePics, L.Orig.BrowserCancel, "cancel");
            if (owned.Count == 0)
            {
                if (DialogLayer.SkinText("text", Words.Get("decals.none"), L.Orig.BrowserWell) is { } none) panel.AddChild(none);
                return;
            }
            Button(choosePics, L.Orig.BrowserUp, "up");
            Button(choosePics, L.Orig.BrowserDown, "down");
            if (lastDecal is { } last && owned.Exists(d => d.Id == last)) Button(choosePics, L.Orig.BrowserCorner, "pick:" + last);
            for (int i = page * perPage; i < owned.Count && i < (page + 1) * perPage; i++)
            {
                var d = owned[i];
                int k = i - page * perPage;
                var at = L.Orig.BrowserFirst + new Vector2(k % L.Orig.BrowserColumns, k / L.Orig.BrowserColumns) * L.Orig.BrowserPitch - BrowserDecal.Icon.Position;
                var cell = Put(panel, new BrowserDecal { Label = d.Name, Picture = Assets.Texture(d.Texture), Count = Game.DecalUses(d.Id) },
                    new Rect2(at, L.Orig.BrowserPitch));
                cell.Pressed += () => browse?.Invoke("pick:" + d.Id);
            }
        }

        void Modify(DecalDef def)
        {
            U.Clear(panel);
            Put(panel, new TextureRect { Texture = modifyPics.Base, MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 400, 300));
            Put(panel, new DecalPreview
            {
                Classic = true,
                Picture = Assets.Texture(def.Texture),
                Angle = decalAngle,
                FlipX = decalFlipX,
                FlipY = decalFlipY,
                Tint = def.Tint && decalColor is { } c ? Color.FromHtml(c) : Colors.White,
            }, L.Orig.BrowserPreview);
            Button(modifyPics, L.Orig.BrowserTurnLeft, "turn-left");
            Button(modifyPics, L.Orig.BrowserTurnRight, "turn-right");
            Button(modifyPics, L.Orig.BrowserFlipV, "flip-v");
            Button(modifyPics, L.Orig.BrowserFlipH, "flip-h");
            int chosen = Array.FindIndex(sizes, s => Mathf.IsEqualApprox(s, decalSize));
            for (int i = 0; i < sizes.Length; i++) Button(modifyPics, L.Orig.BrowserSizes[i], "size:" + i, lit: i == chosen);
            Button(modifyPics, L.Orig.BrowserCorner, "back");
            Button(modifyPics, L.Orig.BrowserDone, "done");
            Button(modifyPics, L.Orig.BrowserCancel, "cancel");
            if (!def.Tint) return;
            var paints = CI.Pack.Paints;
            for (int i = 0; i < paints.Count && i < 27; i++)
            {
                var p = paints[i];
                var r = L.Orig.BrowserColour;
                var sw = Put(panel, new Swatch { Color = Color.FromHtml(p.Color), Classic = true, Quiet = true },
                    new Rect2(r.Position + new Vector2(i % 3, i / 3) * L.Orig.BrowserColourPitch, r.Size));
                sw.Pressed += () => browse?.Invoke("colour:" + p.Color);
            }
        }

        DecalDef? Current() => decalId is { } id ? CI.Pack.Decals.Find(d => d.Id == id) : null;
        browse = what =>
        {
            switch (what)
            {
                case "cancel" or "done":
                    answer(what);
                    return;
                case "up" or "down":
                    page = Math.Clamp(page + (what == "up" ? -1 : 1), 0, pages - 1);
                    Choose();
                    return;
                case "back":
                    Choose();
                    return;
            }
            // Measured: the colour picked for a decal is not kept; the next time it is white again.
            if (what.StartsWith("pick:", StringComparison.Ordinal))
            {
                decalId = what[5..];
                decalColor = null;
            }
            // Measured: TURN turns the decal a quarter turn (the right one clockwise).
            else if (what == "turn-left") decalAngle += Mathf.Pi / 2;
            else if (what == "turn-right") decalAngle -= Mathf.Pi / 2;
            else if (what == "flip-v") decalFlipY = !decalFlipY;
            else if (what == "flip-h") decalFlipX = !decalFlipX;
            else if (what.StartsWith("size:", StringComparison.Ordinal)) decalSize = sizes[int.Parse(what[5..])];
            else if (what.StartsWith("colour:", StringComparison.Ordinal)) decalColor = what[7..];
            if (Current() is { } def) Modify(def);
        };
        Choose();
        var result = await done;
        browse = null;
        if (result != "done" || decalId is null) return;
        placingDecal = true;
        Refresh();
    }

    // ---- Sign in -----------------------------------------------------------------------------------------------

    void ShowSignIn()
    {
        Screen = Screen.SignIn;
        ShowScene(workshop);
        U.Clear(signIn);
        // With a skin the sheet is its picture; the rows are written in its letters.
        var sheet = UiSkin.Screen("signin")?.Base;
        bool sk = sheet is not null;
        if (sk) Put(signIn, new TextureRect { Texture = sheet, MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        else Put(signIn, new SignInPaper(), L.R(0, 0, 640, 480));
        var blue = Color.FromHtml("#4a5ab8");
        var list = profiles.List();
        const int rows = 9;
        float[] cx = [8, 128, 305, 402, 480, 540, 632];
        string[] heads = ["COMMANDS", "MECHANIC", "SKILL", "TOTAL TIME", "CARS", "CASH"];
        if (!sk)
        {
            Put(signIn, new SignInTable { Columns = cx, Rows = rows }, L.R(0, 0, 640, 480));
            for (int i = 0; i < heads.Length; i++)
                Text(signIn, L.R(cx[i], 128, cx[i + 1] - cx[i], 18), heads[i], 10, blue, Look.Plain, HorizontalAlignment.Center);
        }
        DrawnButton SheetButton(string id, string label, Vector2 at, Rect2 plain)
        {
            if (sk && UiSkin.Picture($"signin.{id}.up") is { } up)
                return Put(signIn, new PictureButton { SkinUp = up, SkinDown = UiSkin.Picture($"signin.{id}.down") }, new Rect2(at, up.GetSize()));
            return Put(signIn, new OutlineButton { Label = label, Plain = plain.Size.X > 80 }, plain);
        }
        void Cell(float x0, float x1, float y, string text)
        {
            // Measured: the words' glyphs from 5 below the row's top (167-177 in the first row).
            if (sk && DialogLayer.SkinText("signin", text, L.R(x0, y - 1, x1 - x0, 17), wrap: false) is { } l) signIn.AddChild(l);
            else Text(signIn, L.R(x0, y, x1 - x0, 22), text, 13, Color.FromHtml("#2a3a8a"), Look.Plain, HorizontalAlignment.Center);
        }
        for (int i = 0; i < rows; i++)
        {
            float y = sk ? L.Orig.SignInTop + i * L.Orig.SignInPitch : 150 + i * 24;
            var m = i < list.Count ? list[i] : null;
            if (m is null)
            {
                var nb = SheetButton("new", "NEW", new Vector2(L.Orig.SignInNew.X, y), L.R(84, y + 2, 38, 18));
                nb.Pressed += () => _ = CreateMechanic();
                continue;
            }
            var meta0 = m;
            var del = SheetButton("delete", "DELETE", new Vector2(L.Orig.SignInDelete.X, y), L.R(12, y + 2, 64, 18));
            del.Pressed += () => _ = DeleteMechanic(meta0);
            var g = profiles.Load(CI, m.Id);
            var s = g?.State;
            DrawnButton name = sk
                ? Put(signIn, new PictureButton { Quiet = true }, L.R(cx[1] + 2, y, cx[2] - cx[1] - 4, 20))
                : Put(signIn, new OutlineButton { Label = m.Name.ToUpperInvariant(), Plain = true, Quiet = true }, L.R(cx[1] + 2, y + 1, cx[2] - cx[1] - 4, 21));
            if (sk) Cell(cx[1], cx[2], y, m.Name.ToUpperInvariant());
            name.Pressed += () => Play(meta0);
            int cars = g?.CarsOwned ?? 0;
            Cell(cx[2], cx[3], y, s is null ? "?" : (s.Skill < CI.Pack.Rules.Skills.Count ? SkillWord(s.Skill, CI.Pack.Rules.Skills[s.Skill].Name).ToUpperInvariant() : ""));
            Cell(cx[3], cx[4], y, s is null ? "" : U.Hms(s.PlayTime));
            Cell(cx[4], cx[5], y, s is null ? "" : cars.ToString());
            Cell(cx[5], cx[6], y, s is null ? "" : s.Cash.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        // Options: the original's CREDITS and EXIT; ours add the game folder and the content switch below the table.
        if (!sk) Put(signIn, new OptionsBox(), L.R(435, 12, 113, 108));
        (string Id, string Label, Action Act, Vector2 At)[] opts = [("credits", "CREDITS", () => _ = Credits(), L.Orig.SignInCredits), ("exit", "EXIT", () => _ = ExitGame(), L.Orig.SignInExit)];
        for (int i = 0; i < opts.Length; i++)
        {
            var b = SheetButton(opts[i].Id, opts[i].Label, opts[i].At, L.R(448, 45 + i * 30, 88, 22));
            b.Pressed += opts[i].Act;
        }
        var settings = Settings.Load();
        // Our buttons below the table, in rows that stop short of the original's MEKADA box (from x = 454).
        const float RowEnd = 448;
        float bx = 12, by = 424;
        OutlineButton Small(string label, float width, Action act)
        {
            if (bx + width > RowEnd) (bx, by) = (12, by + 19);
            var b = Put(signIn, new OutlineButton { Label = label, Small = true }, L.R(bx, by, width, 16));
            b.Pressed += act;
            bx += width + 6;
            return b;
        }
        void Switch(Action flip)
        {
            flip();
            settings.Save();
            Reboot?.Invoke();
        }
        Small("GAME FOLDER…", 110, PickGameFolder);
        if (settings.OriginalFolder is not null)
        {
            // Each shows what is in use now (the command line can override the settings) and switches to the other.
            bool content = Original is not null, look = UiSkin.IsOriginal;
            Small(content ? "CONTENT: ORIGINAL" : "CONTENT: PLACEHOLDERS", 140, () => Switch(() => settings.UseOriginal = !content));
            if (settings.SkinFolder is null)
                Small(look ? "LOOK: ORIGINAL" : "LOOK: OPENGG", 116, () => Switch(() => settings.UseOriginalLook = !look));
        }
        if (Original is not null && MekFiles().Length > 0) Small("IMPORT MECHANICS…", 130, () => _ = ImportMechanics());
        // The window: a window or the full screen (F11, Alt+Enter), filling it or at whole multiples of 640 x 480.
        screenButton = Small(ScreenLabel, 110, ScreenMode.Toggle);
        scaleButton = Small(ScaleLabel, 140, () => ScreenMode.SetPixelPerfect(!ScreenMode.PixelPerfect));
        // OpenGG is a beta (0.x): a stamp over the end of the sheet's title, left of OPTIONS, in both looks (the band above
        // the title is empty in both).
        Put(signIn, new BetaStamp(), L.R(320, 8, 112, 32));
        // A problem with the game folder; or, with none chosen yet, how to point OpenGG at the player's copy.
        string? hint = StartupNote ?? (Original is null && settings.OriginalFolder is null ? Words.Get("folder.hint") : null);
        if (hint is not null)
        {
            if (RowEnd - bx < 200) (bx, by) = (12, by + 19);
            var color = StartupNote is not null ? Color.FromHtml("#b83a2a") : Color.FromHtml("#3b4aa6");
            Text(signIn, L.R(bx, by, RowEnd - bx, 26), hint, 10, color, wrap: true);
        }
        Refresh();
        // Without the original's files, the first time: what the beta has (one car model); not in scripted runs.
        if (Original is null && !settings.BetaNoteSeen && !ScreenMode.Scripted)
        {
            settings.BetaNoteSeen = true;
            settings.Save();
            _ = dialogs.Alert("Beta", Words.Get("beta.note"));
        }
    }

    OutlineButton? screenButton, scaleButton;

    static string ScreenLabel => ScreenMode.Fullscreen ? "SCREEN: FULL" : "SCREEN: WINDOW";
    static string ScaleLabel => ScreenMode.PixelPerfect ? "SCALE: PIXEL-PERFECT" : "SCALE: FILL";

    void UpdateScreenButtons()
    {
        if (screenButton is not null && IsInstanceValid(screenButton))
        {
            screenButton.Label = ScreenLabel;
            screenButton.QueueRedraw();
        }
        if (scaleButton is not null && IsInstanceValid(scaleButton))
        {
            scaleButton.Label = ScaleLabel;
            scaleButton.QueueRedraw();
        }
    }

    string[] MekFiles()
    {
        var dir = System.IO.Path.Combine(Original!.Folder, "Data", "Mechanics");
        return System.IO.Directory.Exists(dir)
            ? System.IO.Directory.GetFiles(dir).Where(f => f.EndsWith(".mek", StringComparison.OrdinalIgnoreCase)).Order().ToArray()
            : [];
    }

    /// <summary>The original's mechanics (Data/Mechanics/*.mek) as OpenGG mechanics; those whose name is
    /// already on the sheet are left alone.</summary>
    async Task ImportMechanics()
    {
        var have = profiles.List().Select(m => m.Name.ToUpperInvariant()).ToHashSet();
        var found = new List<Core.Original.OrigMechanic>();
        var notes = new List<string>();
        foreach (var f in MekFiles())
            try
            {
                var m = Core.Original.MekFile.Read(f);
                if (!have.Contains(m.Name.Trim().ToUpperInvariant())) found.Add(m);
            }
            catch (Exception e)
            {
                notes.Add($"{System.IO.Path.GetFileName(f)}: {e.Message}");
            }
        if (found.Count == 0)
        {
            await dialogs.Alert("Import Mechanics", notes.Count > 0 ? string.Join("\n", notes) : "Every mechanic of the original is already here.");
            return;
        }
        int room = Profiles.Max - profiles.List().Count;
        if (room <= 0)
        {
            await dialogs.Alert("Import Mechanics", "The sheet is full. Delete a mechanic first.");
            return;
        }
        var names = string.Join(", ", found.Take(room).Select(m => m.Name.Trim().ToUpperInvariant()));
        if (!await dialogs.Confirm("Import Mechanics", $"Bring over from the original: {names}?", "IMPORT", "CANCEL")) return;
        int problems = 0;
        foreach (var m in found.Take(room))
        {
            var (g, p) = Core.Original.MekImport.Import(Original!, CI, m);
            foreach (var line in p) GD.Print($"import {m.Name}: {line}");
            problems += p.Count;
            profiles.Add(g.State.Mechanic, g);
        }
        if (problems > 0) await dialogs.Alert("Import Mechanics", $"{problems} things had no counterpart and were left out (see the log).");
        ShowSignIn();
    }

    void PickGameFolder()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Your Gearhead Garage folder (the one with Data\\Cars)",
        };
        fd.DirSelected += picked =>
        {
            fd.QueueFree();
            // Forgiving: Data or Data\Cars picked, or the folder the game was installed into (see FindGameFolder).
            if (Core.Original.OriginalGame.FindGameFolder(picked) is not { } dir)
            {
                _ = dialogs.Alert("Game Folder", Words.Get("folder.none", ("folder", picked)));
                return;
            }
            var s = Settings.Load();
            s.OriginalFolder = dir;
            s.UseOriginal = true;
            s.Save();
            Reboot?.Invoke();
        };
        fd.Canceled += fd.QueueFree;
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>A box of the sheet's own is up (Create New Mechanic, Delete, Exit): the sheet's buttons wait.</summary>
    bool sheetBoxUp;

    void Play(ProfileMeta m)
    {
        if (sheetBoxUp) return;
        var g = profiles.Load(CI, m.Id);
        if (g is null)
        {
            _ = dialogs.Alert("Sign In", "That mechanic could not be loaded.");
            return;
        }
        // Measured: signing in has its own sound (not the click).
        Audio.Play("snd.signin");
        StartProfile(m, g);
        // Measured: a new mechanic signing in gets the tutorial's first customer at once; in Jobs Mode the next one is
        // always on the phone.
        if (Game.InJobsMode && Game.State.Job is null) QueueDialog(GetJob);
    }

    /// <summary>
    /// The sign-in sheet's own box (the original's Create New Mechanic, Delete and Exit boxes): a title in its white
    /// field, the words centred on it, OK and CANCEL. Measured: it comes up with the dialogs' sound and goes with theirs;
    /// the words' middle is 74 below its top, their lines 18 apart.
    /// </summary>
    async Task<bool> SignInConfirm(string title, string text)
    {
        if (sheetBoxUp) return false;
        sheetBoxUp = true;
        try { return await SignInConfirmBox(title, text); }
        finally { sheetBoxUp = false; }
    }

    async Task<bool> SignInConfirmBox(string title, string text)
    {
        var framePic = UiSkin.Picture("signin.frame");
        var box = Put(signIn, new SignInBox { Title = title, Picture = framePic },
            framePic is not null ? new Rect2(L.Orig.SignInBox, framePic.GetSize()) : L.R(207, 124, 238, 178));
        if (framePic is not null && DialogLayer.SkinText("signin", text, L.R(12, 38, 217, 72)) is { } words) box.AddChild(words);
        else Text(box, L.R(12, 38, 214, 72), text, 13, Color.FromHtml("#4a5ab8"), Look.Plain, HorizontalAlignment.Center, wrap: true).VerticalAlignment = VerticalAlignment.Center;
        var tcs = new TaskCompletionSource<bool>();
        SignInBoxButtons(box, framePic, tcs);
        Audio.Play("snd.dialog");
        bool ok = await tcs.Task;
        Audio.Play("snd.dialog_close");
        box.QueueFree();
        return ok;
    }

    /// <summary>OK and CANCEL at the bottom of a sign-in box (the skin's own pictures when it has them).</summary>
    void SignInBoxButtons(Control box, Texture2D? framePic, TaskCompletionSource<bool> tcs)
    {
        DrawnButton BoxButton(string id, string label, Rect2 r) => framePic is not null && UiSkin.Picture($"signin.{id}.up") is { } up
            ? Put(box, new PictureButton { SkinUp = up, SkinDown = UiSkin.Picture($"signin.{id}.down") }, new Rect2(r.Position - Vector2.One, up.GetSize()))
            : Put(box, new OutlineButton { Label = label, Plain = true }, r);
        var ok = BoxButton("ok", "OK", L.R(10, 144, 88, 24));
        ok.Pressed += () => tcs.TrySetResult(true);
        var cancel = BoxButton("cancel", "CANCEL", L.R(138, 144, 88, 24));
        cancel.Pressed += () => tcs.TrySetResult(false);
    }

    /// <summary>EXIT; measured: the original asks first.</summary>
    async Task ExitGame()
    {
        if (await SignInConfirm("EXIT", Words.Get("signin.exit"))) GetTree().Quit();
    }

    async Task DeleteMechanic(ProfileMeta m)
    {
        if (!await SignInConfirm($"DELETE {m.Name.ToUpperInvariant()} ?", Words.Get("signin.delete"))) return;
        profiles.Delete(m.Id);
        ShowSignIn();
    }

    async Task CreateMechanic()
    {
        if (sheetBoxUp) return;
        sheetBoxUp = true;
        try { await CreateMechanicBox(); }
        finally { sheetBoxUp = false; }
    }

    async Task CreateMechanicBox()
    {
        var framePic = UiSkin.Picture("signin.frame");
        var box = Put(signIn, new SignInBox { Title = "CREATE NEW MECHANIC", Picture = framePic },
            framePic is not null ? new Rect2(L.Orig.SignInBox, framePic.GetSize()) : L.R(207, 124, 238, 178));
        // Measured: the prompt's middle 74 below the box's top, as the other boxes' words; the name written from 38 in,
        // its middle 125 down, in capitals, with a dash after it for the cursor.
        var prompt = framePic is not null ? DialogLayer.SkinText("signin", "ENTER YOUR NAME...", L.R(0, 65, 241, 18), wrap: false) : null;
        if (prompt is not null) box.AddChild(prompt);
        else Text(box, L.R(0, 38, 238, 20), "ENTER YOUR NAME...", 13, Color.FromHtml("#4a5ab8"), Look.Plain, HorizontalAlignment.Center);
        var input = Put(box, new LineEdit { MaxLength = 16, Alignment = HorizontalAlignment.Center, Flat = true }, L.R(20, 100, 198, 26));
        if (framePic is not null && DialogLayer.SkinText("signin", "-", L.R(38, 116, 198, 18), HorizontalAlignment.Left, wrap: false) is { } shown)
        {
            // The original's own field: the typed name shows in the sheet's letters; the edit field only takes the keys.
            input.Position = new Vector2(38, 112);
            input.AddThemeColorOverride("font_color", Colors.Transparent);
            input.AddThemeColorOverride("caret_color", Colors.Transparent);
            input.AddThemeColorOverride("selection_color", Colors.Transparent);
            box.AddChild(shown);
            input.TextChanged += t => shown.Text = t.ToUpperInvariant() + "-";
        }
        var tcs = new TaskCompletionSource<bool>();
        SignInBoxButtons(box, framePic, tcs);
        input.TextSubmitted += _ => tcs.TrySetResult(true);
        Callable.From(input.GrabFocus).CallDeferred();
        Audio.Play("snd.dialog");
        bool go = await tcs.Task;
        Audio.Play("snd.dialog_close");
        var name = input.Text.Trim();
        box.QueueFree();
        if (!go || name.Length == 0) return;
        if (profiles.Create(CI, name) is not { } created)
        {
            _ = dialogs.Alert("Sign In", "All mechanic slots are taken. Delete one first.");
            return;
        }
        if (!created.Game.InJobsMode)
        {
            created.Game.State.FreePlay = true;
            profiles.Save(created.Meta.Id, created.Game);
        }
        // Measured: the new mechanic's row is on the sheet; signing in is up to you.
        ShowSignIn();
    }

    /// <summary>The credits are up (they have their own music).</summary>
    bool inCredits;

    /// <summary>Our own credits screen (the original's names are not ours to show).</summary>
    async Task Credits()
    {
        inCredits = true;
        Refresh();
        var skin = UiSkin.Screen("credits")?.Base;
        Control page = skin is not null
            ? Put(signIn, new TextureRect { Texture = skin, MouseFilter = MouseFilterEnum.Stop }, L.R(0, 0, 640, 480))
            : Put(signIn, new CreditsPage(), L.R(0, 0, 640, 480));
        var tcs = new TaskCompletionSource<bool>();
        var back = Put(page, new GoldPlate { Label = "Go Back To Sign In", Arrow = true, FontSize = 14, SkinScreen = skin is not null ? "credits" : null },
            skin is not null ? L.Orig.CreditsBack : L.R(484, 446, 150, 28));
        back.Pressed += () => tcs.TrySetResult(true);
        await tcs.Task;
        page.QueueFree();
        inCredits = false;
        Refresh();
    }

    void StartProfile(ProfileMeta m, Game g, bool skillCheck = true)
    {
        Game.Event -= OnGameEvent;
        meta = m;
        Game = g;
        workshop.Game = g;
        auctionScene.Game = g;
        lotScene.Game = g;
        junkScene.Game = g;
        g.Event += OnGameEvent;
        // A save made at the Auction or in the JunkYard: an auction you were only watching is left (nothing was paid;
        // ticking on behind the WorkShop it drew the screen anew every frame and no button could be pressed), parts paid
        // for in the Purchase Bin come home; a car of yours on the block stays there (its screen comes up below).
        if (g.State.Auction is { Closed: true } or { Closed: false, Mode: AuctionMode.Buy }) g.LeaveAuction();
        if (g.State.PurchaseBin.Count > 0) g.LeaveJunkyard();
        DropCarry(null);
        attaching = null;
        finishing = null;
        finishingModel = null;
        finishingCar = null;
        jobCompleteUp = false;
        carHeld = 0;
        workshop.EndShowcase();
        boltSlot = null;
        placingDecal = false;
        xray = false;
        Region = View.Complete;
        tool = "ratchet";
        binCount = g.State.Bin.Count;
        binPage = int.MaxValue;
        workshop.SetVehicle(null);
        auctionScene.Show(null);
        lotScene.Show(null);
        Screen = Screen.Workshop;
        ShowScene(workshop);
        View3D.OrigLook.UseWorkshop();
        workshop.SetVehicle(g.State.Workshop);
        workshop.SetRegion(Region, animate: false);
        Refresh();
        PrefetchCatalog();
        // Measured: as you sign in, the skill levels your cash has reached come one by one, each with its Available Cars,
        // over the WorkShop's tabs with no car and no commands; a car of yours in top condition that never had its Car
        // Complete gets it (over a blank WorkShop). The car comes into the view after the last box. (Both at once: not
        // seen; the advances first here.)
        signingIn = true;
        if (skillCheck) g.CheckSkill();
        g.CheckCarComplete();
        signingIn = false;
        if (g.State.Auction is { Closed: false, Mode: AuctionMode.Sell }) Go(Screen.Auction);
    }

    // ---- test hooks (Main's --autoshot tour) -----------------------------------------------------------

    /// <summary>Plays a game that is never saved. A demo's made-up state (Free Play still Learning, say) brings no Skill
    /// Advance at the start unless asked for.</summary>
    public void StartDemo(Game g, bool skillCheck = false) => StartProfile(new ProfileMeta { Id = "", Name = g.State.Mechanic }, g, skillCheck);

    /// <summary>Lab: the WorkShop camera at an angle, distance and target, at once.</summary>
    public void DemoCamera(float yaw, float pitch, float dist, Vector3 target) =>
        workshop.Orbit.MoveTo(target + View3D.OrbitCamera.Offset(yaw, pitch, dist), target, animate: false);

    /// <summary>Lab: one model on its own in the WorkShop (see <see cref="View3D.WorkshopScene.ShowModel"/>).</summary>
    public Vector3 DemoModel(string id) => workshop.ShowModel(id);

    /// <summary>Lab: the WorkShop's own framing of the current tab, at once.</summary>
    public void DemoFrame() => workshop.Frame(animate: false);

    /// <summary>Lab: what a click at this point of the view (in its pixels on the 640 x 480 screen) would pick, and where
    /// the car's middle shows in it.</summary>
    public string DemoPick(Vector2 at)
    {
        string hit = workshop.Pick(at) is { } h ? h.Pick.ToString() ?? "?" : "nothing";
        var mid = workshop.View?.Bounds(View.Complete)?.GetCenter() is { } c ? workshop.Camera.UnprojectPosition(c) : Vector2.Zero;
        return $"{hit}; the car's middle at {mid.X:0.#},{mid.Y:0.#} of {workshop.GetViewport().GetVisibleRect().Size}";
    }

    public View3D.OrbitCamera DemoOrbit => workshop.Orbit;

    /// <summary>Lab: the view at these angles, the camera where the WorkShop's rig puts it.</summary>
    public void DemoAngles(float yaw, float pitch)
    {
        workshop.Orbit.Yaw = yaw;
        workshop.Orbit.Pitch = pitch;
        workshop.Frame(animate: false);
    }

    public (Vector3 Centre, float Level) DemoFraming => workshop.Framing;

    /// <summary>Lab: the WorkShop car's triangles, as drawn now, to a file (see <see cref="VehicleView.ExportTriangles"/>).</summary>
    public void DemoExport(string path)
    {
        if (Screen == Screen.Auction) auctionScene.ExportCar(path);
        else workshop.View?.ExportTriangles(path);
    }

    /// <summary>Lab: your car in the WorkShop goes on the Auction's stage (as Auction Car does), no questions.</summary>
    public void DemoAuctionOwnCar()
    {
        if (Game.WorkshopVehicle() is null && Game.State.Lot.Count > 0) Game.BringToWorkshop(Game.State.Lot[0]);
        if (Game.WorkshopVehicle() is { Owner: Core.Sim.Owner.Player } v && Game.AuctionOwnCar(v.Id).Ok) Go(Screen.Auction);
    }

    /// <summary>Lab: the JunkYard's shelf shown as it is set, without the change a visit makes.</summary>
    bool junkAsIs;

    /// <summary>Lab: the JunkYard at an area's start; <paramref name="bare"/> leaves its parts out (the scene alone).</summary>
    public void DemoJunk(Core.Content.Region area, bool bare, string? shelfOf = null, IEnumerable<string>? remove = null, IEnumerable<string>? last = null)
    {
        // A shelf other than the WorkShop car's: the car that was in for a job is not in the save.
        if (shelfOf is not null && CI.HasCar(shelfOf))
        {
            if (Game.WorkshopVehicle() is { } v && v.ModelId != shelfOf) Game.PutCarInLot();
            Game.State.JunkFor = shelfOf;
        }
        // The shelf as the yard showed it: parts gone since the save, parts shown at the end of the row.
        var shelf = Game.Junk;
        foreach (var id in remove ?? []) if (shelf.FindIndex(j => j.Part.PartId == id) is var i and >= 0) shelf.RemoveAt(i);
        foreach (var id in last ?? [])
            if (shelf.FindIndex(j => j.Part.PartId == id) is var i and >= 0)
            {
                var it = shelf[i];
                shelf.RemoveAt(i);
                shelf.Add(it);
            }
        junkAsIs = true;
        Go(Screen.Junkyard);
        junkAsIs = false;
        junkArea = area;
        junkKey = "";
        Render();
        if (bare) junkScene.Show([], area);
        junkScene.ResetPan();
    }

    /// <summary>Lab: the JunkYard's camera this far along its path (metres).</summary>
    public void DemoJunkPan(float metres) => junkScene.SetPan(metres);

    /// <summary>Lab: the JunkYard's part under the pointer (its highlight), or none.</summary>
    public void DemoJunkHover(string? itemId) => junkScene.Hover(itemId);

    /// <summary>Lab: the JunkYard's parts and where they stand, to a file.</summary>
    public void DemoExportJunk(string path) => junkScene.ExportItems(path);

    /// <summary>Lab: the JunkYard's camera.</summary>
    public string DemoJunkGeometry => junkScene.Geometry();

    /// <summary>Lab: redraw the WorkShop car after its state was changed behind the game's back.</summary>
    public void DemoSync() => workshop.View?.Sync(animateChanges: false);

    /// <summary>Lab: turn the view as holding the arrow keys does (yaw: Right 1, Left −1; pitch: Up 1, Down −1).</summary>
    public void DemoTurn(float yaw, float pitch, double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 1 / 120.0)
            workshop.RotateHeld(yaw, pitch, Math.Min(1 / 120.0, seconds - t));
        workshop.Orbit.Update(0);
    }

    /// <summary>Lab: bolt mode on this slot (its bolts shown, the camera on the part, at once).</summary>
    public void DemoBoltSlot(string slotId)
    {
        SetBoltSlot(slotId);
        for (int i = 0; i < 20; i++) workshop.Orbit.Update(0.25);
        // Lab: where the camera went and what it looks at, for fitting the zoom.
        var box = workshop.View?.SlotBounds(slotId);
        GD.Print($"lab: bolt mode on {slotId}: box {box?.Size} centre {box?.GetCenter()}, bolts' middle {workshop.View?.BoltCentre(slotId)}, camera at {workshop.Orbit.Distance:0.###} from {workshop.Orbit.Target}");
    }

    public void DemoBoltMode()
    {
        if (Game.WorkshopVehicle() is not { } v) return;
        var slot = CI.Car(v.ModelId).Slots.Where(s => s.Region.ToView() == Region && v.Slots[s.Id].Part is not null)
            .OrderByDescending(s => v.Slots[s.Id].Fasteners.Count).FirstOrDefault(s => v.Slots[s.Id].Fasteners.Count > 1);
        if (slot is null) return;
        SetBoltSlot(slot.Id);
        Game.UseFastener(slot.Id, 0, "ratchet");
    }

    public Task DemoJob() => GetJob();

    /// <summary>Check: the car's damaged body parts without bolts off and into the Parts Bin (as clicks would take them).</summary>
    public void DemoTakeOffDamaged()
    {
        if (Game.WorkshopVehicle() is not { } v) return;
        foreach (var (slotId, st) in v.Slots.ToList())
            if (st.Part is { Condition: < Condition.Green } && st.Fasteners.Count == 0 && CI.Slot(v.ModelId, slotId).Region == Core.Content.Region.Body)
                Run(Game.RemovePart(slotId));
    }

    /// <summary>Check: a click on a bolt of the part in bolt mode, as the Impact Wrench does it.</summary>
    public void DemoBoltClick(string slotId, int index) => BoltClick(slotId, index);

    /// <summary>Check: the Impact Wrench box's CANCEL.</summary>
    public void DemoCancelBolts() => CancelBolts();

    /// <summary>Check: the part pictures made so far, to files.</summary>
    public void DemoSaveThumbs(string dir) => thumbs.SaveAll(dir);

    /// <summary>Lab: the Catalog at this section, its first spread.</summary>
    public void DemoCatalogSection(string id)
    {
        catalogTab = id;
        catalogSpread = 0;
        Refresh();
    }

    /// <summary>Check: bolt mode is open.</summary>
    public bool DemoBoltOpen => boltSlot is not null;

    /// <summary>Check: the title of the dialog on top (null: none).</summary>
    public string? DemoDialogTitle => dialogs.TopTitle;

    /// <summary>Boxes the WorkShop's car came with still up (the car kept out of the view).</summary>
    public int DemoCarHeld => carHeld;

    /// <summary>Check: where a click (screen coordinates) buys the first part of the Catalog's spread that costs at most
    /// <paramref name="maxPrice"/>, with its name and price; null when there is none.</summary>
    public (Vector2 At, string Name, decimal Price)? DemoCatalogCard(decimal maxPrice)
    {
        if (Screen != Screen.Catalog || catalogTab == "decals") return null;
        var v = Game.WorkshopVehicle();
        var car = v is null ? null : CI.Car(v.ModelId);
        var parts = CatalogParts(car);
        const int perPage = 10;
        for (int page = 0; page < 2; page++)
        {
            var area = page == 0 ? L.LeftPage : L.RightPage;
            var shown = parts.Skip(catalogSpread * perPage * 2 + page * perPage).Take(perPage).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i].Price > maxPrice) continue;
                var r = Skinned
                    ? new Rect2(L.Orig.PartColumns[page * 2 + i % 2], L.Orig.PartTop + (i / 2) * L.Orig.PartPitch, L.Orig.PartCard.X, L.Orig.PartCard.Y)
                    : L.R(area.Position.X + 12 + (i % 2) * 117, area.Position.Y + 10 + (i / 2) * 63, 102, 48);
                return (r.GetCenter(), shown[i].Name, shown[i].Price);
            }
        }
        return null;
    }

    /// <summary>Check: where a click on the 3D view (screen coordinates) picks this slot's part, if anywhere near its middle.</summary>
    public Vector2? DemoSlotPoint(string slotId)
    {
        if (workshop.View is not { } view) return null;
        var world = view.SlotCenter(slotId);
        var cam = workshop.Camera;
        if (cam.IsPositionBehind(world)) return null;
        var p = cam.UnprojectPosition(world);
        Vector2[] offsets = [Vector2.Zero, new(6, 0), new(-6, 0), new(0, 6), new(0, -6), new(12, 0), new(-12, 0), new(0, 12), new(0, -12), new(12, 12), new(-12, -12), new(-12, 12), new(12, -12), new(20, 0), new(-20, 0)];
        foreach (var d in offsets)
            if (workshop.Pick(p + d)?.Pick is PartPick pp && pp.SlotId == slotId) return viewArea.Position + p + d;
        return null;
    }

    /// <summary>Check: the bolts of the part in bolt mode that a click would hit (screen coordinates), in or out as asked.</summary>
    public List<Vector2> DemoBoltPoints(string slotId, bool wantIn)
    {
        var list = new List<Vector2>();
        if (workshop.View is not { } view) return list;
        var cam = workshop.Camera;
        foreach (var b in view.Bolts(slotId))
        {
            if (b.In != wantIn || cam.IsPositionBehind(b.Global)) continue;
            var p = cam.UnprojectPosition(b.Global);
            if (workshop.Pick(p)?.Pick is FastenerPick fp && fp.SlotId == slotId) list.Add(viewArea.Position + p);
        }
        return list;
    }

    /// <summary>Check: the JunkYard's area on show.</summary>
    public Core.Content.Region DemoJunkArea => junkArea;

    /// <summary>Check: where each of the JunkYard's parts on show lies (in the scene).</summary>
    public Dictionary<string, Vector3> DemoJunkPlaces() => junkScene.Places();

    /// <summary>Check: the JunkYard's parts on show and where a click (screen coordinates) buys each.</summary>
    public List<(string Id, Vector2 At)> DemoJunkPoints() =>
        junkScene.ItemPoints().Select(x => (x.Id, viewArea.Position + x.At)).ToList();

    /// <summary>Check: the Car Lot's chosen car, where a click (screen coordinates) would bring it in.</summary>
    public Vector2? DemoLotCarPoint()
    {
        if (lotScene.LotChoice() is not { } c) return null;
        var p = c.At;
        Vector2[] offsets = [Vector2.Zero, new(0, 10), new(0, -10), new(10, 0), new(-10, 0), new(0, 20), new(15, 10), new(-15, 10)];
        foreach (var d in offsets)
            if (lotScene.PickParked(p + d) == c.Index) return viewArea.Position + p + d;
        return null;
    }

    /// <summary>Check: presses one of our own sign-in buttons by the start of its label.</summary>
    public bool DemoPressSignIn(string labelStart)
    {
        foreach (var c in signIn.GetChildren())
            if (c is OutlineButton b && b.Label.StartsWith(labelStart, StringComparison.OrdinalIgnoreCase))
            {
                b.EmitSignal(BaseButton.SignalName.Pressed);
                return true;
            }
        return false;
    }

    /// <summary>
    /// Check: does the job in the WorkShop through the rules, as a player would: takes off what has to come off (bolts
    /// and all), repairs or buys what the customer asked for, puts everything back and bolts it down. Stops when the job
    /// finishes. Returns what it could not do.
    /// </summary>
    public string DemoSolveJob()
    {
        if (Game.State.Job is not { } job || Game.WorkshopVehicle() is not { } v) return "no job";
        var need = job.Reqs.SelectMany(q => q.SlotIds).Distinct().ToList();
        var leaveOff = job.Reqs.Where(q => q.Type == JobReqType.Remove).SelectMany(q => q.SlotIds).ToHashSet();
        var closure = VehicleRules.RemovalClosure(CI, v.ModelId, need);
        var order = new List<string>();
        bool Present(string id) => v.Slots[id].Part is not null;
        bool progress = true;
        while (progress && closure.Any(Present))
        {
            progress = false;
            foreach (var id in closure.Where(Present).ToList())
            {
                if (Game.WorkshopVehicle() != v) return "";
                foreach (var b in CI.Slot(v.ModelId, id).BlockedBy)
                    if (CI.Slot(v.ModelId, b).Openable is not null && v.Slots[b].Part is not null && !v.Slots[b].Open) Game.ToggleOpen(b);
                if (v.Slots[id].Fasteners.Any(f => f)) Game.UseAllFasteners(id, "ratchet", remove: true);
                if (Game.WorkshopVehicle() != v) return "";
                if (Present(id) && Game.RemovePart(id).Ok)
                {
                    order.Add(id);
                    progress = true;
                }
            }
        }
        if (closure.Any(Present)) return "could not take off " + string.Join(", ", closure.Where(Present));
        // What the customer asked for: repaired, or new from the Catalog.
        var uids = new Dictionary<string, string>();
        foreach (var q in job.Reqs)
            foreach (var id in q.SlotIds)
            {
                if (q.Type == JobReqType.Remove) continue;
                var slot = CI.Slot(v.ModelId, id);
                var have = Game.State.Bin.FirstOrDefault(b => b.From == v.Id && CI.Part(b.Part.PartId).SlotType == slot.SlotType
                    && (q.Parts is null || q.Parts.Contains(b.Part.PartId)));
                if (have is not null && have.Part.Condition >= q.MinCondition) { uids[id] = have.Part.Uid; continue; }
                if (have is not null && have.Part.Condition > Condition.Black && Game.RepairItem(have.Part.Uid).Ok) { uids[id] = have.Part.Uid; continue; }
                var buy = q.Parts?.FirstOrDefault(p => CI.Part(p).SlotType == slot.SlotType) ?? slot.DefaultPart ?? CI.Pack.Parts.FirstOrDefault(p => p.SlotType == slot.SlotType)?.Id;
                if (buy is null) return $"nothing to buy for {id}";
                // The JunkYard first when it is cheaper (the pack jobs' fees assume it), the Catalog otherwise.
                var junk = Game.Junk.Where(j => CI.Part(j.Part.PartId).SlotType == slot.SlotType && (q.Parts is null || q.Parts.Contains(j.Part.PartId)))
                    .Select(j => (Item: j, Cost: Economy.JunkPrice(CI, j.Part) + (j.Part.Condition >= q.MinCondition ? 0 : Economy.RepairCost(CI, j.Part))))
                    .Where(x => x.Item.Part.Condition > Condition.Black || q.MinCondition <= Condition.Black)
                    .OrderBy(x => x.Cost).FirstOrDefault();
                if (junk.Item is not null && junk.Cost < CI.Part(buy).Price && Game.BuyJunk(junk.Item.Id).Ok)
                {
                    Game.LeaveJunkyard();
                    var got = Game.State.Bin.First(b => b.Part.Uid == junk.Item.Part.Uid);
                    if (got.Part.Condition < q.MinCondition && !Game.RepairItem(got.Part.Uid).Ok) return $"could not repair the junk {id}";
                    uids[id] = got.Part.Uid;
                    continue;
                }
                var r = Game.BuyPart(buy);
                if (!r.Ok) return $"could not buy {buy}: {r.Msg}";
                uids[id] = r.Data!;
            }
        // Back on: the parts that came off (the required ones by their new uids), in an order that fits.
        var toInstall = new List<string>();
        foreach (var id in Enumerable.Reverse(order)) if (!leaveOff.Contains(id) && !toInstall.Contains(id)) toInstall.Add(id);
        foreach (var id in uids.Keys) if (!toInstall.Contains(id)) toInstall.Add(id);
        string UidFor(string id) => uids.TryGetValue(id, out var u) ? u
            : Game.State.Bin.FirstOrDefault(b => b.From == v.Id && CI.Part(b.Part.PartId).SlotType == CI.Slot(v.ModelId, id).SlotType)?.Part.Uid ?? "";
        progress = true;
        while (progress && toInstall.Count > 0)
        {
            progress = false;
            foreach (var id in toInstall.ToList())
            {
                if (Game.WorkshopVehicle() != v) return "";
                var uid = UidFor(id);
                if (uid.Length == 0) return $"no part for {id}";
                if (!Game.InstallPart(id, uid).Ok) continue;
                toInstall.Remove(id);
                progress = true;
                if (Game.WorkshopVehicle() != v) return "";
                Game.UseAllFasteners(id, "ratchet", remove: false);
                if (Game.WorkshopVehicle() != v) return "";
            }
        }
        return toInstall.Count > 0 ? "could not put back " + string.Join(", ", toInstall) : "";
    }

    /// <summary>Replay: the original's saves brought over as they are, in the order given (the sheet's rows), with no
    /// questions; then the sign-in sheet.</summary>
    public void ReplayImport(IEnumerable<string> mekFiles)
    {
        foreach (var f in mekFiles)
        {
            var m = Core.Original.MekFile.Read(f);
            var (g, p) = Core.Original.MekImport.Import(Original!, CI, m);
            foreach (var line in p) GD.Print($"import {m.Name}: {line}");
            profiles.Add(g.State.Mechanic, g);
        }
        ShowSignIn();
    }

    /// <summary>Tour: Job Help (the Job Update box).</summary>
    public Task DemoJobHelp() => JobHelp();

    /// <summary>Tour: is a dialog up?</summary>
    public bool DemoDialogOpen => dialogs.IsOpen;

    /// <summary>Tour: put the WorkShop car in the Car Lot.</summary>
    public void DemoPutCarInLot() => Run(Game.PutCarInLot());

    /// <summary>Tour: the WorkShop's Go To Auction.</summary>
    public void DemoGoToAuction() => GoToAuction();

    /// <summary>Tour: the Car Lot's camera (bays along the lane), the car its figures are about (where it is in the
    /// view) and the marker under the view (on the screen).</summary>
    public (float Pos, (int Index, Vector2 At)? Choice, float? Marker) DemoLot =>
        (lotScene.LotPos, lotScene.LotChoice(), lotBar is { } b && IsInstanceValid(b) && b.MarkerX is { } m ? b.Position.X + m : null);

    /// <summary>Lab: the Car Lot's camera (its transform and field of view) and each parked car's transform, as text.</summary>
    public string DemoLotGeometry => lotScene.Geometry();

    /// <summary>Lab: the Auction's camera, as text.</summary>
    public string DemoAuctionGeometry => auctionScene.Geometry();

    /// <summary>Tour: the Car Lot's camera at this point of the row, the figures and the marker redrawn.</summary>
    public void DemoLotPos(float pos)
    {
        lotScene.SetLotPos(pos);
        Render();
    }

    /// <summary>Tour: click the car the Car Lot's figures are about.</summary>
    public void DemoLotClickChoice()
    {
        LotClick();
    }

    /// <summary>Tour: Body Paint on the BODY tab, a stripe sprayed over the view and one panel filled.</summary>
    public void DemoPaint()
    {
        if (Game.WorkshopVehicle() is not { } v) return;
        SetRegion(View.Body);
        SetTool("paint");
        var red = CI.Pack.Paints.FirstOrDefault(p => p.Name == "Red")?.Id ?? paintId;
        var yellow = CI.Pack.Paints.FirstOrDefault(p => p.Name == "Yellow")?.Id ?? paintId;
        var size = viewArea.Size;
        for (float x = size.X * 0.2f; x < size.X * 0.8f; x += 2)
            if (workshop.PaintPoint(new Vector2(x, size.Y * 0.55f)) is { } p)
                Game.Spray(p.Uv.X, p.Uv.Y, 5, red, (p.Cell.Position.X, p.Cell.Position.Y, p.Cell.End.X, p.Cell.End.Y));
        if (workshop.PaintPoint(new Vector2(size.X * 0.5f, size.Y * 0.35f)) is { } q)
            Game.PaintArea(q.Cell.Position.X, q.Cell.Position.Y, q.Cell.End.X, q.Cell.End.Y, yellow);
        paintId = red;
        Refresh();
    }

    /// <summary>Tour: opens the Decal Browser (the Body view's DECALS).</summary>
    public void DemoDecalBrowser() => _ = DecalBrowser();

    /// <summary>Tour: presses a button of the original's Decal Browser ("pick:&lt;decal&gt;", "first" for the first
    /// decal listed, "size:0" for BIGGER, "colour:#rrggbb", "back", "done", "cancel" ...).</summary>
    public void DemoBrowse(string what) =>
        browse?.Invoke(what == "first" && CI.Pack.Decals.Find(d => Game.DecalUses(d.Id) > 0) is { } d ? "pick:" + d.Id : what);

    /// <summary>Tour: answers the dialog on top with its first button.</summary>
    public void DemoCloseDialog() => dialogs.CloseTop();

    public void DemoConfirmDialog() => dialogs.ConfirmTop();

    /// <summary>Tour: does the first tutorial job (take the damaged panel off, repair it, put it back).</summary>
    public void DemoFinishJob()
    {
        if (Game.State.Job is not { } job || Game.WorkshopVehicle() is not { } v) return;
        SetRegion(View.Body);
        foreach (var (slotId, st) in v.Slots.ToList())
        {
            if (st.Part is not { Condition: < Condition.Green } part) continue;
            if (CI.Slot(v.ModelId, slotId).Region != Core.Content.Region.Body) continue;
            Game.UseAllFasteners(slotId, "ratchet", remove: true);
            if (!Game.RemovePart(slotId).Ok) continue;
            Game.RepairItem(part.Uid);
            Game.InstallPart(slotId, part.Uid);
            Game.UseAllFasteners(slotId, "ratchet", remove: false);
        }
    }

    /// <summary>Tour: does a job whose parts come off and go on straight away (repairs the damaged ones,
    /// buys the missing ones).</summary>
    public void DemoFixJob()
    {
        if (Game.State.Job is not { } job || Game.WorkshopVehicle() is not { } v) return;
        foreach (var slotId in job.Reqs.SelectMany(q => q.SlotIds).ToList())
        {
            if (Game.WorkshopVehicle() is null) return;
            SetRegion(CI.Slot(v.ModelId, slotId).Region.ToView());
            if (v.Slots[slotId].Part is { } part)
            {
                Game.UseAllFasteners(slotId, "ratchet", remove: true);
                if (!Game.RemovePart(slotId).Ok) continue;
                Game.RepairItem(part.Uid);
                Game.InstallPart(slotId, part.Uid);
            }
            else if ((CI.Slot(v.ModelId, slotId).DefaultPart ?? CI.Pack.Parts.FirstOrDefault(p => p.SlotType == CI.Slot(v.ModelId, slotId).SlotType)?.Id) is { } buy
                     && Game.BuyPart(buy) is { Ok: true } bought)
                Game.InstallPart(slotId, bought.Data!);
            if (Game.WorkshopVehicle() is not null) Game.UseAllFasteners(slotId, "ratchet", remove: false);
        }
    }

    void ExitToSignIn()
    {
        if (Screen == Screen.Junkyard) LeaveJunkyard();
        SaveNow();
        ShowSignIn();
    }
}
