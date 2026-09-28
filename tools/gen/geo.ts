// Geometry helpers for building placeholder models out of primitives.

import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import type { Vec3 } from './types.ts';
import type { GlbPrimitive } from './glb.ts';

export interface MatDef {
  name: string;
  /** Linear RGB. */
  color: [number, number, number];
  metallic: number;
  roughness: number;
  emissive?: [number, number, number];
  opacity?: number;
}

const lin = (c: number) => Math.pow(c / 255, 2.2);
export function srgb(hex: number): [number, number, number] {
  return [lin((hex >> 16) & 255), lin((hex >> 8) & 255), lin(hex & 255)];
}

function mat(name: string, hex: number, metallic: number, roughness: number, extra: Partial<MatDef> = {}): MatDef {
  return { name, color: srgb(hex), metallic, roughness, ...extra };
}

/** Shared material library. Names matter: the engine looks some of them up (e.g. "paint"). */
export const M = {
  paint: mat('paint', 0x8a1c1c, 0.35, 0.32),
  steel: mat('steel', 0x8c8f94, 0.85, 0.45),
  darkSteel: mat('dark_steel', 0x4a4d52, 0.8, 0.5),
  castIron: mat('cast_iron', 0x46474a, 0.55, 0.78),
  aluminum: mat('aluminum', 0xb8bcc2, 0.9, 0.38),
  chrome: mat('chrome', 0xeceef2, 1.0, 0.07),
  rubber: mat('rubber', 0x151515, 0.0, 0.92),
  plastic: mat('plastic_black', 0x1d1e21, 0.0, 0.6),
  plasticGrey: mat('plastic_grey', 0x5a5d62, 0.0, 0.55),
  exhaust: mat('exhaust_steel', 0x5c5049, 0.6, 0.72),
  rust: mat('rust', 0x6e4630, 0.35, 0.88),
  copper: mat('copper', 0xb87333, 1.0, 0.35),
  brass: mat('brass', 0xc9a54a, 1.0, 0.35),
  red: mat('red_coat', 0xa11d1d, 0.3, 0.4),
  blue: mat('blue_coat', 0x1d4fa1, 0.3, 0.4),
  yellow: mat('yellow_coat', 0xd9b21c, 0.2, 0.45),
  orange: mat('orange_coat', 0xd9661c, 0.2, 0.45),
  green: mat('green_coat', 0x2f7a3a, 0.2, 0.45),
  glass: mat('glass', 0x9fb4c4, 0.0, 0.05, { opacity: 0.35 }),
  headlight: mat('headlight', 0xf0f4ff, 0.2, 0.1, { emissive: [0.45, 0.45, 0.4] }),
  taillight: mat('taillight', 0xb3121a, 0.1, 0.2, { emissive: [0.3, 0.01, 0.01] }),
  amber: mat('amber_light', 0xe08a1a, 0.1, 0.2, { emissive: [0.2, 0.08, 0.0] }),
  friction: mat('friction', 0x2a2622, 0.0, 0.95),
  radiatorCore: mat('radiator_core', 0x2f3134, 0.6, 0.7),
  fabric: mat('fabric', 0x2b2b2e, 0.0, 0.95),
  underbody: mat('underbody', 0x1a1a1c, 0.2, 0.9),
  wood: mat('wood', 0x8a6440, 0.0, 0.8),
  toolRed: mat('tool_red', 0xb01818, 0.3, 0.35),
  concrete: mat('concrete', 0x8a8a86, 0.0, 0.95),
  liftYellow: mat('lift_yellow', 0xd8a91c, 0.3, 0.5),
  paper: mat('paper', 0xe8e2d0, 0.0, 0.9),
  // Lamp lenses and trims of the second-generation bodies: lamps switched off (no glow), glossy. The lenses let through
  // the reflectors behind them, the coloured lamps' reflectors coloured metal (as they look through their lenses).
  lensRed: mat('lens_red', 0x8e0d14, 0.0, 0.08, { opacity: 0.55 }),
  lensAmber: mat('lens_amber', 0xc86a10, 0.0, 0.08, { opacity: 0.55 }),
  lensClear: mat('lens_clear', 0xdfe6ea, 0.0, 0.05, { opacity: 0.28 }),
  reflRed: mat('reflector_red', 0x8a0c12, 1.0, 0.3),
  reflAmber: mat('reflector_amber', 0xc8680e, 1.0, 0.3),
  seal: mat('seal', 0x121213, 0.0, 0.75),
  glassTint: mat('glass_tint', 0x40607a, 0.0, 0.05, { opacity: 0.55 }),
  frit: mat('frit_black', 0x0b0b0c, 0.0, 0.15),
  trim: mat('trim_black', 0x18191b, 0.1, 0.45),
};

export interface Xform {
  p?: Vec3;
  r?: Vec3;
  s?: Vec3;
}

function applyXform(geo: THREE.BufferGeometry, t?: Xform): THREE.BufferGeometry {
  if (!t) return geo;
  const m = new THREE.Matrix4();
  m.compose(
    new THREE.Vector3(...(t.p ?? [0, 0, 0])),
    new THREE.Quaternion().setFromEuler(new THREE.Euler(...(t.r ?? [0, 0, 0]), 'XYZ')),
    new THREE.Vector3(...(t.s ?? [1, 1, 1])),
  );
  geo.applyMatrix4(m);
  return geo;
}

export type Axis = 'x' | 'y' | 'z';

/** Rotate a Y-aligned geometry onto the given axis. */
function orient(geo: THREE.BufferGeometry, axis: Axis): THREE.BufferGeometry {
  if (axis === 'x') geo.rotateZ(-Math.PI / 2);
  else if (axis === 'z') geo.rotateX(Math.PI / 2);
  return geo;
}

export class ModelBuilder {
  items: { geo: THREE.BufferGeometry; mat: MatDef }[] = [];

  add(geo: THREE.BufferGeometry, m: MatDef, t?: Xform): this {
    this.items.push({ geo: applyXform(geo, t), mat: m });
    return this;
  }

  /** A box, its edges bevelled a little (a sixth of its smallest side, at most 8 mm) so they catch the light. */
  box(size: Vec3, m: MatDef, t?: Xform): this {
    const r = Math.min(0.008, Math.min(...size) / 6);
    if (r < 0.0008) return this.add(new THREE.BoxGeometry(...size), m, t);
    return this.add(new RoundedBoxGeometry(size[0], size[1], size[2], 1, r), m, t);
  }

  /** A box with its edges and corners rounded by `r` (smooth normals). */
  rbox(size: Vec3, r: number, m: MatDef, t?: Xform, segs = 2): this {
    return this.add(new RoundedBoxGeometry(size[0], size[1], size[2], segs, Math.min(r, ...size.map((v) => v / 2 - 1e-4))), m, t);
  }

  /** A curved panel (see sheet.ts): its outer face, inner face and rim, each with its material. */
  sheet(g: { outer: THREE.BufferGeometry; inner: THREE.BufferGeometry | null; rim: THREE.BufferGeometry | null }, outer: MatDef, inner: MatDef = outer, rim: MatDef = outer, t?: Xform): this {
    this.add(g.outer, outer, t);
    if (g.inner) this.add(g.inner, inner, t);
    if (g.rim) this.add(g.rim, rim, t);
    return this;
  }

  /** Cylinder of radius r and length h along `axis`, centered on the origin. */
  cyl(r: number, h: number, m: MatDef, t?: Xform, o: { axis?: Axis; rTop?: number; segs?: number; open?: boolean; thetaLength?: number; thetaStart?: number } = {}): this {
    const rTop = o.rTop ?? r;
    // More sides the bigger it is; closed ones get their end edges chamfered, so they catch the light.
    const segs = o.segs ?? (Math.max(r, rTop) < 0.015 ? 14 : Math.max(r, rTop) < 0.06 ? 24 : 40);
    const f = Math.min(0.005, Math.min(r, rTop, h / 2) * 0.18);
    const whole = (o.thetaLength ?? Math.PI * 2) >= Math.PI * 2 - 1e-6;
    if (o.open || !whole || f < 0.0006) {
      const g = new THREE.CylinderGeometry(rTop, r, h, segs, 1, o.open ?? false, o.thetaStart ?? 0, o.thetaLength ?? Math.PI * 2);
      return this.add(orient(g, o.axis ?? 'y'), m, t);
    }
    const profile = [[0, -h / 2], [r - f, -h / 2], [r, -h / 2 + f], [rTop, h / 2 - f], [rTop - f, h / 2], [0, h / 2]].map(([x, y]) => new THREE.Vector2(x, y));
    return this.add(orient(new THREE.LatheGeometry(profile, segs), o.axis ?? 'y'), m, t);
  }

  /** Hexagonal prism (nut / bolt head) along `axis`. */
  hex(r: number, h: number, m: MatDef, t?: Xform, axis: Axis = 'y'): this {
    return this.cyl(r, h, m, t, { axis, segs: 6 });
  }

  torus(R: number, r: number, m: MatDef, t?: Xform, o: { axis?: Axis; arc?: number; segs?: number } = {}): this {
    const g = new THREE.TorusGeometry(R, r, 10, o.segs ?? 28, o.arc ?? Math.PI * 2);
    // TorusGeometry lies in the XY plane (axis Z). Re-orient to the requested axis.
    const axis = o.axis ?? 'z';
    if (axis === 'y') g.rotateX(Math.PI / 2);
    else if (axis === 'x') g.rotateY(Math.PI / 2);
    return this.add(g, m, t);
  }

  /** Tube along a polyline (smoothed with a Catmull-Rom curve). */
  tube(points: Vec3[], r: number, m: MatDef, o: { closed?: boolean; segs?: number; radial?: number; tension?: number } = {}): this {
    const curve = new THREE.CatmullRomCurve3(points.map((p) => new THREE.Vector3(...p)), o.closed ?? false, 'catmullrom', o.tension ?? 0.3);
    const g = new THREE.TubeGeometry(curve, o.segs ?? Math.max(8, points.length * 8), r, o.radial ?? 10, o.closed ?? false);
    return this.add(g, m);
  }

  /** Lathe around `axis`: profile points are [radius, height]. */
  lathe(profile: [number, number][], m: MatDef, t?: Xform, o: { axis?: Axis; segs?: number } = {}): this {
    const g = new THREE.LatheGeometry(profile.map(([x, y]) => new THREE.Vector2(x, y)), o.segs ?? 28);
    return this.add(orient(g, o.axis ?? 'y'), m, t);
  }

  /** Extrude a shape (XY plane) by `depth` along +Z. */
  extrude(shape: THREE.Shape, depth: number, m: MatDef, t?: Xform, bevel = 0): this {
    const g = new THREE.ExtrudeGeometry(shape, {
      depth,
      bevelEnabled: bevel > 0,
      bevelSize: bevel,
      bevelThickness: bevel,
      bevelSegments: 2,
      curveSegments: 16,
    });
    return this.add(g, m, t);
  }

  /** Quad strip between two copies of a 2D polyline (x,y) placed at z0 and z1. */
  ruled(poly: [number, number][], z0: number, z1: number, m: MatDef, t?: Xform): this {
    const pos: number[] = [];
    const uv: number[] = [];
    let len = 0;
    for (let i = 0; i < poly.length - 1; i++) {
      const [ax, ay] = poly[i];
      const [bx, by] = poly[i + 1];
      const seg = Math.hypot(bx - ax, by - ay);
      pos.push(ax, ay, z0, bx, by, z0, bx, by, z1, ax, ay, z0, bx, by, z1, ax, ay, z1);
      uv.push(len, 0, len + seg, 0, len + seg, 1, len, 0, len + seg, 1, len, 1);
      len += seg;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.computeVertexNormals();
    return this.add(g, m, t);
  }

  /** Merge another builder into this one with a transform. */
  merge(other: ModelBuilder, t?: Xform): this {
    for (const it of other.items) this.add(it.geo.clone(), it.mat, t);
    return this;
  }

  build(): GlbPrimitive[] {
    const groups = new Map<string, { mat: MatDef; geos: THREE.BufferGeometry[] }>();
    for (const { geo, mat } of this.items) {
      const g = geo.clone();
      for (const name of Object.keys(g.attributes)) {
        if (name !== 'position' && name !== 'normal' && name !== 'uv') g.deleteAttribute(name);
      }
      if (!g.getAttribute('normal')) g.computeVertexNormals();
      const count = g.getAttribute('position').count;
      if (!g.getAttribute('uv')) {
        g.setAttribute('uv', new THREE.Float32BufferAttribute(new Float32Array(count * 2), 2));
      }
      // Keep everything indexed so shared vertices are stored once.
      if (!g.index) g.setIndex(Array.from({ length: count }, (_, i) => i));
      g.clearGroups();
      let entry = groups.get(mat.name);
      if (!entry) {
        entry = { mat, geos: [] };
        groups.set(mat.name, entry);
      }
      entry.geos.push(g);
    }
    const out: GlbPrimitive[] = [];
    for (const { mat, geos } of groups.values()) {
      const merged = mergeGeometries(geos, false);
      if (!merged) throw new Error(`merge failed for material ${mat.name}`);
      out.push({ mat, geo: merged });
    }
    return out;
  }
}

/** Rounded rectangle shape centered on the origin. */
export function roundedRect(w: number, h: number, r: number): THREE.Shape {
  const s = new THREE.Shape();
  const x = -w / 2;
  const y = -h / 2;
  s.moveTo(x + r, y);
  s.lineTo(x + w - r, y);
  s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r);
  s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h);
  s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r);
  s.quadraticCurveTo(x, y, x + r, y);
  return s;
}

export function circlePath(cx: number, cy: number, r: number): THREE.Path {
  const p = new THREE.Path();
  p.absarc(cx, cy, r, 0, Math.PI * 2, true);
  return p;
}
