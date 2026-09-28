// Assembles placeholder cars: slots (where parts go), part definitions and models.

import * as THREE from 'three';
import type { CarModelDef, FastenerDef, PartDef, Region, SlotDef, Vec3 } from './types.ts';
import { ModelBuilder, M, type MatDef } from './geo.ts';
import * as P from './parts.ts';
import { buildBody, hingeOf, hoodFrontOf, outlineY, SEDAN, COUPE, PICKUP, type BodySpec } from './body.ts';
import { buildBody2, hood2 } from './body2.ts';
import { caliper2, rotor2, wheel2 } from './parts2.ts';

// ---- registry --------------------------------------------------------------

type PartInput = Omit<PartDef, 'model' | 'fasteners'> & { repairable?: boolean; selfFastening?: boolean };

// The original only knows "bolts". Big ones take the Impact Wrench; small ones on engine
// accessories and body panels take the Impact Wrench (a guess, see docs/FIDELITY.md).
const BIG_KINDS = new Set(['bolt_l', 'lug_nut']);
const RUNNING_GEAR_CATEGORIES = new Set(['wheels', 'brakes', 'suspension', 'exhaust', 'drivetrain']);

function normalizeFasteners(category: string, fs: FastenerDef[]): FastenerDef[] {
  return fs
    .filter((f) => f.kind !== 'spark_plug' && f.kind !== 'spin_on')
    .map((f) => ({ ...f, kind: RUNNING_GEAR_CATEGORIES.has(category) || BIG_KINDS.has(f.kind) ? 'bolt' : 'small_bolt' }));
}

export class Registry {
  parts = new Map<string, PartDef>();
  models = new Map<string, ModelBuilder>();

  part(input: PartInput, built: P.BuiltPart): string {
    const { repairable: _r, selfFastening: _s, ...def } = input;
    void _r;
    void _s;
    if (this.parts.has(def.id)) return def.id;
    const model = `part.${def.id}`;
    this.models.set(model, built.model);
    // Like the original's cost ranges, which start at about a tenth of the top price.
    const priceMin = def.priceMin ?? Math.max(1, Math.round(def.price * 0.12));
    this.parts.set(def.id, { ...def, priceMin, model, fasteners: normalizeFasteners(def.category, built.fasteners) });
    return def.id;
  }

  model(id: string, b: ModelBuilder): string {
    this.models.set(id, b);
    return id;
  }
}

// ---- transform helpers -----------------------------------------------------

const r4 = (v: number) => Math.round(v * 10000) / 10000;
const rv = (v: Vec3): Vec3 => [r4(v[0]), r4(v[1]), r4(v[2])];

function mat4(pos: Vec3, rot: Vec3): THREE.Matrix4 {
  return new THREE.Matrix4().compose(new THREE.Vector3(...pos), new THREE.Quaternion().setFromEuler(new THREE.Euler(...rot, 'XYZ')), new THREE.Vector3(1, 1, 1));
}

/** Compose parent (pos, rot) with a local (pos, rot). */
export function xf(parentPos: Vec3, parentRot: Vec3, localPos: Vec3, localRot: Vec3 = [0, 0, 0]): { pos: Vec3; rot: Vec3 } {
  const m = mat4(parentPos, parentRot).multiply(mat4(localPos, localRot));
  const p = new THREE.Vector3();
  const q = new THREE.Quaternion();
  m.decompose(p, q, new THREE.Vector3());
  const e = new THREE.Euler().setFromQuaternion(q, 'XYZ');
  return { pos: rv([p.x, p.y, p.z]), rot: rv([e.x, e.y, e.z]) };
}

function point(parentPos: Vec3, parentRot: Vec3, local: Vec3): Vec3 {
  return xf(parentPos, parentRot, local).pos;
}

const add = (a: Vec3, b: Vec3): Vec3 => rv([a[0] + b[0], a[1] + b[1], a[2] + b[2]]);
const sub = (a: Vec3, b: Vec3): Vec3 => rv([a[0] - b[0], a[1] - b[1], a[2] - b[2]]);
const norm = (v: Vec3): Vec3 => {
  const l = Math.hypot(...v) || 1;
  return rv([v[0] / l, v[1] / l, v[2] / l]);
};

interface SlotOpts {
  rot?: Vec3;
  removeDir?: Vec3;
  required?: boolean;
  openable?: SlotDef['openable'];
}

class SlotList {
  slots: SlotDef[] = [];
  /** Region given to slots added from now on. */
  region: Region = 'engine';
  add(id: string, name: string, slotType: string, family: string, parent: string | null, blockedBy: string[], pos: Vec3, defaultPart: string | null, o: SlotOpts = {}): void {
    this.slots.push({
      id,
      name,
      slotType,
      family,
      region: this.region,
      parent,
      blockedBy,
      pos: rv(pos),
      rot: rv(o.rot ?? [0, 0, 0]),
      removeDir: norm(o.removeDir ?? [0, 1, 0]),
      required: o.required ?? true,
      defaultPart,
      ...(o.openable ? { openable: o.openable } : {}),
    });
  }
}

const HOOD = 'body.hood';
const TRANS = 'drivetrain.transmission';

// ---- engines ---------------------------------------------------------------

/** Inline four. Returns the car-local position of the exhaust outlet. */
function addI4(reg: Registry, S: SlotList, E: Vec3): { exhaustOutlet: Vec3 } {
  S.region = 'engine';
  const at = (p: Vec3) => add(E, p);
  const block = reg.part({ id: 'i4_block', name: 'Engine Block (2.2L I4)', category: 'engine', slotType: 'i4_block', price: 1400 }, P.i4Block());
  const head = reg.part({ id: 'i4_head', name: 'Cylinder Head (I4)', category: 'engine', slotType: 'i4_head', price: 620 }, P.i4Head());
  const cam = reg.part({ id: 'i4_camshaft', name: 'Camshaft (I4)', category: 'engine', slotType: 'i4_camshaft', price: 260 }, P.i4Camshaft());
  const vc = reg.part({ id: 'i4_valve_cover', name: 'Valve Cover (I4)', category: 'engine', slotType: 'i4_valve_cover', price: 90 }, P.valveCoverI4(M.plastic));
  reg.part({ id: 'i4_valve_cover_chrome', name: 'Chrome Valve Cover (I4)', category: 'engine', slotType: 'i4_valve_cover', price: 185, custom: true, valueBonus: 150 }, P.valveCoverI4(M.chrome));
  reg.part({ id: 'i4_valve_cover_red', name: 'Red Valve Cover (I4)', category: 'engine', slotType: 'i4_valve_cover', price: 150, custom: true, valueBonus: 110 }, P.valveCoverI4(M.red));
  const coil = reg.part({ id: 'i4_coil', name: 'Ignition Coil Pack', category: 'ignition', slotType: 'i4_coil', price: 130 }, P.coilPackI4());
  const plug = reg.part({ id: 'spark_plug', name: 'Spark Plug', category: 'ignition', slotType: 'spark_plug', price: 5 }, P.sparkPlug());
  const intake = reg.part({ id: 'i4_intake', name: 'Intake Manifold (I4)', category: 'intake', slotType: 'i4_intake', price: 310 }, P.intakeI4());
  const exm = reg.part({ id: 'i4_exhaust_manifold', name: 'Exhaust Manifold (I4)', category: 'engine', slotType: 'i4_exhaust_manifold', price: 240 }, P.exhaustManifoldI4());
  const pan = reg.part({ id: 'i4_oil_pan', name: 'Oil Pan (I4)', category: 'engine', slotType: 'i4_oil_pan', price: 95 }, P.oilPan(0.52, 0.33, 0.13));
  const filter = reg.part({ id: 'oil_filter', name: 'Oil Filter', category: 'engine', slotType: 'oil_filter', price: 9 }, P.oilFilter(M.blue));
  const crank = reg.part({ id: 'i4_crankshaft', name: 'Crankshaft (I4)', category: 'engine', slotType: 'i4_crankshaft', price: 480 }, P.crankshaft(P.I4.cylX, [-0.25, -0.13, 0, 0.13, 0.25], P.I4.beltX, 0.072));
  const fly = reg.part({ id: 'i4_flywheel', name: 'Flywheel (I4)', category: 'engine', slotType: 'i4_flywheel', price: 160 }, P.flywheel(0.13));
  const piston = reg.part({ id: 'i4_piston', name: 'Piston & Rod (I4)', category: 'engine', slotType: 'i4_piston', price: 85 }, P.piston(0.3));
  const alt = reg.part({ id: 'i4_alternator', name: 'Alternator (70A)', category: 'electrical', slotType: 'i4_alternator', price: 190 }, P.alternator(1));
  const wp = reg.part({ id: 'i4_water_pump', name: 'Water Pump (I4)', category: 'cooling', slotType: 'i4_water_pump', price: 95 }, P.waterPump());
  const pulleys: [number, number, number][] = [[P.I4.crankY, 0, 0.072], [0.13, 0, 0.047], [0.1, 0.23, 0.032]];
  const belt = reg.part({ id: 'i4_belt', name: 'Drive Belt (I4)', category: 'engine', slotType: 'i4_belt', price: 28 }, P.driveBelt(pulleys, [0.04, 0.13]));
  const thermo = reg.part({ id: 'i4_thermostat', name: 'Thermostat (I4)', category: 'cooling', slotType: 'i4_thermostat', price: 32 }, P.thermostat());
  const starter = reg.part({ id: 'i4_starter', name: 'Starter (I4)', category: 'electrical', slotType: 'i4_starter', price: 165 }, P.starter());

  S.add('eng.block', 'Engine Block', 'i4_block', 'engine_block', null, [HOOD], at([0, 0, 0]), block);
  S.add('eng.head', 'Cylinder Head', 'i4_head', 'cylinder_head', 'eng.block', [HOOD, 'eng.valve_cover', 'eng.camshaft'], at([0, 0.3, 0]), head);
  S.add('eng.camshaft', 'Camshaft', 'i4_camshaft', 'camshaft', 'eng.head', [HOOD, 'eng.valve_cover'], at([0, 0.425, -0.05]), cam);
  S.add('eng.valve_cover', 'Valve Cover', 'i4_valve_cover', 'valve_cover', 'eng.head', [HOOD], at([0, 0.4, 0]), vc);
  S.add('ign.coil', 'Ignition Coil Pack', 'i4_coil', 'ignition_coil', 'eng.valve_cover', [HOOD], at([0, 0.4895, 0.05]), coil);
  P.I4.cylX.forEach((x, i) => {
    S.add(`ign.plug${i + 1}`, `Spark Plug #${i + 1}`, 'spark_plug', 'spark_plug', 'eng.head', [HOOD, 'ign.coil'], at([x, 0.415, 0.05]), plug);
  });
  S.add('int.manifold', 'Intake Manifold', 'i4_intake', 'intake_manifold', 'eng.head', [HOOD, 'int.airbox'], at([0, 0.35, -0.15]), intake, { removeDir: [0, 0.4, -1] });
  S.add('exh.manifold', 'Exhaust Manifold', 'i4_exhaust_manifold', 'exhaust_manifold', 'eng.head', [HOOD, 'exh.front'], at([0, 0.35, 0.15]), exm, { removeDir: [0, 0.4, 1] });
  S.add('eng.flywheel', 'Flywheel', 'i4_flywheel', 'flywheel', 'eng.block', [TRANS], at([-0.295, P.I4.crankY, 0]), fly, { removeDir: [-1, 0, 0] });
  S.add('eng.oil_pan', 'Oil Pan', 'i4_oil_pan', 'oil_pan', 'eng.block', ['eng.flywheel'], at([0, 0, 0]), pan, { removeDir: [0, -1, 0] });
  S.add('eng.oil_filter', 'Oil Filter', 'oil_filter', 'oil_filter', 'eng.block', [], at([-0.12, 0.1, 0.17]), filter, { removeDir: [0, 0, 1] });
  const pistons = P.I4.cylX.map((_, i) => `eng.piston${i + 1}`);
  S.add('eng.crankshaft', 'Crankshaft', 'i4_crankshaft', 'crankshaft', 'eng.block', ['eng.oil_pan', 'eng.belt', 'eng.flywheel', ...pistons], at([0, P.I4.crankY, 0]), crank, { removeDir: [0, -1, 0] });
  P.I4.cylX.forEach((x, i) => {
    S.add(pistons[i], `Piston #${i + 1}`, 'i4_piston', 'piston', 'eng.block', [HOOD, 'eng.head', 'eng.oil_pan'], at([x, P.I4.pistonTopY, 0]), piston);
  });
  S.add('eng.belt', 'Drive Belt', 'i4_belt', 'drive_belt', 'eng.block', [HOOD], at([P.I4.beltX, 0, 0]), belt, { removeDir: [0.4, 1, 0] });
  S.add('elec.alternator', 'Alternator', 'i4_alternator', 'alternator', 'eng.block', [HOOD, 'eng.belt'], at([0.265, 0.1, 0.23]), alt, { removeDir: [0, 1, 0.6] });
  S.add('cool.water_pump', 'Water Pump', 'i4_water_pump', 'water_pump', 'eng.block', [HOOD, 'eng.belt'], at([0.3, 0.13, 0]), wp, { removeDir: [1, 0.4, 0] });
  S.add('cool.thermostat', 'Thermostat', 'i4_thermostat', 'thermostat', 'eng.head', [HOOD], at([0.25, 0.36, -0.08]), thermo, { removeDir: [1, 0.5, 0] });
  S.add('elec.starter', 'Starter', 'i4_starter', 'starter', 'eng.block', [], at([-0.2, 0.07, -0.225]), starter, { removeDir: [0, -1, 0] });

  const boxLocal: Vec3 = [0.36, 0.32, -0.52];
  const throttle = add([0, 0.35, -0.15], P.I4_THROTTLE);
  const tubeEnd = sub(throttle, boxLocal);
  const box = reg.part({ id: 'airbox_i4', name: 'Air Filter Box', category: 'intake', slotType: 'airbox_i4', price: 70 }, P.airboxBase(tubeEnd));
  const lid = reg.part({ id: 'airbox_lid_i4', name: 'Air Box Lid', category: 'intake', slotType: 'airbox_lid_i4', price: 35 }, P.airboxLid());
  const af = reg.part({ id: 'air_filter_panel', name: 'Air Filter (panel)', category: 'intake', slotType: 'air_filter_panel', price: 16 }, P.panelFilter(P.EXTRA.ceramic));
  reg.part({ id: 'air_filter_panel_perf', name: 'Performance Air Filter (panel)', category: 'intake', slotType: 'air_filter_panel', price: 49, custom: true, valueBonus: 40 }, P.panelFilter(M.red));
  S.add('int.airbox', 'Air Filter Box', 'airbox_i4', 'airbox', null, [HOOD], at(boxLocal), box);
  S.add('int.airbox_lid', 'Air Box Lid', 'airbox_lid_i4', 'airbox_lid', 'int.airbox', [HOOD], at(add(boxLocal, [0, 0.05, 0])), lid);
  S.add('int.air_filter', 'Air Filter', 'air_filter_panel', 'air_filter', 'int.airbox', [HOOD, 'int.airbox_lid'], at(add(boxLocal, [0, 0.035, 0])), af);

  return { exhaustOutlet: at(add([0, 0.35, 0.15], P.I4_EXH_OUTLET)) };
}

/** V8. Returns the car-local positions of both exhaust outlets. */
function addV8(reg: Registry, S: SlotList, E: Vec3): { outlets: [Vec3, Vec3] } {
  S.region = 'engine';
  const at = (p: Vec3) => add(E, p);
  const c = Math.cos(P.V8.bank);
  const s = Math.sin(P.V8.bank);
  const bank = (side: 1 | -1) => ({
    pos: [0, P.V8.crankY + P.V8.deckDist * c, side * P.V8.deckDist * s] as Vec3,
    rot: (side > 0 ? [P.V8.bank, 0, 0] : [-P.V8.bank, Math.PI, 0]) as Vec3,
  });

  const block = reg.part({ id: 'v8_block', name: 'Engine Block (5.0L V8)', category: 'engine', slotType: 'v8_block', price: 2200 }, P.v8Block());
  const head = reg.part({ id: 'v8_head', name: 'Cylinder Head (V8)', category: 'engine', slotType: 'v8_head', price: 560 }, P.v8Head());
  const vc = reg.part({ id: 'v8_valve_cover', name: 'Valve Cover (V8)', category: 'engine', slotType: 'v8_valve_cover', price: 70 }, P.valveCoverV8(M.blue));
  reg.part({ id: 'v8_valve_cover_chrome', name: 'Chrome Valve Cover (V8)', category: 'engine', slotType: 'v8_valve_cover', price: 120, custom: true, valueBonus: 110 }, P.valveCoverV8(M.chrome));
  const cam = reg.part({ id: 'v8_camshaft', name: 'Camshaft (V8)', category: 'engine', slotType: 'v8_camshaft', price: 290 }, P.v8Camshaft());
  const intake = reg.part({ id: 'v8_intake', name: 'Intake Manifold (V8)', category: 'intake', slotType: 'v8_intake', price: 380 }, P.intakeV8());
  const carb = reg.part({ id: 'v8_carb', name: 'Carburetor (4-barrel)', category: 'intake', slotType: 'v8_carb', price: 260 }, P.carburetor());
  const dist = reg.part({ id: 'v8_distributor', name: 'Distributor', category: 'ignition', slotType: 'v8_distributor', price: 140 }, P.distributor());
  const plug = reg.part({ id: 'spark_plug', name: 'Spark Plug', category: 'ignition', slotType: 'spark_plug', price: 5 }, P.sparkPlug());
  const exm = P.exhaustManifoldV8();
  const exmId = reg.part({ id: 'v8_exhaust_manifold', name: 'Exhaust Manifold (V8)', category: 'engine', slotType: 'v8_exhaust_manifold', price: 210 }, exm.part);
  const pan = reg.part({ id: 'v8_oil_pan', name: 'Oil Pan (V8)', category: 'engine', slotType: 'v8_oil_pan', price: 120 }, P.oilPan(0.62, 0.36, 0.15));
  const filter = reg.part({ id: 'oil_filter', name: 'Oil Filter', category: 'engine', slotType: 'oil_filter', price: 9 }, P.oilFilter(M.blue));
  const crank = reg.part({ id: 'v8_crankshaft', name: 'Crankshaft (V8)', category: 'engine', slotType: 'v8_crankshaft', price: 620 }, P.crankshaft(P.V8.cylX, [-0.3, -0.15, 0, 0.15, 0.3], P.V8.beltX, 0.08));
  const fly = reg.part({ id: 'v8_flywheel', name: 'Flywheel (V8)', category: 'engine', slotType: 'v8_flywheel', price: 190 }, P.flywheel(0.15));
  const piston = reg.part({ id: 'v8_piston', name: 'Piston & Rod (V8)', category: 'engine', slotType: 'v8_piston', price: 90 }, P.piston(0.27));
  const alt = reg.part({ id: 'v8_alternator', name: 'Alternator (95A)', category: 'electrical', slotType: 'v8_alternator', price: 230 }, P.alternator(1.12));
  const wp = reg.part({ id: 'v8_water_pump', name: 'Water Pump (V8)', category: 'cooling', slotType: 'v8_water_pump', price: 120 }, P.waterPump());
  const pulleys: [number, number, number][] = [[P.V8.crankY, 0, 0.08], [0.16, 0, 0.047], [0.3, 0.22, 0.034]];
  const belt = reg.part({ id: 'v8_belt', name: 'Drive Belt (V8)', category: 'engine', slotType: 'v8_belt', price: 32 }, P.driveBelt(pulleys, [0.08, -0.14]));
  const thermo = reg.part({ id: 'v8_thermostat', name: 'Thermostat (V8)', category: 'cooling', slotType: 'v8_thermostat', price: 30 }, P.thermostat());
  const starter = reg.part({ id: 'v8_starter', name: 'Starter (V8)', category: 'electrical', slotType: 'v8_starter', price: 180 }, P.starter());
  const cleaner = reg.part({ id: 'v8_air_cleaner', name: 'Air Cleaner Housing', category: 'intake', slotType: 'v8_air_cleaner', price: 60 }, P.roundAirCleaner());
  const lid = reg.part({ id: 'v8_air_cleaner_lid', name: 'Air Cleaner Lid', category: 'intake', slotType: 'v8_air_cleaner_lid', price: 25 }, P.roundAirCleanerLid(M.plastic));
  reg.part({ id: 'v8_air_cleaner_lid_chrome', name: 'Chrome Air Cleaner Lid', category: 'intake', slotType: 'v8_air_cleaner_lid', price: 60, custom: true, valueBonus: 60 }, P.roundAirCleanerLid(M.chrome));
  const af = reg.part({ id: 'air_filter_round', name: 'Air Filter (round)', category: 'intake', slotType: 'air_filter_round', price: 18 }, P.roundFilter(P.EXTRA.ceramic));
  reg.part({ id: 'air_filter_round_perf', name: 'Performance Air Filter (round)', category: 'intake', slotType: 'air_filter_round', price: 55, custom: true, valueBonus: 45 }, P.roundFilter(M.red));

  S.add('eng.block', 'Engine Block', 'v8_block', 'engine_block', null, [HOOD], at([0, 0, 0]), block);
  const outlets: Vec3[] = [];
  for (const side of [1, -1] as const) {
    const k = side > 0 ? 'r' : 'l';
    const K = side > 0 ? 'Right' : 'Left';
    const bk = bank(side);
    const headT = { pos: at(bk.pos), rot: bk.rot };
    S.add(`eng.head_${k}`, `${K} Cylinder Head`, 'v8_head', 'cylinder_head', 'eng.block', [HOOD, `eng.valve_cover_${k}`, 'int.manifold'], headT.pos, head, { rot: headT.rot, removeDir: point([0, 0, 0], bk.rot, [0, 1, 0]) });
    const vcT = xf(headT.pos, headT.rot, [0, 0.1, 0]);
    S.add(`eng.valve_cover_${k}`, `${K} Valve Cover`, 'v8_valve_cover', 'valve_cover', `eng.head_${k}`, [HOOD], vcT.pos, vc, { rot: vcT.rot, removeDir: point([0, 0, 0], bk.rot, [0, 1, 0]) });
    const exT = xf(headT.pos, headT.rot, [0, 0.03, 0.09]);
    S.add(`exh.manifold_${k}`, `${K} Exhaust Manifold`, 'v8_exhaust_manifold', 'exhaust_manifold', `eng.head_${k}`, [HOOD, 'exh.front'], exT.pos, exmId, {
      rot: exT.rot,
      removeDir: point([0, 0, 0], bk.rot, [0, 0.3, 1]),
    });
    outlets.push(point(exT.pos, exT.rot, exm.outlet));
    P.V8.cylX.forEach((x, i) => {
      const n = side > 0 ? i * 2 + 1 : i * 2 + 2;
      const pl = xf(headT.pos, headT.rot, [x, 0.075, 0.105], [Math.PI / 2, 0, 0]);
      S.add(`ign.plug${n}`, `Spark Plug #${n}`, 'spark_plug', 'spark_plug', `eng.head_${k}`, [HOOD], pl.pos, plug, { rot: pl.rot, removeDir: point([0, 0, 0], bk.rot, [0, 0, 1]) });
      const pi = xf(at(bk.pos), bk.rot, [x, -0.03, 0]);
      S.add(`eng.piston${n}`, `Piston #${n}`, 'v8_piston', 'piston', 'eng.block', [HOOD, `eng.head_${k}`, 'eng.oil_pan'], pi.pos, piston, { rot: pi.rot, removeDir: point([0, 0, 0], bk.rot, [0, 1, 0]) });
    });
  }
  const pistons = Array.from({ length: 8 }, (_, i) => `eng.piston${i + 1}`);
  S.add('eng.camshaft', 'Camshaft', 'v8_camshaft', 'camshaft', 'eng.block', [HOOD, 'int.manifold', 'cool.water_pump'], at([0, 0.16, 0]), cam, { removeDir: [1, 0, 0] });
  S.add('int.manifold', 'Intake Manifold', 'v8_intake', 'intake_manifold', 'eng.block', [HOOD, 'ign.distributor'], at([0, 0.36, 0]), intake);
  S.add('int.carb', 'Carburetor', 'v8_carb', 'carburetor', 'int.manifold', [HOOD], at([0, 0.465, 0]), carb);
  S.add('int.air_cleaner', 'Air Cleaner Housing', 'v8_air_cleaner', 'airbox', 'int.carb', [HOOD, 'int.air_cleaner_lid', 'int.air_filter'], at([0, 0.55, 0]), cleaner);
  S.add('int.air_cleaner_lid', 'Air Cleaner Lid', 'v8_air_cleaner_lid', 'airbox_lid', 'int.air_cleaner', [HOOD], at([0, 0.62, 0]), lid);
  S.add('int.air_filter', 'Air Filter', 'air_filter_round', 'air_filter', 'int.air_cleaner', [HOOD, 'int.air_cleaner_lid'], at([0, 0.565, 0]), af);
  S.add('ign.distributor', 'Distributor', 'v8_distributor', 'distributor', 'eng.block', [HOOD], at([-0.3, 0.33, 0]), dist);
  S.add('eng.flywheel', 'Flywheel', 'v8_flywheel', 'flywheel', 'eng.block', [TRANS], at([-0.345, P.V8.crankY, 0]), fly, { removeDir: [-1, 0, 0] });
  S.add('eng.oil_pan', 'Oil Pan', 'v8_oil_pan', 'oil_pan', 'eng.block', ['eng.flywheel'], at([0, 0, 0]), pan, { removeDir: [0, -1, 0] });
  S.add('eng.oil_filter', 'Oil Filter', 'oil_filter', 'oil_filter', 'eng.block', [], at([0.05, 0.05, 0.195]), filter, { removeDir: [0, 0, 1] });
  S.add('eng.crankshaft', 'Crankshaft', 'v8_crankshaft', 'crankshaft', 'eng.block', ['eng.oil_pan', 'eng.belt', 'eng.flywheel', ...pistons], at([0, P.V8.crankY, 0]), crank, { removeDir: [0, -1, 0] });
  S.add('eng.belt', 'Drive Belt', 'v8_belt', 'drive_belt', 'eng.block', [HOOD], at([P.V8.beltX, 0, 0]), belt, { removeDir: [0.4, 1, 0] });
  S.add('elec.alternator', 'Alternator', 'v8_alternator', 'alternator', 'eng.block', [HOOD, 'eng.belt'], at([P.V8.beltX - 0.075 * 1.12, 0.3, 0.22]), alt, { removeDir: [0, 1, 0.6] });
  S.add('cool.water_pump', 'Water Pump', 'v8_water_pump', 'water_pump', 'eng.block', [HOOD, 'eng.belt'], at([P.V8.beltX - 0.04, 0.16, 0]), wp, { removeDir: [1, 0.4, 0] });
  S.add('cool.thermostat', 'Thermostat', 'v8_thermostat', 'thermostat', 'int.manifold', [HOOD], at([0.27, 0.39, 0]), thermo, { removeDir: [1, 0.5, 0] });
  S.add('elec.starter', 'Starter', 'v8_starter', 'starter', 'eng.block', [], at([-0.26, 0.03, -0.25]), starter, { removeDir: [0, -1, 0] });
  return { outlets: [outlets[0], outlets[1]] };
}

// ---- exhaust ---------------------------------------------------------------

interface ExhaustRoute {
  floorY: number;
  zPipe: number;
  catX: number;
  jointX: number;
  muffler: Vec3;
  tail: Vec3;
}

function frontPipe(inlets: Vec3[], route: ExhaustRoute): P.BuiltPart {
  const b = new ModelBuilder();
  const o = inlets[0];
  const rel = (p: Vec3): Vec3 => sub(p, o);
  const { floorY, zPipe, catX, jointX } = route;
  const merge: Vec3 = [Math.min(...inlets.map((i) => i[0])) - 0.3, floorY, zPipe];
  const fasteners: FastenerDef[] = [];
  for (const inlet of inlets) {
    const ri = rel(inlet);
    b.cyl(0.045, 0.01, M.exhaust, { p: [ri[0], ri[1] - 0.005, ri[2]] });
    b.tube([ri, [ri[0], ri[1] - 0.08, ri[2]], [ri[0] - 0.12, floorY - o[1] + 0.03, (ri[2] + merge[2] - o[2]) / 2], rel(merge)], 0.028, M.exhaust);
    for (const dz of [-0.035, 0.035]) fasteners.push(P.F('bolt_m', [ri[0], ri[1] - 0.012, ri[2] + dz], [0, -1, 0]));
  }
  b.tube([rel(merge), rel([catX + 0.2, floorY, zPipe])], 0.028, M.exhaust);
  b.cyl(0.075, 0.36, M.exhaust, { p: rel([catX, floorY, zPipe]), s: [1, 0.75, 1.25] }, { axis: 'x' });
  b.box([0.3, 0.004, 0.2], M.aluminum, { p: rel([catX, floorY + 0.06, zPipe]) });
  b.tube([rel([catX - 0.2, floorY, zPipe]), rel([jointX, floorY, zPipe])], 0.028, M.exhaust);
  b.cyl(0.045, 0.01, M.exhaust, { p: rel([jointX, floorY, zPipe]) }, { axis: 'x' });
  return { model: b, fasteners };
}

function muffler(route: ExhaustRoute, sport: boolean): P.BuiltPart {
  const b = new ModelBuilder();
  const o: Vec3 = [route.jointX, route.floorY, route.zPipe];
  const rel = (p: Vec3): Vec3 => sub(p, o);
  const m = route.muffler;
  b.cyl(0.045, 0.01, M.exhaust, { p: [-0.006, 0, 0] }, { axis: 'x' });
  b.tube([[0, 0, 0], rel([m[0] + 0.5, route.floorY + 0.01, route.zPipe]), rel([m[0] + 0.25, m[1], m[2]])], 0.026, M.exhaust);
  const len = sport ? 0.55 : 0.45;
  b.cyl(0.1, len, sport ? M.chrome : M.exhaust, { p: rel(m), s: [1, 0.6, 1.3] }, { axis: 'x' });
  const t = route.tail;
  b.tube([rel([m[0] - len / 2, m[1], m[2]]), rel([t[0] + 0.25, t[1], t[2]]), rel(t)], 0.026, M.exhaust);
  if (sport) {
    b.cyl(0.04, 0.12, M.chrome, { p: rel([t[0] - 0.02, t[1], t[2] - 0.05]) }, { axis: 'x' });
    b.cyl(0.04, 0.12, M.chrome, { p: rel([t[0] - 0.02, t[1], t[2] + 0.05]) }, { axis: 'x' });
  } else {
    b.cyl(0.03, 0.08, M.chrome, { p: rel([t[0] - 0.02, t[1], t[2]]) }, { axis: 'x' });
  }
  const mr = rel(m);
  return {
    model: b,
    fasteners: [
      P.F('bolt_m', [0.004, 0.035, 0], [1, 0, 0]),
      P.F('bolt_m', [0.004, -0.035, 0], [1, 0, 0]),
      P.F('bolt_m', [mr[0] + 0.14, mr[1], mr[2] + 0.13], [0, 0, 1]),
      P.F('bolt_m', [mr[0] - 0.14, mr[1], mr[2] + 0.13], [0, 0, 1]),
    ],
  };
}

function addExhaust(reg: Registry, S: SlotList, carId: string, inlets: Vec3[], route: ExhaustRoute): void {
  S.region = 'running_gear';
  const fp = reg.part({ id: `exh_front_${carId}`, name: 'Exhaust Pipe & Catalytic Converter', category: 'exhaust', slotType: `exh_front_${carId}`, price: 340 }, frontPipe(inlets, route));
  const mf = reg.part({ id: `muffler_${carId}`, name: 'Muffler', category: 'exhaust', slotType: `muffler_${carId}`, price: 120 }, muffler(route, false));
  reg.part({ id: `muffler_${carId}_sport`, name: 'Sport Muffler (dual tips)', category: 'exhaust', slotType: `muffler_${carId}`, price: 280, custom: true, valueBonus: 250 }, muffler(route, true));
  S.add('exh.front', 'Exhaust', `exh_front_${carId}`, 'exhaust_front', null, ['exh.muffler'], inlets[0], fp, { removeDir: [0, -1, 0] });
  S.add('exh.muffler', 'Muffler', `muffler_${carId}`, 'muffler', null, [], [route.jointX, route.floorY, route.zPipe], mf, { removeDir: [0, -1, 0] });
}

// ---- drivetrain ----------------------------------------------------------------

function addDrivetrain(reg: Registry, S: SlotList, carId: string, E: Vec3, engineHalfLen: number, crankY: number, rearAxleX: number, wheelR: number, track: number): void {
  S.region = 'running_gear';
  const transLen = carId === 'pickup' ? 0.8 : 0.7;
  const face: Vec3 = [E[0] - engineHalfLen - 0.06, E[1] + crankY, 0];
  const out: Vec3 = [face[0] - transLen - 0.07, face[1], 0];
  const diff: Vec3 = [rearAxleX, wheelR, 0];
  const trans = reg.part({ id: `transmission_${carId}`, name: 'Transmission', category: 'drivetrain', slotType: `transmission_${carId}`, price: 900 }, P.transmission(transLen));
  const shaft = reg.part({ id: `driveshaft_${carId}`, name: 'Drive Train', category: 'drivetrain', slotType: `driveshaft_${carId}`, price: 240 }, P.driveshaft(out[0] - diff[0] - 0.09, out[1] - diff[1]));
  const axle = reg.part({ id: `rear_axle_${carId}`, name: 'Back Drive Train (axle & differential)', category: 'drivetrain', slotType: `rear_axle_${carId}`, price: 780 }, P.rearAxle(track));
  S.add(TRANS, 'Transmission', `transmission_${carId}`, 'transmission', null, ['drivetrain.driveshaft'], face, trans, { removeDir: [-1, -0.4, 0] });
  S.add('drivetrain.driveshaft', 'Drive Train', `driveshaft_${carId}`, 'driveshaft', null, [], out, shaft, { removeDir: [0, -1, 0] });
  S.add('drivetrain.rear_axle', 'Back Drive Train', `rear_axle_${carId}`, 'rear_axle', null, ['drivetrain.driveshaft'], diff, axle, { removeDir: [0, -1, 0] });
}

// ---- chassis ---------------------------------------------------------------

interface ChassisSpec {
  axles: [number, number];
  track: number;
  wheelR: number;
  rimR: number;
  tireW: number;
  lugR: number;
  rotorR: number;
  size: 'small' | 'large';
}

function addChassis(reg: Registry, S: SlotList, c: ChassisSpec): void {
  S.region = 'running_gear';
  const wType = c.size === 'small' ? 'wheel_r15' : 'wheel_r16';
  const inch = c.size === 'small' ? '15"' : '16"';
  const wheel = reg.part({ id: wType, name: `${inch} Steel Wheel`, category: 'wheels', slotType: wType, price: 110 }, wheel2(c.wheelR, c.rimR, c.tireW, 'steel', c.lugR));
  reg.part({ id: `${wType}_alloy`, name: `${inch} Alloy Wheel`, category: 'wheels', slotType: wType, price: 270, custom: true, valueBonus: 350 }, wheel2(c.wheelR, c.rimR, c.tireW, 'alloy', c.lugR));
  reg.part({ id: `${wType}_chrome`, name: `${inch} Chrome Wheel`, category: 'wheels', slotType: wType, price: 390, custom: true, valueBonus: 520 }, wheel2(c.wheelR, c.rimR, c.tireW, 'chrome', c.lugR));
  const rotor = reg.part({ id: `rotor_${c.size}`, name: 'Brake Rotor', category: 'brakes', slotType: `rotor_${c.size}`, price: 45 }, rotor2(c.rotorR, c.lugR));
  const caliper = reg.part({ id: `caliper_${c.size}`, name: 'Brake Caliper', category: 'brakes', slotType: `caliper_${c.size}`, price: 85 }, caliper2(c.rotorR, M.castIron));
  reg.part({ id: `caliper_${c.size}_red`, name: 'Red Performance Caliper', category: 'brakes', slotType: `caliper_${c.size}`, price: 150, custom: true, valueBonus: 90 }, caliper2(c.rotorR, M.red));
  const pads = reg.part({ id: `pads_${c.size}`, name: 'Brake Pads', category: 'brakes', slotType: `pads_${c.size}`, price: 30 }, P.brakePads(c.rotorR));
  const strutType = c.size === 'small' ? 'strut_car' : 'strut_truck';
  const shockType = c.size === 'small' ? 'shock_car' : 'shock_truck';
  // The small cars' struts end under the bonnet (at 0.45 m their tops came through it).
  const strutLen = c.size === 'small' ? 0.34 : 0.5;
  const strut = reg.part({ id: strutType, name: 'Front Shock', category: 'suspension', slotType: strutType, price: 110 }, P.strut(strutLen, c.size === 'small' ? 0.055 : 0.065, M.darkSteel));
  reg.part({ id: `${strutType}_sport`, name: 'Sport Front Shock', category: 'suspension', slotType: strutType, price: 190, custom: true, valueBonus: 120 }, P.strut(strutLen, c.size === 'small' ? 0.055 : 0.065, M.yellow));
  const shock = reg.part({ id: shockType, name: 'Back Shock', category: 'suspension', slotType: shockType, price: 60 }, P.shock(c.size === 'small' ? 0.28 : 0.34, M.darkSteel));

  const wheels: { k: string; label: string; x: number; side: 1 | -1; front: boolean }[] = [
    { k: 'fl', label: 'Front Left', x: c.axles[0], side: -1, front: true },
    { k: 'fr', label: 'Front Right', x: c.axles[0], side: 1, front: true },
    { k: 'rl', label: 'Back Left', x: c.axles[1], side: -1, front: false },
    { k: 'rr', label: 'Back Right', x: c.axles[1], side: 1, front: false },
  ];
  for (const w of wheels) {
    const center: Vec3 = [w.x, c.wheelR, w.side * (c.track / 2)];
    const rot: Vec3 = w.side > 0 ? [0, 0, 0] : [0, Math.PI, 0];
    const out: Vec3 = [0, 0, w.side];
    const whl = `wheel.${w.k}`;
    const cal = `brake.caliper.${w.k}`;
    const pad = `brake.pads.${w.k}`;
    S.add(whl, `${w.label} Wheel`, wType, 'wheel', null, [], center, wheel, { rot, removeDir: out });
    const rc = xf(center, rot, [0, 0, -0.016]);
    S.add(cal, `${w.label} Brake Caliper`, `caliper_${c.size}`, 'brake_caliper', null, [whl], rc.pos, caliper, { rot, removeDir: out });
    S.add(pad, `${w.label} Brake Pads`, `pads_${c.size}`, 'brake_pads', null, [whl, cal], rc.pos, pads, { rot, removeDir: out });
    S.add(`brake.rotor.${w.k}`, `${w.label} Brake Rotor`, `rotor_${c.size}`, 'brake_rotor', null, [whl, cal, pad], rc.pos, rotor, { rot, removeDir: out });
    if (w.front) {
      const st = xf(center, rot, [0, c.rotorR + 0.035, -0.12]);
      S.add(`susp.${w.k}`, `${w.label} Shock`, strutType, 'strut', null, [whl], st.pos, strut, { rot, removeDir: out });
    } else {
      const sh = xf(center, rot, [-(c.rotorR + 0.03), 0.0, -0.12]);
      S.add(`susp.${w.k}`, `${w.label} Shock`, shockType, 'shock', null, [whl], sh.pos, shock, { rot, removeDir: out });
    }
  }
}

// ---- cooling / electrical / body --------------------------------------------

function addCooling(reg: Registry, S: SlotList, kind: 'i4' | 'v8', radPos: Vec3, battPos: Vec3): void {
  S.region = 'engine';
  const [h, w] = kind === 'i4' ? [0.36, 0.56] : [0.44, 0.66];
  const K = kind.toUpperCase();
  const rad = reg.part({ id: `radiator_${kind}`, name: `Radiator (${K})`, category: 'cooling', slotType: `radiator_${kind}`, price: kind === 'i4' ? 230 : 290 }, P.radiator(h, w));
  const fan = reg.part({ id: `fan_${kind}`, name: `Radiator Fan (${K})`, category: 'cooling', slotType: `fan_${kind}`, price: kind === 'i4' ? 120 : 140 }, P.radiatorFan(h * 0.95, w * 0.8));
  const bat = reg.part({ id: 'battery', name: 'Battery', category: 'electrical', slotType: 'battery', price: 79 }, P.battery());
  S.add('cool.radiator', 'Radiator', `radiator_${kind}`, 'radiator', null, [HOOD, 'cool.fan'], radPos, rad);
  S.add('cool.fan', 'Radiator Fan', `fan_${kind}`, 'radiator_fan', 'cool.radiator', [HOOD], add(radPos, [-0.06, 0, 0]), fan);
  S.add('elec.battery', 'Battery', 'battery', 'battery', null, [HOOD], battPos, bat);
}

/** Hood, spoiler and every panel of the body. Also registers the static shell model. `gen2`: the second-generation
 * body (curved panels and details; same panels, places and bolts). */
function addBody(reg: Registry, S: SlotList, carId: string, spec: BodySpec, spoilerX: number | null, gen2 = false): void {
  S.region = 'body';
  const hinge = hingeOf(spec);
  const front = hoodFrontOf(spec);
  const dx = front[0] - hinge[0];
  const dy = front[1] - hinge[1];
  const hw = spec.width - 0.05;
  const body = gen2 ? buildBody2(spec) : { ...buildBody(spec), shape: null };
  const hoodOf = (scoop: boolean) => (body.shape ? hood2(body.shape, scoop) : P.hood(dx, dy, hw, scoop));
  const hoodId = reg.part({ id: `hood_${carId}`, name: 'Hood', category: 'body', slotType: `hood_${carId}`, price: 350 }, hoodOf(false));
  reg.part({ id: `hood_${carId}_scoop`, name: 'Hood with Air Scoop', category: 'body', slotType: `hood_${carId}`, price: 520, custom: true, valueBonus: 400 }, hoodOf(true));
  S.add(HOOD, 'Hood', `hood_${carId}`, 'hood', null, [], [hinge[0], hinge[1], 0], hoodId, {
    removeDir: [-0.3, 1, 0],
    openable: { axis: [0, 0, 1], angle: 1.15 },
  });
  const { shell, panels } = body;
  reg.model(`body.${carId}`, shell);
  for (const pnl of panels) {
    const id = `${pnl.key}_${carId}`;
    reg.part({ id, name: pnl.name, category: pnl.category, slotType: id, price: pnl.price }, { model: pnl.model, fasteners: pnl.fasteners });
    S.add(`body.${pnl.key}`, pnl.name, id, pnl.family, pnl.parent ? `body.${pnl.parent}` : null, pnl.blockedBy.map((k) => `body.${k}`), pnl.origin, id, { removeDir: pnl.removeDir });
  }
  if (spoilerX !== null) {
    reg.part({ id: 'spoiler_wing', name: 'Rear Spoiler', category: 'body', slotType: 'spoiler_car', price: 260, custom: true, valueBonus: 300 }, P.spoiler(1.3));
    S.add('body.spoiler', 'Spoiler', 'spoiler_car', 'spoiler', 'body.trunk', [], [spoilerX, outlineY(spec, spoilerX), 0], null, { required: false });
  }
}

// ---- cars -------------------------------------------------------------------

/** The cars built into the pack. The beta plays one model, the sedan (the one car in the second generation); the coupe
 * and the pickup come back if playing without the original draws interest. */
export const PACK_CARS = ['sedan'];

export function buildCars(reg: Registry): CarModelDef[] {
  const cars: CarModelDef[] = [];
  const builders: Record<string, () => void> = { sedan, coupe, pickup };
  for (const id of PACK_CARS) builders[id]();
  return cars;

  function sedan(): void {
    const S = new SlotList();
    const spec = SEDAN;
    const E = spec.engine;
    addBody(reg, S, 'sedan', spec, -2.12, true);
    const { exhaustOutlet } = addI4(reg, S, E);
    addCooling(reg, S, 'i4', [2.07, 0.5, 0], [1.88, 0.52, 0.56]);
    addExhaust(reg, S, 'sedan', [exhaustOutlet], { floorY: 0.17, zPipe: 0.26, catX: 0.55, jointX: -0.3, muffler: [-1.9, 0.22, 0.38], tail: [-2.36, 0.22, 0.55] });
    addDrivetrain(reg, S, 'sedan', E, 0.26, P.I4.crankY, spec.axles[1], spec.wheelR, spec.track);
    addChassis(reg, S, { axles: spec.axles, track: spec.track, wheelR: spec.wheelR, rimR: 0.19, tireW: 0.2, lugR: 0.055, rotorR: 0.13, size: 'small' });
    cars.push({
      id: 'sedan', make: 'Norland', name: 'Tamarack LX', year: 1987, bodyStyle: 'sedan', engineLabel: '2.2L I4',
      bodyModel: 'body.sedan', paintMaterial: 'paint', defaultPaint: '#7a1e22', baseValue: 6500, minSkill: 0,
      dims: { length: 4.7, width: spec.width, height: 1.42, wheelbase: 2.66 }, slots: S.slots,
    });
  }

  function coupe(): void {
    const S = new SlotList();
    const spec = COUPE;
    const E = spec.engine;
    addBody(reg, S, 'coupe', spec, -2.06);
    const { exhaustOutlet } = addI4(reg, S, E);
    addCooling(reg, S, 'i4', [2.08, 0.46, 0], [1.86, 0.48, 0.56]);
    addExhaust(reg, S, 'coupe', [exhaustOutlet], { floorY: 0.15, zPipe: 0.26, catX: 0.45, jointX: -0.35, muffler: [-1.8, 0.2, 0.38], tail: [-2.28, 0.2, 0.52] });
    addDrivetrain(reg, S, 'coupe', E, 0.26, P.I4.crankY, spec.axles[1], spec.wheelR, spec.track);
    addChassis(reg, S, { axles: spec.axles, track: spec.track, wheelR: spec.wheelR, rimR: 0.19, tireW: 0.21, lugR: 0.055, rotorR: 0.13, size: 'small' });
    cars.push({
      id: 'coupe', make: 'Kestrel', name: 'Vireo GT', year: 1979, bodyStyle: 'coupe', engineLabel: '2.2L I4 16V',
      bodyModel: 'body.coupe', paintMaterial: 'paint', defaultPaint: '#1f4f8f', baseValue: 8200, minSkill: 0,
      dims: { length: 4.55, width: spec.width, height: 1.3, wheelbase: 2.55 }, slots: S.slots,
    });
  }

  function pickup(): void {
    const S = new SlotList();
    const spec = PICKUP;
    const E = spec.engine;
    addBody(reg, S, 'pickup', spec, null);
    const { outlets } = addV8(reg, S, E);
    addCooling(reg, S, 'v8', [2.44, 0.86, 0], [2.18, 0.9, 0.62]);
    addExhaust(reg, S, 'pickup', outlets, { floorY: 0.36, zPipe: 0.3, catX: 0.4, jointX: -0.4, muffler: [-2.15, 0.42, 0.42], tail: [-2.72, 0.42, 0.62] });
    addDrivetrain(reg, S, 'pickup', E, 0.32, P.V8.crankY, spec.axles[1], spec.wheelR, spec.track);
    addChassis(reg, S, { axles: spec.axles, track: spec.track, wheelR: spec.wheelR, rimR: 0.2, tireW: 0.25, lugR: 0.07, rotorR: 0.15, size: 'large' });
    cars.push({
      id: 'pickup', make: 'Brixton', name: 'Hauler 1500', year: 1976, bodyStyle: 'pickup', engineLabel: '5.0L V8',
      bodyModel: 'body.pickup', paintMaterial: 'paint', defaultPaint: '#2f5d3a', baseValue: 9800, minSkill: 1,
      dims: { length: 5.3, width: spec.width, height: 1.8, wheelbase: 3.2 }, slots: S.slots,
    });
  }
}

export type { MatDef };
