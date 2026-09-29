using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenGG.Assets;
using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using OpenGG.Ui;
using OpenGG.View3D;

namespace OpenGG;

public enum Screen { SignIn, Workshop, Catalog, Junkyard, Auction, Lot }

public enum Brush { Small, Medium, Large, Panel }

/// <summary>
/// Wires the simulation, the 3D scenes and the UI together. The UI is laid out like the original:
/// a 640 × 480 screen (scaled to the window), one screen at a time, positions in <see cref="L"/>.
/// </summary>
public partial class App : Control
{
    static readonly string[] Titles = ["", "Workshop", "Catalog", "JunkYard", "Auction", "CarLot"];

    public ContentIndex CI { get; }
    public AssetStore Assets { get; }
    public AudioBank Audio { get; } = new() { Name = "Audio" };
    public Game Game { get; private set; }
    public string PackId { get; }
    /// <summary>Main's hook to reload the content (set by Main).</summary>
    public Action? Reboot { get; init; }
    /// <summary>Shown on the sign-in screen (e.g. the original game could not be read).</summary>
    public string? StartupNote { get; init; }
    /// <summary>The original game whose content is in use (null with the placeholder content).</summary>
    public Core.Original.OriginalGame? Original { get; init; }
    readonly Profiles profiles;
    ProfileMeta? meta;
    Thumbnails thumbs = null!;

    // ---- UI state ----------------------------------------------------------------------------
    public Screen Screen { get; private set; } = Screen.SignIn;
    public View Region { get; private set; } = View.Complete;
    string tool = "ratchet";
    bool xray;
    string paintId;
    Brush brush = Brush.Panel;
    string? decalId;
    string? decalColor;
    float decalSize = 0.45f;
    float decalAngle;
    bool decalFlipX, decalFlipY, placingDecal;
    string catalogTab = "body";
    int catalogSpread;
    /// <summary>Measured: each section of the Catalog opens at the page it was left on.</summary>
    readonly Dictionary<string, int> catalogSpreads = [];
    /// <summary>The JunkYard's Purchase Bin page.</summary>
    int purchasePage;
    int binPage;
    int binCount;
    /// <summary>The Car Lot: ◄ (+1) or ► (−1) held down on the screen, the car its figures are about, its marker.</summary>
    int lotHeld;
    /// <summary>The JunkYard's ◄ (−1) or ► (1) held down on the screen.</summary>
    int junkHeld;
    int lotChoice = -1;
    LotBar? lotBar;
    /// <summary>Skill Advance and Available Cars are up after a job, or Car Complete as you sign in: the WorkShop behind
    /// them is blank.</summary>
    bool advancing;
    /// <summary>The profile is coming up, or a car is coming into the WorkShop: a Car Complete it brings comes over a blank
    /// WorkShop.</summary>
    bool signingIn;
    /// <summary>Boxes that came with the car as you signed in or brought it in are still to be closed: the car is not in
    /// the view yet, and the commands are empty (measured: a grey view under them; the car comes in after the last OK).</summary>
    int carHeld;
    Region junkArea = Core.Content.Region.Engine;
    int junkPage;
    string? boltSlot;
    string junkHint = "";
    string? hoverLabel;

    // ---- nodes -----------------------------------------------------------------------------------
    Control gameRoot = null!, signIn = null!, workshopUi = null!, screenUi = null!;
    /// <summary>With a skin: the screen's own picture, and the code-drawn furniture it replaces.</summary>
    TextureRect backdrop = null!;
    Control[] chrome = [];
    /// <summary>With a skin: a picture behind the 3D view (the sky) and one in front of it (the Auction's
    /// stage and chairs are part of its screen).</summary>
    TextureRect sky = null!, front = null!;
    Control viewArea = null!;
    SubViewportContainer viewContainer = null!;
    SubViewport subViewport = null!;
    ViewInput viewInput = null!;
    Label mechanicName = null!, partName = null!;
    EmbossLabel title = null!;
    MoneyBox money = null!;
    readonly TabPlate[] tabs = new TabPlate[4];
    AssembledTag assembledTag = null!;
    ArrowKeysHint viewHint = null!;
    BoltLabels boltLabels = null!;
    /// <summary>The job that just finished, while its car is on show and the thanks come up.</summary>
    Job? finishing;
    /// <summary>The model of the finished job's car (its Parts Bin stays up meanwhile).</summary>
    string? finishingModel;
    /// <summary>The finished job's car as the job left it (it is no longer in the game's state).</summary>
    VehicleState? finishingCar;
    /// <summary>Job Complete! has come up: the Job Help panel is gone (measured), and the view stays as it is.</summary>
    bool jobCompleteUp;
    Control toolLayer = null!, cmdLayer = null!, binLayer = null!, boltLayer = null!, carryLayer = null!;
    TextureRect photo = null!;
    DialogLayer dialogs = null!;
    WorkshopScene workshop = null!;
    ShowroomScene auctionScene = null!, lotScene = null!;
    JunkyardScene junkScene = null!;
    StageScene? current;

    bool refreshQueued, renderWaits;
    double saveIn = -1, autosave = 30, auctionRender;

    public App(ContentIndex ci, AssetStore assets, string packId)
    {
        CI = ci;
        Assets = assets;
        PackId = packId;
        profiles = new Profiles(packId);
        Game = Game.Create(ci, "Nobody", 1);
        paintId = ci.Pack.Paints.FirstOrDefault()?.Id ?? "";
        decalId = ci.Pack.Decals.FirstOrDefault()?.Id;
        Name = "App";
    }

    /// <summary>With a skin: its pointer, scaled like the screen.</summary>
    void SetCursor(string id)
    {
        cursorWanted = id;
        if (!UiSkin.Active || id == cursor) return;
        if (id == "none")
        {
            // Measured: aiming a decal, the original shows the decal where the pointer is, and no pointer.
            var blank = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
            Input.SetCustomMouseCursor(blank, Input.CursorShape.Arrow);
            Input.SetCustomMouseCursor(blank, Input.CursorShape.PointingHand);
            cursor = id;
            return;
        }
        if (UiSkin.SetCursor(id, ScreenMode.Scale)) cursor = id;
    }

    string? cursor;
    string cursorWanted = "pointer";

    /// <summary>The window was resized, or switched to or from the full screen: the pointer at the new size, the 3D
    /// view's pixels as sharp as the scale allows, the sign-in sheet's buttons saying what is in use.</summary>
    void ScreenChanged()
    {
        cursor = null;
        SetCursor(cursorWanted);
        // The original's look is pixel art, drawn with its pixels kept square; the 3D view (at the original's 380 x 257)
        // too, at whole multiples; between them it is smoothed rather than drawn with pixels of two sizes.
        float scale = ScreenMode.Scale;
        if (viewContainer is not null && !ModernLook.On)
            viewContainer.TextureFilter = UiSkin.Active && Mathf.Abs(scale - Mathf.Round(scale)) > 0.01f ? TextureFilterEnum.Linear : TextureFilterEnum.ParentNode;
        if (viewArea is not null) UpdateViewResolution();
        UpdateScreenButtons();
    }

    /// <summary>Over the car in the WorkShop the pointer is the chosen tool (measured: a part being carried keeps the hand).</summary>
    string ToolCursor() => Screen != Screen.Workshop || Game.WorkshopVehicle() is null || carry is not null ? "pointer" : CurrentTool.Action switch
    {
        ToolAction.Fasten => "wrench",
        ToolAction.Paint => placingDecal && StampsDecals ? "none" : "paint",
        ToolAction.Camera => "camera",
        _ => "pointer",
    };

    public override void _Ready()
    {
        // OpenGG's own look (no skin): the modern studio; its placeholder models restyled as they load.
        ModernLook.On = !UiSkin.Active;
        if (ModernLook.On)
        {
            Assets.Styler = ModernLook.Style;
            ModernLook.Setup();
        }
        Theme = Look.Build();
        SetCursor("pointer");
        // A skin with a button sound: every button clicks, as in the original.
        if (UiSkin.HasSound("snd.button")) DrawnButton.AnyPressed = () => Audio.Play("snd.button");
        Position = Vector2.Zero;
        Size = new Vector2(640, 480);
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        AddChild(Audio);
        Audio.Assets = Assets;
        thumbs = new Thumbnails(Assets, ModernLook.On ? 112 * ModernLook.DetailScale : 112) { Name = "Thumbnails" };
        AddChild(thumbs);
        BuildLayout();
        ScreenMode.Changed += ScreenChanged;
        GetTree().Root.SizeChanged += ScreenChanged;
        ScreenChanged();
        workshop = new WorkshopScene(Assets, Game);
        auctionScene = new ShowroomScene(Assets, Game, ShowroomScene.Kind.Auction);
        lotScene = new ShowroomScene(Assets, Game, ShowroomScene.Kind.Lot);
        junkScene = new JunkyardScene(Assets, Game);
        ShowScene(workshop);
        ShowSignIn();
    }

    static T Put<T>(Control parent, T node, Rect2 r) where T : Control
    {
        node.Position = r.Position;
        node.Size = r.Size;
        parent.AddChild(node);
        return node;
    }

    static Label Text(Control parent, Rect2 r, string text, int size, Color color, Font? font = null, HorizontalAlignment align = HorizontalAlignment.Left, bool shadow = false, bool wrap = false)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = !wrap,
        };
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontOverride("font", font ?? Look.CondensedBold);
        l.AddThemeConstantOverride("line_spacing", -2);
        if (shadow)
        {
            l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            l.AddThemeConstantOverride("shadow_offset_x", 1);
            l.AddThemeConstantOverride("shadow_offset_y", 1);
        }
        return Put(parent, l, r);
    }

    void BuildLayout()
    {
        gameRoot = Put(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        var metal = Put(gameRoot, new MetalRect(), L.R(0, 0, 640, 480));
        backdrop = Put(gameRoot, new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false }, L.R(0, 0, 640, 480));

        // Top bar: our name where the original has its logo, the mechanic, the screen title, the money.
        var logo = Put(gameRoot, new Logo(), L.Logo);
        var caption = Text(gameRoot, L.MechanicCaption, "Mechanic:", 10, Look.Muted, align: HorizontalAlignment.Center);
        mechanicName = Text(gameRoot, L.MechanicName, "", 14, Look.White, Look.CondensedBold, HorizontalAlignment.Center);
        title = Put(gameRoot, new EmbossLabel { Font = Look.Impact, FontSize = 32, Align = HorizontalAlignment.Right, Face = Look.TitleGrey, Light = Look.TitleLight }, L.Title);
        money = Put(gameRoot, new MoneyBox(), L.Money);

        // The 3D view, moved around per screen.
        viewArea = Put(gameRoot, new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Pass }, L.View);
        sky = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        viewArea.AddChild(sky);
        viewContainer = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        viewContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewArea.AddChild(viewContainer);
        subViewport = new SubViewport { Msaa3D = ModernLook.On ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Msaa2X, HandleInputLocally = false, AudioListenerEnable3D = false };
        viewContainer.AddChild(subViewport);
        viewInput = new ViewInput();
        viewInput.GuiInput += OnViewInput;
        viewInput.MouseEntered += () => SetCursor(ToolCursor());
        viewInput.MouseExited += () =>
        {
            SetCursor("pointer");
            ClearHover();
            workshop.View?.Hover(null);
            workshop.View?.LightRegion(null);
            workshop.View?.HideDecalPreview();
            decalAim?.Hide();
            // Measured: the JunkYard keeps the part lit and named when the pointer leaves the view straight from it.
        };
        viewArea.AddChild(viewInput);
        viewHint = Put(viewArea, new ArrowKeysHint(), L.ViewHint);
        decalAim = new DecalAim();
        viewArea.AddChild(decalAim);
        boltLabels = new BoltLabels();
        boltLabels.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewArea.AddChild(boltLabels);
        assembledTag = Put(viewArea, new AssembledTag(), L.Assembled);
        photo = new TextureRect { Visible = false, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        photo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewArea.AddChild(photo);

        front = Put(gameRoot, new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false }, L.R(0, 0, 640, 480));

        // WorkShop furniture that never moves.
        workshopUi = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        Put(workshopUi, new TabStrip(), L.TabStrip);
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            // Measured: a tab goes to its view as it is pressed, not as it is let go.
            tabs[i] = Put(workshopUi, new TabPlate { Quiet = true, ActionMode = BaseButton.ActionModeEnum.Press }, L.Tabs[i]);
            tabs[i].Pressed += () => ClickTab(TabList[k].View);
        }
        partName = Text(workshopUi, L.PartName, "", 13, Look.White, shadow: true);
        toolLayer = Put(workshopUi, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        var divider = Put(workshopUi, new DividerBar(), L.Divider);
        chrome = [metal, logo, caption, title, divider];
        if (UiSkin.Active)
        {
            // The original's pictures are pixel art for 640 x 480: keep their pixels sharp when scaled up.
            TextureFilter = TextureFilterEnum.Nearest;
            if (UiSkin.Font("text") is { } nameFont)
            {
                mechanicName.AddThemeFontOverride("font", nameFont);
                mechanicName.AddThemeFontSizeOverride("font_size", UiSkin.FontSize(nameFont));
                mechanicName.Position = L.Orig.MechanicName.Position;
                mechanicName.Size = L.Orig.MechanicName.Size;
            }
            if (UiSkin.Font("text") is { } text)
            {
                partName.AddThemeFontOverride("font", text);
                partName.AddThemeFontSizeOverride("font_size", UiSkin.FontSize(text));
                partName.RemoveThemeColorOverride("font_shadow_color");
                partName.Position = L.Orig.PartName.Position;
                partName.Size = L.Orig.PartName.Size;
                partName.VerticalAlignment = VerticalAlignment.Top;
            }
            assembledTag.Position = L.View.Position * 0 + L.Orig.Assembled;
            assembledTag.Size = new Vector2(92, 22);
            viewHint.Position = L.Orig.UseKeys;
            viewHint.Size = new Vector2(228, 28);
        }
        cmdLayer = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        binLayer = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        screenUi = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        boltLayer = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        // A part carried from the Parts Bin goes over everything.
        carryLayer = Put(gameRoot, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));

        signIn = Put(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.R(0, 0, 640, 480));
        dialogs = Put(this, new DialogLayer { Name = "Dialogs", Sound = id => Audio.Play(id) }, L.R(0, 0, 640, 480));
    }

    /// <summary>Moves the 3D view to where this screen has it.</summary>
    void PlaceView(Rect2 r)
    {
        viewArea.Visible = r.Size.X > 0;
        if (viewArea.Position == r.Position && viewArea.Size == r.Size) return;
        viewArea.Position = r.Position;
        viewArea.Size = r.Size;
        UpdateViewResolution();
    }

    /// <summary>
    /// Our own look draws the 3D at the screen's own resolution: the view keeps its place and size on the 640 x 480 screen
    /// (all that goes by it, picking included, is unchanged) while its picture has one pixel per screen pixel. The
    /// original's look keeps the original's resolution (380 x 257 in the WorkShop), scaled with the rest.
    /// </summary>
    void UpdateViewResolution()
    {
        if (!ModernLook.On || subViewport is null) return;
        var logical = (Vector2I)viewArea.Size.Round();
        if (logical.X <= 0 || logical.Y <= 0) return;
        float k = Args.Get("view-scale") is { } forced && float.TryParse(forced, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f
            : Mathf.Max(1f, ScreenMode.Scale);
        var physical = (Vector2I)(viewArea.Size * k).Round();
        viewContainer.Stretch = false;
        subViewport.Size2DOverrideStretch = true;
        subViewport.Size2DOverride = logical;
        subViewport.Size = physical;
        viewContainer.Scale = new Vector2(logical.X / (float)physical.X, logical.Y / (float)physical.Y);
        viewContainer.TextureFilter = TextureFilterEnum.Linear;
    }

    /// <summary>Tests: the 3D view's own picture, at the resolution it is drawn at.</summary>
    public Image ViewImage() => subViewport.GetTexture().GetImage();

    // ---- scenes ------------------------------------------------------------------------------------

    void ShowScene(StageScene s)
    {
        if (current == s) return;
        if (current is not null) subViewport.RemoveChild(current);
        current = s;
        subViewport.AddChild(s);
        s.Camera.MakeCurrent();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    public void Refresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        CallDeferred(MethodName.Render);
    }

    /// <summary>Shows a failed command the way the original does: in a dialog. <paramref name="installing"/>: the
    /// command was putting a part on (a part in the way then has other words).</summary>
    public bool Run<T>(Result<T> r, bool installing = false)
    {
        if (r.Ok) return true;
        Audio.Play("snd.error", 0.5f);
        var (head, text) = Explain(r.Code, r.Msg ?? "That did not work.", r.Slots, installing);
        QueueDialog(() => dialogs.Alert(head, text));
        return false;
    }

    /// <summary>A failed command's title and words; a skin can have its own for those the original has.</summary>
    (string Head, string Text) Explain(string code, string msg, IReadOnlyList<string>? slots, bool installing)
    {
        var v = Game.WorkshopVehicle();
        string Names() => v is null || slots is null ? "" : string.Join(", ", slots.Where(id => CI.HasSlot(v.ModelId, id)).Select(id => CI.Slot(v.ModelId, id).Name));
        string OnIt() => v is not null && slots?.FirstOrDefault() is { } id && v.Slots.GetValueOrDefault(id)?.Part is { } p ? CI.Part(p.PartId).Name : Names();
        // Measured: "until the Front Axle is attached", "until the HotRod Dual Carbs are attached".
        string Is() => slots is { Count: > 1 } || Names().EndsWith('s') ? "are" : "is";
        var assembly = Words.Get("assembly.title");
        return code switch
        {
            "cash" => (Words.Get("cash.title"), Words.Or("cash", msg)),
            "broken" => (Words.Get("repair.title"), Words.Or("repair.broken", msg)),
            "good" => (Words.Get("repair.title"), Words.Or("repair.good", msg)),
            "lot_full" => (Words.Get("lotfull.title"), Words.Or("lotfull", msg)),
            "children" => (assembly, Words.Or("remove.first", msg, ("parts", Names()), ("is", Is()))),
            "blocked" when !installing => (assembly, Words.Or("remove.first", msg, ("parts", Names()), ("is", Is()))),
            "no_parent" => (assembly, Words.Or("attach.first", msg, ("parts", Names()), ("is", Is()))),
            "worn_out" => (assembly, Words.Or("attach.worn", msg)),
            "occupied" => (assembly, Words.Or("attach.replaces", msg, ("part", OnIt()))),
            "blocked" or "fastened" or "closed" or "reach" or "wrong_slot" or "loose" or "after" => (assembly, msg),
            _ => (Words.Get("error.title"), msg),
        };
    }

    /// <summary>Messages the player must see go in a dialog (the original has no pop-up notes).</summary>
    public void Toast(string msg, LogKind kind = LogKind.Info)
    {
        if (kind == LogKind.Bad) QueueDialog(() => dialogs.Alert("Error", msg));
    }

    /// <summary>A picture of the content's, drawn at <paramref name="size"/>; our own look makes it bigger, sharp on the
    /// screen it is shown on.</summary>
    Texture2D? Icon(string id, int size = 48) => Assets.Icon(id, ModernLook.On ? size * ModernLook.DetailScale : size);

    // ---- dialogs in turn ---------------------------------------------------------------------------

    Task dialogQueue = Task.CompletedTask;

    /// <summary>Shows a dialog after the ones already waiting (Job Complete, then Skill Advance...).</summary>
    void QueueDialog(Func<Task> show) => dialogQueue = Chain(dialogQueue, show);

    static async Task Chain(Task before, Func<Task> show)
    {
        try { await before; } catch (Exception e) { GD.PushError(e.ToString()); }
        await show();
    }

    /// <summary>
    /// Measured: "Skill Advance" comes up after the job's OK, over a blank WorkShop (no tabs, no commands); from Novice
    /// up it is followed by "Available Cars" (more cars at the Auction, maybe new jobs). Then the WorkShop comes back.
    /// One level at a time: cash past two bars brings the pair of boxes twice (as you sign in: over the WorkShop's tabs,
    /// no car and no commands).
    /// </summary>
    async Task SkillAdvance(SkillEvent s)
    {
        // The tutorial's tips after the first job came up over the WorkShop as it was (the finished car still there);
        // under Skill Advance after a job the view is empty. Earned otherwise (money from a part scrapped, measured),
        // both boxes come over the WorkShop as it is.
        if (s.AfterJob)
        {
            advancing = true;
            workshop.EndShowcase();
            Refresh();
        }
        await dialogs.Alert("Skill Advance", Words.Get("skill.up", ("from", SkillWord(s.Level - 1, s.From)), ("to", SkillWord(s.Level, s.Name))));
        // Our own pack (one car model in the beta) has no cars to announce: then no Available Cars.
        if (s.Level >= 2 && (Original is not null || CI.Pack.Cars.Any(c => c.MinSkill == s.Level)))
            await dialogs.Alert("Available Cars", Words.Get("skill.cars"));
        if (s.AfterJob)
        {
            advancing = false;
            Refresh();
        }
    }

    /// <summary>
    /// Measured: "Car Complete" comes up at once as a car of yours is first in top condition, to the job's fanfare, over
    /// the WorkShop as it is (a blank one as you sign in or bring the car in from the Car Lot), saying how long the car
    /// took and what it cost.
    /// </summary>
    async Task CarComplete(CarCompleteEvent c, bool blank)
    {
        if (blank)
        {
            advancing = true;
            Refresh();
        }
        await dialogs.Alert("Car Complete", Words.Get("car.complete", ("time", CarCompleteWords.Took(c.Seconds)), ("cost", U.Money(c.Spent))));
        if (blank)
        {
            advancing = false;
            Refresh();
        }
    }


    /// <summary>Keeps the WorkShop's car out of the view while a box it came with is up (see <see cref="carHeld"/>).</summary>
    void HoldCar()
    {
        if (carHeld++ == 0) workshop.SetVehicle(null);
        Refresh();
    }

    /// <summary>A box the car came with is closed: after the last one the car comes into the view.</summary>
    void ReleaseCar(Game earned)
    {
        if (Game != earned || carHeld == 0 || --carHeld > 0) return;
        workshop.SetVehicle(Game.State.Workshop);
        Refresh();
    }

    /// <summary>A skill level's name: the skin's for it if it has one ("skill.name.4"), else the content's.</summary>
    static string SkillWord(int level, string name) => Words.Or($"skill.name.{level}", name);

    /// <summary>Go To Auction. Measured: refused with the Car Lot full.</summary>
    void GoToAuction()
    {
        bool had = Game.State.Auction is not null;
        if (!Run(Game.GoToAuction())) return;
        auctionFresh = !had;
        Go(Screen.Auction);
    }

    /// <summary>The Auction's car was made on the way there: it drives on as the screen comes up.</summary>
    bool auctionFresh;

    /// <summary>The hammer fell on a car you bid on or sold.</summary>
    async Task AuctionClosed(AuctionState a)
    {
        var r = a.Result;
        // Measured: winning, and your car sold, ring the till as the box comes up.
        if (r?.Buyer == Bidder.Player || (a.Mode == AuctionMode.Sell && r?.Buyer == Bidder.Rival)) Audio.Play("snd.cash");
        if (a.Mode == AuctionMode.Buy)
        {
            if (r?.Buyer != Bidder.Player) return; // somebody else got it: the next car is already up
            await dialogs.Alert("Winning Bid!", Words.Get("auction.won"));
            Game.LeaveAuction();
            Go(Screen.Workshop);
            return;
        }
        // Measured: the price in whole dollars (bids always are), what you made or lost to the cent.
        string price = r is null ? "" : U.Money(r.Price).Replace(".00", "");
        if (r?.Buyer == Bidder.Rival)
        {
            var p = r.Profit ?? 0;
            await dialogs.Alert("Sold!", Words.Get(p >= 0 ? "auction.sold.profit" : "auction.sold.loss", ("price", price), ("amount", U.Money(Math.Abs(p)))));
        }
        // Measured: outbidding everybody for your own car calls the sale off; all it costs is the service fee.
        else if (r?.Buyer == Bidder.Player) await dialogs.Alert(Words.Get("auction.cancelled.title"), Words.Get("auction.cancelled", ("fee", U.Money(r.Fee ?? 0).Replace(".00", ""))));
        else await dialogs.Alert("No Sale", Words.Get("auction.nosale"));
        Game.LeaveAuction();
        Go(Screen.Workshop);
    }

    public void Go(Screen s)
    {
        if (Game.InJobsMode && s is Screen.Lot or Screen.Auction) return;
        // Measured: back in the WorkShop from another screen the view shown sounds its tab (COMPLETE's excepted).
        if (s == Screen.Workshop && Screen is not (Screen.Workshop or Screen.SignIn) && Region != View.Complete && Game.WorkshopVehicle() is not null)
            Audio.Play(TabSound(Region));
        if (Screen == Screen.Junkyard && s != Screen.Junkyard) LeaveJunkyard();
        if (Screen == Screen.Catalog) catalogSpreads[catalogTab] = catalogSpread;
        if (Screen == Screen.Catalog && turningCard is not null) StopCardTurn(null, turningCard);
        if (s == Screen.Junkyard && Screen != Screen.Junkyard) purchasePage = 0;
        Screen = s;
        ClearHover();
        placingDecal = false;
        if (s is not (Screen.Lot or Screen.Auction or Screen.Junkyard)) OrigLook.UseWorkshop(); // those light cars and parts their own way
        switch (s)
        {
            case Screen.Auction:
                ShowScene(auctionScene);
                auctionScene.LightCars();
                bool fresh = auctionFresh || Game.State.Auction is null;
                auctionFresh = false;
                if (Game.State.Auction is null) Game.NextAuctionCar();
                shownFigures = null;
                // The new car's event has put it on the stage already: it drives on instead.
                if (fresh) auctionScene.Show(null);
                auctionScene.Show(Game.State.Auction?.VehicleId, driveOn: fresh, entering: fresh);
                break;
            case Screen.Lot:
                ShowScene(lotScene);
                lotScene.ShowLot(Game.State.Lot);
                lotScene.OpenLot();
                lotScene.LightCars();
                lotHeld = 0;
                LotAmbience();
                break;
            case Screen.Junkyard:
                // Measured: the yard opens at the area of the tab shown (BODY from BODY; ENGINE otherwise, as far as
                // seen), each area at the start of its row, nothing lit; its shelf changes by itself as it comes up, and
                // its rows are laid out afresh (the lab's shelf is shown as it is set).
                if (junkAsIs) Game.OpenJunkyard();
                else Game.EnterJunkyard();
                junkScene.NewVisit();
                junkArea = Region switch { View.Body => Core.Content.Region.Body, View.RunningGear => Core.Content.Region.RunningGear, _ => Core.Content.Region.Engine };
                junkKey = "";
                junkHeld = 0;
                junkPans.Clear();
                junkHint = "";
                ShowScene(junkScene);
                junkScene.Hover(null);
                junkScene.ResetPan();
                break;
            default:
                ShowScene(workshop);
                // The Catalog opens at the section of the tab you were on (BODY from COMPLETE), measured, with a
                // sound of its own.
                if (s == Screen.Catalog)
                {
                    Audio.Play("snd.catalog");
                    catalogTab = Region switch { View.Engine => "engine", View.RunningGear => "running_gear", _ => "body" };
                    catalogSpread = catalogSpreads.GetValueOrDefault(catalogTab);
                }
                break;
        }
        if (s != Screen.Auction && Game.State.Auction is { Closed: true }) Game.LeaveAuction();
        if (s != Screen.Auction && Game.State.Auction is { Mode: AuctionMode.Buy, Closed: false }) Game.LeaveAuction();
        Refresh();
    }

    /// <summary>The Car Lot's sounds have been heard in this run of the game.</summary>
    static bool lotAmbienceDone;
    int lotVisit;

    /// <summary>
    /// Measured in two runs of the original: on the first visit to the Car Lot a crow calls 21.2 s in and a car drives
    /// off 31.2 s in; then never again in that run (four more visits, one of four minutes, and a new sign-in).
    /// </summary>
    void LotAmbience()
    {
        int visit = ++lotVisit;
        if (lotAmbienceDone) return;
        lotAmbienceDone = true;
        void At(double s, string id) => GetTree().CreateTimer(s).Timeout += () =>
        {
            if (Screen == Screen.Lot && lotVisit == visit) Audio.Play(id);
        };
        At(21.2, "snd.lot.crow");
        At(31.2, "snd.lot.car");
    }

    /// <summary>What you bought at the JunkYard goes to the Parts Bin.</summary>
    void LeaveJunkyard()
    {
        if (Game.LeaveJunkyard() > 0) binPage = int.MaxValue;
    }

    // ---- render ----------------------------------------------------------------------------------

    void Render()
    {
        refreshQueued = false;
        // The Auction is drawn anew as its figures change, every frame: a button pressed there would be gone before it
        // is let go, and the click lost. While the mouse button is down there, the screen waits (see _Process).
        if (Screen == Screen.Auction && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            renderWaits = true;
            return;
        }
        renderWaits = false;
        bool inGame = Screen != Screen.SignIn;
        signIn.Visible = !inGame;
        gameRoot.Visible = inGame;
        // Measured: the sign-in sheet has its music, the credits theirs; the game's screens are quiet.
        Audio.PlayMusic(inGame ? null : inCredits ? "music.credits" : "music.signin");
        SyncCrowd();
        if (!inGame) return;
        RenderTopBar();
        bool ws = Screen == Screen.Workshop;
        workshopUi.Visible = ws;
        viewHint.Visible = ws && carHeld == 0 && (Game.WorkshopVehicle() is not null || finishing is not null);
        assembledTag.Visible = false;
        U.Clear(screenUi);
        U.Clear(cmdLayer);
        U.Clear(binLayer);
        U.Clear(boltLayer);
        switch (Screen)
        {
            case Screen.Workshop:
                PlaceView(L.View);
                RenderBackdrop();
                RenderWorkshop();
                break;
            case Screen.Catalog:
                PlaceView(L.R(0, 0, 0, 0));
                RenderBackdrop();
                RenderCatalog();
                break;
            case Screen.Junkyard:
                PlaceView(L.JunkView);
                RenderBackdrop();
                RenderJunkyard();
                break;
            case Screen.Auction:
                // As the original: the 3D fills the whole view; with a skin the screen's stage front and chairs are drawn
                // over its lower part.
                PlaceView(L.AuctionView);
                RenderBackdrop();
                RenderAuction();
                break;
            case Screen.Lot:
                PlaceView(L.LotView);
                RenderBackdrop();
                lotScene.ShowLot(Game.State.Lot);
                RenderLot();
                break;
        }
    }

    /// <summary>The skin's name for the screen that is up.</summary>
    string SkinScreenId => Screen switch
    {
        Screen.Workshop => "workshop",
        Screen.Catalog => catalogTab == "decals" ? "catalog.decals" : "catalog",
        Screen.Junkyard => "junkyard",
        Screen.Auction => "auction",
        Screen.Lot => "lot",
        _ => "signin",
    };

    /// <summary>Shows the skin's picture of this screen (and hides what it replaces), if there is one.</summary>
    bool RenderBackdrop()
    {
        var tex = UiSkin.Screen(SkinScreenId)?.Base;
        backdrop.Texture = tex;
        backdrop.Visible = tex is not null;
        foreach (var c in chrome) c.Visible = tex is null;
        // The sky behind the 3D scene, over the part of the view the skin says; the 3D is then see-through.
        var skyPic = tex is null ? null : UiSkin.Picture(SkinScreenId + ".sky");
        sky.Texture = skyPic;
        sky.Visible = skyPic is not null;
        if (skyPic is not null)
        {
            // Measured: each screen draws the clouds its own way (the Auction's mirrored).
            var at = Screen switch
            {
                Screen.Auction => L.Orig.AuctionSky,
                Screen.Lot => new Rect2(Vector2.Zero, L.Orig.LotSky),
                _ => new Rect2(Vector2.Zero, viewArea.Size),
            };
            sky.Position = at.Position;
            sky.Size = at.Size;
            sky.FlipH = Screen == Screen.Auction;
        }
        subViewport.TransparentBg = skyPic is not null;
        var frontPic = tex is null ? null : UiSkin.Picture(SkinScreenId + ".front");
        front.Texture = frontPic;
        front.Visible = frontPic is not null;
        return tex is not null;
    }

    /// <summary>True when the skin draws this screen: controls are cut from its pictures.</summary>
    bool Skinned => backdrop.Visible;

    /// <summary>A tooltip of ours; the original's look has none.</summary>
    string Tip(string text) => UiSkin.Active ? "" : text;

    void RenderTopBar()
    {
        // Measured: a finished job's budget stays in the money box until the WorkShop is cleared.
        var job = Game.State.Job ?? finishing;
        mechanicName.Text = Game.State.Mechanic.ToLowerInvariant();
        title.Text = Titles[(int)Screen];
        title.QueueRedraw();
        var amount = job?.Budget ?? Game.State.Cash;
        money.Set(U.Money(amount), amount < 0 ? Color.FromHtml("#d84a3a") : Look.MoneyGreen);
        money.TooltipText = Tip(job is not null ? "What is left of the job's budget" : "Your cash");
    }

    // ---- game events --------------------------------------------------------------------------------

    void OnGameEvent(GameEvent e)
    {
        switch (e)
        {
            case FastenerEvent f:
                // Measured: a bolt coming out and one going in sound different.
                if (quietBolts) break;
                Audio.Play(UiSkin.HasSound("snd.bolt_out") ? (f.Removed ? "snd.bolt_out" : "snd.bolt_in") : CI.Tool(f.ToolId).Sound, 0.7f);
                break;
            case PartEvent p:
                if (!p.Undo) Audio.Play(p.Removed ? "snd.part_off" : "snd.part_on");
                if (!p.Removed)
                {
                    // A part goes on loose: straight into bolt mode to do the bolts up.
                    var st = Game.WorkshopVehicle()?.Slots.GetValueOrDefault(p.SlotId);
                    if (st?.Fasteners.Count > 0) SetBoltSlot(p.SlotId, attach: true);
                    else Reframe();
                }
                else if (boltSlot == p.SlotId) SetBoltSlot(null);
                else Reframe();
                break;
            case OpenEvent:
                Audio.Play("snd.hood");
                break;
            case PaintEvent:
                workshop.View?.RefreshPaint();
                QueueSave();
                break;
            case WorkshopEvent w:
                // Measured: a car brought in whole from the Car Lot gets its Car Complete over a blank WorkShop, as at
                // signing in (the event comes right after this one).
                if (w.VehicleId is not null && !signingIn)
                {
                    signingIn = true;
                    Callable.From(() => signingIn = false).CallDeferred();
                }
                DropCarry(null);
                attaching = null;
                boltSlot = null;
                // Measured: a car coming into the WorkShop (from the Car Lot, the Auction, a job) is shown in COMPLETE,
                // whatever view the last one was in. A job's car has no Camera or Body Paint.
                Region = View.Complete;
                if (Game.State.Job is not null && CurrentTool.Action is ToolAction.Camera or ToolAction.Paint) tool = "ratchet";
                workshop.SetVehicle(w.VehicleId);
                workshop.SetRegion(Region, animate: false);
                PrefetchCatalog();
                break;
            case JobDoneEvent j:
                // Measured: the last part shows on the car as it goes on, the customer's picture turns to the thanks
                // one, a tenth of a second later the view goes to COMPLETE and circles the car until "Job Complete!"
                // comes up. The car is still in the game's state here; it leaves right after.
                DropCarry(null);
                attaching = null;
                boltSlot = null;
                boltAttach = false;
                if (workshop.View is { } done)
                {
                    done.BoltSlot = null;
                    done.Sync(animateChanges: false);
                }
                finishing = j.Job;
                finishingModel = j.ModelId;
                finishingCar = Game.WorkshopVehicle();
                jobCompleteUp = false;
                workshop.Showcase();
                // Measured: as the last part goes on, the car starts up to a fanfare (one of two).
                Audio.Play(UiSkin.HasSound("snd.fanfare.2") && Random.Shared.Next(2) == 1 ? "snd.fanfare.2" : "snd.fanfare");
                Audio.Play("snd.drive_off", 0.7f);
                Refresh();
                QueueDialog(() => JobDone(j));
                break;
            case CarCompleteEvent c:
                {
                    // Measured: the job's fanfare with the last bolt, the box at once.
                    Audio.Play("snd.fanfare");
                    var earned = Game;
                    bool blank = signingIn;
                    if (blank) HoldCar();
                    QueueDialog(async () =>
                    {
                        try { if (Game == earned) await CarComplete(c, blank); }
                        finally { if (blank) ReleaseCar(earned); }
                    });
                }
                break;
            case SkillEvent s:
                {
                    var earned = Game;
                    bool atSignIn = signingIn;
                    if (atSignIn) HoldCar();
                    QueueDialog(async () =>
                    {
                        try { if (Game == earned && Screen != Screen.SignIn) await SkillAdvance(s); }
                        finally { if (atSignIn) ReleaseCar(earned); }
                    });
                }
                break;
            case AuctionEvent a:
                // Measured: every rival bid is one call of the auctioneer's; the player's bid is its button's click.
                if (a.What == AuctionWhat.RivalBid && Screen == Screen.Auction) Audio.Play(AuctioneerCall());
                if (a.What == AuctionWhat.PlayerBid && !UiSkin.HasSound("snd.button")) Audio.Play("snd.click", 0.6f);
                if (a.What == AuctionWhat.NextCar)
                {
                    calls.Clear();
                    if (Screen == Screen.Auction) auctionScene.Show(a.Auction.VehicleId, driveOn: a.Auction.Arriving > 0);
                }
                if (a.What == AuctionWhat.Closed)
                {
                    Audio.Play("snd.gavel");
                    if (Screen == Screen.Auction) _ = AuctionClosed(a.Auction);
                }
                Refresh();
                break;
            case ChangedEvent:
                workshop.View?.Sync();
                if (boltSlot is not null && (Game.WorkshopVehicle() is not { } v || !v.Slots.TryGetValue(boltSlot, out var bs) || bs.Part is null))
                    SetBoltSlot(null);
                if (Screen == Screen.Auction) auctionScene.Show(Game.State.Auction?.VehicleId);
                Refresh();
                QueueSave();
                break;
        }
    }

    /// <summary>The auctioneer's calls for the car on the block, in the order he goes round them.</summary>
    readonly List<string> calls = [];
    int nextCall;

    /// <summary>Measured: the auctioneer has nine calls; for each car he goes round all nine in an order of his own, and
    /// round again in the same order. Without them (OpenGG's own sounds) a bid clicks.</summary>
    string AuctioneerCall()
    {
        if (calls.Count == 0)
        {
            for (int i = 1; UiSkin.HasSound($"snd.auctioneer.{i}"); i++) calls.Add($"snd.auctioneer.{i}");
            if (calls.Count == 0) return "snd.click";
            for (int i = calls.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (calls[i], calls[j]) = (calls[j], calls[i]);
            }
            nextCall = 0;
        }
        return calls[nextCall++ % calls.Count];
    }

    /// <summary>The crowd at the Auction, while its screen is up (measured: one recording looped without a break).</summary>
    AudioStreamPlayer? crowd;

    void SyncCrowd()
    {
        if (Screen == Screen.Auction) crowd ??= Audio.Loop("snd.crowd");
        else if (crowd is not null)
        {
            AudioBank.StopLoop(crowd);
            crowd = null;
        }
    }

    // ---- clock and saving ----------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Screen == Screen.SignIn || meta is null) return;
        double dt = Math.Min(0.25, delta);
        Game.Tick(dt, atWork: Screen == Screen.Workshop && !dialogs.IsOpen, auctionRuns: !dialogs.IsOpen || Screen == Screen.Auction);
        if (Screen == Screen.Workshop && !dialogs.IsOpen && !workshop.ShowcaseOn && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            // Measured: Left takes the eye round to the left (the car turns its nose to the right), Up raises
            // the eye towards the roof, Down lowers it towards the underside.
            float yaw = (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.Kp6) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.Kp4) ? 1 : 0);
            float pitch = (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.Kp8) ? 1 : 0) - (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.Kp2) ? 1 : 0);
            workshop.RotateHeld(yaw, pitch, dt);
        }
        if (Screen == Screen.Lot && !dialogs.IsOpen) GlideLot(dt);
        if (Screen == Screen.Junkyard && !dialogs.IsOpen) GlideJunk(dt);
        UpdateBoltLabels();
        if (renderWaits && !Input.IsMouseButtonPressed(MouseButton.Left)) Refresh();
        EngineHold(dt);
        // Show Condition lasts while its plate is held (the plate may be drawn anew meanwhile).
        if (xrayMouse && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            xrayMouse = false;
            SetXray(false);
        }
        if (spraying) SprayAt(mousePos);
        if (Screen == Screen.Auction && Game.State.Auction is { Closed: false })
        {
            auctionRender -= dt;
            if (auctionRender <= 0)
            {
                auctionRender = 0.1;
                Refresh();
            }
        }
        if (saveIn > 0 && (saveIn -= delta) <= 0) SaveNow();
        if ((autosave -= delta) <= 0)
        {
            autosave = 30;
            SaveNow();
        }
    }

    /// <summary>Time to the next footstep while the Car Lot's camera glides (null: it is not gliding).</summary>
    double? lotStep;

    /// <summary>The Car Lot's camera glides while ◄ or ► (on the screen or the keyboard) is held down; the figures and the
    /// marker follow the car nearest the middle of the view.</summary>
    void GlideLot(double dt)
    {
        if (lotHeld != 0 && !Input.IsMouseButtonPressed(MouseButton.Left)) lotHeld = 0;
        int dir = Math.Clamp(lotHeld + (Input.IsKeyPressed(Key.Left) ? 1 : 0) - (Input.IsKeyPressed(Key.Right) ? 1 : 0), -1, 1);
        if (dir == 0 || !lotScene.Glide(dir, dt))
        {
            lotStep = null;
            return;
        }
        // Measured: the camera walks along the lane as it does in the JunkYard, a footstep every 0.42 s or so.
        lotStep = (lotStep ?? FirstStep) - dt;
        if (lotStep <= 0)
        {
            Audio.Play("snd.footstep");
            lotStep += StepEvery;
        }
        var choice = lotScene.LotChoice();
        if ((choice?.Index ?? -1) != lotChoice) Refresh();
        else if (lotBar is not null && IsInstanceValid(lotBar))
        {
            lotBar.MarkerX = LotMarker(choice, lotBar);
            lotBar.QueueRedraw();
        }
    }

    void QueueSave() => saveIn = 0.8;

    void SaveNow()
    {
        saveIn = -1;
        if (meta is { Id.Length: > 0 } && !profiles.Save(meta.Id, Game)) GD.PushWarning("Could not save the game.");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && meta is not null) SaveNow();
        if (what == NotificationApplicationFocusOut) DropCarry(null);
    }

    public override void _ExitTree()
    {
        ScreenMode.Changed -= ScreenChanged;
        // Scenes that are not on screen are detached from the tree; free them ourselves.
        foreach (var s in new StageScene[] { workshop, auctionScene, lotScene, junkScene })
            if (s is not null && IsInstanceValid(s) && !s.IsInsideTree()) s.Free();
    }
}

/// <summary>Our name in the top-left corner, where the original has its logo.</summary>
public partial class Logo : Control
{
    public Logo() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var f = Look.Impact;
        // Slanted like a racing badge.
        DrawSetTransformMatrix(new Transform2D(new Vector2(1, 0), new Vector2(-0.18f, 1), new Vector2(8, 32)));
        DrawStringOutline(f, Vector2.Zero, "OpenGG", HorizontalAlignment.Left, -1, 30, 4, Color.FromHtml("#f0f0f0"));
        DrawString(f, Vector2.Zero, "OpenGG", HorizontalAlignment.Left, -1, 30, Color.FromHtml("#d21c18"));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        var g = Look.Heavy;
        DrawString(g, new Vector2(123, 30), "Garage", HorizontalAlignment.Left, -1, 23, Look.TitleLight);
        DrawString(g, new Vector2(125, 32), "Garage", HorizontalAlignment.Left, -1, 23, new Color(0, 0, 0, 0.8f));
        DrawString(g, new Vector2(124, 31), "Garage", HorizontalAlignment.Left, -1, 23, Look.TitleGrey);
    }
}

/// <summary>The dark bar between the tools and the command column.</summary>
public partial class DividerBar : Control
{
    public DividerBar() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#3a3c3a"));
        DrawLine(new Vector2(0, 0), new Vector2(0, Size.Y), Color.FromHtml("#5a5d5a"));
        DrawLine(new Vector2(Size.X - 1, 0), new Vector2(Size.X - 1, Size.Y), Color.FromHtml("#1a1a1a"));
    }
}
