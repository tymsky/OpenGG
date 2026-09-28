// Placeholder part models (built from primitives) together with their fastener layouts.
// All coordinates are part-local, in meters. x = car forward, y = up, z = car right.

import * as THREE from 'three';
import { ModelBuilder, M, roundedRect, circlePath, type MatDef } from './geo.ts';
import type { FastenerDef, Vec3 } from './types.ts';

export interface BuiltPart {
  model: ModelBuilder;
  fasteners: FastenerDef[];
}

export const F = (kind: string, pos: Vec3, dir: Vec3): FastenerDef => ({ kind, pos, dir });
const PI = Math.PI;

const ceramic: MatDef = { name: 'ceramic', color: [0.85, 0.85, 0.82], metallic: 0, roughness: 0.3 };
const cardboard: MatDef = { name: 'cardboard', color: [0.42, 0.28, 0.14], metallic: 0, roughness: 0.95 };
export const EXTRA = { ceramic, cardboard };

/** Rectangle with round through-holes, extruded along +Y from y0 to y0 + h. `holes` are [x, z, r]. */
function perforatedSlab(b: ModelBuilder, w: number, d: number, h: number, holes: [number, number, number][], m: MatDef, y0 = 0, rad = 0.02, t: { p?: Vec3; r?: Vec3 } = {}) {
  const s = roundedRect(w, d, rad);
  // shape y maps to -z after rotating the extrusion onto +Y
  for (const [x, z, r] of holes) s.holes.push(circlePath(x, -z, r));
  const g = new THREE.ExtrudeGeometry(s, { depth: h, bevelEnabled: false, curveSegments: 18 });
  g.rotateX(-PI / 2);
  g.translate(0, y0, 0);
  if (t.r) g.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(new THREE.Euler(...t.r, 'XYZ')));
  if (t.p) g.translate(...t.p);
  b.add(g, m);
}

/** Closed belt path (YZ plane at x = 0) around a set of pulleys [y, z, r]. */
function beltPath(pulleys: [number, number, number][]): Vec3[] {
  const pts: [number, number][] = [];
  for (const [y, z, r] of pulleys) {
    for (let a = 0; a < 48; a++) {
      const t = (a / 48) * PI * 2;
      pts.push([y + Math.cos(t) * (r + 0.004), z + Math.sin(t) * (r + 0.004)]);
    }
  }
  // Monotone-chain convex hull
  pts.sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const cross = (o: [number, number], a: [number, number], c: [number, number]) => (a[0] - o[0]) * (c[1] - o[1]) - (a[1] - o[1]) * (c[0] - o[0]);
  const lower: [number, number][] = [];
  for (const p of pts) {
    while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop();
    lower.push(p);
  }
  const upper: [number, number][] = [];
  for (let i = pts.length - 1; i >= 0; i--) {
    const p = pts[i];
    while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop();
    upper.push(p);
  }
  const hull = lower.slice(0, -1).concat(upper.slice(0, -1));
  // thin out to keep the tube light
  const out: Vec3[] = [];
  for (let i = 0; i < hull.length; i += 3) out.push([0, hull[i][0], hull[i][1]]);
  return out;
}

// ---------------------------------------------------------------------------
// Inline-four engine (engine-local origin = block bottom center, crank at y = -0.03)
// ---------------------------------------------------------------------------

export const I4 = {
  cylX: [-0.195, -0.065, 0.065, 0.195],
  crankY: -0.03,
  pistonTopY: 0.27,
  beltX: 0.34,
};

export function i4Block(): BuiltPart {
  const b = new ModelBuilder();
  perforatedSlab(b, 0.52, 0.34, 0.3, I4.cylX.map((x) => [x, 0, 0.043] as [number, number, number]), M.castIron);
  b.box([0.03, 0.28, 0.24], M.aluminum, { p: [0.275, 0.13, 0] }); // timing cover
  for (const s of [-1, 1]) {
    b.box([0.07, 0.06, 0.03], M.darkSteel, { p: [0.13, 0.1, s * 0.185] });
    b.box([0.07, 0.06, 0.03], M.darkSteel, { p: [-0.13, 0.1, s * 0.185] });
    for (const x of [-0.2, -0.07, 0.07, 0.2]) b.box([0.012, 0.22, 0.012], M.castIron, { p: [x, 0.14, s * 0.172] });
  }
  b.cyl(0.004, 0.2, M.yellow, { p: [-0.18, 0.36, 0.16] }); // dipstick
  b.torus(0.012, 0.004, M.yellow, { p: [-0.18, 0.465, 0.16] }, { axis: 'x' });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.13, 0.13]) for (const s of [-1, 1]) fasteners.push(F('bolt_l', [x, 0.1, s * 0.2], [0, 0, s]));
  return { model: b, fasteners };
}

export function i4Head(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.5, 0.1, 0.3], M.aluminum, { p: [0, 0.05, 0] });
  for (const x of I4.cylX) {
    b.box([0.05, 0.03, 0.006], M.darkSteel, { p: [x, 0.05, -0.152] });
    b.box([0.04, 0.03, 0.006], M.darkSteel, { p: [x, 0.05, 0.152] });
    // valve springs visible from the top once the cam is out
    for (const dx of [-0.02, 0.02]) b.cyl(0.009, 0.02, M.steel, { p: [x + dx, 0.105, -0.05] });
  }
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.2, -0.1, 0, 0.1, 0.2]) for (const z of [-0.095, 0.095]) fasteners.push(F('bolt_l', [x, 0.1, z], [0, 1, 0]));
  return { model: b, fasteners };
}

export function i4Camshaft(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.013, 0.5, M.steel, {}, { axis: 'x' });
  for (const x of I4.cylX) for (const dx of [-0.028, 0.028]) b.cyl(0.021, 0.014, M.steel, { p: [x + dx, 0.006, 0] }, { axis: 'x' });
  const caps = [-0.25, -0.13, 0, 0.13, 0.25];
  const fasteners: FastenerDef[] = [];
  for (const x of caps) {
    b.box([0.028, 0.022, 0.06], M.aluminum, { p: [x, 0.008, 0] });
    for (const z of [-0.022, 0.022]) fasteners.push(F('bolt_s', [x, 0.019, z], [0, 1, 0]));
  }
  return { model: b, fasteners };
}

export function valveCoverI4(m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  perforatedSlab(b, 0.5, 0.26, 0.055, I4.cylX.map((x) => [x, 0.05, 0.014] as [number, number, number]), m, 0, 0.03);
  perforatedSlab(b, 0.46, 0.22, 0.015, I4.cylX.map((x) => [x, 0.05, 0.014] as [number, number, number]), m, 0.055, 0.03);
  b.box([0.52, 0.008, 0.28], M.darkSteel, { p: [0, 0.004, 0] });
  b.cyl(0.022, 0.02, M.yellow, { p: [0.14, 0.08, -0.06] });
  for (const z of [-0.07, -0.035]) b.box([0.4, 0.006, 0.012], m, { p: [0, 0.072, z] }); // ribs
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.23, -0.08, 0.08, 0.23]) for (const z of [-0.1, 0.1]) fasteners.push(F('bolt_s', [x, 0.07, z], [0, 1, 0]));
  return { model: b, fasteners };
}

export function sparkPlug(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.0075, 0.035, ceramic, { p: [0, 0.024, 0] });
  b.cyl(0.004, 0.01, M.steel, { p: [0, 0.046, 0] });
  b.hex(0.011, 0.012, M.steel, { p: [0, 0, 0] });
  b.cyl(0.007, 0.022, M.steel, { p: [0, -0.017, 0] });
  b.box([0.003, 0.006, 0.003], M.steel, { p: [0, -0.031, 0] });
  return { model: b, fasteners: [] };
}

export function coilPackI4(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.46, 0.035, 0.05], M.plastic, {});
  for (const x of I4.cylX) {
    b.cyl(0.012, 0.03, M.rubber, { p: [x, -0.03, 0] });
    b.box([0.03, 0.012, 0.03], M.plasticGrey, { p: [x, 0.022, 0] });
  }
  b.box([0.03, 0.03, 0.03], M.plasticGrey, { p: [0.25, 0, 0] });
  return { model: b, fasteners: [F('bolt_s', [-0.215, 0.018, 0], [0, 1, 0]), F('bolt_s', [0.215, 0.018, 0], [0, 1, 0])] };
}

export function intakeI4(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.5, 0.07, 0.012], M.aluminum, { p: [0, 0, -0.006] });
  for (const x of I4.cylX) b.tube([[x, 0, -0.01], [x, 0.01, -0.07], [x * 0.85, 0.06, -0.13], [x * 0.8, 0.09, -0.17]], 0.02, M.aluminum);
  b.cyl(0.05, 0.46, M.aluminum, { p: [0, 0.1, -0.18] }, { axis: 'x' });
  b.cyl(0.034, 0.06, M.aluminum, { p: [0.26, 0.1, -0.18] }, { axis: 'x' });
  b.box([0.03, 0.03, 0.03], M.plastic, { p: [0.25, 0.14, -0.18] });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.13, 0, 0.13]) for (const y of [-0.024, 0.024]) fasteners.push(F('bolt_m', [x, y, -0.013], [0, 0, -1]));
  return { model: b, fasteners };
}
/** Throttle body front face, intake-local. */
export const I4_THROTTLE: Vec3 = [0.29, 0.1, -0.18];

export function exhaustManifoldI4(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.5, 0.06, 0.012], M.exhaust, { p: [0, 0, 0.006] });
  for (const x of I4.cylX) b.tube([[x, 0, 0.01], [x, -0.02, 0.06], [x * 0.5 + 0.03, -0.1, 0.11], [0.05, -0.19, 0.12]], 0.02, M.exhaust);
  b.cyl(0.03, 0.08, M.exhaust, { p: [0.05, -0.22, 0.12] });
  b.cyl(0.045, 0.01, M.exhaust, { p: [0.05, -0.262, 0.12] });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.13, 0, 0.13]) for (const y of [-0.022, 0.022]) fasteners.push(F('bolt_m', [x, y, 0.013], [0, 0, 1]));
  return { model: b, fasteners };
}
/** Collector outlet, manifold-local. */
export const I4_EXH_OUTLET: Vec3 = [0.05, -0.268, 0.12];

export function oilPan(len: number, width: number, depth: number): BuiltPart {
  const b = new ModelBuilder();
  b.box([len, 0.01, width], M.steel, { p: [0, -0.005, 0] });
  b.box([len * 0.5, 0.07, width - 0.03], M.steel, { p: [len * 0.24, -0.04, 0] });
  b.box([len * 0.5, depth, width - 0.05], M.steel, { p: [-len * 0.24, -depth / 2 - 0.005, 0] });
  b.hex(0.01, 0.012, M.steel, { p: [-len * 0.4, -depth - 0.01, 0] });
  const fasteners: FastenerDef[] = [];
  const n = 5;
  for (let i = 0; i < n; i++) {
    const x = -len / 2 + 0.04 + (i * (len - 0.08)) / (n - 1);
    for (const s of [-1, 1]) fasteners.push(F('bolt_s', [x, -0.012, s * (width / 2 - 0.01)], [0, -1, 0]));
  }
  fasteners.push(F('bolt_s', [-len / 2 + 0.012, -0.012, 0], [0, -1, 0]));
  fasteners.push(F('bolt_s', [len / 2 - 0.012, -0.012, 0], [0, -1, 0]));
  return { model: b, fasteners };
}

export function crankshaft(throwsX: number[], mainsX: number[], frontX: number, pulleyR: number): BuiltPart {
  const b = new ModelBuilder();
  const span = mainsX[mainsX.length - 1] - mainsX[0];
  b.cyl(0.024, span + 0.04, M.steel, { p: [(mainsX[0] + mainsX[mainsX.length - 1]) / 2, 0, 0] }, { axis: 'x' });
  throwsX.forEach((x, i) => {
    const up = i % 2 === 0 ? 1 : -1;
    b.cyl(0.02, 0.04, M.steel, { p: [x, 0.035 * up, 0] }, { axis: 'x' });
    for (const dx of [-0.028, 0.028]) b.box([0.014, 0.1, 0.075], M.castIron, { p: [x + dx, 0.012 * up, 0] });
  });
  const fasteners: FastenerDef[] = [];
  for (const x of mainsX) {
    b.box([0.03, 0.03, 0.1], M.castIron, { p: [x, -0.035, 0] });
    for (const z of [-0.035, 0.035]) fasteners.push(F('bolt_l', [x, -0.052, z], [0, -1, 0]));
  }
  b.cyl(0.018, frontX - mainsX[mainsX.length - 1], M.steel, { p: [(frontX + mainsX[mainsX.length - 1]) / 2, 0, 0] }, { axis: 'x' });
  b.cyl(pulleyR, 0.03, M.darkSteel, { p: [frontX, 0, 0] }, { axis: 'x' });
  b.cyl(0.03, 0.03, M.steel, { p: [mainsX[0] - 0.025, 0, 0] }, { axis: 'x' }); // flange
  return { model: b, fasteners };
}

/** Flywheel, bolted to the rear of the crankshaft. Local origin at its center, axis X. */
export function flywheel(r: number): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(r, 0.024, M.darkSteel, {}, { axis: 'x', segs: 36 });
  b.cyl(r + 0.004, 0.012, M.steel, {}, { axis: 'x', segs: 60 }); // ring gear
  b.cyl(0.05, 0.03, M.steel, { p: [0.006, 0, 0] }, { axis: 'x' });
  const fasteners: FastenerDef[] = [];
  for (let i = 0; i < 6; i++) {
    const a = (i / 6) * PI * 2;
    fasteners.push(F('bolt_l', [-0.016, Math.cos(a) * 0.035, Math.sin(a) * 0.035], [-1, 0, 0]));
  }
  return { model: b, fasteners };
}

/** Carburetor (V8). Local origin at its base. */
export function carburetor(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.12, 0.012, 0.12], M.aluminum, { p: [0, 0.006, 0] });
  b.box([0.1, 0.07, 0.1], M.aluminum, { p: [0, 0.047, 0] });
  b.box([0.04, 0.05, 0.12], M.brass, { p: [0.065, 0.04, 0] }); // float bowl
  for (const x of [-0.025, 0.025]) for (const z of [-0.025, 0.025]) b.cyl(0.018, 0.012, M.darkSteel, { p: [x, 0.084, z] });
  b.cyl(0.005, 0.06, M.steel, { p: [-0.06, 0.05, 0.03] }, { axis: 'z' }); // linkage
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.05, 0.05]) for (const z of [-0.05, 0.05]) fasteners.push(F('bolt_s', [x, 0.014, z], [0, 1, 0]));
  return { model: b, fasteners };
}

/** Transmission. Local origin at the bell housing face, extending towards -X. */
export function transmission(len: number): BuiltPart {
  const b = new ModelBuilder();
  b.lathe([[0.2, 0], [0.19, 0.08], [0.13, 0.22], [0.11, len - 0.1], [0.06, len]], M.aluminum, { r: [0, 0, PI / 2] }, { segs: 24 });
  b.box([0.25, 0.04, 0.2], M.castIron, { p: [-len * 0.55, -0.12, 0] }); // pan
  b.cyl(0.03, 0.08, M.steel, { p: [-len - 0.03, 0, 0] }, { axis: 'x' }); // output
  const fasteners: FastenerDef[] = [];
  for (let i = 0; i < 4; i++) {
    const a = PI / 4 + (i * PI) / 2;
    fasteners.push(F('bolt_l', [-0.012, Math.cos(a) * 0.17, Math.sin(a) * 0.17], [-1, 0, 0]));
  }
  return { model: b, fasteners };
}

/** Drive shaft between the transmission output (origin) and the rear differential (-len along X). */
export function driveshaft(len: number, drop: number): BuiltPart {
  const b = new ModelBuilder();
  b.tube([[0, 0, 0], [-len * 0.5, -drop * 0.5, 0], [-len, -drop, 0]], 0.035, M.darkSteel, { radial: 12, segs: 12 });
  for (const t of [0, 1]) b.cyl(0.045, 0.02, M.steel, { p: [-len * t, -drop * t, 0] }, { axis: 'x' });
  return {
    model: b,
    fasteners: [F('bolt_l', [-0.015, 0.03, 0.03], [0, 0, 1]), F('bolt_l', [-0.015, -0.03, -0.03], [0, 0, -1]), F('bolt_l', [-len + 0.015, -drop + 0.03, 0.03], [0, 0, 1]), F('bolt_l', [-len + 0.015, -drop - 0.03, -0.03], [0, 0, -1])],
  };
}

/** Rear axle with differential. Local origin at the differential center. */
export function rearAxle(track: number): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.13, 0.18, M.castIron, {}, { axis: 'x' });
  b.cyl(0.1, 0.02, M.darkSteel, { p: [-0.1, 0, 0] }, { axis: 'x' });
  b.cyl(0.035, track - 0.25, M.darkSteel, {}, { axis: 'z' });
  for (const s of [-1, 1]) b.box([0.08, 0.06, 0.12], M.darkSteel, { p: [0, 0.1, s * 0.35] });
  return {
    model: b,
    fasteners: [F('bolt_l', [0.03, 0.135, -0.35], [0, 1, 0]), F('bolt_l', [-0.03, 0.135, -0.35], [0, 1, 0]), F('bolt_l', [0.03, 0.135, 0.35], [0, 1, 0]), F('bolt_l', [-0.03, 0.135, 0.35], [0, 1, 0])],
  };
}

export function piston(rodLen: number): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.041, 0.05, M.aluminum, { p: [0, -0.025, 0] });
  for (const y of [-0.008, -0.016]) b.torus(0.041, 0.0022, M.darkSteel, { p: [0, y, 0] }, { axis: 'y' });
  b.cyl(0.01, 0.07, M.steel, { p: [0, -0.032, 0] }, { axis: 'z' });
  b.box([0.022, rodLen - 0.03, 0.012], M.steel, { p: [0, -0.03 - (rodLen - 0.03) / 2, 0] });
  b.cyl(0.028, 0.024, M.steel, { p: [0, -rodLen, 0] }, { axis: 'x' });
  b.box([0.024, 0.02, 0.07], M.steel, { p: [0, -rodLen - 0.02, 0] });
  return { model: b, fasteners: [F('bolt_m', [0, -rodLen - 0.032, -0.026], [0, -1, 0]), F('bolt_m', [0, -rodLen - 0.032, 0.026], [0, -1, 0])] };
}

export function alternator(scale = 1): BuiltPart {
  const b = new ModelBuilder();
  const s = scale;
  b.cyl(0.062 * s, 0.12 * s, M.aluminum, {}, { axis: 'x' });
  for (let i = 0; i < 6; i++) b.box([0.1 * s, 0.006, 0.01], M.darkSteel, { p: [0, 0.062 * s * Math.cos((i * PI) / 3), 0.062 * s * Math.sin((i * PI) / 3)] });
  b.cyl(0.05 * s, 0.02, M.plastic, { p: [-0.07 * s, 0, 0] }, { axis: 'x' });
  b.cyl(0.055 * s, 0.01, M.darkSteel, { p: [0.063 * s, 0, 0] }, { axis: 'x' });
  b.cyl(0.03, 0.025, M.steel, { p: [0.075 * s, 0, 0] }, { axis: 'x' });
  b.box([0.03, 0.03, 0.05], M.aluminum, { p: [0, 0.07 * s, 0.01] });
  b.box([0.03, 0.03, 0.05], M.aluminum, { p: [0, -0.07 * s, 0.01] });
  b.cyl(0.008, 0.02, M.copper, { p: [-0.06 * s, 0.035, 0.035] }, { axis: 'x' });
  return { model: b, fasteners: [F('bolt_m', [0, 0.075 * s, 0.037], [0, 0, 1]), F('bolt_m', [0, -0.075 * s, 0.037], [0, 0, 1])] };
}

export function waterPump(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.052, 0.03, M.aluminum, {}, { axis: 'x' });
  b.cyl(0.018, 0.06, M.aluminum, { p: [0, -0.03, -0.06] }, { axis: 'z' });
  b.cyl(0.045, 0.02, M.steel, { p: [0.04, 0, 0] }, { axis: 'x' });
  b.cyl(0.012, 0.03, M.steel, { p: [0.03, 0, 0] }, { axis: 'x' });
  const fasteners: FastenerDef[] = [];
  for (const y of [-0.042, 0.042]) for (const z of [-0.042, 0.042]) fasteners.push(F('bolt_s', [0.018, y, z], [1, 0, 0]));
  return { model: b, fasteners };
}

/** Serpentine belt with its tensioner. `pulleys` are [y, z, r] in the belt plane. `idler` is [y, z]. */
export function driveBelt(pulleys: [number, number, number][], idler: [number, number]): BuiltPart {
  const b = new ModelBuilder();
  b.tube(beltPath([...pulleys, [idler[0], idler[1], 0.03]]), 0.006, M.rubber, { closed: true, radial: 6, segs: 120, tension: 0.1 });
  b.cyl(0.03, 0.02, M.steel, { p: [0, idler[0], idler[1]] }, { axis: 'x' });
  b.box([0.01, 0.07, 0.02], M.darkSteel, { p: [-0.012, idler[0] - 0.03, idler[1]] });
  return { model: b, fasteners: [F('bolt_m', [0.012, idler[0], idler[1]], [1, 0, 0])] };
}

export function thermostat(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.03, 0.045, 0.05], M.aluminum, { p: [0.015, 0, 0] });
  b.cyl(0.017, 0.05, M.aluminum, { p: [0.05, 0.012, 0] }, { axis: 'x' });
  return { model: b, fasteners: [F('bolt_s', [0.032, 0.016, 0.018], [1, 0, 0]), F('bolt_s', [0.032, -0.016, -0.018], [1, 0, 0])] };
}

export function starter(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.045, 0.16, M.darkSteel, {}, { axis: 'x' });
  b.cyl(0.022, 0.1, M.steel, { p: [0.01, 0.055, 0] }, { axis: 'x' });
  b.cyl(0.035, 0.04, M.aluminum, { p: [-0.1, 0, 0] }, { axis: 'x' });
  b.box([0.012, 0.1, 0.09], M.aluminum, { p: [-0.08, 0, 0] });
  return { model: b, fasteners: [F('bolt_m', [-0.075, -0.05, -0.032], [0, -1, 0]), F('bolt_m', [-0.075, -0.05, 0.032], [0, -1, 0])] };
}

export function oilFilter(m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.037, 0.09, m, { p: [0, 0, 0.045] }, { axis: 'z' });
  b.cyl(0.03, 0.006, M.darkSteel, { p: [0, 0, 0.092] }, { axis: 'z' });
  return { model: b, fasteners: [] };
}

// ---------------------------------------------------------------------------
// Air intake
// ---------------------------------------------------------------------------

/** Panel airbox base (open top). `tubeEnd` is the throttle body position relative to the airbox origin. */
export function airboxBase(tubeEnd: Vec3): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.24, 0.01, 0.2], M.plastic, { p: [0, -0.045, 0] });
  for (const s of [-1, 1]) {
    b.box([0.01, 0.1, 0.2], M.plastic, { p: [s * 0.115, 0, 0] });
    b.box([0.24, 0.1, 0.01], M.plastic, { p: [0, 0, s * 0.095] });
    b.box([0.05, 0.012, 0.04], M.plastic, { p: [s * 0.13, -0.04, -0.08] });
  }
  const [ex, ey, ez] = tubeEnd;
  b.tube([[0, 0.0, 0.09], [0, 0.03, 0.14], [ex * 0.4, ey * 0.8, ez * 0.9], [ex + 0.01, ey, ez]], 0.035, M.rubber, { radial: 14 });
  return {
    model: b,
    fasteners: [F('bolt_s', [-0.13, -0.03, -0.08], [0, 1, 0]), F('bolt_s', [0.13, -0.03, -0.08], [0, 1, 0]), F('hose_clamp', [ex + 0.02, ey + 0.036, ez], [0, 1, 0])],
  };
}

export function airboxLid(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.24, 0.05, 0.2], M.plastic, { p: [0, 0.025, 0] });
  for (const x of [-0.08, -0.04, 0, 0.04, 0.08]) b.box([0.008, 0.008, 0.18], M.plastic, { p: [x, 0.053, 0] });
  const fasteners: FastenerDef[] = [];
  for (const s of [-1, 1]) for (const z of [-0.06, 0.06]) fasteners.push(F('latch', [s * 0.122, 0.01, z], [s, 0, 0]));
  return { model: b, fasteners };
}

export function panelFilter(pleat: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.21, 0.012, 0.012], M.rubber, { p: [0, 0, 0.08] });
  b.box([0.21, 0.012, 0.012], M.rubber, { p: [0, 0, -0.08] });
  b.box([0.012, 0.012, 0.17], M.rubber, { p: [0.1, 0, 0] });
  b.box([0.012, 0.012, 0.17], M.rubber, { p: [-0.1, 0, 0] });
  for (let i = 0; i < 12; i++) b.box([0.008, 0.02, 0.16], pleat, { p: [-0.088 + i * 0.016, 0.004, 0] });
  return { model: b, fasteners: [] };
}

export function roundAirCleaner(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.17, 0.012, M.plastic, { p: [0, 0.006, 0] });
  b.cyl(0.17, 0.04, M.plastic, { p: [0, 0.02, 0] }, { open: true });
  b.cyl(0.006, 0.09, M.steel, { p: [0, 0.05, 0] });
  b.cyl(0.035, 0.08, M.plastic, { p: [0.2, 0.02, 0] }, { axis: 'x' }); // snorkel
  return { model: b, fasteners: [F('bolt_s', [-0.05, 0.014, 0], [0, 1, 0]), F('bolt_s', [0.05, 0.014, 0], [0, 1, 0])] };
}

export function roundAirCleanerLid(m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.172, 0.014, m, {});
  b.cyl(0.12, 0.008, m, { p: [0, 0.01, 0] });
  return { model: b, fasteners: [F('wing_nut', [0, 0.018, 0], [0, 1, 0])] };
}

export function roundFilter(pleat: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.lathe([[0.12, 0], [0.16, 0], [0.16, 0.05], [0.12, 0.05], [0.12, 0]], pleat, {}, { segs: 36 });
  b.torus(0.14, 0.006, M.rubber, { p: [0, 0.001, 0] }, { axis: 'y' });
  b.torus(0.14, 0.006, M.rubber, { p: [0, 0.049, 0] }, { axis: 'y' });
  return { model: b, fasteners: [] };
}

// ---------------------------------------------------------------------------
// Cooling & electrical (body mounted)
// ---------------------------------------------------------------------------

export function radiator(h: number, w: number): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.035, h, w], M.radiatorCore, {});
  for (let i = 0; i < 18; i++) b.box([0.037, 0.004, w - 0.02], M.darkSteel, { p: [0, -h / 2 + (i + 0.5) * (h / 18), 0] });
  for (const s of [-1, 1]) b.box([0.045, h + 0.04, 0.04], M.plastic, { p: [0, 0, s * (w / 2 + 0.02)] });
  b.cyl(0.02, 0.03, M.steel, { p: [0, h / 2 + 0.03, -w / 2 + 0.04] });
  b.cyl(0.02, 0.06, M.plastic, { p: [-0.04, h / 2 - 0.07, -w / 2 - 0.02] }, { axis: 'x' });
  b.cyl(0.02, 0.06, M.plastic, { p: [-0.04, -h / 2 + 0.07, w / 2 + 0.02] }, { axis: 'x' });
  for (const s of [-1, 1]) b.box([0.04, 0.02, 0.05], M.darkSteel, { p: [0, h / 2 + 0.01, s * (w / 2 - 0.08)] });
  return {
    model: b,
    fasteners: [
      F('bolt_s', [0, h / 2 + 0.022, -(w / 2 - 0.08)], [0, 1, 0]),
      F('bolt_s', [0, h / 2 + 0.022, w / 2 - 0.08], [0, 1, 0]),
      F('hose_clamp', [-0.055, h / 2 - 0.07 + 0.022, -w / 2 - 0.02], [0, 1, 0]),
      F('hose_clamp', [-0.055, -h / 2 + 0.07 + 0.022, w / 2 + 0.02], [0, 1, 0]),
    ],
  };
}

export function radiatorFan(h: number, w: number): BuiltPart {
  const b = new ModelBuilder();
  const R = Math.min(h, w) * 0.42;
  b.box([0.02, 0.02, w], M.plastic, { p: [0, h / 2, 0] });
  b.box([0.02, 0.02, w], M.plastic, { p: [0, -h / 2, 0] });
  b.box([0.02, h, 0.02], M.plastic, { p: [0, 0, w / 2] });
  b.box([0.02, h, 0.02], M.plastic, { p: [0, 0, -w / 2] });
  b.torus(R, 0.012, M.plastic, {}, { axis: 'x' });
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * PI * 2;
    b.box([0.004, R * 0.9, 0.05], M.plastic, { p: [-0.01, Math.cos(a) * R * 0.48, Math.sin(a) * R * 0.48], r: [a + 0.3, 0, 0] });
  }
  b.cyl(0.045, 0.07, M.darkSteel, { p: [-0.035, 0, 0] }, { axis: 'x' });
  return {
    model: b,
    fasteners: [F('bolt_s', [0, h / 2 + 0.012, -w / 2 + 0.05], [0, 1, 0]), F('bolt_s', [0, h / 2 + 0.012, w / 2 - 0.05], [0, 1, 0]), F('bolt_s', [0, -h / 2 - 0.012, 0], [0, -1, 0])],
  };
}

export function battery(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.24, 0.17, 0.175], M.plastic, {});
  b.box([0.242, 0.05, 0.177], M.blue, { p: [0, 0.02, 0] });
  b.box([0.24, 0.012, 0.175], M.plasticGrey, { p: [0, 0.091, 0] });
  for (const s of [-1, 1]) {
    b.cyl(0.012, 0.025, M.steel, { p: [s * 0.085, 0.105, 0.055] });
    b.box([0.03, 0.012, 0.03], s > 0 ? M.red : M.plastic, { p: [s * 0.085, 0.1, 0.02] });
  }
  b.box([0.03, 0.01, 0.2], M.steel, { p: [0, 0.102, -0.03] });
  return {
    model: b,
    fasteners: [F('bolt_s', [0, 0.11, -0.03], [0, 1, 0]), F('bolt_s', [-0.085, 0.12, 0.055], [0, 1, 0]), F('bolt_s', [0.085, 0.12, 0.055], [0, 1, 0])],
  };
}

// ---------------------------------------------------------------------------
// Wheels, brakes, suspension (wheel-local: axis +Z points outward)
// ---------------------------------------------------------------------------

export type RimStyle = 'steel' | 'alloy' | 'chrome';

export function wheel(R: number, rimR: number, w: number, style: RimStyle, lugR: number): BuiltPart {
  const b = new ModelBuilder();
  const h = w / 2;
  b.lathe(
    [[rimR, -h + 0.012], [R - 0.035, -h], [R - 0.008, -h + 0.03], [R, -h + 0.06], [R, h - 0.06], [R - 0.008, h - 0.03], [R - 0.035, h], [rimR, h - 0.012]],
    M.rubber, {}, { axis: 'z', segs: 36 },
  );
  // tread blocks
  for (let i = 0; i < 36; i++) {
    const a = (i / 36) * PI * 2;
    b.box([0.008, 0.028, w - 0.07], M.rubber, { p: [Math.cos(a) * (R + 0.001), Math.sin(a) * (R + 0.001), 0], r: [0, 0, a] });
  }
  const rimMat = style === 'chrome' ? M.chrome : style === 'alloy' ? M.aluminum : M.plasticGrey;
  b.cyl(rimR, w - 0.03, rimMat, {}, { axis: 'z', open: true, segs: 32 });
  b.torus(rimR - 0.003, 0.008, rimMat, { p: [0, 0, h - 0.02] }, { axis: 'z', segs: 36 });
  if (style === 'steel') {
    b.cyl(rimR - 0.004, 0.012, M.plasticGrey, { p: [0, 0, 0.03] }, { axis: 'z', segs: 32 });
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * PI * 2;
      b.cyl(0.018, 0.004, M.darkSteel, { p: [Math.cos(a) * rimR * 0.66, Math.sin(a) * rimR * 0.66, 0.037] }, { axis: 'z', segs: 12 });
    }
    b.cyl(0.03, 0.012, M.chrome, { p: [0, 0, 0.045] }, { axis: 'z' });
  } else {
    const spokes = style === 'chrome' ? 10 : 5;
    b.cyl(lugR + 0.03, 0.014, rimMat, { p: [0, 0, 0.032] }, { axis: 'z' });
    for (let i = 0; i < spokes; i++) {
      const a = (i / spokes) * PI * 2 + PI / 2;
      const len = rimR - lugR - 0.02;
      const r0 = lugR + 0.02 + len / 2;
      b.box([len, style === 'chrome' ? 0.014 : 0.032, 0.016], rimMat, { p: [Math.cos(a) * r0, Math.sin(a) * r0, 0.034], r: [0, 0, a] });
    }
    b.cyl(0.028, 0.014, style === 'chrome' ? M.chrome : M.plastic, { p: [0, 0, 0.045] }, { axis: 'z' });
  }
  const fasteners: FastenerDef[] = [];
  for (let i = 0; i < 5; i++) {
    const a = PI / 2 + (i * 2 * PI) / 5;
    fasteners.push(F('lug_nut', [Math.cos(a) * lugR, Math.sin(a) * lugR, 0.052], [0, 0, 1]));
  }
  return { model: b, fasteners };
}

export function rotor(Rr: number, lugR: number): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(Rr, 0.022, M.steel, {}, { axis: 'z', segs: 36 });
  b.cyl(Rr * 0.62, 0.024, M.darkSteel, {}, { axis: 'z', segs: 24 });
  b.cyl(0.075, 0.035, M.darkSteel, { p: [0, 0, 0.028] }, { axis: 'z' });
  for (let i = 0; i < 5; i++) {
    const a = PI / 2 + (i * 2 * PI) / 5;
    b.cyl(0.006, 0.03, M.steel, { p: [Math.cos(a) * lugR, Math.sin(a) * lugR, 0.06] }, { axis: 'z', segs: 8 });
  }
  return { model: b, fasteners: [F('screw', [0.0, -0.03, 0.047], [0, 0, 1])] };
}

export function caliper(Rr: number, m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  const x = -(Rr - 0.025);
  b.box([0.05, 0.12, 0.075], m, { p: [x, 0.025, 0] });
  b.cyl(0.025, 0.02, m, { p: [x, 0.025, -0.045] }, { axis: 'z' });
  b.box([0.02, 0.14, 0.02], M.darkSteel, { p: [x - 0.03, 0.025, -0.03] });
  b.tube([[x - 0.02, 0.08, -0.03], [x - 0.05, 0.12, -0.06], [x - 0.08, 0.16, -0.12]], 0.004, M.rubber);
  return { model: b, fasteners: [F('bolt_m', [x - 0.012, 0.025 - 0.045, 0.04], [0, 0, 1]), F('bolt_m', [x - 0.012, 0.025 + 0.045, 0.04], [0, 0, 1])] };
}

export function brakePads(Rr: number): BuiltPart {
  const b = new ModelBuilder();
  const x = -(Rr - 0.025);
  for (const s of [-1, 1]) {
    b.box([0.04, 0.09, 0.009], M.friction, { p: [x, 0.025, s * 0.0155] });
    b.box([0.045, 0.1, 0.005], M.steel, { p: [x, 0.025, s * 0.0225] });
  }
  return { model: b, fasteners: [F('clip', [x, 0.075, 0.03], [0, 0, 1])] };
}

export function strut(len: number, springR: number, bodyMat: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.06, 0.06, 0.05], M.darkSteel, { p: [0, 0.02, 0] });
  b.cyl(0.028, len * 0.55, bodyMat, { p: [0, 0.05 + len * 0.275, 0] });
  b.cyl(0.01, len * 0.25, M.chrome, { p: [0, 0.05 + len * 0.55 + len * 0.1, 0] });
  const pts: Vec3[] = [];
  const y0 = 0.05 + len * 0.3;
  const y1 = len * 0.95;
  for (let i = 0; i <= 64; i++) {
    const t = i / 64;
    const a = t * PI * 2 * 6;
    pts.push([Math.cos(a) * springR, y0 + (y1 - y0) * t, Math.sin(a) * springR]);
  }
  b.tube(pts, 0.008, M.red, { segs: 160, radial: 6, tension: 0.5 });
  b.cyl(springR + 0.015, 0.008, M.darkSteel, { p: [0, y0, 0] });
  b.cyl(springR + 0.015, 0.008, M.darkSteel, { p: [0, y1, 0] });
  b.cyl(0.06, 0.03, M.rubber, { p: [0, y1 + 0.03, 0] });
  return { model: b, fasteners: [F('bolt_l', [0, 0.0, 0.027], [0, 0, 1]), F('bolt_l', [0, 0.04, 0.027], [0, 0, 1])] };
}

export function shock(len: number, bodyMat: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.torus(0.022, 0.009, M.rubber, {}, { axis: 'z' });
  b.cyl(0.022, len * 0.6, bodyMat, { p: [0, 0.03 + len * 0.3, 0] });
  b.cyl(0.009, len * 0.35, M.chrome, { p: [0, 0.03 + len * 0.6 + len * 0.15, 0] });
  b.torus(0.022, 0.009, M.rubber, { p: [0, len, 0] }, { axis: 'z' });
  return { model: b, fasteners: [F('bolt_l', [0, 0, 0.02], [0, 0, 1]), F('bolt_l', [0, len, 0.02], [0, 0, 1])] };
}

// ---------------------------------------------------------------------------
// V8 engine (engine-local origin = block bottom center, crank at y = -0.03)
// ---------------------------------------------------------------------------

export const V8 = {
  cylX: [-0.21, -0.07, 0.07, 0.21],
  crankY: -0.03,
  deckDist: 0.3,
  bank: PI / 4,
  beltX: 0.405,
};

export function v8Block(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.64, 0.12, 0.38], M.castIron, { p: [0, 0.06, 0] });
  const c = Math.cos(V8.bank);
  const s = Math.sin(V8.bank);
  for (const side of [1, -1]) {
    const deck: Vec3 = [0, V8.crankY + V8.deckDist * c, side * V8.deckDist * s];
    const rot: Vec3 = side > 0 ? [V8.bank, 0, 0] : [-V8.bank, PI, 0];
    perforatedSlab(b, 0.6, 0.17, 0.17, V8.cylX.map((x) => [x, 0, 0.045] as [number, number, number]), M.castIron, -0.17, 0.015, { p: deck, r: rot });
  }
  b.box([0.03, 0.3, 0.3], M.aluminum, { p: [0.335, 0.12, 0] }); // timing cover
  for (const side of [-1, 1]) for (const x of [-0.12, 0.12]) b.box([0.07, 0.06, 0.03], M.darkSteel, { p: [x, 0.08, side * 0.205] });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.12, 0.12]) for (const side of [-1, 1]) fasteners.push(F('bolt_l', [x, 0.08, side * 0.222], [0, 0, side]));
  return { model: b, fasteners };
}

export function v8Head(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.58, 0.1, 0.18], M.castIron, { p: [0, 0.05, 0] });
  for (const x of V8.cylX) for (const dx of [-0.025, 0.025]) b.box([0.012, 0.015, 0.05], M.steel, { p: [x + dx, 0.107, 0] }); // rockers
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.24, -0.12, 0, 0.12, 0.24]) for (const z of [-0.06, 0.06]) fasteners.push(F('bolt_l', [x, 0.1, z], [0, 1, 0]));
  return { model: b, fasteners };
}

export function valveCoverV8(m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  b.extrude(roundedRect(0.56, 0.16, 0.03), 0.06, m, { r: [-PI / 2, 0, 0] });
  b.box([0.58, 0.008, 0.17], M.darkSteel, { p: [0, 0.004, 0] });
  for (const x of [-0.18, -0.06, 0.06, 0.18]) b.box([0.05, 0.006, 0.12], m, { p: [x, 0.063, 0] });
  b.cyl(0.02, 0.02, M.plastic, { p: [0.2, 0.07, 0] });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.25, 0, 0.25]) for (const z of [-0.068, 0.068]) fasteners.push(F('bolt_s', [x, 0.064, z], [0, 1, 0]));
  return { model: b, fasteners };
}

export function v8Camshaft(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.016, 0.62, M.steel, {}, { axis: 'x' });
  for (const x of [-0.24, -0.19, -0.14, -0.09, -0.04, 0.01, 0.06, 0.11, 0.16, 0.21]) b.cyl(0.024, 0.012, M.steel, { p: [x, 0.006, 0] }, { axis: 'x' });
  b.box([0.01, 0.08, 0.08], M.darkSteel, { p: [0.3, 0, 0] });
  return { model: b, fasteners: [F('bolt_s', [0.306, 0.025, 0.025], [1, 0, 0]), F('bolt_s', [0.306, -0.025, -0.025], [1, 0, 0])] };
}

export function intakeV8(): BuiltPart {
  const b = new ModelBuilder();
  b.box([0.54, 0.09, 0.22], M.aluminum, { p: [0, 0.045, 0] });
  b.cyl(0.06, 0.03, M.aluminum, { p: [0, 0.105, 0] });
  for (const x of V8.cylX) for (const side of [-1, 1]) b.tube([[x, 0.01, side * 0.1], [x, -0.03, side * 0.14], [x, -0.075, side * 0.18]], 0.022, M.aluminum);
  for (const side of [-1, 1]) b.box([0.54, 0.012, 0.03], M.aluminum, { p: [0, 0.006, side * 0.12] });
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.21, -0.07, 0.07, 0.21]) for (const side of [-1, 1]) fasteners.push(F('bolt_m', [x, 0.018, side * 0.125], [0, 1, 0]));
  return { model: b, fasteners };
}

export function distributor(): BuiltPart {
  const b = new ModelBuilder();
  b.cyl(0.032, 0.1, M.aluminum, { p: [0, 0.05, 0] });
  b.cyl(0.058, 0.05, M.plastic, { p: [0, 0.12, 0] });
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * PI * 2;
    b.cyl(0.008, 0.02, M.plastic, { p: [Math.cos(a) * 0.042, 0.155, Math.sin(a) * 0.042] });
  }
  b.box([0.05, 0.008, 0.03], M.steel, { p: [0.035, 0.004, 0] });
  return { model: b, fasteners: [F('bolt_m', [0.05, 0.012, 0], [0, 1, 0])] };
}

export function exhaustManifoldV8(): { part: BuiltPart; outlet: Vec3 } {
  const b = new ModelBuilder();
  b.box([0.56, 0.05, 0.012], M.exhaust, { p: [0, 0, 0.006] });
  for (const x of V8.cylX) b.tube([[x, 0, 0.01], [x, -0.005, 0.04], [x * 0.95, -0.015, 0.06]], 0.018, M.exhaust);
  b.cyl(0.028, 0.5, M.exhaust, { p: [-0.02, -0.015, 0.062] }, { axis: 'x' });
  // outlet elbow going "down" in world space (see notes in cars.ts: local (0,-0.707,0.707))
  const d: Vec3 = [0, -0.707, 0.707];
  const start: Vec3 = [-0.27, -0.015, 0.062];
  const out: Vec3 = [start[0] - 0.03, start[1] + d[1] * 0.12, start[2] + d[2] * 0.12];
  b.tube([start, [start[0] - 0.02, start[1] + d[1] * 0.05, start[2] + d[2] * 0.05], out], 0.028, M.exhaust);
  const fasteners: FastenerDef[] = [];
  for (const x of [-0.14, 0, 0.14]) for (const y of [-0.016, 0.016]) fasteners.push(F('bolt_m', [x, y, 0.013], [0, 0, 1]));
  return { part: { model: b, fasteners }, outlet: out };
}

// ---------------------------------------------------------------------------
// Body parts
// ---------------------------------------------------------------------------

/** Hood panel. `dx, dy`: vector from the hinge (origin) to the front edge. */
export function hood(dx: number, dy: number, width: number, scoop: boolean): BuiltPart {
  const b = new ModelBuilder();
  const len = Math.hypot(dx, dy);
  const ang = Math.atan2(dy, dx);
  const s = new THREE.Shape();
  s.moveTo(0, 0);
  s.lineTo(len, 0);
  s.lineTo(len, -0.018);
  s.lineTo(0, -0.018);
  s.lineTo(0, 0);
  b.extrude(s, width, M.paint, { p: [0, 0, -width / 2], r: [0, 0, 0] });
  // crown: a slightly raised center strip
  b.box([len * 0.9, 0.01, width * 0.5], M.paint, { p: [len * 0.5, 0.004, 0] });
  if (scoop) {
    b.box([0.4, 0.07, 0.42], M.paint, { p: [len * 0.45, 0.04, 0] });
    b.box([0.02, 0.05, 0.36], M.plastic, { p: [len * 0.45 + 0.2, 0.045, 0] });
  }
  for (const side of [-1, 1]) {
    b.box([0.14, 0.02, 0.03], M.darkSteel, { p: [0.07, -0.028, side * (width / 2 - 0.09)] });
  }
  b.box([0.04, 0.03, 0.08], M.darkSteel, { p: [len - 0.05, -0.03, 0] });
  // rotate the whole panel so it follows the hood line (hinge at origin)
  for (const it of b.items) it.geo.rotateZ(ang);
  const rot = (p: Vec3): Vec3 => [p[0] * Math.cos(ang) - p[1] * Math.sin(ang), p[0] * Math.sin(ang) + p[1] * Math.cos(ang), p[2]];
  const fasteners: FastenerDef[] = [];
  for (const side of [-1, 1]) for (const x of [0.03, 0.11]) fasteners.push(F('bolt_s', rot([x, -0.04, side * (width / 2 - 0.09)]), [0, -1, 0]));
  return { model: b, fasteners };
}

export function spoiler(width: number): BuiltPart {
  const b = new ModelBuilder();
  for (const s of [-1, 1]) b.box([0.05, 0.1, 0.03], M.plastic, { p: [0, 0.05, s * (width / 2 - 0.12)] });
  const wing = new THREE.Shape();
  wing.moveTo(-0.13, 0);
  wing.quadraticCurveTo(-0.05, 0.035, 0.12, 0.012);
  wing.lineTo(0.12, 0.0);
  wing.quadraticCurveTo(-0.03, 0.005, -0.13, 0);
  b.extrude(wing, width, M.paint, { p: [0, 0.1, -width / 2] });
  for (const s of [-1, 1]) b.box([0.26, 0.07, 0.008], M.paint, { p: [0, 0.11, s * (width / 2)] });
  return { model: b, fasteners: [F('bolt_s', [-0.027, 0.012, -(width / 2 - 0.12)], [-1, 0, 0]), F('bolt_s', [-0.027, 0.012, width / 2 - 0.12], [-1, 0, 0])] };
}
