// JSON schema of a content pack, as written by this generator (ai_*.json).
//
// The game (C#, core/OpenGG.Core/Content) reads the same JSON. Keep the two in sync.
// The engine never hard-codes cars, parts, jobs or prices: everything comes from a pack.
// The bundled pack ("ai") is generated placeholder content (files prefixed with ai_).
// The "original" pack is built at runtime from the player's own copy of the game.

export type Vec3 = [number, number, number];

/** The three work areas of a car, as in the original game (plus the "complete" overview). */
export type Region = 'engine' | 'body' | 'running_gear';
export const REGIONS: Region[] = ['engine', 'body', 'running_gear'];

/**
 * Part condition, the original's four colours:
 * 3 = green (good), 2 = yellow (minor damage), 1 = red (major damage, still repairable),
 * 0 = black (destroyed: can only be scrapped).
 */
export type Condition = 0 | 1 | 2 | 3;
export const GOOD: Condition = 3;

export interface FastenerKindDef {
  id: string;
  name: string;
  /** How the engine draws it. The original shows bolts as small white cubes. */
  visual: 'cube' | 'hex' | 'lug' | 'none';
  /** Half-size in meters (rendering + picking). */
  size: number;
}

/**
 * pointer: the default cursor (take parts off / put them on).
 * fasten: undoes and tightens bolts of the listed kinds.
 * paint: body paint (spray can) and decals.
 * camera: snapshot of the car.
 * start_engine: turn the key, listen for trouble.
 */
export type ToolAction = 'pointer' | 'fasten' | 'paint' | 'camera' | 'start_engine';

export interface ToolDef {
  id: string;
  name: string;
  action: ToolAction;
  fastenerKinds: string[];
  /** Regions (work-area tabs) where the tool is offered. "complete" = the overview tab. */
  regions: (Region | 'complete')[];
  icon: string;
  sound?: string;
  hotkey?: string;
}

export interface FastenerDef {
  kind: string;
  /** Part-local position. */
  pos: Vec3;
  /** Part-local unit vector pointing out of the surface (unscrew direction). */
  dir: Vec3;
}

export interface PartDef {
  id: string;
  name: string;
  /** Catalog grouping (engine, electrical, glass, wheels...). */
  category: string;
  /** Slots with the same slotType accept this part. */
  slotType: string;
  model: string;
  /** Catalog (new) price: the top of the part's price range. */
  price: number;
  /** The bottom of the price range. JunkYard, repair and scrap prices move between the two. */
  priceMin?: number;
  fasteners: FastenerDef[];
  /** Aftermarket / custom part. */
  custom?: boolean;
  /** Added to the car value while installed. */
  valueBonus?: number;
  description?: string;
}

export interface SlotDef {
  id: string;
  name: string;
  slotType: string;
  /** Functional family shared across cars (e.g. "air_filter"); used by jobs and diagnosis. */
  family: string;
  region: Region;
  /** The slot this part is mounted on. Children must be removed before the parent. */
  parent: string | null;
  /** Slots whose parts must be removed (or opened) before this one is reachable. */
  blockedBy: string[];
  /** Car-local position of the part origin. */
  pos: Vec3;
  /** Car-local Euler rotation (XYZ order, radians). */
  rot: Vec3;
  /** Car-local direction the part travels when taken off. */
  removeDir: Vec3;
  required: boolean;
  defaultPart: string | null;
  /** Hinged parts (hood). Rotation around a part-local axis through the part origin. */
  openable?: { axis: Vec3; angle: number };
}

export interface CarModelDef {
  id: string;
  make: string;
  name: string;
  year: number;
  bodyStyle: 'sedan' | 'coupe' | 'pickup';
  engineLabel: string;
  /** The static shell (floor, pillars, firewall...). Panels are parts. */
  bodyModel: string;
  /** Material name in the body/panel models that receives the paint color. */
  paintMaterial: string;
  defaultPaint: string;
  /** Value of a perfect, stock car. */
  baseValue: number;
  /** Skill level index needed before it shows up at the auction. */
  minSkill: number;
  dims: { length: number; width: number; height: number; wheelbase: number };
  slots: SlotDef[];
}

export type Difficulty = 'easy' | 'medium' | 'hard' | 'expert';

/**
 * fix: parts in these families arrive damaged and must end up green.
 * missing: these slots arrive empty and must be filled with a green part.
 * install: one of `parts` (e.g. a custom upgrade) must be installed in a slot of these families.
 */
export interface JobRequirementDef {
  type: 'fix' | 'missing' | 'install';
  families: string[];
  count: [number, number];
  parts?: string[];
}

export interface JobTemplateDef {
  id: string;
  /** Request text. Words between *asterisks* are highlighted. {name} = customer, {car} = car name. */
  text: string;
  /** Shown by Job Help while you work (the original's NAG phase). */
  nag?: string;
  /** Shown when the job is done (the original's THANK phase). */
  thanks?: string;
  difficulty: Difficulty;
  requirements: JobRequirementDef[];
  feeMultiplier: number;
  weight: number;
  /** Only for these body styles (all if omitted). */
  bodyStyles?: CarModelDef['bodyStyle'][];
}

export interface DecalDef {
  id: string;
  name: string;
  texture: string;
  price: number;
  /** width / height */
  aspect: number;
  /** How many times one purchase can be applied. */
  uses?: number;
  /** The Decal Browser's palette recolours it. */
  tint?: boolean;
}

export interface PaintDef {
  id: string;
  name: string;
  color: string;
}

export interface SkillLevelDef {
  name: string;
  /** Needs your cash to have been at least this much at some point... */
  cash: number;
  /** ...and at least this many cars repaired (jobs done plus own cars sold). */
  cars?: number;
  /** Job difficulties offered at this level. */
  difficulties: Difficulty[];
}

/** Which part families make the engine misbehave when they are missing or broken (Start Engine). */
export interface DiagnosisDef {
  /** Nothing at all (no power). */
  dead: string[];
  /** Clicks, won't crank. */
  noCrank: string[];
  /** Cranks but won't start. */
  noStart: string[];
  /** Runs, but knocks or shakes. */
  rough: string[];
  /** Runs, but very loud. */
  loud: string[];
  /** Minimum condition that counts as working. */
  workingCondition: Condition;
}

/**
 * Tunable rules. The money formulas measured in the original are fixed in the engine
 * (core/OpenGG.Core/Sim/Economy.cs); these are the numbers around them.
 */
export interface RulesDef {
  /** Measured in the 2002 release: $5000. */
  startCash: number;
  skills: SkillLevelDef[];
  /** A part's condition is its colour plus a random extra in ±this (measured: ±0.1). */
  conditionSpread: number;
  /** Condition odds for JunkYard stock, by condition [black, red, yellow, green]. Black sells as red. */
  junkConditionWeights: [number, number, number, number];
  /** Parts of a new JunkYard shelf picked at random, [min, max], after those it gets for the car that came in
   *  (measured: 15 to 21). */
  junkShelfRandom: [number, number];
  /** Shelf items that come and go after each job, [min, max]. */
  junkChurn: [number, number];
  /** Fees from which a job reads Medium, Hard, Expert (measured: 250, 550, 1050). */
  difficultyFees: [number, number, number];
  /** Condition odds for parts that are not part of the job, in customer cars. */
  jobWearWeights: [number, number, number, number];
  /** Condition odds for damaged job parts ("fix" requirements). */
  jobDamageWeights: [number, number, number, number];
  /** Condition odds for parts of auction cars. */
  auctionWearWeights: [number, number, number, number];
  auctionMissingChance: number;
  /** Fee = (parts * feePartFactor + labor) * difficulty factor * template factor. */
  feePartFactor: number;
  laborPerPart: number;
  laborPerFastener: number;
  difficultyFee: Record<Difficulty, number>;
  /** Measured: a car is on the block for about 20.5 s. */
  auctionDuration: number;
  /** Measured: the asking price drops by auctionDrop every auctionTick seconds. */
  auctionTick: number;
  auctionDrop: number;
  /** Measured: every car goes on the block at a Current Bid of $100, yours too. */
  auctionOpeningBid: number;
  /** Fixed step per car that each bid adds to the asking price, as a fraction of the car's value. */
  auctionStep: [number, number];
  /** The other bidders go on while the asking price is at most the opening bid plus this many steps. */
  auctionRivalSteps: [number, number];
  /** Seconds between the beats the other bidders bid on (measured: 0.755 s). */
  auctionBeat: number;
  /** The share of beats that go by without a bid (measured: about one in six). */
  auctionBeatSkip: number;
  paintPrice: number;
  diagnosis: DiagnosisDef;
}

export interface ContentPack {
  id: string;
  name: string;
  tools: ToolDef[];
  fastenerKinds: FastenerKindDef[];
  parts: PartDef[];
  cars: CarModelDef[];
  jobs: JobTemplateDef[];
  names: { first: string[]; last: string[] };
  /** Portrait asset ids for customers. */
  portraits: string[];
  decals: DecalDef[];
  paints: PaintDef[];
  rules: RulesDef;
}

/** Maps logical asset ids (used in content) to files. */
export interface AssetManifest {
  pack: string;
  name: string;
  version: number;
  data: Record<string, string>;
  models: Record<string, string>;
  textures: Record<string, string>;
  sounds: Record<string, string>;
  icons: Record<string, string>;
}
