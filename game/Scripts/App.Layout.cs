using Godot;
using OpenGG.Ui;

namespace OpenGG;

/// <summary>Where everything sits on the 640 × 480 screen, measured from captures of the original.</summary>
public static class L
{
    public static Rect2 R(float x, float y, float w, float h) => new(x, y, w, h);

    // ---- top bar (every screen but Sign In) -----------------------------------------------------------
    public static readonly Rect2 Logo = R(2, 0, 300, 42);
    public static readonly Rect2 MechanicCaption = R(330, 1, 130, 13);
    public static readonly Rect2 MechanicName = R(330, 13, 130, 20);
    public static readonly Rect2 Title = R(466, 0, 170, 42);
    public static readonly Rect2 Money = R(514, 44, 126, 38);

    // ---- WorkShop ----------------------------------------------------------------------------------------
    public static readonly Rect2[] Tabs = [R(4, 45, 121, 33), R(140, 45, 90, 33), R(245, 45, 71, 33), R(331, 45, 174, 33)];
    public static readonly Rect2 View = R(5, 85, 380, 257);
    public static readonly Rect2 ViewHint = R(4, 3, 240, 16);
    public static readonly Rect2 Assembled = R(288, 1, 91, 21);
    public static readonly Rect2 PartName = R(84, 343, 300, 14);
    public static readonly Rect2 Tools = R(385, 85, 123, 260);
    public static readonly float ToolX = 447;
    public static readonly float[] ToolY = [101, 172, 299];
    public static readonly Rect2 Divider = R(508, 82, 6, 398);
    public static readonly float CmdX = 525, CmdW = 115, CmdH = 25;
    public static readonly float[] CmdY = [105, 150, 193, 238, 281, 331, 374, 419];
    public static readonly Rect2 JobHelp = R(525, 146, 115, 118);
    public static readonly Rect2 Exit = R(590, 457, 50, 19);
    public static readonly Rect2 BinTab = R(5, 356, 70, 14);
    public static readonly Rect2 Bin = R(5, 370, 380, 105);
    public static readonly Vector2 BinSlot = new(95, 52);
    public static readonly Rect2 PageUp = R(388, 372, 22, 28);
    public static readonly Rect2 PageNum = R(388, 404, 22, 36);
    public static readonly Rect2 PageDown = R(388, 445, 22, 28);
    public static readonly Rect2 Repair = R(412, 372, 93, 49);
    public static readonly Rect2 Scrap = R(412, 425, 93, 50);
    public static readonly Rect2 BoltBox = R(392, 96, 244, 168);
    public static readonly Rect2 Brushes = R(396, 229, 98, 22);
    public static readonly Rect2 Palette = R(388, 257, 117, 81);
    public static readonly Rect2 DecalsButton = R(406, 339, 78, 18);

    // ---- screens with "Go Back To WorkShop" ---------------------------------------------------------------
    public static readonly Rect2 Prompt = R(10, 46, 320, 20);
    public static readonly Rect2 GoBack = R(333, 50, 164, 28);

    // Catalog: the binder.
    public static readonly Rect2 Book = R(22, 88, 590, 380);
    public static readonly Rect2 LeftPage = R(60, 108, 250, 336);
    public static readonly Rect2 RightPage = R(332, 108, 236, 336);

    // JunkYard.
    public static readonly Rect2 JunkView = R(51, 95, 418, 253);
    public static readonly Rect2 JunkName = R(46, 352, 426, 16);
    public static readonly Rect2 JunkLeft = R(12, 300, 30, 30);
    public static readonly Rect2 JunkRight = R(476, 300, 30, 30);
    public static readonly Rect2 JunkSign = R(508, 92, 126, 170);
    public static readonly Rect2 PurchaseTab = R(5, 380, 96, 14);
    public static readonly Rect2 PurchaseBin = R(5, 394, 380, 82);

    // Auction.
    public static readonly Rect2 AuctionView = R(12, 95, 500, 375);
    public static readonly Rect2 AuctionClock = R(247, 326, 26, 26);

    // Car Lot.
    public static readonly Rect2 LotView = R(83, 97, 465, 288);
    public static readonly Rect2 LotLeft = R(32, 352, 32, 32);
    public static readonly Rect2 LotRight = R(566, 352, 32, 32);
    public static readonly Rect2 LotBar = R(100, 384, 430, 6);

    /// <summary>
    /// With a skin made of the original's own pictures: where its buttons and overlays sit, measured from
    /// those pictures (where the up and down screens differ from the empty one) and from captures.
    /// </summary>
    public static class Orig
    {
        public static readonly Rect2[] Tabs = [R(3, 46, 121, 30), R(139, 46, 90, 30), R(244, 46, 71, 30), R(330, 46, 175, 30)];
        public static readonly float CmdX = 522, CmdW = 116, CmdH = 31;
        public static readonly float[] CmdY = [103, 147, 191, 235, 279, 328, 372, 416];
        public static readonly Rect2 Exit = R(591, 456, 47, 21);
        /// <summary>The first and the second tool, and Start Engine.</summary>
        public static readonly Rect2[] Tools = [R(404, 100, 87, 59), R(401, 165, 89, 64), R(403, 296, 88, 59)];
        public static readonly Rect2 PageUp = R(389, 370, 22, 33);
        public static readonly Rect2 PageDown = R(389, 442, 22, 33);
        public static readonly Rect2 PageNum = R(389, 410, 22, 12);

        /// <summary>
        /// The Parts Bin's places, measured by the condition triangles and the outlines on the captures (session 6): four
        /// across at x 7, 101, 194 and 288, two rows 50 apart from y 372, each 93 x 50.
        /// </summary>
        public static Rect2 BinSlot(int i) => R(7 + Mathf.RoundToInt(i % 4 * 93.7f), 372 + i / 4 * 50, 93, 50);
        /// <summary>The JunkYard's Purchase Bin places, six across (measured: a Right Shock's outline 13..108 x 371..420).</summary>
        public static Rect2 PurchaseSlot(int i) => R(13 + i * 96, 371, 96, 50);
        public static readonly Rect2 JobHelp = R(522, 147, 118, 118);
        /// <summary>The customer's picture in the Job Help tab.</summary>
        public static readonly Rect2 JobHelpPortrait = R(21, 33, 75, 75);
        /// <summary>In the 3D view: the ASSEMBLED tag and the arrow-keys hint.</summary>
        public static readonly Vector2 Assembled = new(288, 0);
        public static readonly Vector2 UseKeys = new(0, 0);
        /// <summary>The money's right edge and top, the mechanic's name (centred).</summary>
        public static readonly Vector2 Money = new(634, 55);
        public static readonly Rect2 MechanicName = R(329, 14, 130, 16);
        /// <summary>Body Paint: the four brushes (24 x 24, 25 apart), the 3 x 9 colours, DECALS.</summary>
        public static readonly Vector2 Brushes = new(395, 229);
        public static readonly Rect2 PaletteCell = R(389, 257, 37, 7);
        public static readonly Vector2 PalettePitch = new(39, 9);
        public static readonly Rect2 Decals = R(406, 339, 80, 20);
        /// <summary>The part under the pointer: under the WorkShop's view, and under the JunkYard's.</summary>
        public static readonly Rect2 PartName = R(85, 344, 300, 16);
        public static readonly Rect2 JunkName = R(121, 351, 350, 16);

        // Screens with "Go Back To WorkShop" (the Auction's plate sits a pixel higher).
        public static readonly Rect2 GoBack = R(333, 49, 166, 30);
        public static readonly Rect2 AuctionGoBack = R(332, 48, 167, 32);

        // Catalog: the tabs (DECALS, ENGINE, BODY, R GEAR), the cards, TURN PAGE.
        public static readonly Rect2[] BookTabs = [R(16, 123, 48, 110), R(575, 123, 49, 108), R(581, 240, 47, 75), R(585, 323, 48, 103)];
        public static readonly float[] PartColumns = [73, 190, 346, 463];
        public static readonly float PartTop = 120, PartPitch = 63;
        public static readonly Vector2 PartCard = new(102, 48);
        /// <summary>Where a part's origin sits on its Catalog card (fitted on the captures).</summary>
        public static readonly Vector2 PartPictureMiddle = new(51.4f, 24.4f);
        public static readonly Color PartCardColor = new(173 / 255f, 170 / 255f, 165 / 255f);
        public static readonly float[] DecalColumns = [74, 130, 186, 242, 348, 404, 460, 516];
        public static readonly float DecalTop = 114, DecalPitch = 80;
        public static readonly Rect2 TurnLeft = R(43, 437, 80, 11);
        public static readonly Rect2 TurnRight = R(514, 437, 80, 11);

        // JunkYard: the arrows, the signpost's planks, the Purchase Bin (six places) and its arrows.
        public static readonly Rect2 JunkLeft = R(12, 315, 34, 33);
        public static readonly Rect2 JunkRight = R(472, 315, 34, 33);
        public static readonly Rect2[] Planks = [R(496, 140, 120, 34), R(496, 174, 120, 34), R(490, 208, 126, 34)];
        public static readonly Rect2 PurchaseBin = R(12, 370, 570, 104);
        public static readonly Rect2 PurchaseUp = R(605, 369, 22, 33);
        public static readonly Rect2 PurchaseDown = R(605, 441, 22, 33);

        // Auction: the two plates, the values' right edge and tops (the money figures), the clock.
        public static readonly Rect2 Bid = R(516, 330, 117, 52);
        public static readonly Rect2 Skip = R(516, 403, 117, 30);
        public static readonly float AuctionValueRight = 622;
        public static readonly float[] AuctionValueTop = [128, 208, 286];
        public static readonly Vector2 AuctionClock = new(266, 294);
        /// <summary>The ">>" before the Current Bid while you lead (measured: white chevrons, 16 x 6, at 520, 135).</summary>
        public static readonly Vector2 AuctionLead = new(520, 135);
        /// <summary>The Auction's sky: the clouds picture mirrored, 506 x 183, from 5 pixels left of the view's corner
        /// (matched pixel by pixel on the captures).</summary>
        public static readonly Rect2 AuctionSky = new(-5, 0, 506, 183);
        /// <summary>The Car Lot's sky: the clouds picture squeezed into a strip at the view's top left (fitted to the
        /// captures pixel by pixel; it stays put while the camera drives along the lane).</summary>
        public static readonly Vector2 LotSky = new(470, 103);

        // Car Lot: the arrows, the values (centred under their titles), the marker on the bar.
        public static readonly Rect2 LotLeft = R(31, 352, 34, 33);
        public static readonly Rect2 LotRight = R(566, 352, 34, 33);
        public static readonly float[] LotValueX = [108.5f, 245.5f, 384, 521];
        public static readonly float LotValueTop = 429;
        /// <summary>The top of the money figures (Number, Orig Cost, Repair Cost) in the Car Lot's boxes.</summary>
        public static readonly float LotCashTop = 427;
        public static readonly Rect2 LotBar = R(101, 369, 428, 16);

        // Sign In: the rows (24 apart), DELETE and NEW, the options, the new-mechanic box, Credits.
        public static readonly float SignInTop = 162, SignInPitch = 24;
        public static readonly Vector2 SignInDelete = new(11, 0), SignInNew = new(86, 0);
        public static readonly Vector2 SignInCredits = new(449, 53), SignInExit = new(468, 85);
        public static readonly Vector2 SignInBox = new(206, 123);
        public static readonly Rect2 CreditsBack = R(483, 445, 145, 30);

        /// <summary>The BOLT label: drawn at this scale with its leader line's end on the bolt.</summary>
        public static readonly float BoltScale = 0.45f;
        public static readonly Vector2 BoltLineEnd = new(134, 122);

        // Decal Browser: one 400 x 300 panel with two pages; the rest in the panel's own coordinates. The buttons are
        // where its pictures' raised and pressed plates differ.
        public static readonly Rect2 Browser = R(120, 90, 400, 300);
        public static readonly Rect2 BrowserUp = R(367, 49, 21, 32), BrowserDown = R(367, 216, 22, 33);
        public static readonly Rect2 BrowserCancel = R(301, 261, 88, 33), BrowserDone = R(195, 261, 88, 33);
        /// <summary>Last Used on the first page, Back on the second.</summary>
        public static readonly Rect2 BrowserCorner = R(8, 266, 64, 24);
        /// <summary>The list's dark well, where "no decals" is written.</summary>
        public static readonly Rect2 BrowserWell = R(13, 49, 351, 200);
        /// <summary>The first decal's picture (48 x 48, the uses left on black at its corner, the name 7 below it);
        /// the others 70 apart, five across (the one measured decal leaves 11 pixels on each side at that pitch).</summary>
        public static readonly Vector2 BrowserFirst = new(24, 73);
        /// <summary>The decals' cells, measured with thirteen kinds: six across 56 apart, two rows 80 apart a page.</summary>
        public static readonly Vector2 BrowserPitch = new(56, 80);
        public const int BrowserColumns = 6, BrowserRows = 2;
        public static readonly Rect2 BrowserTurnLeft = R(19, 65, 48, 42), BrowserTurnRight = R(148, 65, 48, 42);
        public static readonly Rect2 BrowserFlipV = R(19, 154, 21, 64), BrowserFlipH = R(43, 221, 64, 21);
        /// <summary>BIGGER, Normal, smaller: the chosen one stays down.</summary>
        public static readonly Rect2[] BrowserSizes = [R(174, 131, 53, 21), R(174, 177, 53, 21), R(174, 221, 53, 21)];
        /// <summary>The decal in the preview: always 92 x 92, whatever size is chosen (matched on the captures).</summary>
        public static readonly Rect2 BrowserPreview = R(60, 107, 92, 92);
        /// <summary>The colours: 3 x 9, 37 x 15 each, 40 and 18 apart.</summary>
        public static readonly Rect2 BrowserColour = R(258, 69, 37, 15);
        public static readonly Vector2 BrowserColourPitch = new(40, 18);
    }
}
