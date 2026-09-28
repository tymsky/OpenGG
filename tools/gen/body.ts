// Parametric placeholder car bodies, split like the original's BODY region:
// a static shell (floor, firewall, pillars, rear quarters, lights) plus removable panels
// (hood, fenders, doors, windows, windshield, roof, rear window, trunk lid / tailgate, bumpers).

import * as THREE from 'three';
import { ModelBuilder, M, type MatDef } from './geo.ts';
import type { FastenerDef, Vec3 } from './types.ts';

export type SegRole = 'paint' | 'plastic' | 'chrome' | 'none' | 'hood' | 'windshield' | 'roof' | 'rear_window' | 'trunk' | 'tailgate';

export interface Band {
  key: string;
  name: string;
  family: 'fender' | 'door' | 'quarter';
  x0: number;
  x1: number;
}

export interface BodySpec {
  style: 'sedan' | 'coupe' | 'pickup';
  width: number;
  bottomY: number;
  axles: [number, number];
  track: number;
  wheelR: number;
  archR: number;
  /** Silhouette from the front-bottom corner over the roof to the rear-bottom corner. */
  outline: [number, number][];
  /** Role of each outline segment (length = outline.length - 1). */
  segs: SegRole[];
  /** Side bands, front to back. `quarter` bands stay part of the static shell. */
  bands: Band[];
  /** Side window per door band key. */
  windows: Record<string, [number, number][]>;
  firewallX: number;
  bayFrontX: number;
  seats: { x: number; y: number }[];
  dash: { x: number; y: number };
  bed?: { floorY: number; frontX: number; rearX: number; rearWindow: [number, number, number] };
  front: { lightY: number; lightH: number; grilleH: number; bumperY: number; bumperMat: MatDef };
  rear: { lightY: number; lightH: number; bumperY: number; bumperMat: MatDef; lightsVertical?: boolean };
  engine: Vec3;
  engineLen: number;
  /** The second-generation body's corners, rounded: a radius for each outline point (0: left sharp). */
  round?: number[];
}

export interface PanelSpec {
  key: string;
  name: string;
  family: string;
  category: string;
  origin: Vec3;
  model: ModelBuilder;
  fasteners: FastenerDef[];
  removeDir: Vec3;
  parent: string | null;
  blockedBy: string[];
  price: number;
}

const T = 0.03;
const PI = Math.PI;

export function hingeOf(spec: BodySpec): [number, number] {
  return spec.outline[spec.segs.indexOf('hood') + 1];
}
export function hoodFrontOf(spec: BodySpec): [number, number] {
  return spec.outline[spec.segs.indexOf('hood')];
}

/** y of the silhouette's top at x. */
export function outlineY(spec: BodySpec, x: number): number {
  let best = -Infinity;
  const o = spec.outline;
  for (let i = 0; i < o.length - 1; i++) {
    const [ax, ay] = o[i];
    const [bx, by] = o[i + 1];
    if (Math.abs(ax - bx) < 1e-6) continue;
    if ((x <= ax && x >= bx) || (x >= ax && x <= bx)) best = Math.max(best, ay + ((by - ay) * (x - ax)) / (bx - ax));
  }
  if (best > -Infinity) return best;
  // Past the ends of the outline: use the nearest outline point.
  let nearest = o[0];
  for (const p of o) if (Math.abs(p[0] - x) < Math.abs(nearest[0] - x)) nearest = p;
  return nearest[1];
}

/** Closed side silhouette including the wheel arches. */
export function silhouette(spec: BodySpec, archSegs = 24): [number, number][] {
  const pts: [number, number][] = spec.outline.map((p) => [p[0], p[1]]);
  const dy = spec.bottomY - spec.wheelR;
  const dx = Math.sqrt(Math.max(0, spec.archR * spec.archR - dy * dy));
  const [fx, rx] = spec.axles;
  for (const ax of [rx, fx]) {
    pts.push([ax - dx, spec.bottomY]);
    let a0 = Math.atan2(dy, -dx);
    if (a0 < 0) a0 += 2 * PI;
    const a1 = Math.atan2(dy, dx);
    const N = archSegs;
    for (let k = 1; k < N; k++) {
      const a = a0 + ((a1 - a0) * k) / N;
      pts.push([ax + Math.cos(a) * spec.archR, spec.wheelR + Math.sin(a) * spec.archR]);
    }
    pts.push([ax + dx, spec.bottomY]);
  }
  return pts;
}

/** Sutherland-Hodgman clip of a polygon to the slab x0 <= x <= x1. */
export function clipX(poly: [number, number][], x0: number, x1: number): [number, number][] {
  const clip = (input: [number, number][], inside: (p: [number, number]) => boolean, edgeX: number) => {
    const out: [number, number][] = [];
    for (let i = 0; i < input.length; i++) {
      const cur = input[i];
      const prev = input[(i + input.length - 1) % input.length];
      const inCur = inside(cur);
      const inPrev = inside(prev);
      if (inCur !== inPrev) {
        const t = (edgeX - prev[0]) / (cur[0] - prev[0]);
        out.push([edgeX, prev[1] + (cur[1] - prev[1]) * t]);
      }
      if (inCur) out.push(cur);
    }
    return out;
  };
  const a = clip(poly, (p) => p[0] >= x0, x0);
  return clip(a, (p) => p[0] <= x1, x1);
}

function shapeOf(poly: [number, number][], holes: [number, number][][] = []): THREE.Shape {
  const s = new THREE.Shape();
  s.moveTo(poly[0][0], poly[0][1]);
  for (let i = 1; i < poly.length; i++) s.lineTo(poly[i][0], poly[i][1]);
  s.lineTo(poly[0][0], poly[0][1]);
  for (const h of holes) {
    const p = new THREE.Path();
    p.moveTo(h[0][0], h[0][1]);
    for (let i = 1; i < h.length; i++) p.lineTo(h[i][0], h[i][1]);
    p.lineTo(h[0][0], h[0][1]);
    s.holes.push(p);
  }
  return s;
}

const sideZ = (W: number, side: number) => (side > 0 ? W / 2 - T : -W / 2);

/** Build the static shell and the removable body panels of a car. */
export function buildBody(spec: BodySpec): { shell: ModelBuilder; panels: PanelSpec[] } {
  const shell = new ModelBuilder();
  const panels: PanelSpec[] = [];
  const W = spec.width;
  const o = spec.outline;
  const sil = silhouette(spec);
  const sides: [number, string, string][] = [[-1, 'l', 'Left'], [1, 'r', 'Right']];

  // ---- side bands ---------------------------------------------------------
  spec.bands.forEach((band, bi) => {
    const gap = 0.004;
    const x0 = band.x0 + (bi === spec.bands.length - 1 ? 0 : gap);
    const x1 = band.x1 - (bi === 0 ? 0 : gap);
    const poly = clipX(sil, x0, x1);
    const win = spec.windows[band.key];
    const shape = shapeOf(poly, win ? [win] : []);
    for (const [side, s, S] of sides) {
      const z = sideZ(W, side);
      if (band.family === 'quarter') {
        shell.extrude(shape, T, M.paint, { p: [0, 0, z] });
        continue;
      }
      const key = `${band.key}_${s}`;
      const origin: Vec3 = [(x0 + x1) / 2, spec.bottomY, side * (W / 2)];
      const b = new ModelBuilder().extrude(shape, T, M.paint, { p: [-origin[0], -origin[1], z - origin[2]] });
      const fasteners: FastenerDef[] = [];
      if (band.family === 'door') {
        const beltY = win ? win[0][1] : spec.bottomY + 0.5;
        // handle
        b.box([0.1, 0.022, 0.018], M.chrome, { p: [x0 + 0.18 - origin[0], beltY - 0.08 - origin[1], side * 0.008] });
        for (const y of [beltY - 0.12, spec.bottomY + 0.18]) fasteners.push({ kind: 'bolt_s', pos: [x1 - 0.035 - origin[0], y - origin[1], side * 0.004], dir: [0, 0, side] });
        panels.push({ key, name: `${band.name.replace('{S}', S)}`, family: 'door', category: 'body', origin, model: b, fasteners, removeDir: [0, 0, side], parent: null, blockedBy: [], price: 260 });
        if (win) {
          const g = new ModelBuilder().extrude(shapeOf(win), 0.005, M.glass, { p: [-origin[0], -origin[1], (side > 0 ? W / 2 - 0.018 : -W / 2 + 0.013) - origin[2]] });
          const wkey = `window_${band.key.replace('door_', '')}_${s}`;
          panels.push({ key: wkey, name: `${band.name.replace('{S}', S).replace('Door', 'Window')}`, family: 'window', category: 'glass', origin, model: g, fasteners: [], removeDir: [0, 1, 0], parent: key, blockedBy: [], price: 70 });
        }
      } else {
        // fender: bolts along the top edge, under the hood's edge
        for (const x of [x1 - 0.08, (x0 + x1) / 2, x0 + 0.12]) {
          fasteners.push({ kind: 'bolt_s', pos: [x - origin[0], outlineY(spec, x) - 0.012 - origin[1], -side * 0.018], dir: [0, 1, 0] });
        }
        panels.push({ key, name: `${S === 'Left' ? 'Front Left' : 'Front Right'} Fender`, family: 'fender', category: 'body', origin, model: b, fasteners, removeDir: [0.2, 0.3, side], parent: null, blockedBy: ['hood', 'bumper_f'], price: 180 });
      }
    }
  });

  // ---- skin between the side panels ------------------------------------------
  const staticMat = (r: SegRole): MatDef | null => (r === 'paint' ? M.paint : r === 'plastic' ? M.plastic : r === 'chrome' ? M.chrome : null);
  for (let i = 0; i < o.length - 1; i++) {
    const role = spec.segs[i];
    const m = staticMat(role);
    if (m) {
      shell.ruled([o[i], o[i + 1]], -W / 2, W / 2, m);
      continue;
    }
    if (role === 'none' || role === 'hood') continue;
    const glass = role === 'windshield' || role === 'rear_window';
    const inset = glass ? 0.06 : 0.003;
    if (glass) {
      shell.ruled([o[i], o[i + 1]], -W / 2, -W / 2 + inset, M.paint); // pillars
      shell.ruled([o[i], o[i + 1]], W / 2 - inset, W / 2, M.paint);
    }
    const mid: Vec3 = [(o[i][0] + o[i + 1][0]) / 2, (o[i][1] + o[i + 1][1]) / 2, 0];
    const b = new ModelBuilder().ruled([o[i], o[i + 1]], -W / 2 + inset, W / 2 - inset, glass ? M.glass : M.paint, { p: [-mid[0], -mid[1], 0] });
    const fasteners: FastenerDef[] = [];
    const names: Record<string, [string, string, number]> = {
      windshield: ['Front Windshield', 'glass', 220],
      rear_window: ['Back Windshield', 'glass', 180],
      roof: ['Roof', 'body', 420],
      trunk: ['Trunk Lid', 'body', 240],
      tailgate: ['Tailgate', 'body', 260],
    };
    const [name, category, price] = names[role];
    let removeDir: Vec3 = [0, 1, 0];
    let blockedBy: string[] = [];
    if (role === 'trunk') {
      const [hx, hy] = o[i];
      for (const z of [-0.45, 0.45]) fasteners.push({ kind: 'bolt_s', pos: [hx - 0.04 - mid[0], hy + 0.004 - mid[1], z], dir: [0, 1, 0] });
    } else if (role === 'tailgate') {
      removeDir = [-1, 0.2, 0];
      for (const z of [-(W / 2 - 0.1), W / 2 - 0.1]) fasteners.push({ kind: 'bolt_s', pos: [o[i + 1][0] - 0.012 - mid[0], spec.bottomY + 0.18 - mid[1], z], dir: [-1, 0, 0] });
    } else if (role === 'roof') {
      blockedBy = ['windshield', 'rear_window'];
    }
    panels.push({ key: role, name, family: role, category, origin: mid, model: b, fasteners, removeDir, parent: null, blockedBy, price });
  }

  // ---- bumpers --------------------------------------------------------------------
  const fr = spec.front;
  const fxFace = Math.max(...o.slice(0, spec.segs.indexOf('hood') + 1).map((p) => p[0]));
  const rr = spec.rear;
  const rxFace = Math.min(...o.slice(spec.segs.indexOf('hood')).map((p) => p[0]));
  for (const [key, name, x, y, mat, dirX] of [
    ['bumper_f', 'Front Bumper', fxFace, fr.bumperY, fr.bumperMat, 1],
    ['bumper_r', 'Back Bumper', rxFace + 0.02, rr.bumperY, rr.bumperMat, -1],
  ] as const) {
    const origin: Vec3 = [x, y, 0];
    const b = new ModelBuilder().box([0.1, 0.18, W + 0.02], mat, {});
    b.box([0.012, 0.09, 0.3], M.paper, { p: [dirX * 0.056, dirX > 0 ? 0.02 : 0.15, 0] });
    const fasteners: FastenerDef[] = [-1, 1].map((s) => ({ kind: 'bolt_s', pos: [0, 0.092, s * (W / 2 - 0.08)] as Vec3, dir: [0, 1, 0] as Vec3 }));
    panels.push({ key, name, family: 'bumper', category: 'body', origin, model: b, fasteners, removeDir: [dirX, 0, 0], parent: null, blockedBy: [], price: 150 });
  }

  // ---- static shell ---------------------------------------------------------------
  const hinge = hingeOf(spec);
  shell.box([0.025, hinge[1] - spec.bottomY, W - 2 * T], M.underbody, { p: [spec.firewallX, (hinge[1] + spec.bottomY) / 2, 0] });
  shell.box([0.12, 0.02, W - 2 * T], M.plastic, { p: [spec.firewallX - 0.05, hinge[1] + 0.005, 0] });
  const frontTop = hoodFrontOf(spec)[1];
  for (const side of [-1, 1]) shell.box([0.04, frontTop - spec.bottomY - 0.05, 0.05], M.underbody, { p: [spec.bayFrontX, (frontTop + spec.bottomY) / 2 - 0.02, side * (W / 2 - 0.2)] });
  shell.box([0.04, 0.04, W - 0.3], M.underbody, { p: [spec.bayFrontX, spec.bottomY + 0.08, 0] });
  shell.box([0.04, 0.04, W - 0.3], M.underbody, { p: [spec.bayFrontX, frontTop - 0.06, 0] });
  const bayLen = spec.bayFrontX - spec.firewallX;
  for (const side of [-1, 1]) shell.box([bayLen, 0.02, 0.22], M.underbody, { p: [spec.firewallX + bayLen / 2, frontTop - 0.1, side * (W / 2 - 0.14)] });

  const floorFront = spec.firewallX;
  const floorRear = o[o.length - 1][0] + 0.05;
  const floorLen = floorFront - floorRear;
  const tunnelHalf = 0.16;
  for (const side of [-1, 1]) {
    const w = W / 2 - T - tunnelHalf;
    shell.box([floorLen, 0.02, w], M.underbody, { p: [floorRear + floorLen / 2, spec.bottomY + 0.01, side * (tunnelHalf + w / 2)] });
    shell.box([floorLen, 0.25, 0.02], M.underbody, { p: [floorRear + floorLen / 2, spec.bottomY + 0.135, side * tunnelHalf] });
    // sills, so the car keeps its outline with the doors off
    shell.box([spec.bands.filter((b) => b.family === 'door').reduce((a, b) => a + (b.x1 - b.x0), 0), 0.08, 0.06], M.paint, {
      p: [spec.bands.filter((b) => b.family === 'door').reduce((a, b) => a + (b.x0 + b.x1) / 2, 0) / Math.max(1, spec.bands.filter((b) => b.family === 'door').length), spec.bottomY + 0.04, side * (W / 2 - 0.05)],
    });
  }
  shell.box([floorLen, 0.02, tunnelHalf * 2], M.underbody, { p: [floorRear + floorLen / 2, spec.bottomY + 0.26, 0] });

  const [fx, rx] = spec.axles;
  for (const ax of [fx, rx]) {
    for (const side of [-1, 1]) {
      shell.cyl(spec.archR + 0.01, 0.34, M.plastic, { p: [ax, spec.wheelR, side * (W / 2 - 0.19)] }, { axis: 'z', open: true, thetaStart: PI / 2, thetaLength: PI });
      const hubZ = side * (spec.track / 2 - 0.07);
      shell.cyl(0.05, 0.06, M.darkSteel, { p: [ax, spec.wheelR, hubZ] }, { axis: 'z' });
      shell.box([0.06, 0.16, 0.05], M.castIron, { p: [ax, spec.wheelR + 0.03, hubZ - side * 0.06] });
      shell.box([0.05, 0.04, spec.track / 2 - 0.2], M.darkSteel, { p: [ax, spec.wheelR - 0.06, side * ((spec.track / 2 - 0.2) / 2 + 0.1)] });
    }
  }

  // lights and grille (the front and rear faces stay with the shell)
  shell.box([0.04, fr.lightH, 0.3], M.headlight, { p: [fxFace - 0.02, fr.lightY, W / 2 - 0.22] });
  shell.box([0.04, fr.lightH, 0.3], M.headlight, { p: [fxFace - 0.02, fr.lightY, -(W / 2 - 0.22)] });
  shell.box([0.03, fr.grilleH, W * 0.36], M.plastic, { p: [fxFace - 0.01, fr.lightY, 0] });
  for (let i = 0; i < 4; i++) shell.box([0.035, 0.008, W * 0.36], M.chrome, { p: [fxFace - 0.008, fr.lightY - fr.grilleH / 2 + (i + 0.5) * (fr.grilleH / 4), 0] });
  for (const side of [-1, 1]) shell.box([0.04, 0.04, 0.1], M.amber, { p: [fxFace - 0.03, fr.bumperY + 0.12, side * (W / 2 - 0.12)] });
  const tlSize: Vec3 = rr.lightsVertical ? [0.03, rr.lightH, 0.1] : [0.03, rr.lightH, 0.34];
  for (const side of [-1, 1]) shell.box(tlSize, M.taillight, { p: [rxFace + 0.005, rr.lightY, side * (W / 2 - (rr.lightsVertical ? 0.06 : 0.22))] });

  // interior
  shell.box([0.2, 0.12, W - 0.1], M.fabric, { p: [spec.dash.x, spec.dash.y, 0] });
  shell.torus(0.17, 0.018, M.plastic, { p: [spec.dash.x - 0.22, spec.dash.y + 0.06, -(W / 2 - 0.45)], r: [0, 0, 0.35] }, { axis: 'x' });
  spec.seats.forEach((seat, i) => {
    for (const side of i === 0 ? [-1, 1] : [0]) {
      const zw = side === 0 ? W - 0.3 : 0.48;
      shell.box([0.5, 0.12, zw], M.fabric, { p: [seat.x, seat.y, side * 0.38] });
      shell.box([0.12, 0.55, zw], M.fabric, { p: [seat.x - 0.25, seat.y + 0.3, side * 0.38], r: [0, 0, 0.15] });
    }
  });
  for (const side of [-1, 1]) {
    const firstDoor = spec.bands.find((b) => b.family === 'door');
    const beltY = firstDoor && spec.windows[firstDoor.key] ? spec.windows[firstDoor.key][0][1] : spec.bottomY + 0.6;
    shell.box([0.07, 0.09, 0.14], M.paint, { p: [spec.firewallX - 0.12, beltY + 0.06, side * (W / 2 + 0.07)] });
  }

  if (spec.bed) {
    const bd = spec.bed;
    const len = bd.frontX - bd.rearX;
    shell.box([len, 0.02, W - 2 * T], M.paint, { p: [bd.rearX + len / 2, bd.floorY, 0] });
    for (let i = 0; i < 7; i++) shell.box([len, 0.012, 0.03], M.darkSteel, { p: [bd.rearX + len / 2, bd.floorY + 0.012, -W / 2 + 0.25 + i * ((W - 0.5) / 6)] });
    const railY = o[spec.segs.indexOf('none', spec.segs.indexOf('hood') + 1)]?.[1] ?? bd.floorY + 0.3;
    shell.box([0.03, railY - bd.floorY, W - 2 * T], M.paint, { p: [bd.frontX + 0.015, (railY + bd.floorY) / 2, 0] });
    const [wx, wy, ww] = bd.rearWindow;
    const cab = new ModelBuilder().box([0.02, 0.26, ww], M.glass, {});
    panels.push({ key: 'rear_window', name: 'Back Window', family: 'rear_window', category: 'glass', origin: [wx, wy, 0], model: cab, fasteners: [], removeDir: [-1, 0.3, 0], parent: null, blockedBy: [], price: 120 });
  }
  return { shell, panels };
}

// ---------------------------------------------------------------------------
// Specs for the three placeholder cars
// ---------------------------------------------------------------------------

export const SEDAN: BodySpec = {
  style: 'sedan',
  width: 1.76,
  bottomY: 0.22,
  axles: [1.33, -1.33],
  track: 1.5,
  wheelR: 0.31,
  archR: 0.37,
  outline: [
    [2.2, 0.22], [2.26, 0.34], [2.26, 0.55], [2.22, 0.76], [1.08, 0.92], [0.4, 1.4], [-0.8, 1.42],
    [-1.55, 1.03], [-2.3, 0.99], [-2.37, 0.78], [-2.38, 0.4], [-2.33, 0.22],
  ],
  segs: ['plastic', 'plastic', 'paint', 'hood', 'windshield', 'roof', 'rear_window', 'trunk', 'paint', 'plastic', 'plastic'],
  bands: [
    { key: 'fender', name: 'Front {S} Fender', family: 'fender', x0: 1.02, x1: 2.4 },
    { key: 'door_f', name: 'Front {S} Door', family: 'door', x0: -0.17, x1: 1.02 },
    { key: 'door_b', name: 'Back {S} Door', family: 'door', x0: -1.12, x1: -0.17 },
    { key: 'quarter', name: 'Quarter', family: 'quarter', x0: -2.5, x1: -1.12 },
  ],
  windows: {
    door_f: [[1.0, 0.96], [0.48, 1.33], [-0.12, 1.36], [-0.12, 0.96]],
    door_b: [[-0.22, 0.96], [-0.22, 1.36], [-0.76, 1.37], [-1.08, 1.2], [-1.08, 0.96]],
  },
  firewallX: 1.08,
  bayFrontX: 2.13,
  seats: [{ x: 0.15, y: 0.5 }, { x: -0.85, y: 0.5 }],
  dash: { x: 0.92, y: 0.86 },
  front: { lightY: 0.64, lightH: 0.1, grilleH: 0.1, bumperY: 0.36, bumperMat: M.plastic },
  rear: { lightY: 0.86, lightH: 0.12, bumperY: 0.36, bumperMat: M.plastic },
  engine: [1.6, 0.28, 0],
  engineLen: 0.52,
  round: [0, 0.06, 0.1, 0.04, 0.06, 0.25, 0.25, 0.1, 0.06, 0.1, 0.1, 0],
};

export const COUPE: BodySpec = {
  style: 'coupe',
  width: 1.78,
  bottomY: 0.2,
  axles: [1.275, -1.275],
  track: 1.52,
  wheelR: 0.31,
  archR: 0.37,
  outline: [
    [2.2, 0.2], [2.29, 0.32], [2.29, 0.52], [2.25, 0.74], [1.0, 0.9], [0.15, 1.28], [-0.62, 1.3],
    [-1.8, 1.0], [-2.22, 0.98], [-2.29, 0.76], [-2.3, 0.38], [-2.25, 0.2],
  ],
  segs: ['plastic', 'plastic', 'paint', 'hood', 'windshield', 'roof', 'rear_window', 'trunk', 'paint', 'plastic', 'plastic'],
  bands: [
    { key: 'fender', name: 'Front {S} Fender', family: 'fender', x0: 0.94, x1: 2.4 },
    { key: 'door_f', name: '{S} Door', family: 'door', x0: -1.2, x1: 0.94 },
    { key: 'quarter', name: 'Quarter', family: 'quarter', x0: -2.5, x1: -1.2 },
  ],
  windows: { door_f: [[0.92, 0.95], [0.25, 1.22], [-0.58, 1.26], [-1.16, 1.07], [-1.16, 0.95]] },
  firewallX: 1.0,
  bayFrontX: 2.15,
  seats: [{ x: 0.0, y: 0.46 }, { x: -0.95, y: 0.46 }],
  dash: { x: 0.84, y: 0.82 },
  front: { lightY: 0.6, lightH: 0.08, grilleH: 0.08, bumperY: 0.33, bumperMat: M.plastic },
  rear: { lightY: 0.82, lightH: 0.1, bumperY: 0.34, bumperMat: M.plastic },
  engine: [1.55, 0.25, 0],
  engineLen: 0.52,
};

export const PICKUP: BodySpec = {
  style: 'pickup',
  width: 1.9,
  bottomY: 0.42,
  axles: [1.6, -1.6],
  track: 1.62,
  wheelR: 0.38,
  archR: 0.45,
  outline: [
    [2.5, 0.42], [2.58, 0.55], [2.58, 0.92], [2.52, 1.14], [1.35, 1.24], [0.8, 1.78], [-0.3, 1.8],
    [-0.36, 1.22], [-2.66, 1.22], [-2.68, 0.52], [-2.62, 0.42],
  ],
  segs: ['chrome', 'paint', 'paint', 'hood', 'windshield', 'roof', 'paint', 'none', 'tailgate', 'chrome'],
  bands: [
    { key: 'fender', name: 'Front {S} Fender', family: 'fender', x0: 1.3, x1: 2.7 },
    { key: 'door_f', name: '{S} Door', family: 'door', x0: -0.3, x1: 1.3 },
    { key: 'bedside', name: 'Bed side', family: 'quarter', x0: -2.8, x1: -0.3 },
  ],
  windows: { door_f: [[1.28, 1.3], [0.88, 1.68], [-0.24, 1.74], [-0.24, 1.3]] },
  firewallX: 1.35,
  bayFrontX: 2.5,
  seats: [{ x: 0.2, y: 0.78 }],
  dash: { x: 1.2, y: 1.18 },
  bed: { floorY: 0.92, frontX: -0.38, rearX: -2.64, rearWindow: [-0.335, 1.55, 1.1] },
  front: { lightY: 0.98, lightH: 0.14, grilleH: 0.26, bumperY: 0.52, bumperMat: M.chrome },
  rear: { lightY: 1.0, lightH: 0.3, bumperY: 0.5, bumperMat: M.chrome, lightsVertical: true },
  engine: [1.9, 0.52, 0],
  engineLen: 0.64,
};
