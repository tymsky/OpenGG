# Fidelity: the original vs. OpenGG

<!-- One bullet per rule: its tag, the rule, then briefly how it was found, in italics. Raw data stays private. -->

What OpenGG does as the original does, how sure we are, and where it differs on purpose. Everything under the game's
areas below is implemented, unless it says otherwise.

Sources: the original's manual; its data files ([formats/tagged-files.md](formats/tagged-files.md)); public web
research (the game's FAQ and forums, 2000–2002); and **measurements of the running game** (the 2002 release, since
2026-09-25): prices on screen and the `.mek` saves before and after each action, screen captures and 60 fps films
fitted pixel by pixel, and its sound output recorded and matched against its sound files. The raw notes, captures and
recordings are private and never committed; what they showed is written here in our own words.

Tags: `M` measured in the running game, `C` confirmed from files or the manual, `L` likely, `G` guess.

## At a glance

- **As the original, measured:** the money rules (prices, repairs, fees, payouts), the jobs and random jobs, the
  WorkShop (views, camera, bolts, Start Engine, Show Condition, the Parts Bin, paint and decals), the Catalog, the
  JunkYard, the Auction, the Car Lot, sign-in and saves, and the screens' layout, fonts, light and sounds.
- **Different on purpose:** money to the cent, a window or the full screen, full colour, decals without dots, screens
  at once, saving as you go, a few keys of our own, no CD Player, and OpenGG's own look without the original's files.
- **Still unknown:** what brings up the controls screen, the Auction's step rule to the dollar (OpenGG's is a fit within
  $5), how stripped the Auction's cars are at each skill, and what picks a random job's how-bad words.

## Money

- `M` A new mechanic starts as Learning with **$5000** (the 1999 manual says $10,000).
- `M` Every part has a price range (min–max) and a **condition value** q = colour / 3 × (1 + extra), black 0. The
  extra, random within about ±0.1, stays with the part, new Catalog parts too; green can go a little above 1. A black
  part is worth its minimum. *SCRAP offered $10.00 for a black $10–100 part, four times.*
- `M` **JunkYard price** = min + (max − min) × 2/3 × q, a black part's its minimum (black parts lie in the yard only in
  the visit they came with: see the JunkYard). **SCRAP pays the same.** *13 shelf prices, to the cent; a Starter that
  came black, $10.00.*
- `M` **Repair** = (max − min) × (1 − q) / 2; the part turns green and keeps its extra. REPAIR turns down black parts
  (scrap them instead) and green ones. *Every repair seen.*
- `M` **Catalog price** = max; a new part is green with a random extra. *q 0.91–1.07 seen.*
- `M` **Job payout** = the fee + what is left of the budget. *9 tutorial and 21 Free Play jobs.*
- `M` The Job Request's difficulty follows the fee: under $250 Easy, under $550 Medium, under $1050 Hard, then Expert.
  *417 offers, all fit ($245.63 Easy, $250.00 Medium, $1049.90 Hard, $1050.31 Expert).*
- `M` A random job's **fee** = 1.25 × what putting the car right costs: the repair of each damaged part, the catalog
  price of each black or missing one, each repair priced with a random extra of its own, not the part's. *Exact for
  black parts ($125.00 for a black $10–100 part); a $19.26 fee for a yellow part repaired for $12.32 fits an extra of
  −0.01, the part's own being +0.09.*
- `M` **Painting costs nothing**, whatever the brush or the colours.
- `M` The Car Lot's **Repair Cost** counts everything spent while the car is in the WorkShop, decals too.

## Jobs

- `M` **Jobs Mode** is the tutorial pack's 9 jobs in order, then Free Play; finishing them is the Skill Advance to
  Novice.
- `M` **Free Play** offers the car job packs' jobs (`escort`, `f150`, `mustang`...) for the cars your skill allows, each
  once, **cheapest first**, and random jobs only when none is left. *22 offers in a row came in fee order.* A pack's
  offer comes back after CANCEL until you take it; a random one is new at every Get A Job. Giving a job up (Job Help's
  CANCEL) costs nothing.
- `C` Each car has a minimum skill (`CarMinSkill`). *The Escort (Novice) came only after the tutorial.*
- `C` A job has start and finish states, a fee that is also its budget, and allowed tabs (the others' plates empty). Its
  "car green" counts only the regions its tabs allow, and a "whole car green" job also needs parts the customer never
  mentions. *The Escort's $77 ENGINE-only job starts without its $235 roof and asks for the car green.*
- `M` A job's car is its start state and nothing more: the packs start from the whole stock car (a Checker Cab whose
  owner took the old engine out comes with body and running gear only). A part missing at the start takes what
  is mounted on it along; a part the start puts on brings back what it goes on. *Escort jobs #24 and #25.*
- `M` The stock car is the parts not marked custom that go on stock parts only: an unmarked part that goes on a custom
  one belongs to that add-on (a T-Bird's HotRod Dual Carbs and Blower, its ChopTop windshields, a Fairmont's Boosters).
  *The T-Bird and Fairmont jobs had them bought and fitted.*
- `M` Job Help shows the hints in turn (1, 2, 1, 2...), with OK / RESTART in Jobs Mode and OK / CANCEL (give up) in Free
  Play. RESTART asks nothing: the Job Request comes back, the budget full, the damage dealt again.
- `M` Exit during a job warns that the job will be lost (OK / CANCEL); after OK it is gone. *Signed in again: an empty
  WorkShop and another offer.* OpenGG drops it as Job Help would, so in Jobs Mode the same job starts over.
- `M` In Jobs Mode each Job Request comes over a blank WorkShop (no tabs, commands or Exit, nothing in the view, the money
  box showing your cash), the first one after signing in too, and has OK only.
- `M` **The job's end**: the last part shows as it goes on and the customer's picture turns to the thanks one; 0.1 s
  later the view goes to COMPLETE, 17.9° above level, and circles the car at the arrow keys' rate (5 rad/s, about 60°
  more in the first 0.1 s) until Job Complete!, 3.0 s after the last part. The car does not drive off: the view stays
  where it stopped under the boxes that follow, and the next car comes in seen from there. The tools, ASSEMBLED and the
  thanks picture stay; from Job Complete! on the Job Help panel is empty and the money box keeps the budget. After the
  first job two short tips follow, over the WorkShop as the job left it. *Four jobs filmed at 60 fps.*

### Random jobs

- `C` In the original's words, read in memory from the player's `random.dat`: its customers (a waiting face, a happy
  face, their words before and after the damage, their thanks), its sentences about what is wrong (a symptom or a cause
  in a region, how much of a region is damaged, one part damaged or missing) and their variables (words picked at
  random, by how bad the damage is, how much of the region it covers, or by the region). A skin can supply other words
  (`jobPhrases`); without either, OpenGG's own random templates stand in.
- `M` An offer is the customer's opening (49 % of offers), what is wrong, and the closing (54 %), each drawn on its
  own, so an offer with nothing wrong can have no words at all. One space goes between the pieces, each with a capital,
  except after an opening ending in a space ("..., but "), which runs on. Inside a piece a sentence starting in lower
  case gets a capital and two spaces ("hey? are you there?" → "Hey?  Are you there?"); one with its capital keeps one.
  Parts are named in lower case, as the car file names them ("my b axle"). Only first letters change; typos are kept.
  *65 offers transcribed, 482 read with the original's own letters.*
- `M` What goes wrong: nothing (the customer only says hello, fee $0, the job done as you take it) 1.7 %; the whole car,
  in one sentence, 24 %; one region, in a sentence on how many of its parts are in what shape, 29 %; two regions, a
  sentence each joined by a joining word, in the order engine, body, running gear, both symptoms or both causes where
  the file has both, 38 %; one part 2.5 %; two parts, the second now and then missing, 4.8 %. At Mekada the whole car
  38.7 %, one region 22.1 %, two regions 32.2 %, one or two parts 6.2 %, nothing 0.8 %. *482 offers split into their
  sentences; 494 at Mekada.*
- `M` How much of a region is damaged, by quarters of its parts: a region told alone 32, 36, 14, 18 % (never fewer than
  three parts); each of two regions 30, 30, 26, 15 %; each region of the whole car 22, 19, 22, 37 % (in the top quarter
  every part half the time); one part at least. *The original logs each offer's damaged parts: 80 offers, 154 regions.*
- `M` The how-many words follow the share of the region's stock parts damaged: "one or two" under a quarter, "a few"
  under a half, "a lot of" under three quarters, "tons of" from there; "a slight", "a medium", "a major" by thirds. The
  how-bad words come as the mildest or the strongest, never one between: for a part the strongest when it is black;
  for a region not by its colours, but the strongest in 98 % of two-region jobs, 56 % of whole-car and 38 % of
  one-region ones. *420 Mekada offers with their logged parts.*
- `M` The damaged parts' colours: a region told alone 22 % black, 43 % red, 35 % yellow; each of two regions 27, 30,
  43 %. *Fitted to 153 offers' fees (1348 parts).* The whole car's and a single part's are too few to fit so; OpenGG
  fits them to the fees' averages (a region's fee comes to 1.33 × its repairs all in red, the whole car's to 1.26), and
  its difficulty words then come out 22 / 17 / 24 / 37 % against the original's 16 / 18 / 29 / 38 % at Mekada.
- `M` The car is any your skill allows, all alike. *364 Mekada offers: 147 models, their minimum skills in the
  proportion of all 165 cars'.*
- `M` A random job's Job Update shows only what is wrong, its sentences one space apart.

## WorkShop

### Views and camera

- `C` Four views: COMPLETE, ENGINE, BODY and RUNNING GEAR; each work view shows only its region.
- `M` The view keeps its angle across tabs and the cars that come in after. The arrow keys held turn it, about 270–300°
  a second, stopping at once: Left takes the eye round to the left (the car's nose turns right), Up raises it towards
  the roof, Down lowers it towards the underside, to about 70° either way on every tab.
- `M` Dragging with the right button turns it as if the car were held (to the right the nose follows the pointer, down
  the eye rises), about 3.4° for each pixel of the 640 × 480 screen, to the same limit. It follows the pointer's pixels,
  not the mouse's own counts; the left button does not turn it. *Four drags fitted by the car's outline.*
- `M` The first car is seen straight from the front, level with its middle, from about 1.08 × the region's diagonal
  (ENGINE 0.92). *Fitted by the cars' outlines.*
- `M` The keys set the view's yaw and pitch (it never rolls), while the eye moves on a circle in its vertical plane
  centred 0.27 × the level distance behind the region's centre, radius 1.27 ×, its angle lagging the pitch by up to
  11.7°: a tilted view looks a little past the centre (about 30 px at 20°) and the eye backs off as it climbs (15 % at
  the ends). *94 % of the outlines shared over 67 views (a plain orbit: 67 %).*
- `M` The view's moves (to another tab's framing, onto a part's bolts and back out, framing the region's parts again
  when one comes off or goes on) take about four frames: 0.64–0.68 of the way after the first, 0.91–0.94 after the
  second, 0.99 after the third. OpenGG's own look glides instead.
- `M` With the WorkShop empty, a black box across the view says so in the screen's white letters; the tab plates stay as
  the last car had them, on COMPLETE, and do nothing.
- `M` A car coming into the WorkShop (picked on the Car Lot, won, a job's) shows in COMPLETE, whatever the last view,
  silently.
- `M` A job's car gets only the Impact Wrench and Start Engine; the Camera and Body Paint are for your own cars.

### Parts and bolts

- `M` There is one part tool, the **Impact Wrench**: clicking a bolted part opens bolt mode.
- `M` **Bolt mode**: the "Impact Wrench Tool" box ("unscrew" when taking a part off, "attach" after putting one on), BOLT
  labels with leader lines, holes black; bolts go in and out at once, and only the bolt or hole under the pointer lights
  up (yellow). The bolts go one way only: taking a part off, only the bolts still in light up and take a click; putting
  one on, only the holes. A click on a bolt already done is the wrench in the air and changes nothing. CANCEL while
  taking a part off puts the bolts taken out back in, one after another from its release, a bolt's sound each and back
  to back; the box and the black holes stay as they were until the last has sounded, then the box goes. So no part stays
  half undone, and out of the box no bolt or hole is drawn. While the box is up only the view (its bolts, the arrow
  keys) and CANCEL answer: the tabs, the command plates in sight, Start Engine and the Parts Bin take no click and make
  no sound. *A Wynn's oil pan, two bolts: the hole under the pointer lit nothing (the bolt there, 45 pixels), each click
  on a done bolt the air wrench's sound; after CANCEL the box came up again with both bolts in, and the view before bolt
  mode and after CANCEL was the same to the pixel. An F350's cooling fan with three of its four bolts out: three bolt
  sounds 0.38 s apart (the sound's length); with two out the box went 0.85 s after the release.* On a running-gear part the view keeps its angle and comes in on the middle of the part's bolts, from about
  1.47 × the part's longest side, and goes back out to the region when done; on an engine part it does not move (body
  parts have no bolts).
- `M` A part goes on and comes off at once, bolted or not: the next frame has it in its place, or gone. *Filmed at 60
  fps.*
- `M` A part with bolts goes on loose and the wrench's box comes up to bolt it; the Parts Bin shows the part at its place
  until the last bolt is in (then the box goes, silently); the box's CANCEL takes the part off again, back to its place
  (with a bolt already in too).
- `M` A part dropped where an alternative is already on gets Assembly Error, naming the part it would replace. So does
  one dropped before what it goes on is there, or clicked before what is on it is off, naming that part with "is" or
  "are" as its name reads.
- `M` Condition comes in four colours. A black part can only be scrapped: taken off, it does not go back on (Assembly
  Error: too far gone to last; it stays in the Parts Bin).
- `M` **ASSEMBLED** shows only when nothing is missing, in the worst part's colour (a black one shows red: the tag comes
  only in red, yellow and green). A part being unscrewed counts as on; one being bolted on does not.
- `M` The view's overlays (ASSEMBLED, the arrow keys' hint) are drawn half a pixel to the right, a touch soft.

### Start Engine

- `M` **Start Engine is held.** Its sound starts at once; let go within half a second and it stops there, the engine not
  started. Held longer, the engine catches: the sound plays out (5 s) whether you let go or not, the engine shakes (a
  pixel or two, about half the frames still) and its spinners (the `.car`'s crankshafts, flywheels and fans) turn about
  their own axes for as long as it runs. An engine that won't start makes its one sound either way; cranking (1.57 s),
  it shakes a pixel at most and its fan turns. *Holds of 0.30–0.45 s were cut, 0.50–3.2 s not; filmed at 13 and 60 fps.*
- `M` A car with sounds of its own (a starter's, an engine's or an exhaust's, in its `.car`) starts with all of those
  fitted at once instead of the usual sound, running as long as the longest, and stops the same way when let go early.
  *A '23 Ford T (4.2 s), a Chevy Mini (9.5 s), a Karmann-Ghia (5.8 s).*
- `M` An accessory's own sound (`.car` 209 on an accessory: the C Cab Hotrod's horn, the Fairmont's rocket booster, the
  Doom Buggy's launchers) plays along with the start's from the press when the engine is to start, and stops with it
  when let go early; not with a click or a crank. *A C Cab Hotrod with its horn: the start's sound and the horn together
  at the press; held 0.3 s both cut at the release; without its Alternator the crank alone.*
- `M` **Why it won't start:** no starter, or any engine part but the block red or black: a click, whatever else is
  missing. Otherwise any other part the engine needs missing, or the block red or black: one crank that doesn't catch.
  Yellow parts still start; nothing outside the engine changes it, nor does a spare in the Parts Bin. With the original's
  look nothing is written under the view (OpenGG's own look says what happened). *Clicks and cranks on a Galant, an F350
  and job cars, one part at a time.*

### Show Condition and the pointer

- `M` **Show Condition** works while held (the `C` key is ours). It draws only the parts (not the body's fixed pieces nor
  the decals), each in its condition colour **added** onto what is behind it: the grey shows through, overlaps add up,
  both sides of every face count and nothing hides them; lit like the car in the WorkShop but without the body's
  highlight; black parts keep their own look. On COMPLETE it shows only the job's regions. *Fitted on the first
  three jobs' views.*
- `M` The part under the pointer lights up as Show Condition draws it (black ones too, in a dark grey), its name under the
  view. Not with Body Paint in hand, nor in bolt mode.
- `M` On COMPLETE the pointer lights the whole region under it (the body, the running gear) and names nothing; a click
  opens that region's tab, with its sound.

### Parts Bin

- `M` 8 places a page: four across at x 7, 101, 194 and 288, two rows 50 apart from y 372, each 93 × 50 on black; the
  names' tops 40 below the place's, a space 6 wide.
- `M` A condition triangle 13 × 10 in the place's corner (red 173, 8, 8; yellow 173, 174, 8; green 8, 113, 8; black 57,
  56, 57) and, round a part under the pointer or picked up, a one-pixel outline a little brighter (red 189, 12, 8;
  yellow 189, 190, 8; green 8, 125, 8; black 57, 60, 57).
- `M` The picture: the part from the car's left side, level, its box's diagonal 50 px, in the middle of its place over
  its name; damaged ones in bare metal by condition, good ones in their own look with their car model's paint. Under the
  pointer it turns about the vertical (half a turn in 0.79 s) and stays as left; a repaired part is shown from the side
  again. *Some parts taken off later showed at other angles, not understood; OpenGG shows every new part from the side.*
- `M` Picking a part up: its button goes down and the whole place (black ground, picture, name, triangle, outline) goes
  with the pointer, held where it was taken, over everything; the pointer stays the hand and the bin's place shows
  outlined. Nothing under it lights up or is named. Let go anywhere over the 3D view (an empty corner too) and the part
  goes on in its own place; over REPAIR or SCRAP their boxes come up; anywhere else nothing happens. *Filmed at 60 fps.*
  OpenGG's own look also shows where the part fits and lights REPAIR and SCRAP.
- `M` It shows the parts that fit the car in the WorkShop (the original keeps them per car model): nothing with the
  WorkShop empty, and the finished job car's parts while it is still on show.
- `M` Scrapped parts go back on their car's JunkYard shelf, black as red.

### Paint and decals

- `C` Body Paint has the 27-colour palette (3 × 9) and four brushes: small, medium and large spray square dabs
  freehand, the panel brush fills the panel's square, all into a 256 × 256 paint picture per car (the original's cars
  map their panels into one). The colours are the game's palette picture (`Gfx24/paintcolors.tga`), read from the
  player's copy; without it OpenGG's placeholders, some well off.
- `M` Decals are bought with a number of uses (from the `.dpk`); placing one uses one up, silently. The Decal Browser:
  TURN a quarter turn (the right one clockwise), ↕ FLIP upside down, ◄FLIP► mirrored, the sizes smaller, Normal and
  BIGGER, and a colour that tints its white, picked afresh each time (the Modify page shows it white again). A decal
  that takes no colour (a flag) has no palette.
- `M` **A decal goes into the car's paint picture.** It is laid flat on the screen, upright whatever the car's angle,
  its middle on the pointer, at ½, 1 or 1½ of its picture's pixels; each of its pixels paints the texel of the panel seen
  there, whichever panel that is, its ground left out. Paint put on afterwards covers it; seen from another angle it is
  stretched where it went on at a slant. *An F350's paint
  picture compared between saves.* Our own cars (no paint
  picture) keep their decals apart, projected onto the car.
- `M` Paint and decals leave no mark on a part in bare metal (red, yellow or black), nor on a panel outside the paint
  picture (an F350 Payload's sides); the decal's use is spent anyway. *0 of 65,536 texels changed.*
- `M` While you aim a decal it replaces the pointer, drawn where it will go: flat on the screen, unlit, its ground left
  out, only the odd columns of its odd rows (a pixel in four), over whatever is behind it. Our own look draws it whole, see-through.
- `M` The Decal Browser: a 400 × 300 panel at (120, 90), two pages. "Choose a Decal:" the decals at 48 × 48 in the
  order bought, six across 56 apart, two rows 80 apart, twelve a page (▲ ▼ turn pages), the uses left in white on a
  black patch at the corner, the names 7 px under; with none, a line says so. Last Used (bottom left) reopens the last
  decal placed as it was set, colour too. "Modify Decal:" the decal at 92 × 92, smoothed, whatever the size (that shows
  only on the car), the chosen size's button down; the colours 3 × 9, each 37 × 15, 40 and 18 apart; picking one marks
  nothing. *Eight captures, to a pixel or two.*

### The 3D look

- `M` The cars are lit per vertex in the colour numbers as stored (not linear light): ambient 0.13 plus one light (0.48)
  from in front, 40° up and 19° to the left, turning with the view. *Fitted to four screenshots; a fifth agrees.*
- `M` Every material comes out at twice its colour times the light: paint, chrome, glass and textures (the `.car`'s
  colour of one half on textured materials is not used).
- `M` Body meshes get the kit's white overlay as a highlight: 0.38, power 14, from a light behind the car, up and to the
  left. *Measured on black paint.*
- `M` A face of a flat-shaded material (shade mode 1: boxy engine parts, a pickup's bed) is lit by a normal of its own,
  from its corners the other way round from the stored normals; in bare metal the same part is smooth.
- `M` Every face is drawn from both sides, so a face seen from its back comes out dark (floor pans from below).
- `M` Damaged parts are drawn bare, without paint or textures, in a flat colour by condition: yellow a pinkish grey, red
  an olive grey, black a dark brown.
- `M` The view is 45° tall everywhere: 63° across the WorkShop's view, 67.5° across the Car Lot's, 58° the Auction's,
  69° the JunkYard's.

## Catalog

- `C` A binder with DECALS, ENGINE, BODY and R GEAR: 2 × 5 parts or 4 × 4 decals a page; buying confirms the price.
- `M` It lists every part of the car in file order, the custom ones too except in Jobs Mode, each in its own region's
  section. It opens at the current tab's section (BODY from COMPLETE, whatever the job's tabs), each section at the page
  it was left on. A spread's pictures come up together, 0.1 s after its pages; another page or section changes them in
  one frame, with no page turning. *Filmed at 60 fps.*
- `M` A card's picture is the part's mesh in its own frame, not as it sits on the car (a flywheel the car turns a quarter
  round shows face on), seen from its −Z side, level, in perspective from 2.8 × the distance of its farthest corner;
  that sphere is 25 px round the origin, which sits in the card's middle; drawn over the name, cut at the card's edges,
  in the car model's paint. The name's tops are 39 below the card's. It turns while the pointer is over the card (a turn
  in about 1.55 s) and stays as left, the next visit too; the bin's pictures do not turn with it. *Twenty
  pictures fitted by their outlines.*
- `M` The pictures have a light of their own: more ambient (0.25) and a weaker light (0.29) from the camera's left and
  in front, no highlight, and a body mesh's texture taken once, not twice as in the WorkShop (so a car's sponsor panels
  come out darker, its wheels' rims as bright). *Fitted pixel by pixel on the ENGINE, BODY and R GEAR pages of a
  Mustang, a pickup and a Commodore: 3.6 off on average in the colour numbers, 38.7 with the WorkShop's light.*
- `M` Which of a picture and its name is on top differs from card to card (a pickup's wheels under their names, the same
  wheels on a Mustang over them, a Commodore's left wheel over its name and its left rear wheel under); the same on a
  second visit. OpenGG draws the picture over the name, as most are. *Why is not known.*

## JunkYard

- `M` **One shelf per car model**, made as the model's first car comes in (a job taken, a car won). A click **buys on the
  spot**; a click in the Purchase Bin gives it back for what you paid; the strip pages six at a time; leaving takes the
  rest to the Parts Bin. Through a visit the parts keep their places, another area looked at and back included: a part
  bought leaves its place empty and one given back goes back to it. The next visit lays the rows out afresh from the
  shelf, where a part given back went last. *A Starter bought, given back, bought again with BODY looked at between: the
  view changed only in its pixels, and given back it was as before to the pixel; the next visit had the parts after it
  moved up and it at the shelf's end, before what that visit brought.*
- `M` **Each visit changes the shelf shown**, before the yard comes up: its black parts turn red, 0 to 3 of its parts go
  (any of them) and 0 to 3 new ones come at its end, any of the model's parts in any colour; a new black one shows, and
  sells, at its minimum until the next visit. The other models' shelves stay as they are; nothing else changes a shelf
  but what is bought from it and scrapped onto it. *28 visits in a row to a Wynn's shelf, from the save the original
  writes as the yard is gone to (before the change): 5, 13, 6 and 4 visits lost 0 to 3 parts, 5, 12, 9 and 2 gained 0
  to 3 (11 green, 13 yellow, 4 red, 8 black); all 12 black parts that stayed were red by the next visit; a new black
  Starter showed $10.00.* A black part bought in the visit it came with goes home black. *A Left head at $10.00, black in
  the Purchase Bin and in the Parts Bin.*
- `M` A new shelf holds, in the car file's order, one of each custom part the car has not (with the unmarked parts that
  go on them) and, for a car won at the Auction, one of each stock part it came without; never an accessory; then 15 to
  21 of the model's parts at random, in any colour (23 % black, 25 % red, 25 % yellow, 27 % green of 986). *22 new shelves in
  the saves: 16–25 parts for job cars without add-ons, up to 67 for stripped cars.* The save written as a job is taken
  lacks the new shelf and the one at its end has it, so a shelf made for a job given up is never saved.
- `M` The Purchase Bin: places 96 × 50 from (13, 371), six across; the part under the pointer outlined in its condition
  colour.
- `M` It opens at the area of the view you were on (BODY from BODY, RUNNING GEAR from RUNNING GEAR, ENGINE from ENGINE
  and from COMPLETE); each area's camera stays where it was left; the plank of the area shown does nothing.
- `M` The view is 418 × 253 at (51, 95). In each area the camera starts at its path's start (play_01) looking at
  CamFocus, as the scene's own camera does, and glides towards play_02 without turning while ◄ ► or the arrow keys are
  held (a click: a short way), 450 of the scene's units a second, to the ends, where the arrows look the same.
- `M` The part under the pointer: its condition colour added onto whatever is behind it, nothing in front hiding it,
  from both sides of every face, lit like the yard's parts: green (17, 136, 17), yellow (188, 181, 16) and red (204, 15,
  13), as for Show Condition. It stays lit and named when the pointer leaves the view straight from it; leaving over
  empty ground clears it. A black part lies there only in the visit it came with; lit, it adds a dark bluish grey of its
  own. *A Left head added (64, 74, 82) to the shelf's wood behind it; OpenGG, the same part in the same view, (64, 73,
  82).*
- `M` Where the parts lie: in the shelf's order along the first board or slab, each turned so that its longest side runs
  back across it (a half turn, a quarter more for a part longer than it is wide), centred across it and lying on it; the
  row moves on by 0.25 m and half the next part's length, starting 0.2 m in. *21 hovered parts in all three areas; the
  steps vary by about 0.12 m.* Once the first two running-gear parts lay at the row's end instead.
- `M` The parts look as on a car in the WorkShop: damaged ones all bare metal in their condition colour, good ones in
  their own materials with their car model's paint.
- `M` One light, from the side of the rows' ends and from below the ground, lights the scene (ambient 0.590 and 0.648 of
  it, times the texture's colour numbers, clamped at white) and the parts (in the cars' doubled terms 0.307 and 0.276,
  without the highlight).

## Auction

- `M` About 20.5 s a car. Every car opens at a Current Bid of **$100**, asking $100 + its fixed step, which it keeps when
  it comes back unless it was changed meanwhile. Bids come only at the asking price, which then goes to bid + step; it
  drops $25 every 2 s (the first as the car arrives), never below the current bid + $25. The last bid wins; a car you
  win goes straight into the WorkShop, the car there to the Car Lot. Place Bid while you lead raises your own bid to the
  asking price, without a box. *Higher openings once noted were read after bids.*
- `M` A car's **step** follows the car as it is, whoever's it is (its Orig and Repair Cost don't count): a base of about
  1.1 % of the catalog prices of all the model's parts, stock and custom, and on top of it each part on the car at its
  price, weighed by the colour of the car's **average** condition (black parts count in it as nothing; the average
  yellow weighs about 0.47 of green, red 0.155), about 5.5 % in a region with all its stock parts and 4.5 % in one not
  whole. A lone part adds nothing and a pair twice its share, and a custom part adds some more of its price, alone too.
  In whole $5, rounded down, never under the base. *77 cars of known make-up put on the block from an edited save (a C
  Cab Hotrod whole in each colour, by region and part by part; black lone parts of seven models: the base; pairs and
  lone parts of a Miata; a whole Mustang and Pickup) and the bought cars' first saves.* OpenGG's rule is a fit: all 80
  steps known within $5, 70 of them exact.
- `M` When a car's time runs out, or on Skip Car, it drives off to the right (gone in about 0.3 s) and the next drives on
  from the left; its figures and a full clock come up about 0.75 s after the change began, the old ones staying till
  then. Coming to the Auction, its first car drives on the same way, in sight 0.17 s after the screen and in its place 0.4 s
  later. Every change of a figure blanks the column for a
  frame or two. While "Winning Bid!" is up there is no Go Back To WorkShop and no Skip Car. *Filmed at 60 fps.*
- `M` The other bidders bid the asking price on a 0.755 s beat (about one beat in six without a bid) up to their limit:
  $100 plus a number of steps drawn each time a car goes on the block, whatever is on the car, and lower the higher your
  skill: at Novice 9.8–15.1 (mostly 13–14.5), at Mekada 3.0–14.5 (half under 8.6). The first bid mostly comes at once;
  past their limit, they bid the moment a drop brings the price back. *25 cars watched to
  the end at Novice, 43 at Mekada; the beat timed on 65 bids.* OpenGG draws the limit
  from the measured spread at your skill; Handy and Expert a third and two thirds of the way from Novice to Mekada (`G`).
- `M` The cars come worn and stripped, the more so the higher your skill: fairly whole at Novice (10–15 % of the stock
  car gone; 3 % black, 31 % red, 35 % yellow, 31 % green), badly beaten up at Expert and Mekada (41–79 % gone; 34 % black,
  55 % red, 10 % yellow, 1 % green), and at Mekada even 73–96 % gone; nothing is left hanging on a missing part. *25
  cars' saves.* OpenGG makes each car like one of those measured: beaten-up ones never at Novice, one in three at Handy
  (by eye, 30 cars on the block at Handy: about a quarter stripped to a shell or a frame; 20 at Novice: none), two in
  three at Expert and Mekada, Mekada's like the seven bought there.
- `M` Selling your car: "you made / lost" = the price − (Orig Cost + Repair Cost). Outbidding everybody for your own car
  calls the sale off for a service fee of 5 % of your bid in whole $5, rounded down; the car stays yours in the
  WorkShop, its costs and its step as they were. *$40 at $825, $205 at $4150.*
- `M` The 3D fills the whole view, 500 × 375 at (12, 95); the screen's stage front, chairs and black hole (its near-black
  edge too) are drawn over it. The camera looks straight along the stage, 7.9° down; every car stands with its own origin
  on one spot, 12 cm left of the stage's BoundBox spot and 18 cm nearer the camera (the scene's Camera02 is not it). The
  stage shows its texture's colour numbers; the car takes the WorkShop's light with ambient 0.046 and 0.532 of it. The
  sky is the clouds picture mirrored, 506 × 183 from 5 px left of the view's corner.

## Car Lot

- `M` The figures: Number, Orig Cost and Repair Cost in the big money figures, Repair Time in the text font's. A car's
  **Number** is how many cars you had bought before it (the first is 0); a sold car's Number is not given again. *A
  session that bought twelve cars in a row and sold them all.* The **Repair Time** runs only while the car is in the
  WorkShop with the WorkShop up and no box open (bolt mode counts; the Catalog, the JunkYard and a box don't), and
  stops for good once the car is complete; the sign-in's TOTAL TIME runs all the time. *30 s of each: the WorkShop idle
  and bolt mode counted in full, the Catalog, a Scrap Part box and the JunkYard not at all, every one of them in TOTAL
  TIME.*
- `M` **Car Complete**: the first time a car of yours in the WorkShop is in top condition (nothing missing, every bolt
  in, every part green; its accessories don't count), a box says so at once with how long it took and what it cost: its
  Repair Time, hours and minutes when there are any and seconds always ("1 hour and 2 minutes and 3 seconds", "2 hours
  and 1 second", "1 minute and 0 second", "0 second"), and its Repair Cost. It comes to the job's fanfare as the last
  bolt of the last part goes in (over the WorkShop as it is), or as the car comes into the WorkShop so, signing in or
  from the Car Lot (over a blank WorkShop), and once for a car (the save's field 1501 of the car, 0 until then); the
  car's Repair Time stops counting there. A custom part in a stock part's place counts as the stock one. *Cars set up
  in the saves: a C Cab Hotrod whole and green (with its horn and without, and with the Blower in the Air Filter's
  place), one with a yellow Alternator repaired and bolted back ("41 seconds, $7.00"), one with its Alternator bolted on
  from the bin (the fanfare with the bolt's sound, the box 0.1 s after the release), one parked whole and brought in
  from the lot; its Repair Time the same after minutes more.*
- `M` Your cars stand in its **twelve bays** from the first, in Number order, along the bay lines, their fronts to the
  lane, each at its bay marker's own origin (car_01 … car_12), not the marker's middle. *Every marker within a pixel over
  14 captures.* Where the others go when a car leaves is `L`: the save keeps only their order.
- `M` The first visit in a run of the game opens on the first bay; later ones where the camera was left. ◄ ► and the
  arrow keys move it along the lane while held (► towards the first bay), 0.95 bays a second, on to the last bay, cars
  parked there or not. The figures and the gold marker under the view follow the car nearest the view's middle; a click
  anywhere in the view brings that car into the WorkShop. *The original draws the lot about 60 times a second, moving it
  a step each frame.*
- `M` Put Car In Lot parks the WorkShop's car and takes you there. With twelve cars of yours, Go To Auction is refused
  ("Car Lot Full"), your car in the WorkShop counted too. *Eleven parked and the twelfth in the WorkShop: refused.*
- `M` The camera sits above the path's start (play_01), a little lower than its middle, looking a fixed way along the
  bays, and drives along the lane without turning.
- `M` A parked car's shadow is a flat, sharp-edged dark rectangle, its footprint (its stock parts, open doors and all),
  the ground under it at 0.46 of its brightness. On the Auction's stage nothing of a shadow shows from its camera: the
  floor round and below the car is as bright with it as with the stage empty (OpenGG draws none there).
- `M` The light: the scene and the cars alike, per face, ambient 0.585 plus 0.588 of a level light from the side of the
  first bays, times the colour numbers of the texture (or of the material), clamped at white, not doubled; the scene's
  own lamp is not it.
- `M` The sky is the clouds picture squeezed into 470 × 103 at the view's top left, staying put as the camera drives.

## Sign-in and mechanics

- `C` Starting shows the first loading screen, at once the second (up while the content is read), then Sign In; nothing
  shows at exit. *The original's own log.* OpenGG shows them with the original's look.
- `M` The mechanics table (9 rows seen; the manual says 10) shows SKILL, TOTAL TIME, CARS (owned) and CASH.
- `M` NEW: the name typed from the left in capitals, a dash for the cursor; OK puts the new row on the sheet (Learning,
  $5000) without signing in. Signing in a Jobs Mode mechanic brings the next customer at once. DELETE and EXIT ask first
  in the sheet's own box (OK / CANCEL, the words' middle 74 below its top).
- `M` **Skill Advance**: Learning to Novice after the tutorial; to Handy, Expert and the top level as soon as your cash
  first goes past $15,000, $35,000 and $120,000 (cash alone: how many cars you fixed doesn't count), each followed by
  "Available Cars" (more cars at the Auction; not after the tutorial's advance). One level at a time: cash past two
  bars brings the two pairs of boxes in turn. Earned by a job's pay both come after the job's OK over a blank WorkShop;
  earned by other money (a part scrapped) at once, over the WorkShop as it is; a save's cash already past a bar brings
  them as you sign in, over the WorkShop's tabs with no car and no commands, the car coming in after the last OK. *A
  scrap that took $14,980 to $15,018.79: Skill Advance to Handy and Available Cars at once. A Novice with the tutorial's
  9 jobs, its save's cash set to $50,000: Handy, Available Cars, Expert, Available Cars as it signed in.* The top level's
  name in the original is the developer's; OpenGG's is Master (a skin can name the levels). The sign-in's MEKADA
  picture does nothing. *Seen at $15,000 and $35,000 and, with a save's cash set to $119,990, at $120,000.*
- `M` IMPORT MECHANICS brings over the original's mechanics (`Data/Mechanics/*.mek`): name, cash, skill, total time,
  jobs done, decals and their uses, every car you own (parts with their condition and extra, paint picture, Orig Cost,
  Repair Cost, Repair Time), the Parts Bin and the JunkYard shelves. Anything without a counterpart is logged.
- `M` The Camera: a click saves a snapshot of the screen, the pointer on it too, per mechanic: `Shot<n>.jpg` (640 ×
  480, best quality) and `Shot<n>.bmp` (100 × 75; 8-bit in the original, 24-bit in OpenGG).

## Screens and dialogs

- `M` One 640 × 480 screen at a time, scaled to the window, laid out from captures: the top bar, tab plates, tools, the
  gold command column with its fixed places, the Parts Bin, REPAIR and SCRAP.
- `M` The view tabs and the tools act as they are pressed; the command plates, Get A Job and the boxes' buttons as they
  are let go, and do nothing if the pointer moved off first. Nothing lights up under the pointer. *Filmed at 60 fps.*
- `M` Yellow dialogs with a black title bar; errors ("Assembly Error!") are dialogs. Every one comes up at (200, 135):
  the words centred, 16 px a line; buttons 132 down, two at 11 and 128, a lone one at 71; the title centred from 11 over
  what is left after its icon (half the room, rounded down), its letters' tops 11 down. The Impact Wrench's box sits
  over the tools (390, 95).
- `M` Job Request, Job Update and Job Complete: a portrait, Difficulty, Fee and the customer's words; the words from
  (102, 54) of the box, Difficulty's and Fee's tops at 237 and 293, the line under them centred in its place (tops at
  326; the Job Update's two lines at 318 and 335). *538 captures.*
- `M` The letters step as the original's: in the text font a space is 6, the figures 0, 3, 5, 6, 7, 8, 9 and ':' a pixel
  shorter than their width and K a pixel longer; the small white letters by their bright columns and 2; in the titles'
  font B, C, I, J, L, O, S, U, i and l a pixel longer. *629 Job Requests (6436 spaces), 519 fees, 58 Repair Times, 176
  titles.* A centred line of the text font can still land a pixel off.
- `C` The dialogs, hints and messages say it all in OpenGG's own words. A skin can word each sentence its own way
  (`texts`, keys in `Ui/Words.cs`), as the original did for instance, kept in the player's own skin; the dialogs then
  break their lines where the original's did.
- `C` The original's look (screens, dialog and overlay pictures, fonts, pointers, sounds, 3D scenes) is read in memory
  from the player's archives (`Gfx24.dat`, `Sound16.dat`, `Scenes.dat`: [formats/dat-archives.md](formats/dat-archives.md)).
  Which file goes where is in `game/Skins/original.json`, the places (`L.Orig`) measured on the original's screens; the
  scenes' cameras, bays and shelves come from their `.3ds` helpers (play_01/02, CamFocus, car_01..12, Shelf01/02,
  BoundBox). LOOK: OPENGG draws OpenGG's own look instead; a skin can replace either.

## Sounds

The original's sound files, played through the skin (`game/Skins/original.json`), each at its own pitch and all at one
level, the music too; where the original is silent, so is OpenGG in its look. Measured by recording the original's
output while it was driven through the game and matching every action against its sound files; `--soundcheck` repeats
the actions on OpenGG, at the same places, so the two can be compared.

- `M` A press clicks on the command plates, OK and CANCEL, Go Back To…, the JunkYard's and the Car Lot's arrows, the
  JunkYard's planks, Place Bid, Skip Car, and the sign-in's NEW and CREDITS. The rest have sounds of their own or none.
- `M` A box opening sounds (errors, Repair, Scrap, Buy, Auction Car, Winning Bid, the Impact Wrench's box...), and closed
  with its buttons another, after the click, together with the action's own (Buy: the till; Repair, Scrap: a flush). The
  Job Request comes up with a phone's beeps, Job Help with a sound of its own; leaving a job with Exit closes its box
  silently.
- `M` Each view tab has its sound, but not when already shown; back in the WorkShop from another screen, the view shown
  sounds its tab (COMPLETE's excepted).
- `M` Picking a tool sounds (the tool in hand does not); Show Condition sounds as it comes on and as it goes off.
- `M` A part coming off (by a click, with its last bolt, into the Purchase Bin) and a part put on sound; the wrench's box
  opening to take bolts out adds a magnet; a bolt out and a bolt in sound different; so does a click that does nothing,
  the wrench in the air. With the last bolt out, the bolt's and the part's sounds come together and the box closes
  silently. A part given back from the Purchase Bin is silent.
- `M` The spray, whatever the brush, while the button is held, cut at the release; a snapshot's shutter. Picking a colour
  or a brush is silent.
- `M` The Catalog's opening, a section's tab, a page turned.
- `M` Signing in; music on the sign-in sheet, other music on the credits, none on the game's screens.
- `M` The JunkYard's and the Car Lot's camera walks: a footstep every 0.42 s while it glides (the lot's 0.43–0.47 s), the
  first 0.21 s in.
- `M` Bolt mode coming in on a wheel: a zoom, with the magnet and the box; going back out, only the button's click.
- `M` The Auction goes on while a box is up (a bid over your cash; the win came right after its OK); ">>" shows before
  the Current Bid (white, at 520, 135) while your bid leads. A crowd's chatter loops while its screen is up; each rival
  bid is one of the auctioneer's nine calls, in an order of his own for each car, round and round; your bid only clicks;
  nobody bangs a gavel. A car won: the till and the Winning Bid! box; your car sold: the till and Sold!.
- `M` The job's end: as the last part goes on, the car starts up to a fanfare (one of two); 2.7 s later the till rings,
  instead of the dialog's sound. *Three jobs, the till at 2.67–2.73 s.*
- `M` The Car Lot, once a run, on the first visit: a crow calls 21.2 s in and a car drives off 31.2 s in. *The same in two
  runs to a tenth of a second; later visits were quiet.*

## Not yet

- Nothing known is left undone.

## Differences on purpose

- **Money to the cent.** The measured game (the 2002 release in Windows' compatibility mode) charged whole dollars
  ($13.06 took $13) and dropped your cash's cents on payday: a bug of the old program on today's systems, not a rule.
  OpenGG charges and pays exactly what the dialogs show.
- **Leaving a job keeps your cash.** The measured game saved a mechanic who left a job and then looked at the credits with
  the job's leftover budget as their cash: a bug too.
- **A window or the full screen.** The original ran full screen at 640 × 480 only (16-bit). OpenGG opens in a window
  sized to the screen, or full screen (F11, Alt+Enter), kept at 4:3 with black bars. The original's look draws its 3D
  at the original's 380 × 257 and scales it with the rest; OpenGG's own look draws it at the screen's resolution.
- **Full colour.** The original's 16-bit colour (5-6-5) cuts the low bits, so dark colours come out darker and banded (a
  dark blue of 12 shows as 8). OpenGG draws in full colour; the fits of its light allow for the cut.
- **Decals without dots.** The original paints a decal only where its screen pixels land, so where the paint picture is
  finer than the screen (a panel at a slant, a small panel drawn big) the decal comes out dotted. OpenGG also fills
  between neighbouring pixels of the decal on the same surface; everything else is as measured.
- **No waiting for the old program.** The original takes about a second to bring in the WorkShop after signing in (the
  mechanic's row outlined meanwhile) and draws each new screen in pieces over a few frames; OpenGG shows it whole at
  once.
- **Saved as you go.** The original writes a mechanic only when a job is taken or done, when you leave the WorkShop for
  the Car Lot, the Auction or the JunkYard, and at Exit (giving a job up writes nothing), so a crash loses what came
  after. OpenGG saves after every change, and drops what the original would never have kept (a shelf first stocked for
  a job given up).
- **Keys and mouse of our own**: Shift+click on a part takes it off, bolts and all, the mouse wheel zooms, 1 to 4 pick
  the view's tools (4 held is Start Engine), Esc is bolt mode's CANCEL or stops aiming a decal, Enter and Esc answer a box, C holds Show
  Condition. The original reacts to no key but the arrows and the number pad's.
- **No CD Player.** The original has a CD Player box (its pictures are in the archives: current track, total tracks,
  PLAY and the rest) that probably plays the game CD's music tracks; no key brings it up, and an installed copy without
  the CD has no tracks for it. OpenGG leaves it out.
- **OpenGG's own look** (without a skin) is a modern take, not a copy: the WorkShop keeps the plain grey, the camera and
  the key light's direction, and adds soft shadows, a studio's reflections, ambient occlusion and a filmic tone curve;
  damaged parts keep their measured colours under a rusty sheet's streaks; parts and bolts slide on and off and the view
  glides. The original's look is left as measured.
- **The sign-in sheet has our additions**: the buttons under the table (GAME FOLDER…, CONTENT, LOOK, SCREEN, SCALE,
  IMPORT MECHANICS…), the line about the game folder and a BETA stamp by the title.
- The placeholder car (a Norland sedan, the beta's one model) and all `ai_` assets are our own; there is no Snap-on,
  Mekada or Head Games branding.

## Still to measure

1. When the original shows its full-screen picture of the WorkShop's controls (it waits for a key). Not for a new
   mechanic, nor with any single key (letters, digits, F1–F12, the number pad, punctuation, Tab, Space, Backspace,
   Enter, Esc, Insert, Delete, Home, End, Page Up and Down, Pause), nor Ctrl or Shift with a letter or a digit, on the
   sign-in sheet or in the WorkShop, empty or not, nor a click on the arrow keys' hint, the ASSEMBLED tag, the Parts
   Bin's tab or page number, the title, the logo, the mechanic's name or the money box. The picture is not loaded at
   the start.
2. What makes a region's how-bad words the mildest or the strongest (not its colours: OpenGG draws them with each kind
   of job's measured odds), and the colours of the whole car's and a single part's damage.
3. The Auction's step to the dollar, and how stripped its cars are at each skill. OpenGG's step is a fit (see
   Auction): the whole cars of a Mustang and a Pickup come $5 off, and so do a few cars bought. With it and the cars
   drawn as measured, its steps run a little high: at Novice a median of 195 (measured about 175), at Mekada 100
   (measured 90, two in three $110 or less; OpenGG's a little over half).

Tried and not in the tested game: the manual's "Model In Photo" (the 2002 archives have no picture for it), a SETTINGS
button on the sign-in sheet (its picture is in the archives, never loaded), and the key chords passed round as cheats
(Delete+End+Page Down, Insert+Home+Page Up: nothing).
