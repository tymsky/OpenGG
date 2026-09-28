// Second-generation part models: the same parts, places and bolts as parts.ts, drawn in more detail (a tyre with its
// sidewalls and tread, pressed and cast rims, a vented brake disc, a rounded caliper).

import * as THREE from 'three';
import { ModelBuilder, M, type MatDef } from './geo.ts';
import type { FastenerDef, Vec3 } from './types.ts';
import { fillet, meshRegion, sheet, sweep, type V2, type V3 } from './sheet.ts';
import { F, type BuiltPart, type RimStyle } from './parts.ts';

const PI = Math.PI;

/** Points along an arc of radius r about (cx, cy), from angle a0 to a1 (radians), `n` steps. */
function arc(cx: number, cy: number, r: number, a0: number, a1: number, n: number): V2[] {
  return Array.from({ length: n + 1 }, (_, i) => {
    const a = a0 + ((a1 - a0) * i) / n;
    return [cx + Math.cos(a) * r, cy + Math.sin(a) * r] as V2;
  });
}

/** A lathe about the wheel's axle (z) from a profile of (radius, z) points. */
function latheZ(b: ModelBuilder, profile: V2[], m: MatDef, segs = 72, phiStart = 0, phiLength = 2 * PI): void {
  const g = new THREE.LatheGeometry(profile.map(([r, z]) => new THREE.Vector2(r, z)), segs, phiStart, phiLength);
  // The lathe turns about its y: turned a quarter about x, its y (the profile's z) becomes z.
  g.rotateX(PI / 2);
  b.add(g, m);
}

/**
 * A wheel (tyre and rim) of outside radius R, rim radius rimR and width w; wheel-local, the axle along z, +z the
 * outside. The lug nuts (fasteners) stay where parts.ts has them: five at radius lugR, 5.2 cm out.
 */
export function wheel2(R: number, rimR: number, w: number, style: RimStyle, lugR: number): BuiltPart {
  const b = new ModelBuilder();
  const h = w / 2;
  // ---- the tyre: beads, sidewalls bulging a little past the tread's width, round shoulders, the tread's base -------
  const base = R - 0.009;
  const sh = 0.028;
  const side = (s: number): V2[] => {
    const pts: V2[] = [
      [rimR + 0.003, s * (h - 0.012)],
      [rimR + 0.02, s * (h - 0.002)],
      [rimR + 0.045, s * (h + 0.005)],
      [rimR + 0.075, s * (h + 0.006)],
      [base - sh, s * (h + 0.002)],
    ];
    // The shoulder: a quarter round up to the tread's base.
    const c: V2 = [base - sh, s * (h - sh + 0.002)];
    const a0 = s > 0 ? PI / 2 : -PI / 2;
    for (const p of arc(c[0], c[1], sh, a0, 0, 8).slice(1)) pts.push(p);
    return pts;
  };
  // Lathe profiles go from the inner bead (z < 0) round to the outer one.
  const profile: V2[] = [...side(-1), [base, 0], ...side(1).reverse()];
  latheZ(b, profile, M.rubber, 96);
  // Tread: three ribs of blocks, the middle one staggered.
  const ribs: [number, number, number][] = [[-(h - sh + 0.004), -0.03, 0], [-0.022, 0.022, 0.5], [0.03, h - sh + 0.004, 0]];
  const n = 60;
  for (const [z0, z1, off] of ribs) {
    const prof: V2[] = fillet([[z0, 0], [z1, 0], [z1, 0.0095], [z0, 0.0095]], 0.003, 1);
    for (let i = 0; i < n; i++) {
      const a0 = ((i + off) / n) * 2 * PI;
      const a1 = a0 + (2 * PI) / n - 0.012;
      const path: V3[] = [];
      const frames: { side: V3; up: V3 }[] = [];
      for (let k = 0; k <= 3; k++) {
        const a = a0 + ((a1 - a0) * k) / 3;
        path.push([Math.cos(a) * (base - 0.001), Math.sin(a) * (base - 0.001), 0]);
        frames.push({ side: [0, 0, 1], up: [Math.cos(a), Math.sin(a), 0] });
      }
      b.add(sweep(path, frames, prof), M.rubber);
    }
  }
  // ---- the rim's barrel: flanges at both sides, the well between ---------------------------------------------------
  const rimMat = style === 'chrome' ? M.chrome : style === 'alloy' ? M.aluminum : M.plasticGrey;
  const barrel: V2[] = [
    [rimR + 0.012, -(h - 0.004)],
    [rimR + 0.004, -(h - 0.0)],
    [rimR - 0.004, -(h - 0.012)],
    [rimR - 0.006, -(h - 0.03)],
    [rimR - 0.024, -0.03],
    [rimR - 0.024, 0.02],
    [rimR - 0.006, h - 0.03],
    [rimR - 0.004, h - 0.012],
    [rimR + 0.004, h],
    [rimR + 0.012, h - 0.004],
  ];
  latheZ(b, barrel, rimMat, 72);
  // ---- the face ---------------------------------------------------------------------------------------------------
  if (style === 'steel') {
    // A pressed steel disc: the flat mounting face at the nuts, a dish out to the rim, a stiffening ridge, six vents.
    const outer: V2[] = arc(0, 0, rimR - 0.008, 0, 2 * PI, 72).slice(0, -1);
    const holes: V2[][] = [arc(0, 0, 0.034, 0, 2 * PI, 24).slice(0, -1).reverse()];
    for (let i = 0; i < 6; i++) {
      const a = (i / 6) * 2 * PI + PI / 6;
      holes.push(arc(Math.cos(a) * 0.128, Math.sin(a) * 0.128, 0.02, 0, 2 * PI, 16).slice(0, -1).reverse());
    }
    const zAt = (r: number) => {
      const t = Math.max(0, Math.min(1, (r - 0.085) / (rimR - 0.095)));
      const dish = 0.03 * t * t * (3 - 2 * t);
      const ridge = 0.006 * Math.exp(-(((r - 0.1) / 0.01) ** 2));
      return 0.04 - 0.025 * Math.max(0, Math.min(1, (r - 0.06) / 0.04)) + dish + ridge;
    };
    const region = meshRegion(outer, holes, () => 0.02);
    const map = (u: number, v: number): V3 => [u, v, zAt(Math.hypot(u, v))];
    b.sheet(sheet(region, map, { thickness: 0.005, outward: () => [0, 0, 1] }), rimMat);
    // The hubcap: a small chrome dome.
    latheZ(b, [[0, 0.064], [0.012, 0.063], [0.024, 0.058], [0.032, 0.05], [0.036, 0.042]], M.chrome, 40);
  } else {
    // A cast face: a disc with windows between the spokes, its edges rounded.
    const spokes = style === 'chrome' ? 10 : 5;
    const shape = new THREE.Shape();
    shape.absarc(0, 0, rimR - 0.004, 0, 2 * PI, false);
    const r0 = lugR + 0.034;
    const r1 = rimR - 0.022;
    const spokeW = style === 'chrome' ? 0.016 : 0.042;
    for (let i = 0; i < spokes; i++) {
      const mid = ((i + 0.5) / spokes) * 2 * PI + PI / 2;
      const half0 = PI / spokes - spokeW / 2 / r0;
      const half1 = PI / spokes - (spokeW * 1.4) / 2 / r1;
      const win: V2[] = [...arc(0, 0, r0, mid - half0, mid + half0, 6), ...arc(0, 0, r1, mid + half1, mid - half1, 10)];
      const rounded = fillet(win, style === 'chrome' ? 0.006 : 0.012, 3);
      const path = new THREE.Path();
      path.moveTo(rounded[0][0], rounded[0][1]);
      for (const p of rounded.slice(1)) path.lineTo(p[0], p[1]);
      path.closePath();
      shape.holes.push(path);
    }
    const g = new THREE.ExtrudeGeometry(shape, { depth: 0.014, bevelEnabled: true, bevelSize: 0.005, bevelThickness: 0.006, bevelSegments: 3, curveSegments: 48 });
    g.translate(0, 0, 0.022);
    b.add(g, rimMat);
    // The centre cap.
    latheZ(b, [[0, 0.062], [0.014, 0.061], [0.024, 0.056], [0.029, 0.048], [0.03, 0.04]], style === 'chrome' ? M.chrome : M.plasticGrey, 40);
  }
  const fasteners: FastenerDef[] = [];
  for (let i = 0; i < 5; i++) {
    const a = PI / 2 + (i * 2 * PI) / 5;
    fasteners.push(F('lug_nut', [Math.cos(a) * lugR, Math.sin(a) * lugR, 0.052], [0, 0, 1]));
  }
  return { model: b, fasteners };
}

/** A vented brake disc: two friction rings with vanes between, on its hat, and the wheel studs. */
export function rotor2(Rr: number, lugR: number): BuiltPart {
  const b = new ModelBuilder();
  const inner = Rr * 0.6;
  // The friction faces and the vent gap (the rings' cross-section, lathed).
  latheZ(b, [[inner, -0.011], [Rr - 0.002, -0.011], [Rr, -0.009], [Rr, -0.003], [inner, -0.003]], M.steel, 72);
  latheZ(b, [[inner, 0.003], [Rr, 0.003], [Rr, 0.009], [Rr - 0.002, 0.011], [inner, 0.011]], M.steel, 72);
  for (let i = 0; i < 36; i++) {
    const a = (i / 36) * 2 * PI;
    const rm = (inner + Rr) / 2;
    b.box([Rr - inner - 0.004, 0.004, 0.007], M.darkSteel, { p: [Math.cos(a) * rm, Math.sin(a) * rm, 0], r: [0, 0, a] });
  }
  // The hat.
  latheZ(b, [[inner + 0.004, -0.004], [0.08, 0.004], [0.08, 0.03], [0.072, 0.036], [0.03, 0.036]], M.darkSteel, 48);
  for (let i = 0; i < 5; i++) {
    const a = PI / 2 + (i * 2 * PI) / 5;
    b.cyl(0.006, 0.03, M.steel, { p: [Math.cos(a) * lugR, Math.sin(a) * lugR, 0.05] }, { axis: 'z', segs: 12 });
  }
  return { model: b, fasteners: [F('screw', [0.0, -0.03, 0.047], [0, 0, 1])] };
}

/** A brake caliper astride the disc's back edge, rounded, with its bleed nipple and hose. */
export function caliper2(Rr: number, m: MatDef): BuiltPart {
  const b = new ModelBuilder();
  const x = -(Rr - 0.025);
  b.rbox([0.055, 0.125, 0.075], 0.014, m, { p: [x, 0.025, 0] });
  b.cyl(0.028, 0.024, m, { p: [x, 0.025, -0.046] }, { axis: 'z', segs: 28 });
  b.rbox([0.02, 0.145, 0.022], 0.006, M.darkSteel, { p: [x - 0.03, 0.025, -0.03] });
  b.cyl(0.004, 0.018, M.brass, { p: [x - 0.01, 0.08, -0.045] }, { segs: 10 });
  b.tube([[x - 0.02, 0.08, -0.03], [x - 0.05, 0.12, -0.06], [x - 0.08, 0.16, -0.12]], 0.004, M.rubber, { radial: 10 });
  return { model: b, fasteners: [F('bolt_m', [x - 0.012, 0.025 - 0.045, 0.04], [0, 0, 1]), F('bolt_m', [x - 0.012, 0.025 + 0.045, 0.04], [0, 0, 1])] };
}

export type { Vec3 };
