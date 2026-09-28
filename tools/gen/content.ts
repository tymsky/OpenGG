// Placeholder game data: tools, fastener kinds, jobs, names, decals, paints, rules.
// Structure follows the original game (see docs/FIDELITY.md);
// every number and every line of text here is our own placeholder, to be calibrated.

import type { DecalDef, FastenerKindDef, JobTemplateDef, PaintDef, RulesDef, ToolDef } from './types.ts';

export const FASTENER_KINDS: FastenerKindDef[] = [
  { id: 'bolt', name: 'Bolt', visual: 'cube', size: 0.012 },
  { id: 'small_bolt', name: 'Small bolt', visual: 'cube', size: 0.008 },
];

// The original has one part tool, the Impact Wrench (the 1999 manual calls it the Air Ratchet): it takes parts off, puts them on and does the
// bolts (the original's manual). Body Paint lives on the BODY tab, the Camera on COMPLETE,
// Start Engine on COMPLETE (and ENGINE).
export const TOOLS: ToolDef[] = [
  { id: 'ratchet', name: 'Impact Wrench', action: 'fasten', fastenerKinds: ['bolt', 'small_bolt'], regions: ['complete', 'engine', 'body', 'running_gear'], icon: 'icon.tool.ratchet', sound: 'snd.ratchet', hotkey: '1' },
  { id: 'paint', name: 'Body Paint', action: 'paint', fastenerKinds: [], regions: ['body'], icon: 'icon.tool.paint', sound: 'snd.spray', hotkey: '2' },
  { id: 'camera', name: 'Camera', action: 'camera', fastenerKinds: [], regions: ['complete'], icon: 'icon.tool.camera', sound: 'snd.camera', hotkey: '3' },
  { id: 'key', name: 'Start Engine', action: 'start_engine', fastenerKinds: [], regions: ['complete', 'engine'], icon: 'icon.tool.key', hotkey: '4' },
];

const CHROME_WHEELS = ['wheel_r15_chrome', 'wheel_r16_chrome'];
const ALLOY_WHEELS = ['wheel_r15_alloy', 'wheel_r16_alloy', ...CHROME_WHEELS];

export const JOBS: JobTemplateDef[] = [
  // easy
  { id: 'air_filter', difficulty: 'easy', text: "Hi, I'm {name}. My {car} feels tired lately. Could you change the *air filter*?", requirements: [{ type: 'fix', families: ['air_filter'], count: [1, 1] }], feeMultiplier: 1, weight: 3 },
  { id: 'flat_tire', difficulty: 'easy', text: "Oh dear, I've got a *flat tire* and my grandson is out of town. Can you help an old lady?", requirements: [{ type: 'fix', families: ['wheel'], count: [1, 1] }], feeMultiplier: 1, weight: 3 },
  { id: 'dead_battery', difficulty: 'easy', text: "My {car} is *dead*. Not a sound when I turn the key.", requirements: [{ type: 'fix', families: ['battery'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'plugs', difficulty: 'easy', text: 'The engine *misfires* at idle. I bet it needs new *spark plugs*.', requirements: [{ type: 'fix', families: ['spark_plug'], count: [2, 4] }], feeMultiplier: 1, weight: 2 },
  { id: 'stolen_wheel', difficulty: 'easy', text: 'Some punks stole a *wheel* off my {car}! Put a new one on, will ya?', requirements: [{ type: 'missing', families: ['wheel'], count: [1, 2] }], feeMultiplier: 1, weight: 2 },
  { id: 'loud', difficulty: 'easy', text: "It's gotten really *loud*. Sounds like a race car, but not the good kind.", requirements: [{ type: 'fix', families: ['muffler'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'windshield', difficulty: 'easy', text: 'A truck threw a rock and cracked my *windshield*.', requirements: [{ type: 'fix', families: ['windshield'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'fender', difficulty: 'easy', text: 'I backed into a post at the grocery store. Could you fix the *fender*?', requirements: [{ type: 'fix', families: ['fender'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  // medium
  { id: 'brakes', difficulty: 'medium', text: 'The *brakes* grind every time I stop. Scares me half to death.', requirements: [{ type: 'fix', families: ['brake_pads'], count: [1, 2] }, { type: 'fix', families: ['brake_rotor'], count: [0, 1] }], feeMultiplier: 1, weight: 3 },
  { id: 'starter', difficulty: 'medium', text: "It *won't start*. It just *clicks* when I turn the key.", requirements: [{ type: 'fix', families: ['starter'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'alternator', difficulty: 'medium', text: 'The *battery* keeps going dead and the headlights flicker at night.', requirements: [{ type: 'fix', families: ['alternator'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'overheat', difficulty: 'medium', text: 'It *overheats* in traffic. The needle goes all the way into the red.', requirements: [{ type: 'fix', families: ['radiator', 'water_pump', 'thermostat', 'radiator_fan'], count: [1, 2] }], feeMultiplier: 1, weight: 2 },
  { id: 'shocks', difficulty: 'medium', text: 'My {car} bounces all over the road. I think the *shocks* are shot.', requirements: [{ type: 'fix', families: ['strut', 'shock'], count: [1, 2] }], feeMultiplier: 1, weight: 2 },
  { id: 'spoiler', difficulty: 'medium', text: 'I want my {car} to look fast. Put a *rear spoiler* on it.', requirements: [{ type: 'install', families: ['spoiler'], count: [1, 1], parts: ['spoiler_wing'] }], feeMultiplier: 1, weight: 1, bodyStyles: ['sedan', 'coupe'] },
  { id: 'chrome_wheels', difficulty: 'medium', text: 'Put some shiny *chrome wheels* on my {car}. All four!', requirements: [{ type: 'install', families: ['wheel'], count: [4, 4], parts: CHROME_WHEELS }], feeMultiplier: 1, weight: 1 },
  { id: 'doors', difficulty: 'medium', text: 'My ex kicked in the *doors* of my {car}. Make them look like new.', requirements: [{ type: 'fix', families: ['door'], count: [1, 2] }], feeMultiplier: 1, weight: 1 },
  { id: 'rattle', difficulty: 'medium', text: 'Something *rattles underneath* and it smells like rotten eggs.', requirements: [{ type: 'fix', families: ['exhaust_front'], count: [1, 1] }], feeMultiplier: 1, weight: 1 },
  // hard
  { id: 'head', difficulty: 'hard', text: "It *overheats* and there's *white smoke*. My brother-in-law says it's the *cylinder head*.", requirements: [{ type: 'fix', families: ['cylinder_head'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'gearbox', difficulty: 'hard', text: "It *won't go into gear* anymore. It grinds and slips.", requirements: [{ type: 'fix', families: ['transmission'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'stripped', difficulty: 'hard', text: 'Thieves stripped my {car} last night! The *battery*, the *starter* and the *air filter* are gone.', requirements: [{ type: 'missing', families: ['battery'], count: [1, 1] }, { type: 'missing', families: ['starter'], count: [1, 1] }, { type: 'missing', families: ['air_filter'], count: [1, 1] }], feeMultiplier: 1, weight: 1 },
  { id: 'mean_look', difficulty: 'hard', text: 'Make my {car} look mean: a *hood scoop*, a *sport muffler* and *alloy wheels* up front.', requirements: [{ type: 'install', families: ['hood'], count: [1, 1], parts: ['hood_sedan_scoop', 'hood_coupe_scoop', 'hood_pickup_scoop'] }, { type: 'install', families: ['muffler'], count: [1, 1], parts: ['muffler_sedan_sport', 'muffler_coupe_sport', 'muffler_pickup_sport'] }, { type: 'install', families: ['wheel'], count: [2, 2], parts: ALLOY_WHEELS }], feeMultiplier: 1, weight: 1 },
  { id: 'blue_smoke', difficulty: 'hard', text: '*Blue smoke* pours out of the tailpipe when I start it up.', requirements: [{ type: 'fix', families: ['piston'], count: [1, 3] }], feeMultiplier: 1, weight: 1 },
  // expert
  { id: 'knock', difficulty: 'expert', text: "There's a deep *knocking* from the engine and it's getting worse every day.", requirements: [{ type: 'fix', families: ['piston'], count: [1, 2] }, { type: 'fix', families: ['crankshaft'], count: [1, 1] }], feeMultiplier: 1, weight: 2 },
  { id: 'rebuild', difficulty: 'expert', text: 'The engine *blew up* on the highway. Rebuild it: *pistons*, *crankshaft*, *camshaft*, the works.', requirements: [{ type: 'fix', families: ['piston'], count: [2, 4] }, { type: 'fix', families: ['crankshaft'], count: [1, 1] }, { type: 'fix', families: ['camshaft'], count: [1, 1] }], feeMultiplier: 1, weight: 1 },
];

export const NAMES = {
  first: ['Bob', 'Linda', 'Carl', 'Doris', 'Earl', 'Fran', 'Gus', 'Helen', 'Ike', 'June', 'Karl', 'Lou', 'Marge', 'Ned', 'Opal', 'Pete', 'Rita', 'Sal', 'Tina', 'Vern', 'Wanda', 'Hank', 'Mabel', 'Dale', 'Rosa', 'Stan', 'Irma', 'Walt'],
  last: ['Anders', 'Baxter', 'Crowley', 'Dunn', 'Ellis', 'Fowler', 'Grant', 'Hughes', 'Iverson', 'Jensen', 'Kowalski', 'Lambert', 'Meyer', 'Novak', 'Olsen', 'Parker', 'Quinn', 'Reyes', 'Schultz', 'Tate', 'Underwood', 'Vance', 'Wozniak', 'Young'],
};

export const DECALS: DecalDef[] = [
  { id: 'flames', name: 'Hot flames', texture: 'tex.decal.flames', price: 45, aspect: 2, uses: 5 },
  { id: 'stripes', name: 'Racing stripes', texture: 'tex.decal.stripes', price: 35, aspect: 2, uses: 5 },
  { id: 'star', name: 'Lone star', texture: 'tex.decal.star', price: 15, aspect: 1, uses: 5 },
  { id: 'number', name: 'Race number', texture: 'tex.decal.number', price: 20, aspect: 1, uses: 5 },
  { id: 'checker', name: 'Checkered flag', texture: 'tex.decal.checker', price: 25, aspect: 1.5, uses: 5 },
  { id: 'bolt', name: 'Lightning bolt', texture: 'tex.decal.bolt', price: 20, aspect: 2, uses: 5 },
];

// 27 colours in 3 columns x 9 rows, like the original's palette (its manual), listed row by row.
const PALETTE: [string, string][] = [
  ['Black', '#030303'], ['Navy', '#161a53'], ['Blue', '#4855a0'],
  ['Maroon', '#511611'], ['Plum', '#622c5f'], ['Violet', '#7c3cc1'],
  ['Red', '#ab2624'], ['Raspberry', '#bc3c7c'], ['Magenta', '#d454d4'],
  ['Orange', '#fc8406'], ['Dusty Pink', '#d89a95'], ['Light Pink', '#f1aff1'],
  ['Olive', '#766127'], ['Grey', '#737d77'], ['Lavender', '#898cc6'],
  ['Dark Green', '#125412'], ['Teal', '#2b6c6c'], ['Slate Blue', '#6a7fb8'],
  ['Green', '#2bb12b'], ['Sea Green', '#3cc27c'], ['Light Cyan', '#85e3e3'],
  ['Yellow Green', '#78b73a'], ['Pale Green', '#89ca8a'], ['Pale Cyan', '#bff2f2'],
  ['Yellow', '#f7f60e'], ['Cream', '#eae9ab'], ['White', '#e8ebea'],
];
export const PAINTS: PaintDef[] = PALETTE.map(([name, color]) => ({ id: name.toLowerCase().replace(/\s+/g, '_'), name, color }));

export const RULES: RulesDef = {
  // Measured in the 2002 release (the 1999 manual said $10,000).
  startCash: 5000,
  // Skill is a rating from several factors; the manual names cars repaired. In the original the tutorial
  // jobs keep you Learning and finishing them makes you Novice. Later levels are ours.
  skills: [
    { name: 'Learning', cash: 0, cars: 0, difficulties: ['easy'] },
    { name: 'Novice', cash: 0, cars: 3, difficulties: ['easy', 'medium'] },
    { name: 'Handy', cash: 15000, cars: 8, difficulties: ['medium', 'easy'] },
    { name: 'Expert', cash: 35000, cars: 15, difficulties: ['medium', 'hard'] },
    { name: 'Master', cash: 120000, cars: 25, difficulties: ['hard', 'expert', 'medium'] },
  ],
  conditionSpread: 0.1,
  // 986 parts of 26 new shelves: 23 % black, 25 % red, 25 % yellow, 27 % green.
  junkConditionWeights: [1, 1, 1, 1],
  // After what the car that came in gets (the custom parts it has not, a won car's missing ones): 15 to 21 at random.
  junkShelfRandom: [15, 21],
  // None: a shelf loses only what is bought from it and gains only what is scrapped (17 jobs' ends in the saves).
  junkChurn: [0, 0],
  // Measured on 417 offers: under $250 Easy, under $550 Medium, under $1050 Hard.
  difficultyFees: [250, 550, 1050],
  jobWearWeights: [0.02, 0.06, 0.2, 0.72],
  jobDamageWeights: [0.25, 0.45, 0.3, 0],
  auctionWearWeights: [0.12, 0.28, 0.3, 0.3],
  auctionMissingChance: 0.05,
  feePartFactor: 1.35,
  laborPerPart: 6,
  laborPerFastener: 1.5,
  difficultyFee: { easy: 1, medium: 1.25, hard: 1.6, expert: 2 },
  auctionDuration: 20.5,
  auctionTick: 2,
  auctionDrop: 25,
  // Measured: every car opens at $100; rivals bid on a 0.755 s beat (about one beat in six without a bid) up to
  // 12.7-15.1 steps above it.
  auctionOpeningBid: 100,
  auctionStep: [0.06, 0.075],
  auctionRivalSteps: [12.7, 15.1],
  auctionBeat: 0.755,
  auctionBeatSkip: 0.17,
  // Measured: painting costs nothing.
  paintPrice: 0,
  diagnosis: {
    dead: ['battery'],
    noCrank: ['starter', 'flywheel', 'crankshaft', 'engine_block'],
    noStart: ['ignition_coil', 'distributor', 'carburetor', 'intake_manifold', 'cylinder_head', 'camshaft', 'airbox'],
    rough: ['spark_plug', 'piston', 'crankshaft', 'camshaft', 'cylinder_head', 'air_filter'],
    loud: ['muffler', 'exhaust_front', 'exhaust_manifold'],
    workingCondition: 1,
  },
};
