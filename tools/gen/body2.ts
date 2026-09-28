// Second-generation car bodies (the sedan first). The panels of body.ts, with the same keys, places, bolts and ways
// off, made as curved sheet metal: the sides swell a little and lean in above the waist, every edge the car shows is
// rolled round, the bonnet, roof and boot lid are crowned, and the panels stand a few millimetres apart. With them come
// the details a car has: lamps with reflectors behind clear lenses, a grille, bumpers wrapping round the corners with
// a rubber strip, mirrors, door handles, rubber seals round the glass, wipers and rub strips along the sides.

import * as THREE from 'three';
import { ModelBuilder, M, type MatDef } from './geo.ts';
import type { FastenerDef, Vec3 } from './types.ts';
import { clipX, hingeOf, hoodFrontOf, outlineY, silhouette, type BodySpec, type PanelSpec } from './body.ts';
import { cross3, fillet, filletEach, gridSurface, meshRegion, norm3, resample, sheet, steps, sub3, surfaceFrames, sweep, type V2, type V3 } from './sheet.ts';

const DEG = Math.PI / 180;
const clamp01 = (t: number) => Math.max(0, Math.min(1, t));
const smooth = (a: number, b: number, x: number) => {
  const t = clamp01((x - a) / (b - a));
  return t * t * (3 - 2 * t);
};

/** A mesh size graded with the distance `d` from a detail: `fine` within `zone` of it, then growing by `rate` a metre
 * up to `coarse` (a sudden step from fine to coarse makes the mesh fine far beyond the detail). */
function grade(d: number, fine: number, zone: number, coarse: number, rate = 0.9): number {
  return d < zone ? fine : Math.min(coarse, fine + (d - zone) * rate);
}

/** How far a rolled edge has come in at depth `d` inside the edge: a roll of radius `r`, turning up to `max`, then
 * going on straight at that angle to the edge (levelling off there would fold the sheet back into a crease) and a
 * little past it (so the normals worked out at the edge itself see the same slope on both sides). */
function rollIn(d: number, r: number, max: number): number {
  if (d >= r) return 0;
  const d0 = r * (1 - Math.sin(max));
  if (d < d0) return r * (1 - Math.cos(max)) + (d0 - Math.max(d, -r)) * Math.tan(max);
  const s = 1 - d / r;
  return r * (1 - Math.sqrt(1 - s * s));
}

/**
 * x at height y along a smooth curve through a profile's (x, y) points: a cubic Hermite through them, so its tangent
 * turns smoothly (a polyline's kinks show in the paint's reflections), its slopes chosen so that between two points it
 * stays between their x (a face's straight stretch stays straight, nothing bulges past the silhouette).
 */
function smoothAlong(pts: [number, number][], y: number): number {
  const p = pts.slice().sort((a, b) => a[1] - b[1]);
  const n = p.length;
  if (y <= p[0][1]) return p[0][0];
  if (y >= p[n - 1][1]) return p[n - 1][0];
  const dy = (k: number) => p[k + 1][1] - p[k][1];
  const d = (k: number) => (p[k + 1][0] - p[k][0]) / dy(k);
  const slope = (i: number) => {
    if (i === 0) return d(0);
    if (i === n - 1) return d(n - 2);
    const a = d(i - 1);
    const b = d(i);
    if (a * b <= 0) return 0;
    const w1 = 2 * dy(i) + dy(i - 1);
    const w2 = dy(i) + 2 * dy(i - 1);
    return (w1 + w2) / (w1 / a + w2 / b);
  };
  let i = 0;
  while (i < n - 2 && y > p[i + 1][1]) i++;
  const [x0, y0] = p[i];
  const [x1, y1] = p[i + 1];
  const h = y1 - y0;
  const t = (y - y0) / h;
  const m0 = slope(i) * h;
  const m1 = slope(i + 1) * h;
  const t2 = t * t;
  const t3 = t2 * t;
  return (2 * t3 - 3 * t2 + 1) * x0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * x1 + (t3 - t2) * m1;
}

/** A rounded rectangle as a closed outline: x from x0 to x1, y from y0 to y1, corners of radius r. */
function rrect(x0: number, x1: number, y0: number, y1: number, r: number, segs = 4): V2[] {
  return fillet([[x0, y0], [x1, y0], [x1, y1], [x0, y1]], r, segs);
}

/** The shape of a car's body: its cross-section at every height and the edges rolled round its silhouette. */
export class BodyShape {
  readonly W: number;
  readonly half: number;
  /** The waist: the side windows' sills. */
  readonly beltY: number;
  /** Where the side is widest. */
  readonly shoulderY: number;
  /** The greenhouse leans in above the waist by this much a metre. */
  readonly tumble = Math.tan(11 * DEG);
  /** The edge rolled round the silhouette's top (shoulders, pillars, roof rails). */
  readonly rTop = 0.055;
  readonly aTop = 65 * DEG;
  /** The corners rolled round at the front and the back, seen from above. */
  readonly rCorner = 0.2;
  readonly aCorner = 72 * DEG;
  /** Seen from above the sides draw in towards the ends, this much at the very ends. */
  readonly taperEnds = 0.03;
  /** The silhouette with its corners rounded (the spec's `round`), and the closed side view made from it. */
  readonly outline: [number, number][];
  readonly silhouette: V2[];
  readonly xMid: number;
  readonly halfLen: number;
  /** How far the sill is tucked in under the widest line. */
  readonly tuck = 0.035;
  /** The silhouette's front and back faces, bottom to top and top to bottom. */
  readonly front: [number, number][];
  readonly rear: [number, number][];

  readonly spec: BodySpec;

  constructor(spec: BodySpec) {
    this.spec = spec;
    this.W = spec.width;
    this.half = spec.width / 2;
    const xs = spec.outline.map((p) => p[0]);
    this.xMid = (Math.max(...xs) + Math.min(...xs)) / 2;
    this.halfLen = (Math.max(...xs) - Math.min(...xs)) / 2;
    const door = spec.bands.find((b) => b.family === 'door');
    const win = door ? spec.windows[door.key] : undefined;
    this.beltY = win ? Math.min(...win.map((p) => p[1])) : spec.bottomY + 0.6;
    this.shoulderY = this.beltY - 0.16;
    this.front = spec.outline.slice(0, spec.segs.indexOf('hood') + 1);
    const tops = ['hood', 'windshield', 'roof', 'rear_window', 'trunk', 'tailgate'];
    let last = 0;
    spec.segs.forEach((r, i) => {
      if (tops.includes(r)) last = i;
    });
    this.rear = spec.outline.slice(last + 1);
    // The side view: over the top the outline with its corners rounded (the spec's `round`), from the bonnet's front
    // corner to the boot lid's back one; down the front and the back the faces' own smooth profiles, so the sides end
    // exactly where the faces are.
    const o = spec.outline;
    const hood = spec.segs.indexOf('hood');
    const tail = last + 1;
    const top = filletEach(o.slice(hood - 1, tail + 2), (spec.round ?? []).slice(hood - 1, tail + 2), 10).slice(1, -1) as [number, number][];
    const down = (x: (y: number) => number, y0: number, y1: number): [number, number][] => {
      const n = Math.max(2, Math.ceil(Math.abs(y1 - y0) / 0.01));
      return Array.from({ length: n }, (_, i) => {
        const y = y0 + ((y1 - y0) * i) / n;
        return [x(y), y] as [number, number];
      });
    };
    const frontUp = down((y) => this.xFront(y), o[0][1], top[0][1]);
    const rearDown = down((y) => this.xRear(y), o[o.length - 1][1], top[top.length - 1][1]).slice(1).reverse();
    this.outline = [...frontUp, ...top, ...rearDown, o[o.length - 1]];
    // The arches finely divided: their lips are worked out from the true circles, and a coarse polygon's chords would
    // cut inside them.
    this.silhouette = silhouette({ ...spec, outline: this.outline }, 72);
  }

  /** The top of the (rounded) silhouette at x. */
  yTop(x: number): number {
    return outlineY({ ...this.spec, outline: this.outline }, x);
  }

  /** The top the side's upper edge rolls round: the silhouette's top, and over the front and back faces its height at
   * their upper corners (a face is no top: rolling round it would pinch the side in where the face leans back). */
  yTopRoll(x: number): number {
    const hi = this.front[this.front.length - 1][0];
    const lo = this.rear[0][0];
    return this.yTop(Math.max(lo, Math.min(hi, x)));
  }

  /** How far the side draws in at x, seen from above: nothing in the middle, `taperEnds` at the ends. */
  taper(x: number): number {
    const t = (x - this.xMid) / this.halfLen;
    return this.taperEnds * t * t * t * t;
  }

  /** The front face's x at height y, on a smooth curve through the silhouette's front points. */
  xFront(y: number): number {
    return smoothAlong(this.front, y);
  }

  xRear(y: number): number {
    return smoothAlong(this.rear, y);
  }

  /** The cross-section's half-width at height y, before the edges roll in: tucked in at the sill, widest at the
   * shoulder; where there is a cabin above (`cabin` 1), a small ledge at the waist and the greenhouse leaning in. */
  zBase(y: number, cabin = 1): number {
    const s = this.spec;
    if (y <= this.shoulderY) return this.half - this.tuck * Math.pow(clamp01((this.shoulderY - y) / (this.shoulderY - s.bottomY)), 2.2);
    if (y <= this.beltY) return this.half - 0.008 * Math.pow((y - this.shoulderY) / (this.beltY - this.shoulderY), 2);
    return this.half - 0.008 - cabin * (0.018 * smooth(this.beltY, this.beltY + 0.012, y) + (y - this.beltY) * this.tumble);
  }

  /** How much of a cabin there is above x: 1 under the roof and the pillars, 0 where the top is the bonnet or the
   * boot lid (just above the waist), easing between. */
  cabin(x: number): number {
    return smooth(0.06, 0.14, this.yTop(x) - this.beltY);
  }

  /** The body side's half-width at (x, y): the cross-section, the edges rolled round the silhouette, the arch lips. */
  zSide(x: number, y: number): number {
    const s = this.spec;
    let z = this.zBase(y, this.cabin(x)) - this.taper(x);
    z -= rollIn(this.yTopRoll(x) - y, this.rTop, this.aTop);
    z -= rollIn(y - s.bottomY, 0.025, 60 * DEG);
    z -= rollIn(this.xFront(y) - x, this.rCorner, this.aCorner);
    z -= rollIn(x - this.xRear(y), this.rCorner, this.aCorner);
    return z + this.lip(x, y);
  }

  /** Round each wheel arch the sheet swells out a centimetre, then turns in at its edge (and on a little past it, so
   * the normals worked out at the edge see the same slope on both sides). */
  lip(x: number, y: number): number {
    const s = this.spec;
    for (const ax of s.axles) {
      const r = Math.hypot(x - ax, y - s.wheelR) - s.archR;
      if (r < -0.018 || r > 0.07) continue;
      return 0.01 * Math.pow(1 - r / 0.07, 2) * smooth(0, 0.015, r) - rollIn(r, 0.018, 70 * DEG);
    }
    return 0;
  }

  /** The side's normal at (x, y) (right side; mirror z for the left). */
  sideNormal(x: number, y: number): V3 {
    const h = 1e-4;
    const dzdx = (this.zSide(x + h, y) - this.zSide(x - h, y)) / (2 * h);
    const dzdy = (this.zSide(x, y + h) - this.zSide(x, y - h)) / (2 * h);
    return norm3([-dzdx, -dzdy, 1]);
  }

  /** How fine a side panel's mesh must be at (u, v): fine at rolled edges, the waist and the arches. */
  sideDetail(u: number, v: number): number {
    const s = this.spec;
    let m = grade(this.yTopRoll(u) - v, 0.012, this.rTop + 0.004, 0.07);
    m = Math.min(m, grade(v - s.bottomY, 0.016, 0.025, 0.07));
    if (this.cabin(u) > 0) m = Math.min(m, grade(Math.abs(v - this.beltY - 0.006), 0.01, 0.012, 0.07));
    const toEnd = Math.min(this.xFront(v) - u, u - this.xRear(v));
    m = Math.min(m, grade(toEnd, 0.02, this.rCorner, 0.07), grade(toEnd, 0.01, 0.012, 0.07));
    for (const ax of s.axles) {
      const r = Math.abs(Math.hypot(u - ax, v - s.wheelR) - s.archR);
      m = Math.min(m, grade(r, 0.015, 0.06, 0.07), grade(r, 0.004, 0.02, 0.07));
    }
    return m;
  }

  /** A side panel: the outline (with holes) in the side view, as sheet metal on the body's side. */
  sideSheet(outline: V2[], holes: V2[][], side: number, origin: V3) {
    const region = meshRegion(outline, holes, (u, v) => this.sideDetail(u, v));
    const map = (u: number, v: number): V3 => [u - origin[0], v - origin[1], side * this.zSide(u, v) - origin[2]];
    return sheet(region, map, { thickness: 0.01, hem: true, outward: () => [0, 0, side] });
  }

  /** A point on the side, relative to `origin`, and the side's outward normal there. */
  onSide(x: number, y: number, side: number, origin: V3, out = 0): { p: V3; n: V3 } {
    const n0 = this.sideNormal(x, y);
    const n: V3 = [n0[0], n0[1], n0[2] * side];
    const z = side * this.zSide(x, y);
    return { p: [x + n[0] * out - origin[0], y + n[1] * out - origin[1], z + n[2] * out - origin[2]], n };
  }
}

/** A strip swept along the side at height y, from x0 to x1, broken where the wheel arches are. */
function sideStrip(b: ModelBuilder, shape: BodyShape, y: number, x0: number, x1: number, side: number, origin: V3, profile: V2[], mat = M.trim): void {
  const s = shape.spec;
  // Not round the corners at the ends: the strip stops where they begin to turn.
  x1 = Math.min(x1, shape.xFront(y) - 0.08);
  x0 = Math.max(x0, shape.xRear(y) + 0.08);
  if (x1 - x0 < 0.05) return;
  const runs: number[][] = [];
  let run: number[] = [];
  const n = Math.max(2, Math.ceil((x1 - x0) / 0.02));
  for (let i = 0; i <= n; i++) {
    const x = x0 + ((x1 - x0) * i) / n;
    const inArch = s.axles.some((ax) => Math.hypot(x - ax, y - s.wheelR) < s.archR + 0.03);
    if (inArch) {
      if (run.length > 1) runs.push(run);
      run = [];
    } else run.push(x);
  }
  if (run.length > 1) runs.push(run);
  for (const xs of runs) {
    const pts = xs.map((x) => shape.onSide(x, y, side, origin));
    const path = pts.map((q) => q.p);
    b.add(sweep(path, surfaceFrames(path, (i) => pts[i].n, false), profile), mat);
  }
}

/** A seal swept round a closed outline on the side (a window's opening). */
function sideSeal(b: ModelBuilder, shape: BodyShape, loop: V2[], side: number, origin: V3, profile: V2[]): void {
  const pts = resample(loop, 0.02).map(([x, y]) => shape.onSide(x, y, side, origin));
  const path = pts.map((q) => q.p);
  b.add(sweep(path, surfaceFrames(path, (i) => pts[i].n, true), profile, { closedPath: true }), M.seal);
}

// ---------------------------------------------------------------------------------------------------------------

/** The static shell and the removable panels of a second-generation body (same keys and places as body.ts). */
export function buildBody2(spec: BodySpec): { shell: ModelBuilder; panels: PanelSpec[]; shape: BodyShape } {
  const shape = new BodyShape(spec);
  const shell = new ModelBuilder();
  const panels: PanelSpec[] = [];
  const W = spec.width;
  const o = spec.outline;
  const sil = shape.silhouette;
  const sides: [number, string, string][] = [[-1, 'l', 'Left'], [1, 'r', 'Right']];
  const stripY = spec.bottomY + 0.3;
  const stripProfile = rrect(-0.016, 0.016, -0.002, 0.008, 0.004);
  const sealProfile = rrect(-0.009, 0.009, -0.002, 0.003, 0.002, 1);
  const beltProfile = rrect(-0.005, 0.005, -0.001, 0.004, 0.002, 1);

  // ---- side bands ---------------------------------------------------------------------------------------------
  spec.bands.forEach((band, bi) => {
    const gap = 0.003;
    const x0 = band.x0 + (bi === spec.bands.length - 1 ? 0 : gap);
    const x1 = band.x1 - (bi === 0 ? 0 : gap);
    const poly = clipX(sil, x0, x1);
    const win = spec.windows[band.key];
    const opening = win ? fillet(win, 0.035, 5) : null;
    for (const [side, s, S] of sides) {
      if (band.family === 'quarter') {
        const zero: V3 = [0, 0, 0];
        shell.sheet(shape.sideSheet(poly, [], side, zero), M.paint);
        sideStrip(shell, shape, stripY, x0 + 0.01, x1 - 0.01, side, zero, stripProfile);
        // The chrome belt carried back along the C-pillar's foot, the red side marker near the back, the mud flap.
        sideStrip(shell, shape, shape.beltY - 0.016, x1 - 0.3, x1 - 0.01, side, zero, beltProfile, M.chrome);
        shell.rbox([0.07, 0.022, 0.006], 0.0029, M.reflRed, { p: shape.onSide(shape.xRear(0.62) + 0.3, 0.62, side, zero, 0.0005).p });
        mudFlap(shell, shape, spec.axles[1], side, zero);
        if (side > 0) {
          // The fuel filler's door on the right rear quarter.
          const fx = Math.max(x0 + 0.25, spec.axles[1] - 0.12);
          const fy = shape.beltY - 0.12;
          const fd = shape.onSide(fx, fy, side, zero, 0.0005);
          shell.torus(0.055, 0.0018, M.seal, { p: fd.p }, { axis: 'z', segs: 40 });
        }
        continue;
      }
      const key = `${band.key}_${s}`;
      const origin: Vec3 = [(x0 + x1) / 2, spec.bottomY, side * (W / 2)];
      const b = new ModelBuilder().sheet(shape.sideSheet(poly, opening ? [opening] : [], side, origin), M.paint);
      sideStrip(b, shape, stripY, x0 + 0.012, x1 - 0.012, side, origin, stripProfile);
      const fasteners: FastenerDef[] = [];
      if (band.family === 'door') {
        const beltY = win ? win[0][1] : spec.bottomY + 0.5;
        // The handle: a chrome grip on a dark plate.
        b.rbox([0.13, 0.04, 0.012], 0.006, M.trim, { p: shape.onSide(x0 + 0.18, beltY - 0.08, side, origin, -0.004).p });
        b.rbox([0.11, 0.022, 0.018], 0.008, M.chrome, { p: shape.onSide(x0 + 0.18, beltY - 0.08, side, origin, 0.006).p });
        if (band.key === 'door_f') {
          mirror(b, shape, side, origin);
          b.cyl(0.009, 0.008, M.chrome, { p: shape.onSide(x0 + 0.28, beltY - 0.08, side, origin, 0.003).p }, { axis: 'z', segs: 16 });
        }
        if (win) {
          // The chrome belt along the window's foot.
          const wx = win.map((p) => p[0]);
          sideStrip(b, shape, shape.beltY - 0.016, Math.max(x0 + 0.01, Math.min(...wx) - 0.01), Math.min(x1 - 0.01, Math.max(...wx) + 0.01), side, origin, beltProfile, M.chrome);
        }
        doorCard(b, shape, x0, x1, side, origin);
        for (const y of [beltY - 0.12, spec.bottomY + 0.18]) {
          const q = shape.onSide(x1 - 0.035, y, side, origin, 0.002);
          fasteners.push({ kind: 'bolt_s', pos: q.p, dir: [0, 0, side] });
        }
        if (opening) sideSeal(b, shape, opening, side, origin, sealProfile);
        panels.push({ key, name: `${band.name.replace('{S}', S)}`, family: 'door', category: 'body', origin, model: b, fasteners, removeDir: [0, 0, side], parent: null, blockedBy: [], price: 260 });
        if (win) {
          // The glass: a flat pane following the greenhouse's lean, a little bigger than the opening, behind it.
          const pane = fillet(win.map(([x, y]) => [x, y] as V2), 0.035, 5);
          const grown = offsetOut(pane, 0.008);
          const zAt = (x: number, y: number) => shape.zBase(shape.beltY + 0.013) - shape.taper(x) - (y - (shape.beltY + 0.013)) * shape.tumble - 0.013;
          const region = meshRegion(grown, [], () => 0.05);
          const map = (u: number, v: number): V3 => [u - origin[0], v - origin[1], side * zAt(u, v) - origin[2]];
          const g = new ModelBuilder().sheet(sheet(region, map, { thickness: 0.004, outward: () => [0, 0, side] }), M.glass);
          const wkey = `window_${band.key.replace('door_', '')}_${s}`;
          panels.push({ key: wkey, name: `${band.name.replace('{S}', S).replace('Door', 'Window')}`, family: 'window', category: 'glass', origin, model: g, fasteners: [], removeDir: [0, 1, 0], parent: key, blockedBy: [], price: 70 });
        }
      } else {
        // The fender's top flange under the bonnet's edge, with its bolts: as far forward as the bonnet goes.
        const xEnd = Math.min(x1 - 0.1, hoodFrontOf(spec)[0] - 0.03);
        const flange: V3[] = [];
        for (let x = x0 + 0.03; x <= xEnd; x += 0.02) {
          const y = shape.yTop(x) - 0.014;
          const z = side * (shape.zSide(x, shape.yTop(x) - 1e-4) - 0.03);
          flange.push([x - origin[0], y - origin[1], z - origin[2]]);
        }
        if (flange.length > 1) {
          const frames = flange.map(() => ({ side: [0, 0, side] as V3, up: [0, 1, 0] as V3 }));
          b.add(sweep(flange, frames, rrect(-0.018, 0.018, -0.002, 0.002, 0.0015, 2)), M.paint);
        }
        for (const x of [xEnd - 0.02, (x0 + xEnd) / 2, x0 + 0.12]) {
          const z = side * (shape.zSide(x, shape.yTop(x) - 1e-4) - 0.03);
          fasteners.push({ kind: 'bolt_s', pos: [x - origin[0], shape.yTop(x) - 0.012 - origin[1], z - origin[2]], dir: [0, 1, 0] });
        }
        // The amber side marker near the front, a badge behind the arch, the mud flap, the aerial on the right.
        b.rbox([0.07, 0.022, 0.006], 0.0029, M.reflAmber, { p: shape.onSide(shape.xFront(0.6) - 0.3, 0.6, side, origin, 0.0005).p });
        const archBack = spec.axles[0] - Math.sqrt(Math.max(0, spec.archR ** 2 - (0.64 - spec.wheelR) ** 2));
        b.rbox([0.075, 0.018, 0.006], 0.003, M.chrome, { p: shape.onSide((x0 + archBack) / 2, 0.64, side, origin, 0.002).p });
        mudFlap(b, shape, spec.axles[0], side, origin);
        if (side > 0) aerial(b, shape, x0 + 0.1, side, origin);
        panels.push({ key, name: `${S === 'Left' ? 'Front Left' : 'Front Right'} Fender`, family: 'fender', category: 'body', origin, model: b, fasteners, removeDir: [0.2, 0.3, side], parent: null, blockedBy: ['hood', 'bumper_f'], price: 180 });
      }
    }
  });

  // ---- the panels over the top: windshield, roof, rear window, boot lid ----------------------------------------------
  for (let i = 0; i < o.length - 1; i++) {
    const role = spec.segs[i];
    if (!['windshield', 'roof', 'rear_window', 'trunk'].includes(role)) continue;
    const a = o[i];
    const c = o[i + 1];
    const mid: Vec3 = [(a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0];
    const names: Record<string, [string, string, number]> = {
      windshield: ['Front Windshield', 'glass', 220],
      rear_window: ['Back Windshield', 'glass', 180],
      roof: ['Roof', 'body', 420],
      trunk: ['Trunk Lid', 'body', 240],
    };
    const [name, category, price] = names[role];
    const fasteners: FastenerDef[] = [];
    let blockedBy: string[] = [];
    const b = new ModelBuilder();
    if (role === 'windshield' || role === 'rear_window') glassPane(b, shell, shape, a, c, mid, role === 'windshield');
    else {
      const top = topPanel(shape, Math.min(a[0], c[0]), Math.max(a[0], c[0]), role === 'roof' ? 0.028 : 0.016, mid);
      b.sheet(top.geo, M.paint);
      if (role === 'roof') for (const sd of [-1, 1]) dripRail(b, top, Math.min(a[0], c[0]), Math.max(a[0], c[0]), sd, mid);
      if (role === 'trunk') {
        // The maker's badge on the lid, to the right.
        const bx = Math.min(a[0], c[0]) + 0.08;
        b.rbox([0.03, 0.006, 0.15], 0.003, M.chrome, { p: [bx - mid[0], top.y(bx, 0.45) + 0.002 - mid[1], 0.45] });
      }
      if (role === 'trunk') {
        const [hx] = a;
        for (const z of [-0.45, 0.45]) fasteners.push({ kind: 'bolt_s', pos: [hx - 0.04 - mid[0], top.y(hx - 0.04, z) + 0.004 - mid[1], z], dir: [0, 1, 0] });
        // The lock, in the middle of the lid's back edge.
        const lx = Math.min(a[0], c[0]) + 0.03;
        b.cyl(0.014, 0.012, M.chrome, { p: [lx - mid[0], top.y(lx, 0) - 0.03 - mid[1], 0] }, { axis: 'x', segs: 20 });
      } else blockedBy = ['windshield', 'rear_window'];
    }
    panels.push({ key: role, name, family: role, category, origin: mid, model: b, fasteners, removeDir: [0, 1, 0], parent: null, blockedBy, price });
  }

  // ---- bumpers ------------------------------------------------------------------------------------------------------
  const fr = spec.front;
  const rr = spec.rear;
  const fxFace = Math.max(...shape.front.map((p) => p[0]));
  const rxFace = Math.min(...shape.rear.map((p) => p[0]));
  for (const [key, name, x, y, dirX] of [
    ['bumper_f', 'Front Bumper', fxFace, fr.bumperY, 1],
    ['bumper_r', 'Back Bumper', rxFace + 0.02, rr.bumperY, -1],
  ] as const) {
    const origin: Vec3 = [x, y, 0];
    const b = bumper(shape, dirX, dirX > 0 ? fxFace : rxFace, origin);
    const fasteners: FastenerDef[] = [-1, 1].map((s) => ({ kind: 'bolt_s', pos: [0, 0.092, s * (W / 2 - 0.08)] as Vec3, dir: [0, 1, 0] as Vec3 }));
    panels.push({ key, name, family: 'bumper', category: 'body', origin, model: b, fasteners, removeDir: [dirX, 0, 0], parent: null, blockedBy: [], price: 150 });
  }

  // ---- the static shell --------------------------------------------------------------------------------------------
  faces(shell, shape);
  structure(shell, shape);
  interior(shell, shape);
  return { shell, panels, shape };
}

/** A closed outline grown outwards by `d` (a pane bigger than its opening). */
function offsetOut(p: V2[], d: number): V2[] {
  const n = p.length;
  let area = 0;
  for (let i = 0; i < n; i++) area += p[i][0] * p[(i + 1) % n][1] - p[(i + 1) % n][0] * p[i][1];
  const ccw = area > 0;
  return p.map((cur, i) => {
    const prev = p[(i + n - 1) % n];
    const next = p[(i + 1) % n];
    const t: V2 = [next[0] - prev[0], next[1] - prev[1]];
    const l = Math.hypot(...t) || 1;
    const nrm: V2 = ccw ? [t[1] / l, -t[0] / l] : [-t[1] / l, t[0] / l];
    return [cur[0] + nrm[0] * d, cur[1] + nrm[1] * d];
  });
}

/**
 * A panel over the top (roof, boot lid) between x0 and x1: as wide as the sides' rolled top edges allow, crowned by
 * `crown` in the middle, its front and back edges rolled down.
 */
function topPanel(shape: BodyShape, x0: number, x1: number, crown: number, origin: V3) {
  const gap = 0.005;
  const a = x0 + gap;
  const c = x1 - gap;
  const edge = (x: number) => shape.zSide(x, shape.yTop(x) - 1e-4) - gap;
  const y = (x: number, z: number) => {
    const e = edge(x);
    return shape.yTop(x) + crown * Math.max(0, 1 - (z / e) ** 2)
      - rollIn(x - a, 0.03, 60 * DEG) - rollIn(c - x, 0.03, 60 * DEG) - rollIn(e - Math.abs(z), 0.012, 55 * DEG);
  };
  const n = Math.max(8, Math.ceil((c - a) / 0.05));
  const xs = Array.from({ length: n + 1 }, (_, i) => a + ((c - a) * i) / n);
  const outline: V2[] = [...xs.map((x) => [x, -edge(x)] as V2), ...xs.slice().reverse().map((x) => [x, edge(x)] as V2)];
  const region = meshRegion(outline, [], (u, v) => {
    return Math.min(grade(Math.min(u - a, c - u), 0.01, 0.03, 0.08), grade(edge(u) - Math.abs(v), 0.01, 0.013, 0.08));
  });
  const map = (u: number, v: number): V3 => [u - origin[0], y(u, v) - origin[1], v - origin[2]];
  return { geo: sheet(region, map, { thickness: 0.01, hem: true, outward: () => [0, 1, 0] }), y, edge };
}

/**
 * A windshield or rear window from outline point `a` to `c`: glass following the rounded silhouette, bulging out a
 * little in the middle, a black ceramic border printed round it, in a rubber seal. It stands in from the pillars'
 * edges (more lower down, where the pillars broaden towards their feet), from the roof's edge and, at the back, from
 * the deck under it, as a car's glass does: body-coloured sheet in the shell frames it.
 */
function glassPane(b: ModelBuilder, shell: ModelBuilder, shape: BodyShape, a: [number, number], c: [number, number], origin: V3, front: boolean): void {
  // Along the silhouette from a to c, measured in metres along it.
  const N = 80;
  const xs: number[] = [];
  const us: number[] = [];
  let acc = 0;
  for (let i = 0; i <= N; i++) {
    const x = a[0] + ((c[0] - a[0]) * i) / N;
    if (i > 0) acc += Math.hypot(x - xs[i - 1], shape.yTop(x) - shape.yTop(xs[i - 1]));
    xs.push(x);
    us.push(acc);
  }
  const len = acc;
  const xAt = (u: number) => {
    let i = 0;
    while (i < N - 1 && us[i + 1] < u) i++;
    const t = Math.max(0, Math.min(1, (u - us[i]) / (us[i + 1] - us[i] || 1)));
    return xs[i] + (xs[i + 1] - xs[i]) * t;
  };
  // The silhouette's outward normal at x (up, and forwards or backwards).
  const normal = (x: number): [number, number] => {
    const h = 1e-3;
    const slope = (shape.yTop(x + h) - shape.yTop(x - h)) / (2 * h);
    const l = Math.hypot(slope, 1);
    return [-slope / l, 1 / l];
  };
  // How far across the glass reaches: `inset` short of the pillars' edges at its top, `flare` more a metre lower down.
  const [inset, flare] = front ? [0.06, 0.14] : [0.07, 0.1];
  const edgeZ = (x: number) => shape.zSide(x, shape.yTop(x) - 1e-4);
  const xHigh = a[1] > c[1] ? a[0] : c[0];
  const zHigh = edgeZ(xHigh) - inset;
  const half = (x: number) => Math.min(edgeZ(x) - inset, zHigh + (shape.yTop(xHigh) - shape.yTop(x)) * flare);
  // And along it: the windshield from just above the cowl to 4 cm under the roof's edge, the rear window from 5 cm
  // under the roof's edge to 10 cm above the boot lid's. Its ceramic border is wider along the lower edge.
  const [u0, u1] = front ? [0.012, len - 0.04] : [0.05, len - 0.1];
  const [f0, f1] = front ? [0.05, 0.035] : [0.035, 0.05];
  const wrap = 0.018;
  const H = 0.8;
  const map = (u: number, v: number): V3 => {
    const x = xAt(u);
    const t = v / H;
    const n = normal(x);
    const back = 0.004 - wrap * (1 - t * t);
    return [x - n[0] * back - origin[0], shape.yTop(x) - n[1] * back - origin[1], t * half(x) - origin[2]];
  };
  const outward = (u: number): V3 => {
    const n = normal(xAt(u));
    return [n[0], n[1], 0];
  };
  const outline = rrect(u0, u1, -H, H, 0.06, 6);
  b.sheet(sheet(meshRegion(outline, [], () => 0.05), map, { thickness: 0.005, outward }), M.glass);
  const lift = (u: number, v: number, d: number): V3 => {
    const p = map(u, v);
    const o = outward(u);
    return [p[0] + o[0] * d, p[1] + o[1] * d, p[2]];
  };
  // The border, printed on the glass's inside (seen through it).
  const vOf = (m: number) => (m * H) / half(xAt((u0 + u1) / 2));
  const fs = vOf(0.03);
  const inner = rrect(u0 + f0, u1 - f1, -H + fs, H - fs, 0.04, 6);
  b.sheet(sheet(meshRegion(outline, [inner], () => 0.03), (u, v) => lift(u, v, -0.001), { thickness: 0, outward }), M.frit);
  // The windshield's tinted band across its top; the rear window's heating wires, inside, and the strips feeding them.
  if (front) {
    const band = meshRegion(rrect(u1 - 0.17, u1 - 0.006, -H + 0.02, H - 0.02, 0.05, 4), [], () => 0.05);
    b.sheet(sheet(band, (u, v) => lift(u, v, 0.0008), { thickness: 0, outward }), M.glassTint);
  } else {
    const vw = H - fs - vOf(0.025);
    const wire = rrect(-0.0008, 0.0008, -0.0003, 0.0003, 0.0002, 1);
    let last = u0;
    for (let u = u0 + f0 + 0.035; u < u1 - f1 - 0.03; u += 0.045) {
      const line: V3[] = [];
      for (let v = -vw; v <= vw + 1e-6; v += (2 * vw) / 24) line.push(lift(u, v, -0.0015));
      b.add(sweep(line, surfaceFrames(line, () => outward(u), false), wire), M.trim);
      last = u;
    }
    for (const sd of [-1, 1]) {
      const bus: V3[] = [];
      const ub: number[] = [];
      for (let u = u0 + f0 + 0.025; u <= last + 0.01 + 1e-6; u += 0.02) {
        bus.push(lift(u, sd * (vw + vOf(0.004)), -0.0015));
        ub.push(u);
      }
      b.add(sweep(bus, surfaceFrames(bus, (i) => outward(ub[i]), false), rrect(-0.004, 0.004, -0.0003, 0.0003, 0.0002, 1)), M.trim);
    }
  }
  // The seal round it.
  const loop = resample(outline, 0.025);
  const path = loop.map(([u, v]) => map(u, v));
  const nrm = loop.map(([u, v]) => {
    const h = 1e-4;
    const n = norm3(cross3(sub3(map(u + h, v), map(u - h, v)), sub3(map(u, v + h), map(u, v - h))));
    const o = outward(u);
    return n[0] * o[0] + n[1] * o[1] < 0 ? ([-n[0], -n[1], -n[2]] as V3) : n;
  });
  b.add(sweep(path, surfaceFrames(path, (i) => nrm[i], true), rrect(-0.008, 0.008, -0.002, 0.0035, 0.002, 1), { closedPath: true }), M.seal);

  // The frame round it, in the shell: the pillars' tops, the roof's front (or back) edge and the deck under the rear
  // window, crowned as the glass is where they meet it, rolled down at the pillars' edges and at the ends that meet the
  // roof and the boot lid.
  const e0 = 0.003;
  const zEdge = (u: number) => edgeZ(xAt(u)) - 0.004;
  const uo = steps(e0, len - e0, Math.max(16, Math.ceil(len / 0.02)));
  const outer: V2[] = [...uo.map((u) => [u, -zEdge(u)] as V2), ...uo.slice().reverse().map((u) => [u, zEdge(u)] as V2)];
  const hole = resample(outline, 0.02).map(([u, v]) => [u, (v / H) * half(xAt(u))] as V2);
  const toEnd = (u: number) => (front ? len - e0 - u : Math.min(u - e0, len - e0 - u));
  const frameAt = (u: number, z: number): V3 => {
    const x = xAt(u);
    const n = normal(x);
    const t = Math.min(1, Math.abs(z) / half(x));
    const off = 0.001 + (wrap - 0.005) * (1 - t * t) - rollIn(zEdge(u) - Math.abs(z), 0.012, 55 * DEG) - rollIn(toEnd(u), 0.03, 60 * DEG);
    return [x + n[0] * off, shape.yTop(x) + n[1] * off, z];
  };
  const size = (u: number, z: number) => Math.min(grade(zEdge(u) - Math.abs(z), 0.007, 0.014, 0.03), grade(toEnd(u), 0.008, 0.032, 0.03));
  shell.sheet(sheet(meshRegion(outer, [hole], size), frameAt, { thickness: 0.01, hem: true, outward: (u) => outward(u) }), M.paint);
}

/** A bumper wrapping round the corners: a rounded bar with a rubber strip, and the number plate. */
function bumper(shape: BodyShape, dirX: number, faceX: number, origin: V3): ModelBuilder {
  const b = new ModelBuilder();
  const ends = dirX > 0 ? shape.xFront(origin[1]) - 0.25 : shape.xRear(origin[1]) + 0.25;
  const half = shape.zSide(ends, origin[1]) + 0.015;
  const R = 0.2;
  const zc = half - R;
  const xf = faceX + dirX * 0.05;
  // The bar's path round the front (or back), seen from above, with its outward direction.
  const path: V3[] = [];
  const out: V3[] = [];
  const arc = (sgn: number, from: number, to: number, n: number) => {
    for (let k = 0; k <= n; k++) {
      const ang = from + ((to - from) * k) / n;
      path.push([xf - dirX * R * (1 - Math.cos(ang)) - origin[0], 0, sgn * (zc + R * Math.sin(ang)) - origin[2]]);
      out.push(norm3([dirX * Math.cos(ang), 0, sgn * Math.sin(ang)]));
    }
  };
  const maxA = 75 * DEG;
  arc(-1, maxA, 0, 18);
  for (let k = 1; k < 20; k++) {
    const z = -zc + (2 * zc * k) / 20;
    path.push([xf - origin[0], 0, z - origin[2]]);
    out.push([dirX, 0, 0]);
  }
  arc(1, 0, maxA, 18);
  const frames = out.map((o) => ({ side: o, up: [0, 1, 0] as V3 }));
  b.add(sweep(path, frames, rrect(-0.075, 0, -0.085, 0.085, 0.024, 5)), M.plastic);
  // The rubber strip along its face.
  b.add(sweep(path.map((p) => [p[0], p[1] + 0.018, p[2]] as V3), frames, rrect(-0.004, 0.009, -0.016, 0.016, 0.005, 3)), M.seal);
  // The number plate in its frame (the back one above the bumper, on the tail).
  const plateY = dirX > 0 ? -0.02 : 0.15;
  const plateX = dirX > 0 ? xf - origin[0] + 0.004 : -0.03;
  b.rbox([0.01, 0.125, 0.345], 0.008, M.trim, { p: [plateX, plateY, 0] });
  b.rbox([0.01, 0.105, 0.325], 0.004, M.paper, { p: [plateX + dirX * 0.003, plateY, 0] });
  // Its characters, raised and dark.
  for (let k = 0; k < 6; k++) b.rbox([0.006, 0.055, 0.028], 0.003, M.trim, { p: [plateX + dirX * 0.007, plateY - 0.004, -0.11 + k * 0.044 + (k > 2 ? 0.01 : 0)] });
  if (dirX < 0) for (const sd of [-1, 1]) b.rbox([0.012, 0.03, 0.09], 0.006, M.reflRed, { p: [xf - origin[0] - dirX * 0.002, -0.01, sd * (zc - 0.02)] });
  if (dirX < 0) {
    // The plate's holder down to the bumper.
    for (const z of [-0.12, 0.12]) b.rbox([0.02, 0.1, 0.02], 0.005, M.trim, { p: [-0.035, 0.08, z] });
  }
  return b;
}

/** The black band across the front with the headlamps and the grille: from under the lamps up to under the bonnet's
 * rolled edge, between the fenders' front edges. */
function frontBand(shape: BodyShape): { w: number; y0: number; y1: number } {
  const fr = shape.spec.front;
  const ly = fr.lightY;
  const x = shape.xFront(ly);
  return { w: 2 * (shape.zSide(x, ly) - 0.006), y0: ly - fr.lightH / 2 - 0.018, y1: hoodFrontOf(shape.spec)[1] - 0.02 };
}

/** A mud flap behind the wheel on axle `ax`: black rubber hanging from the arch's back edge. */
function mudFlap(b: ModelBuilder, shape: BodyShape, ax: number, side: number, origin: V3): void {
  const s = shape.spec;
  const R = s.wheelR;
  const x = ax - (Math.sqrt(R * R - (R - 0.17) ** 2) + 0.024);
  b.rbox([0.01, 0.19, 0.2], 0.004, M.seal, { p: [x - origin[0], 0.165 - origin[1], side * (s.track / 2) - origin[2]] });
}

/** The radio's aerial on the right front fender, near the windshield: a chrome mast leaning back on its base. */
function aerial(b: ModelBuilder, shape: BodyShape, x: number, side: number, origin: V3): void {
  const y = shape.yTop(x) - 0.004;
  const z = side * (shape.zSide(x, shape.yTop(x) - 1e-4) - 0.018);
  const base: V3 = [x - origin[0], y - origin[1], z - origin[2]];
  b.cyl(0.014, 0.012, M.plastic, { p: base }, { segs: 16 });
  const lean = 0.2;
  const len = 0.6;
  b.cyl(0.0025, len, M.chrome, { p: [base[0] - Math.sin(lean) * len / 2, base[1] + Math.cos(lean) * len / 2, base[2]], r: [0, 0, lean] }, { segs: 8 });
  b.cyl(0.004, 0.012, M.chrome, { p: [base[0] - Math.sin(lean) * len, base[1] + Math.cos(lean) * len, base[2]], r: [0, 0, lean] }, { segs: 8 });
}

/** The trim panel on a door's inside below the window, with its armrest and pull, seen through the glass. */
function doorCard(b: ModelBuilder, shape: BodyShape, x0: number, x1: number, side: number, origin: V3): void {
  const s = shape.spec;
  const y0 = s.bottomY + 0.12;
  const y1 = shape.beltY - 0.02;
  const xa = x0 + 0.04;
  const xb = x1 - 0.04;
  const region = meshRegion(rrect(xa, xb, y0, y1, 0.03, 3), [], () => 0.08);
  const map = (u: number, v: number): V3 => [u - origin[0], v - origin[1], side * (shape.zSide(u, v) - 0.035) - origin[2]];
  b.sheet(sheet(region, map, { thickness: 0, outward: () => [0, 0, -side] }), M.fabric);
  const ay = shape.beltY - 0.2;
  const am = (xa + xb) / 2;
  b.rbox([Math.min(0.45, xb - xa - 0.1), 0.04, 0.06], 0.015, M.fabric, { p: [am - origin[0], ay - origin[1], side * (shape.zSide(am, ay) - 0.065) - origin[2]] });
  const px = xb - 0.12;
  const py = shape.beltY - 0.1;
  b.rbox([0.1, 0.02, 0.012], 0.006, M.chrome, { p: [px - origin[0], py - origin[1], side * (shape.zSide(px, py) - 0.045) - origin[2]] });
}

/** The roof's drip rail along one side: a thin chrome channel along its edge. */
function dripRail(b: ModelBuilder, top: { y: (x: number, z: number) => number; edge: (x: number) => number }, x0: number, x1: number, sd: number, origin: V3): void {
  const path: V3[] = [];
  for (let x = x0 + 0.04; x <= x1 - 0.04 + 1e-6; x += 0.03) {
    const e = top.edge(x);
    path.push([x - origin[0], top.y(x, sd * e) - 0.002 - origin[1], sd * (e + 0.003) - origin[2]]);
  }
  const frames = path.map(() => ({ side: [0, 0, sd] as V3, up: [0, 1, 0] as V3 }));
  b.add(sweep(path, frames, rrect(-0.003, 0.003, -0.004, 0.003, 0.0015, 1)), M.chrome);
}

/** A door mirror at the front door's corner: a black housing on a stalk, its glass facing back. */
function mirror(b: ModelBuilder, shape: BodyShape, side: number, origin: V3): void {
  const s = shape.spec;
  const mx = s.firewallX - 0.1;
  const my = shape.beltY + 0.07;
  const base = shape.onSide(mx, my - 0.03, side, origin, 0.012);
  b.rbox([0.05, 0.03, 0.05], 0.012, M.trim, { p: base.p });
  const zOut = side * (shape.zSide(mx, my) + 0.075) - origin[2];
  b.rbox([0.07, 0.095, 0.16], 0.03, M.trim, { p: [mx - origin[0], my - origin[1], zOut] });
  b.rbox([0.006, 0.08, 0.14], 0.02, M.chrome, { p: [mx - 0.034 - origin[0], my - origin[1], zOut] });
}

/** Where a lamp sits: the middle of its opening, the surface's outward normal there, "up" along the surface and
 * "across" it. */
interface Frame {
  p: V3;
  n: V3;
  up: V3;
  across: V3;
}

/** A place on the front (end 1) or the back (-1) face at height y, across at z, `inset` behind it. */
function faceFrame(shape: BodyShape, end: number, y: number, z: number, inset = 0): Frame {
  const X = (yy: number) => (end > 0 ? shape.xFront(yy) : shape.xRear(yy));
  const h = 1e-3;
  const dX = (X(y + h) - X(y - h)) / (2 * h);
  const n = norm3([end, -end * dX, 0]);
  return { p: [X(y) - n[0] * inset, y - n[1] * inset, z], n, up: norm3([dX, 1, 0]), across: [0, 0, 1] };
}

/** One of a lamp's cells: its share of the lamp's width (0 at the start of "across", 1 at its end), its lens and its
 * reflector, and a bulb in it or not. */
interface Cell {
  from: number;
  to: number;
  lens: MatDef;
  refl: MatDef;
  bulb?: boolean;
}

interface LampOpts {
  w: number;
  h: number;
  r: number;
  /** How deep its housing goes behind the lens. */
  depth: number;
  cells: Cell[];
  /** The lens's prisms: flutes up and down ('v') or ribs across ('h'). */
  ribs: 'v' | 'h';
  bezel: MatDef;
  /** The housing's walls and the walls between its cells. */
  walls: MatDef;
}

/** A surface over a grid in a lamp's (u, v), its points outside the rounded corners (half sizes hw, hh, radius r)
 * pulled in onto them. */
function roundedGrid(us: number[], vs: number[], hw: number, hh: number, r: number, map: (u: number, v: number) => V3, outward: () => V3): THREE.BufferGeometry {
  const g = gridSurface(us, vs, map, outward);
  const pos = g.getAttribute('position') as THREE.BufferAttribute;
  const uv = g.getAttribute('uv') as THREE.BufferAttribute;
  for (let i = 0; i < pos.count; i++) {
    const u = uv.getX(i);
    const v = uv.getY(i);
    const cx = Math.abs(u) - (hw - r);
    const cy = Math.abs(v) - (hh - r);
    const d = Math.hypot(cx, cy);
    if (cx <= 0 || cy <= 0 || d <= r) continue;
    const p = map(Math.sign(u) * (hw - r + (cx * r) / d), Math.sign(v) * (hh - r + (cy * r) / d));
    pos.setXYZ(i, p[0], p[1], p[2]);
  }
  return g;
}

/**
 * A lamp set into a surface, its lens level with it: the housing going back from the opening, divided into cells,
 * each with a reflector dish (and a bulb) behind its lens; the lenses with fine prisms that break up their
 * reflections; a bead round the opening.
 */
function lamp(b: ModelBuilder, f: Frame, o: LampOpts): void {
  const at = (u: number, v: number, d: number): V3 => [
    f.p[0] + f.across[0] * u + f.up[0] * v + f.n[0] * d,
    f.p[1] + f.across[1] * u + f.up[1] * v + f.n[1] * d,
    f.p[2] + f.across[2] * u + f.up[2] * v + f.n[2] * d,
  ];
  const hw = o.w / 2;
  const hh = o.h / 2;
  const out = () => f.n;
  const outline = rrect(-hw, hw, -hh, hh, o.r, 3);
  b.add(sweep([at(0, 0, 0.0005), at(0, 0, -o.depth)], [0, 1].map(() => ({ side: f.across, up: f.up })), outline, { caps: false }), o.walls);
  o.cells.forEach((c, k) => {
    const u0 = -hw + o.w * c.from;
    const u1 = -hw + o.w * c.to;
    const uc = (u0 + u1) / 2;
    const cw = (u1 - u0) / 2;
    // The reflector: a dish, deepest behind the cell's middle.
    const dish = (u: number, v: number) => at(u, v, -o.depth * (1 - 0.3 * (((u - uc) / cw) ** 2 + (v / hh) ** 2)));
    b.add(roundedGrid(steps(u0, u1, Math.max(4, Math.ceil((u1 - u0) / 0.01))), steps(-hh, hh, Math.max(4, Math.ceil(o.h / 0.01))), hw, hh, o.r, dish, out), c.refl);
    if (c.bulb) {
      const q = at(uc, 0, -o.depth * 0.55);
      b.add(new THREE.SphereGeometry(Math.min(0.012, o.h * 0.12), 16, 10).translate(q[0], q[1], q[2]), M.chrome);
    }
    // The lens, a little behind the surface, with its prisms.
    const g0 = c.from > 0 ? u0 + 0.0015 : u0;
    const g1 = c.to < 1 ? u1 - 0.0015 : u1;
    const pr = o.ribs === 'v' ? (k % 2 ? 0.008 : 0.011) : 0.012;
    const lens = (u: number, v: number) => at(u, v, -0.0015 + (o.ribs === 'v'
      ? 0.0006 * Math.sin((2 * Math.PI * (u - u0)) / pr) + 0.00025 * Math.sin((2 * Math.PI * v) / 0.025)
      : 0.0006 * Math.sin((2 * Math.PI * v) / pr)));
    const us = o.ribs === 'v' ? steps(g0, g1, Math.ceil(((g1 - g0) / pr) * 6)) : steps(g0, g1, Math.max(3, Math.ceil((g1 - g0) / 0.01)));
    const vs = o.ribs === 'v' ? steps(-hh, hh, Math.max(4, Math.ceil((o.h / 0.025) * 6))) : steps(-hh, hh, Math.ceil((o.h / pr) * 6));
    b.add(roundedGrid(us, vs, hw, hh, o.r, lens, out), c.lens);
    // The wall between it and the next cell.
    if (k < o.cells.length - 1) {
      const wall = [at(u1, -hh, 0), at(u1, hh, 0)];
      b.add(sweep(wall, [0, 1].map(() => ({ side: f.across, up: f.n })), rrect(-0.0015, 0.0015, -o.depth, 0.0004, 0.0007, 1)), o.walls);
    }
  });
  // The bead round the opening.
  const loop = resample(outline, 0.008).map(([u, v]) => at(u, v, 0));
  b.add(sweep(loop, surfaceFrames(loop, out, true), rrect(-0.0032, 0.0032, -0.0015, 0.0022, 0.0012, 1), { closedPath: true }), o.bezel);
}

/** The front and the back of the shell: the faces between the corners, with the lamps, the grille and the black band
 * set into them. */
function faces(shell: ModelBuilder, shape: BodyShape): void {
  const s = shape.spec;
  const fr = s.front;
  const rr = s.rear;
  const trunkRear = shape.rear[0];
  const band = frontBand(shape);
  // A face's half-width at height y: a little inside the corners' rolled ends, so its rolled sides tuck in behind them.
  const faceHalf = (end: number, y: number) => shape.zSide((end > 0 ? shape.xFront(y) : shape.xRear(y)) - end * 0.0015, y) - 0.006;

  // Where the lamps and the grille go, from the faces' real widths (the corners are rolled well round).
  const ly = fr.lightY;
  const lh = fr.lightH;
  const lampW = Math.min(0.3, band.w * 0.21);
  const lampZ = band.w / 2 - lampW / 2 - 0.04;
  const gh = fr.grilleH;
  const gw = Math.min(s.width * 0.36, 2 * (lampZ - lampW / 2 - 0.06));
  const iw = 0.12;
  const ih = 0.034;
  const iy = band.y0 - 0.045;
  const iz = faceHalf(1, iy) - iw / 2 - 0.05;
  const rly = rr.lightY;
  const rlh = rr.lightH;
  const rw = rr.lightsVertical ? 0.1 : Math.min(0.34, faceHalf(-1, rly) * 0.48);
  const rz = faceHalf(-1, rly) - rw / 2 - 0.04;
  const stripW = 2 * (rz - rw / 2 - 0.02);
  // The openings are a little bigger than what goes in them; the beads round them cover the gaps.
  const grow = 0.003;
  const opening = (zc: number, yc: number, w: number, h: number, r: number) => rrect(zc - w / 2 - grow, zc + w / 2 + grow, yc - h / 2 - grow, yc + h / 2 + grow, r + grow, 3);

  for (const end of [1, -1]) {
    const xAt = (y: number) => (end > 0 ? shape.xFront(y) : shape.xRear(y));
    // At the front up to the lamps' band, at the back up to just under the boot lid's rolled edge.
    const topY = end > 0 ? band.y0 : trunkRear[1] - 0.045;
    const y0 = s.bottomY + 0.004;
    const edge = (y: number) => faceHalf(end, y);
    const n = 12;
    const ys = Array.from({ length: n + 1 }, (_, i) => y0 + ((topY - y0) * i) / n);
    const outline: V2[] = [...ys.map((y) => [-edge(y), y] as V2), ...ys.slice().reverse().map((y) => [edge(y), y] as V2)];
    const holes: V2[][] = end > 0
      ? [-1, 1].map((sd) => opening(sd * iz, iy, iw, ih, 0.008))
      : [...[-1, 1].map((sd) => opening(sd * rz, rly, rw, rlh, 0.01)), ...(stripW > 0.1 ? [rrect(-stripW / 2, stripW / 2, rly - rlh * 0.35, rly + rlh * 0.35, 0.006, 2)] : [])];
    const region = meshRegion(outline, holes, (u, v) => grade(edge(v) - Math.abs(u), 0.01, 0.02, 0.025));
    const map = (u: number, v: number): V3 => [xAt(v) - end * rollIn(edge(v) - Math.abs(u), 0.02, 55 * DEG), v, u];
    // A single surface: a hem along its sides would cross the corners' own hems.
    shell.sheet(sheet(region, map, { thickness: 0, outward: () => [end, 0, 0] }), M.paint);
  }

  // ---- front: the black band with the headlamps and the grille set into it; the indicators under it ------------------
  {
    const recess = 0.003;
    const outline = rrect(-band.w / 2, band.w / 2, band.y0 - 0.012, band.y1, 0.008, 2);
    const holes = [...[-1, 1].map((sd) => opening(sd * lampZ, ly, lampW, lh, 0.012)), opening(0, ly, gw + 0.024, gh + 0.024, 0.01)];
    shell.sheet(sheet(meshRegion(outline, holes, () => 0.03), (u, v) => [shape.xFront(v) - recess, v, u], { thickness: 0.012, hem: true, outward: () => [1, 0, 0] }), M.trim);
    // The panel over the radiator closes the bay behind the band.
    shell.rbox([0.12, 0.012, band.w], 0.004, M.underbody, { p: [shape.xFront(band.y1) - 0.08, band.y1 - 0.004, 0] });
    for (const sd of [-1, 1]) {
      // The dipped and main beams outboard, the driving lamp inboard, each in its own reflector ("across" runs to +z).
      const outer: Cell = { from: 0, to: 0.6, lens: M.lensClear, refl: M.chrome, bulb: true };
      const inner: Cell = { from: 0.6, to: 1, lens: M.lensClear, refl: M.chrome, bulb: true };
      const cells: Cell[] = sd > 0 ? [{ ...inner, from: 0, to: 0.4 }, { ...outer, from: 0.4, to: 1 }] : [outer, inner];
      lamp(shell, faceFrame(shape, 1, ly, sd * lampZ, recess), { w: lampW, h: lh, r: 0.012, depth: 0.06, cells, ribs: 'v', bezel: M.chrome, walls: M.chrome });
      lamp(shell, faceFrame(shape, 1, iy, sd * iz), {
        w: iw, h: ih, r: 0.008, depth: 0.025, cells: [{ from: 0, to: 1, lens: M.lensAmber, refl: M.reflAmber }], ribs: 'v', bezel: M.trim, walls: M.trim,
      });
    }
    // The grille in its opening: the opening's walls and dark back, chrome bars and dark uprights inside, a chrome bead
    // round it, the maker's badge on the bars.
    const g = faceFrame(shape, 1, ly, 0, recess);
    const ow = gw / 2 + 0.012;
    const oh = gh / 2 + 0.012;
    const at = (u: number, v: number, d: number): V3 => [g.p[0] + g.up[0] * v + g.n[0] * d, g.p[1] + g.up[1] * v + g.n[1] * d, u];
    const gOut = rrect(-ow, ow, -oh, oh, 0.01, 3);
    shell.add(sweep([at(0, 0, 0.0005), at(0, 0, -0.045)], [0, 1].map(() => ({ side: g.across, up: g.up })), gOut, { caps: false }), M.trim);
    shell.add(roundedGrid(steps(-ow, ow, 12), steps(-oh, oh, 3), ow, oh, 0.01, (u, v) => at(u, v, -0.045), () => g.n), M.underbody);
    const tilt: V3 = [0, 0, Math.atan2(g.n[1], g.n[0])];
    for (let k = 0; k < 4; k++) shell.rbox([0.016, 0.011, gw], 0.004, M.chrome, { p: at(0, -gh / 2 + (k + 0.5) * (gh / 4), -0.012), r: tilt });
    for (let k = 1; k < 10; k++) shell.rbox([0.02, gh + 0.02, 0.005], 0.002, M.trim, { p: at(-gw / 2 + (gw * k) / 10, 0, -0.024), r: tilt });
    const bead = resample(gOut, 0.01).map(([u, v]) => at(u, v, 0));
    shell.add(sweep(bead, surfaceFrames(bead, () => g.n, true), rrect(-0.0035, 0.0035, -0.0015, 0.0025, 0.0012, 1), { closedPath: true }), M.chrome);
    shell.rbox([0.01, 0.032, 0.075], 0.009, M.chrome, { p: at(0, 0, -0.002), r: tilt });
    // The apron under the bumper, with its air slot.
    shell.rbox([0.02, 0.03, gw * 1.1], 0.01, M.trim, { p: [shape.xFront(fr.bumperY - 0.12) + 0.004, fr.bumperY - 0.12, 0] });
  }

  // ---- back: the lamps and the black strip between them, set into the face -------------------------------------------
  const red = { lens: M.lensRed, refl: M.reflRed };
  const amber = { lens: M.lensAmber, refl: M.reflAmber };
  const white = { lens: M.lensClear, refl: M.chrome };
  for (const sd of [-1, 1]) {
    // Red outboard, then amber, then the white reversing lamp inboard ("across" runs to +z).
    const cells: Cell[] = sd > 0
      ? [{ from: 0, to: 0.18, ...white }, { from: 0.18, to: 0.42, ...amber }, { from: 0.42, to: 1, ...red }]
      : [{ from: 0, to: 0.58, ...red }, { from: 0.58, to: 0.82, ...amber }, { from: 0.82, to: 1, ...white }];
    lamp(shell, faceFrame(shape, -1, rly, sd * rz), { w: rw, h: rlh, r: 0.01, depth: 0.04, cells, ribs: 'h', bezel: M.trim, walls: M.trim });
  }
  if (stripW > 0.1) {
    // Under the face's edges round its opening, a little behind them, with a red reflector along it.
    const f = faceFrame(shape, -1, rly, 0, 0.003);
    const at = (u: number, v: number, d: number): V3 => [f.p[0] + f.up[0] * v + f.n[0] * d, f.p[1] + f.up[1] * v + f.n[1] * d, u];
    const sh = rlh * 0.35 + 0.005;
    shell.sheet(sheet(meshRegion(rrect(-stripW / 2 - 0.005, stripW / 2 + 0.005, -sh, sh, 0.008, 2), [], () => 0.03), (u, v) => at(u, v, 0), { thickness: 0.006, hem: true, outward: () => f.n }), M.trim);
    const rwid = stripW / 2 - 0.03;
    shell.add(roundedGrid(steps(-rwid, rwid, 40), steps(-0.012, 0.012, 18), rwid, 0.012, 0.004, (u, v) => at(u, v, 0.0008 + 0.0004 * Math.sin((2 * Math.PI * v) / 0.008)), () => f.n), M.reflRed);
  }
}

/** What holds the shell together and what shows through its openings: firewall, floor, arches, hubs, cowl, wipers,
 * mirrors. */
function structure(shell: ModelBuilder, shape: BodyShape): void {
  const s = shape.spec;
  const W = s.width;
  const T = 0.03;
  const hinge = hingeOf(s);
  shell.box([0.025, hinge[1] - s.bottomY, W - 2 * T], M.underbody, { p: [s.firewallX, (hinge[1] + s.bottomY) / 2, 0] });
  // The cowl: black grille under the windshield's foot.
  shell.rbox([0.13, 0.02, W - 0.3], 0.008, M.trim, { p: [s.firewallX - 0.05, hinge[1] + 0.004, 0] });
  for (let k = -8; k <= 8; k++) shell.rbox([0.07, 0.006, 0.012], 0.002, M.underbody, { p: [s.firewallX - 0.05, hinge[1] + 0.013, k * 0.07] });
  // Wipers resting at the windshield's foot.
  for (const zp of [-0.38, 0.12]) {
    const px = s.firewallX - 0.07;
    const py = hinge[1] + 0.03;
    shell.cyl(0.012, 0.03, M.trim, { p: [px, py - 0.01, zp] }, { segs: 14 });
    const blade: V3[] = [[px, py, zp], [px - 0.05, py + 0.03, zp + 0.22], [px - 0.09, py + 0.055, zp + 0.44]];
    shell.tube(blade, 0.005, M.trim, { segs: 16, radial: 8 });
    shell.rbox([0.012, 0.012, 0.42], 0.004, M.trim, { p: [px - 0.07, py + 0.045, zp + 0.24], r: [0, 0.09, 0.5] });
  }
  const frontTop = hoodFrontOf(s)[1];
  for (const side of [-1, 1]) shell.rbox([0.04, frontTop - s.bottomY - 0.05, 0.05], 0.008, M.underbody, { p: [s.bayFrontX, (frontTop + s.bottomY) / 2 - 0.02, side * (W / 2 - 0.2)] });
  shell.rbox([0.04, 0.04, W - 0.3], 0.008, M.underbody, { p: [s.bayFrontX, s.bottomY + 0.08, 0] });
  shell.rbox([0.04, 0.04, W - 0.3], 0.008, M.underbody, { p: [s.bayFrontX, frontTop - 0.06, 0] });
  const bayLen = s.bayFrontX - s.firewallX;
  for (const side of [-1, 1]) shell.rbox([bayLen, 0.02, 0.22], 0.006, M.underbody, { p: [s.firewallX + bayLen / 2, frontTop - 0.1, side * (W / 2 - 0.14)] });

  const o = s.outline;
  const floorFront = s.firewallX;
  const floorRear = o[o.length - 1][0] + 0.05;
  const floorLen = floorFront - floorRear;
  const tunnelHalf = 0.16;
  // The sills run between the wheel arches, inside the doors.
  const sillFront = s.axles[0] - s.archR - 0.02;
  const sillRear = s.axles[1] + s.archR + 0.02;
  const sillLen = sillFront - sillRear;
  const sillX = (sillFront + sillRear) / 2;
  // The floor: between the wheels all along, out to the sills only between the arches (it ran through the wheels).
  const floorOut = s.track / 2 - 0.16;
  for (const side of [-1, 1]) {
    const w = floorOut - tunnelHalf;
    shell.box([floorLen, 0.02, w], M.underbody, { p: [floorRear + floorLen / 2, s.bottomY + 0.01, side * (tunnelHalf + w / 2)] });
    const wOut = W / 2 - T - 0.04 - floorOut;
    shell.box([sillLen, 0.02, wOut], M.underbody, { p: [sillX, s.bottomY + 0.01, side * (floorOut + wOut / 2)] });
    shell.box([floorLen, 0.25, 0.02], M.underbody, { p: [floorRear + floorLen / 2, s.bottomY + 0.135, side * tunnelHalf] });
    // The sill behind the doors, so the car keeps its outline with them off.
    shell.rbox([sillLen, 0.1, 0.07], 0.025, M.paint, { p: [sillX, s.bottomY + 0.05, side * (shape.zSide(sillX, s.bottomY + 0.05) - 0.05)] });
  }
  shell.box([floorLen, 0.02, tunnelHalf * 2], M.underbody, { p: [floorRear + floorLen / 2, s.bottomY + 0.26, 0] });

  const [fx, rx] = s.axles;
  for (const ax of [fx, rx]) {
    for (const side of [-1, 1]) {
      shell.cyl(s.archR + 0.01, 0.34, M.plastic, { p: [ax, s.wheelR, side * (W / 2 - 0.19)] }, { axis: 'z', open: true, thetaStart: Math.PI / 2, thetaLength: Math.PI, segs: 48 });
      const hubZ = side * (s.track / 2 - 0.07);
      shell.cyl(0.05, 0.06, M.darkSteel, { p: [ax, s.wheelR, hubZ] }, { axis: 'z', segs: 24 });
      shell.rbox([0.06, 0.16, 0.05], 0.01, M.castIron, { p: [ax, s.wheelR + 0.03, hubZ - side * 0.06] });
      shell.rbox([0.05, 0.04, s.track / 2 - 0.2], 0.012, M.darkSteel, { p: [ax, s.wheelR - 0.06, side * ((s.track / 2 - 0.2) / 2 + 0.1)] });
    }
  }

}

/** Seats, dashboard and steering wheel, seen through the glass. */
function interior(shell: ModelBuilder, shape: BodyShape): void {
  const s = shape.spec;
  const W = s.width;
  shell.rbox([0.26, 0.14, W - 0.12], 0.04, M.fabric, { p: [s.dash.x, s.dash.y, 0] });
  // The instruments' hood and the wheel in front of the driver (left).
  const dz = -(W / 2 - 0.45);
  shell.rbox([0.14, 0.07, 0.4], 0.025, M.fabric, { p: [s.dash.x - 0.04, s.dash.y + 0.09, dz] });
  const wc: V3 = [s.dash.x - 0.24, s.dash.y + 0.07, dz];
  shell.torus(0.18, 0.016, M.plastic, { p: wc, r: [0, 0, 0.35] }, { axis: 'x', segs: 40 });
  shell.cyl(0.035, 0.05, M.plastic, { p: wc, r: [0, 0, 0.35 + Math.PI / 2] }, { segs: 20 });
  shell.rbox([0.014, 0.03, 0.34], 0.008, M.plastic, { p: wc, r: [0, 0, 0.35] });
  // The gear lever and the handbrake on the tunnel, the instruments before the driver, the mirror and the sun visors
  // under the roof's front edge, the parcel shelf under the rear window.
  const tunnelTop = s.bottomY + 0.27;
  shell.cyl(0.007, 0.2, M.plastic, { p: [s.dash.x - 0.42, tunnelTop + 0.1, 0], r: [0, 0, -0.25] }, { segs: 10 });
  shell.rbox([0.035, 0.04, 0.035], 0.015, M.plastic, { p: [s.dash.x - 0.445, tunnelTop + 0.2, 0] });
  shell.rbox([0.2, 0.03, 0.03], 0.012, M.plastic, { p: [s.dash.x - 0.7, tunnelTop + 0.03, 0.08], r: [0, 0, 0.3] });
  for (const dz of [-0.075, 0.075]) {
    shell.cyl(0.045, 0.006, M.underbody, { p: [s.dash.x - 0.11, s.dash.y + 0.085, dz + -(W / 2 - 0.45)] }, { axis: 'x', segs: 28 });
    shell.torus(0.045, 0.004, M.chrome, { p: [s.dash.x - 0.114, s.dash.y + 0.085, dz + -(W / 2 - 0.45)] }, { axis: 'x', segs: 28 });
  }
  const wsTop = s.outline[s.segs.indexOf('windshield') + 1];
  shell.rbox([0.02, 0.05, 0.2], 0.012, M.plastic, { p: [wsTop[0] - 0.08, wsTop[1] - 0.09, 0] });
  shell.cyl(0.006, 0.05, M.plastic, { p: [wsTop[0] - 0.07, wsTop[1] - 0.045, 0] }, { segs: 8 });
  for (const dz of [-0.36, 0.36]) shell.rbox([0.14, 0.014, 0.4], 0.006, M.fabric, { p: [wsTop[0] - 0.1, wsTop[1] - 0.05, dz], r: [0, 0, 0.2] });
  const rearSeat = s.seats[s.seats.length - 1];
  shell.rbox([0.36, 0.022, W - 0.24], 0.008, M.fabric, { p: [rearSeat.x - 0.52, shape.beltY + 0.02, 0] });
  s.seats.forEach((seat, i) => {
    for (const side of i === 0 ? [-1, 1] : [0]) {
      const zw = side === 0 ? W - 0.3 : 0.48;
      shell.rbox([0.5, 0.13, zw], 0.05, M.fabric, { p: [seat.x, seat.y, side * 0.38] });
      shell.rbox([0.13, 0.56, zw], 0.05, M.fabric, { p: [seat.x - 0.25, seat.y + 0.3, side * 0.38], r: [0, 0, 0.15] });
      if (side !== 0) shell.rbox([0.1, 0.16, zw * 0.55], 0.04, M.fabric, { p: [seat.x - 0.31, seat.y + 0.65, side * 0.38], r: [0, 0, 0.15] });
    }
  });
}

/** The bonnet of a second-generation body, about its hinge: curved like the body, crowned, its front edge rolled
 * down, with its hinges and latch underneath (and an air scoop if asked). */
export function hood2(shape: BodyShape, scoop: boolean): { model: ModelBuilder; fasteners: FastenerDef[] } {
  const s = shape.spec;
  const hinge = hingeOf(s);
  const front = hoodFrontOf(s);
  const gap = 0.005;
  const a = hinge[0] + 0.006;
  const c = front[0] - 0.004;
  const edge = (x: number) => shape.zSide(x, shape.yTop(x) - 1e-4) - gap;
  const y = (x: number, z: number) => shape.yTop(x) + 0.022 * Math.max(0, 1 - (z / edge(x)) ** 2)
    - rollIn(c - x, 0.03, 70 * DEG) - rollIn(edge(x) - Math.abs(z), 0.012, 55 * DEG);
  const n = Math.max(8, Math.ceil((c - a) / 0.05));
  const xs = Array.from({ length: n + 1 }, (_, i) => a + ((c - a) * i) / n);
  const outline: V2[] = [...xs.map((x) => [x, -edge(x)] as V2), ...xs.slice().reverse().map((x) => [x, edge(x)] as V2)];
  const region = meshRegion(outline, [], (u, v) => {
    return Math.min(grade(c - u, 0.01, 0.03, 0.08), grade(edge(u) - Math.abs(v), 0.01, 0.013, 0.08));
  });
  const map = (u: number, v: number): V3 => [u - hinge[0], y(u, v) - hinge[1], v];
  const b = new ModelBuilder().sheet(sheet(region, map, { thickness: 0.012, hem: true, outward: () => [0, 1, 0] }), M.paint);
  const fasteners: FastenerDef[] = [];
  for (const side of [-1, 1]) {
    const hz = side * (edge(hinge[0] + 0.07) - 0.09);
    const hy = y(hinge[0] + 0.07, hz) - hinge[1];
    b.rbox([0.14, 0.02, 0.03], 0.005, M.darkSteel, { p: [0.07, hy - 0.028, hz] });
    for (const x of [0.03, 0.11]) fasteners.push({ kind: 'bolt_s', pos: [x, y(hinge[0] + x, hz) - hinge[1] - 0.04, hz], dir: [0, -1, 0] });
  }
  b.rbox([0.04, 0.03, 0.08], 0.006, M.darkSteel, { p: [c - hinge[0] - 0.05, y(c - 0.05, 0) - hinge[1] - 0.03, 0] });
  // The washer jets, near the back edge.
  for (const z of [-0.28, 0.28]) b.rbox([0.02, 0.012, 0.012], 0.005, M.trim, { p: [a - hinge[0] + 0.06, y(a + 0.06, z) - hinge[1] + 0.004, z] });
  if (scoop) {
    const sx = a + (c - a) * 0.45;
    const sy = y(sx, 0) - hinge[1];
    b.rbox([0.42, 0.075, 0.44], 0.03, M.paint, { p: [sx - hinge[0], sy + 0.025, 0] });
    b.rbox([0.02, 0.045, 0.36], 0.012, M.trim, { p: [sx - hinge[0] + 0.205, sy + 0.035, 0] });
  }
  return { model: b, fasteners };
}
