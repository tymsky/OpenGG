// Curved sheet panels and swept profiles (the generator's second generation of models).
//
// A panel starts as a region of a plane (an outline with holes, in some 2D "u, v" coordinates), is triangulated and
// refined wherever it must bend, and is then mapped onto a surface by a function (u, v) -> 3D point. Normals come from
// the mapping itself (finite differences), so the shading is smooth however the triangles fall. The sheet gets a
// thickness and a rim along every edge. Swept profiles (rubber seals, bumpers, trims) follow a 3D path with a frame
// given at every point.

import * as THREE from 'three';

export type V2 = [number, number];
export type V3 = [number, number, number];

const sub3 = (a: V3, b: V3): V3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add3 = (a: V3, b: V3): V3 => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul3 = (a: V3, k: number): V3 => [a[0] * k, a[1] * k, a[2] * k];
const dot3 = (a: V3, b: V3) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross3 = (a: V3, b: V3): V3 => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
export const norm3 = (a: V3): V3 => {
  const l = Math.hypot(a[0], a[1], a[2]) || 1;
  return [a[0] / l, a[1] / l, a[2] / l];
};
export { sub3, add3, mul3, dot3, cross3 };

// ---- polygons ------------------------------------------------------------------------------------------------

export function polyArea(p: V2[]): number {
  let a = 0;
  for (let i = 0; i < p.length; i++) {
    const [x0, y0] = p[i];
    const [x1, y1] = p[(i + 1) % p.length];
    a += x0 * y1 - x1 * y0;
  }
  return a / 2;
}

/** Counter-clockwise (outer outlines) or clockwise (holes). */
export function oriented(p: V2[], ccw: boolean): V2[] {
  return (polyArea(p) > 0) === ccw ? p.slice() : p.slice().reverse();
}

/** Splits every edge longer than `step` into equal pieces. */
export function resample(p: V2[], step: number, closed = true): V2[] {
  const out: V2[] = [];
  const n = closed ? p.length : p.length - 1;
  for (let i = 0; i < n; i++) {
    const a = p[i];
    const b = p[(i + 1) % p.length];
    const k = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / step));
    for (let j = 0; j < k; j++) out.push([a[0] + ((b[0] - a[0]) * j) / k, a[1] + ((b[1] - a[1]) * j) / k]);
  }
  if (!closed) out.push(p[p.length - 1]);
  return out;
}

/** Rounds every corner of a closed polygon with radius `r` (less where the edges are short). */
export function fillet(p: V2[], r: number, segs = 6): V2[] {
  const out: V2[] = [];
  const n = p.length;
  for (let i = 0; i < n; i++) {
    const prev = p[(i + n - 1) % n];
    const cur = p[i];
    const next = p[(i + 1) % n];
    const d0: V2 = [prev[0] - cur[0], prev[1] - cur[1]];
    const d1: V2 = [next[0] - cur[0], next[1] - cur[1]];
    const l0 = Math.hypot(...d0);
    const l1 = Math.hypot(...d1);
    const u0: V2 = [d0[0] / l0, d0[1] / l0];
    const u1: V2 = [d1[0] / l1, d1[1] / l1];
    const cosA = Math.max(-1, Math.min(1, u0[0] * u1[0] + u0[1] * u1[1]));
    const ang = Math.acos(cosA);
    if (ang > Math.PI - 0.05 || ang < 0.05) {
      out.push(cur);
      continue;
    }
    // Distance from the corner to where the arc touches the edges.
    let t = r / Math.tan(ang / 2);
    t = Math.min(t, l0 * 0.45, l1 * 0.45);
    const rr = t * Math.tan(ang / 2);
    const a: V2 = [cur[0] + u0[0] * t, cur[1] + u0[1] * t];
    const b: V2 = [cur[0] + u1[0] * t, cur[1] + u1[1] * t];
    const bis: V2 = [u0[0] + u1[0], u0[1] + u1[1]];
    const bl = Math.hypot(...bis);
    const cd = rr / Math.sin(ang / 2);
    const c: V2 = [cur[0] + (bis[0] / bl) * cd, cur[1] + (bis[1] / bl) * cd];
    const a0 = Math.atan2(a[1] - c[1], a[0] - c[0]);
    let a1 = Math.atan2(b[1] - c[1], b[0] - c[0]);
    let da = a1 - a0;
    while (da > Math.PI) da -= 2 * Math.PI;
    while (da < -Math.PI) da += 2 * Math.PI;
    for (let k = 0; k <= segs; k++) {
      const aa = a0 + (da * k) / segs;
      out.push([c[0] + Math.cos(aa) * rr, c[1] + Math.sin(aa) * rr]);
    }
    void a1;
  }
  return out;
}

/** Rounds the corners of an open polyline, each by its own radius (`radii[i]` for point i; 0 or none: sharp); the
 * ends stay where they are. */
export function filletEach(p: V2[], radii: number[], segs = 8): V2[] {
  const out: V2[] = [p[0]];
  for (let i = 1; i < p.length - 1; i++) {
    const r = radii[i] ?? 0;
    if (r <= 0) {
      out.push(p[i]);
      continue;
    }
    out.push(...cornerArc(p[i - 1], p[i], p[i + 1], r, segs));
  }
  out.push(p[p.length - 1]);
  return out;
}

/** The arc that rounds the corner at `cur` between the edges to `prev` and `next`. */
function cornerArc(prev: V2, cur: V2, next: V2, r: number, segs: number): V2[] {
  const d0: V2 = [prev[0] - cur[0], prev[1] - cur[1]];
  const d1: V2 = [next[0] - cur[0], next[1] - cur[1]];
  const l0 = Math.hypot(...d0);
  const l1 = Math.hypot(...d1);
  const u0: V2 = [d0[0] / l0, d0[1] / l0];
  const u1: V2 = [d1[0] / l1, d1[1] / l1];
  const ang = Math.acos(Math.max(-1, Math.min(1, u0[0] * u1[0] + u0[1] * u1[1])));
  if (ang > Math.PI - 0.01 || ang < 0.01) return [cur];
  let t = r / Math.tan(ang / 2);
  t = Math.min(t, l0 * 0.45, l1 * 0.45);
  const rr = t * Math.tan(ang / 2);
  const a: V2 = [cur[0] + u0[0] * t, cur[1] + u0[1] * t];
  const b: V2 = [cur[0] + u1[0] * t, cur[1] + u1[1] * t];
  const bis: V2 = [u0[0] + u1[0], u0[1] + u1[1]];
  const bl = Math.hypot(...bis);
  const cd = rr / Math.sin(ang / 2);
  const c: V2 = [cur[0] + (bis[0] / bl) * cd, cur[1] + (bis[1] / bl) * cd];
  const a0 = Math.atan2(a[1] - c[1], a[0] - c[0]);
  let da = Math.atan2(b[1] - c[1], b[0] - c[0]) - a0;
  while (da > Math.PI) da -= 2 * Math.PI;
  while (da < -Math.PI) da += 2 * Math.PI;
  return Array.from({ length: segs + 1 }, (_, k) => [c[0] + Math.cos(a0 + (da * k) / segs) * rr, c[1] + Math.sin(a0 + (da * k) / segs) * rr] as V2);
}

/** Rounds the inner corners of an open polyline with radius `r` (its ends stay where they are). */
export function filletOpen(p: V2[], r: number, segs = 8): V2[] {
  if (p.length < 3) return p.slice();
  const closed = fillet(p, r, segs);
  // fillet() treats the polyline as closed: drop what it made of the two end corners, keep the ends themselves.
  const out: V2[] = [p[0]];
  const inner = fillet([p[p.length - 1], ...p, p[0]].slice(1, -1), r, segs);
  void closed;
  // Points of `inner` that belong to corners 1..n-2: skip the arcs fillet() put at the ends (indices 0 and n-1).
  const n = p.length;
  let k = 0;
  for (let i = 0; i < n; i++) {
    const cornerPts: V2[] = [];
    // Each corner contributed either 1 point (straight) or segs + 1 points (an arc); find them by walking.
    const isArc = !(inner[k][0] === p[i][0] && inner[k][1] === p[i][1]);
    const count = isArc ? segs + 1 : 1;
    for (let j = 0; j < count; j++) cornerPts.push(inner[k + j]);
    k += count;
    if (i > 0 && i < n - 1) out.push(...cornerPts);
  }
  out.push(p[n - 1]);
  return out;
}

/** Moves every point of a closed polygon by `d` along its outward normal (negative: inwards). Mitred, fine for
 * gently bent outlines. */
export function offsetPoly(p: V2[], d: number): V2[] {
  const ccw = polyArea(p) > 0;
  const n = p.length;
  const out: V2[] = [];
  for (let i = 0; i < n; i++) {
    const a = p[(i + n - 1) % n];
    const b = p[i];
    const c = p[(i + 1) % n];
    const n1 = edgeNormal(a, b, ccw);
    const n2 = edgeNormal(b, c, ccw);
    let m: V2 = [n1[0] + n2[0], n1[1] + n2[1]];
    const ml = Math.hypot(...m) || 1;
    m = [m[0] / ml, m[1] / ml];
    const cosHalf = Math.max(0.3, m[0] * n1[0] + m[1] * n1[1]);
    out.push([b[0] + (m[0] * d) / cosHalf, b[1] + (m[1] * d) / cosHalf]);
  }
  return out;
}

function edgeNormal(a: V2, b: V2, ccw: boolean): V2 {
  const dx = b[0] - a[0];
  const dy = b[1] - a[1];
  const l = Math.hypot(dx, dy) || 1;
  // Outward of a counter-clockwise loop is to the right of the direction of travel.
  return ccw ? [dy / l, -dx / l] : [-dy / l, dx / l];
}

// ---- regions ---------------------------------------------------------------------------------------------------

export interface Region {
  pts: V2[];
  tris: number[];
  /** Every boundary loop as vertex indices in order: the outline counter-clockwise, then the holes clockwise. */
  loops: number[][];
}

/**
 * Triangulates an outline with holes into well-shaped triangles no longer than `maxEdge` at each edge's middle: the
 * outline triangulated, points seeded evenly inside it (Delaunay insertion: the outline's own triangles are long
 * slivers across the region, which splitting multiplies), then longest-edge bisection where the sizes ask for more,
 * and Delaunay flips again at the end. The mesh stays whole throughout.
 */
export function meshRegion(outer: V2[], holes: V2[][], maxEdge: (u: number, v: number) => number): Region {
  const o = oriented(outer, true);
  const hs = holes.map((h) => oriented(h, false));
  const pts: V2[] = [...o, ...hs.flat()];
  const faces = THREE.ShapeUtils.triangulateShape(o.map(([x, y]) => new THREE.Vector2(x, y)), hs.map((h) => h.map(([x, y]) => new THREE.Vector2(x, y))));
  const loops: number[][] = [];
  let start = 0;
  for (const l of [o, ...hs]) {
    loops.push(Array.from({ length: l.length }, (_, i) => start + i));
    start += l.length;
  }
  const mids = new Map<string, number>();
  const key = (a: number, b: number) => (a < b ? `${a},${b}` : `${b},${a}`);
  const T: [number, number, number][] = [];
  const alive: boolean[] = [];
  const onEdge = new Map<string, number[]>();
  const ccw = (a: number, b: number, c: number) => (pts[b][0] - pts[a][0]) * (pts[c][1] - pts[a][1]) - (pts[c][0] - pts[a][0]) * (pts[b][1] - pts[a][1]);
  const addTri = (a: number, b: number, c: number) => {
    if (ccw(a, b, c) < 0) [b, c] = [c, b];
    const id = T.length;
    T.push([a, b, c]);
    alive.push(true);
    for (const [p, q] of [[a, b], [b, c], [c, a]]) {
      const k = key(p, q);
      const l = onEdge.get(k);
      if (l) l.push(id);
      else onEdge.set(k, [id]);
    }
    return id;
  };
  const removeTri = (id: number) => {
    alive[id] = false;
    const [a, b, c] = T[id];
    for (const [p, q] of [[a, b], [b, c], [c, a]]) {
      const l = onEdge.get(key(p, q))!;
      l.splice(l.indexOf(id), 1);
    }
  };
  for (const [a, b, c] of faces) addTri(a, b, c);

  // ---- Delaunay flips ----
  const opposite = (id: number, p: number, q: number) => T[id].find((v) => v !== p && v !== q)!;
  const inCircle = (a: number, b: number, c: number, d: number) => {
    // > 0 when d is inside the circumcircle of the counter-clockwise triangle (a, b, c).
    const [ax, ay] = [pts[a][0] - pts[d][0], pts[a][1] - pts[d][1]];
    const [bx, by] = [pts[b][0] - pts[d][0], pts[b][1] - pts[d][1]];
    const [cx, cy] = [pts[c][0] - pts[d][0], pts[c][1] - pts[d][1]];
    return (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
  };
  const flip = (queue: string[]) => {
    const queued = new Set(queue);
    for (let guard = 0; queue.length && guard < 5_000_000; guard++) {
      const k = queue.pop()!;
      queued.delete(k);
      const l = onEdge.get(k);
      if (!l || l.length !== 2) continue;
      const [t1, t2] = l;
      const [p, q] = k.split(',').map(Number);
      const r = opposite(t1, p, q);
      const u = opposite(t2, p, q);
      const [a, b] = ccw(p, q, r) > 0 ? [p, q] : [q, p];
      if (inCircle(a, b, r, u) <= 1e-18) continue;
      // Only where the quad is convex (the other diagonal lies inside it).
      if (ccw(r, u, p) * ccw(r, u, q) >= 0) continue;
      removeTri(t1);
      removeTri(t2);
      addTri(r, u, p);
      addTri(u, r, q);
      for (const e of [key(p, r), key(r, q), key(q, u), key(u, p)]) {
        if (!queued.has(e)) {
          queued.add(e);
          queue.push(e);
        }
      }
    }
  };

  // ---- seed points inside, evenly, away from the edges ----
  const edges: [V2, V2][] = [];
  for (const l of [o, ...hs]) for (let i = 0; i < l.length; i++) edges.push([l[i], l[(i + 1) % l.length]]);
  const distToEdges = (x: number, y: number) => {
    let best = Infinity;
    for (const [a, b] of edges) {
      const dx = b[0] - a[0];
      const dy = b[1] - a[1];
      const t = Math.max(0, Math.min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy || 1)));
      best = Math.min(best, Math.hypot(x - a[0] - dx * t, y - a[1] - dy * t));
    }
    return best;
  };
  const insidePoly = (poly: V2[], x: number, y: number) => {
    let c = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
      const [xi, yi] = poly[i];
      const [xj, yj] = poly[j];
      if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) c = !c;
    }
    return c;
  };
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const [x, y] of o) {
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
  }
  const containing = (x: number, y: number) => {
    for (let id = T.length - 1; id >= 0; id--) {
      if (!alive[id]) continue;
      const [a, b, c] = T[id];
      const p = pts.length;
      pts.push([x, y]);
      const inside = ccw(a, b, p) >= 0 && ccw(b, c, p) >= 0 && ccw(c, a, p) >= 0;
      pts.pop();
      if (inside) return id;
    }
    return -1;
  };
  // Spacing: the region's coarse size, capped so a narrow region still gets a row or two.
  const span = Math.min(maxX - minX, maxY - minY);
  const step = Math.max(0.01, Math.min(0.05, span / 3));
  let row = 0;
  for (let y = minY + step / 2; y < maxY; y += step * 0.866, row++) {
    for (let x = minX + step / 2 + (row % 2 ? step / 2 : 0); x < maxX; x += step) {
      if (!insidePoly(o, x, y) || hs.some((h) => insidePoly(h, x, y))) continue;
      if (distToEdges(x, y) < step * 0.55) continue;
      const t = containing(x, y);
      if (t < 0) continue;
      const [a, b, c] = T[t];
      const m = pts.length;
      pts.push([x, y]);
      removeTri(t);
      addTri(a, b, m);
      addTri(b, c, m);
      addTri(c, a, m);
      flip([key(a, b), key(b, c), key(c, a)]);
    }
  }
  flip([...onEdge.keys()]);

  // ---- longest-edge bisection where the sizes ask for it ----
  const len2 = (a: number, b: number) => (pts[a][0] - pts[b][0]) ** 2 + (pts[a][1] - pts[b][1]) ** 2;
  const longest = (id: number): [number, number] => {
    const [a, b, c] = T[id];
    const ab = len2(a, b);
    const bc = len2(b, c);
    const ca = len2(c, a);
    if (ab >= bc && ab >= ca) return [a, b];
    return bc >= ca ? [b, c] : [c, a];
  };
  const across = (id: number, p: number, q: number) => onEdge.get(key(p, q))!.find((t) => t !== id) ?? -1;
  const splitEdge = (id: number, p: number, q: number) => {
    const n = across(id, p, q);
    const m = pts.length;
    pts.push([(pts[p][0] + pts[q][0]) / 2, (pts[p][1] + pts[q][1]) / 2]);
    mids.set(key(p, q), m);
    for (const t of n >= 0 ? [id, n] : [id]) {
      const cyc = T[t];
      removeTri(t);
      for (let i = 0; i < 3; i++) {
        const u = cyc[i];
        const v = cyc[(i + 1) % 3];
        const w = cyc[(i + 2) % 3];
        if ((u === p && v === q) || (u === q && v === p)) {
          addTri(u, m, w);
          addTri(m, v, w);
          break;
        }
      }
    }
  };
  const bisect = (first: number) => {
    const stack = [first];
    while (stack.length) {
      const t = stack[stack.length - 1];
      if (!alive[t]) {
        stack.pop();
        continue;
      }
      const [p, q] = longest(t);
      const n = across(t, p, q);
      if (n >= 0) {
        const [np, nq] = longest(n);
        if (key(np, nq) !== key(p, q)) {
          stack.push(n);
          continue;
        }
      }
      splitEdge(t, p, q);
      stack.pop();
    }
  };
  for (let id = 0; id < T.length; id++) {
    if (!alive[id]) continue;
    const [p, q] = longest(id);
    const mx = (pts[p][0] + pts[q][0]) / 2;
    const my = (pts[p][1] + pts[q][1]) / 2;
    if (Math.sqrt(len2(p, q)) > maxEdge(mx, my)) bisect(id);
  }
  flip([...onEdge.keys()]);

  const tris: number[] = [];
  for (let id = 0; id < T.length; id++) if (alive[id]) tris.push(...T[id]);
  const expand = (a: number, b: number, out: number[]) => {
    const m = mids.get(key(a, b));
    if (m === undefined) {
      out.push(a);
      return;
    }
    expand(a, m, out);
    expand(m, b, out);
  };
  const fullLoops = loops.map((l) => {
    const out: number[] = [];
    for (let i = 0; i < l.length; i++) expand(l[i], l[(i + 1) % l.length], out);
    return out;
  });
  return { pts, tris, loops: fullLoops };
}

// ---- sheets ------------------------------------------------------------------------------------------------------

export interface SheetOpts {
  /** How thick the sheet is (it grows against its normal). 0: a single surface. */
  thickness: number;
  /** Only the edge folded back (a hem) instead of a whole second face behind: the surface is drawn from both sides. */
  hem?: boolean;
  /** Roughly which way the outside faces at (u, v): the normals are turned to agree. */
  outward: (u: number, v: number) => V3;
  /** Step for the normals' finite differences, in (u, v) units. */
  h?: number;
}

export interface SheetGeo {
  outer: THREE.BufferGeometry;
  inner: THREE.BufferGeometry | null;
  rim: THREE.BufferGeometry | null;
}

/** The region mapped onto a surface: an outer face, an inner one `thickness` behind it, and the rim between them. */
export function sheet(r: Region, map: (u: number, v: number) => V3, o: SheetOpts): SheetGeo {
  const h = o.h ?? 1e-4;
  const P = r.pts.map(([u, v]) => map(u, v));
  const N = r.pts.map(([u, v]) => {
    const du = sub3(map(u + h, v), map(u - h, v));
    const dv = sub3(map(u, v + h), map(u, v - h));
    let n = norm3(cross3(du, dv));
    if (dot3(n, o.outward(u, v)) < 0) n = mul3(n, -1);
    return n;
  });
  // The outer face: the region's triangles are counter-clockwise in (u, v); flip them where that faces inwards.
  const flip = (() => {
    const [a, b, c] = [r.tris[0], r.tris[1], r.tris[2]];
    const fn = cross3(sub3(P[b], P[a]), sub3(P[c], P[a]));
    return dot3(fn, N[a]) < 0;
  })();
  const tri = (i: number) => (flip ? [r.tris[i], r.tris[i + 2], r.tris[i + 1]] : [r.tris[i], r.tris[i + 1], r.tris[i + 2]]);
  const outer = indexed(P, N, r.pts, (push) => {
    for (let i = 0; i < r.tris.length; i += 3) push(tri(i));
  });
  if (o.thickness <= 0) return { outer, inner: null, rim: null };
  const Pi = P.map((p, i) => sub3(p, mul3(N[i], o.thickness)));
  const Ni = N.map((n) => mul3(n, -1));
  const inner = o.hem ? null : indexed(Pi, Ni, r.pts, (push) => {
    for (let i = 0; i < r.tris.length; i += 3) {
      const [a, b, c] = tri(i);
      push([a, c, b]);
    }
  });
  // The rim: a quad per boundary edge, facing out of the region.
  const pos: number[] = [];
  const nor: number[] = [];
  for (const loop of r.loops) {
    for (let k = 0; k < loop.length; k++) {
      const a = loop[k];
      const b = loop[(k + 1) % loop.length];
      const t = norm3(sub3(P[b], P[a]));
      const na = norm3(cross3(t, N[a]));
      const nb = norm3(cross3(t, N[b]));
      // Loops run counter-clockwise round the outline and clockwise round holes, so t x N points out of the region
      // when the sheet's outer face is the region's front; else the other way.
      const s = flip ? -1 : 1;
      const qa = mul3(na, s);
      const qb = mul3(nb, s);
      const quad: [V3, V3][] = [
        [P[a], qa], [P[b], qb], [Pi[b], qb],
        [P[a], qa], [Pi[b], qb], [Pi[a], qa],
      ];
      // Wind the quad so its front faces along its normal.
      const fn = cross3(sub3(quad[1][0], quad[0][0]), sub3(quad[2][0], quad[0][0]));
      const order = dot3(fn, qa) >= 0 ? [0, 1, 2, 3, 4, 5] : [0, 2, 1, 3, 5, 4];
      for (const i of order) {
        pos.push(...quad[i][0]);
        nor.push(...quad[i][1]);
      }
    }
  }
  const rim = new THREE.BufferGeometry();
  rim.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  rim.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  return { outer, inner, rim };
}

function indexed(P: V3[], N: V3[], uv: V2[], build: (push: (t: number[]) => void) => void): THREE.BufferGeometry {
  const idx: number[] = [];
  build((t) => idx.push(...t));
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(P.flat(), 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(N.flat(), 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv.flat(), 2));
  g.setIndex(idx);
  return g;
}

// ---- grids -------------------------------------------------------------------------------------------------------

/**
 * A surface over a (u, v) grid, e.g. a glass pane or a bumper's face: `us` and `vs` are the grid lines, `map` puts each
 * point in 3D. Normals by finite differences, turned to agree with `outward`.
 */
export function gridSurface(us: number[], vs: number[], map: (u: number, v: number) => V3, outward: (u: number, v: number) => V3, h = 1e-4): THREE.BufferGeometry {
  const P: V3[] = [];
  const N: V3[] = [];
  const UV: V2[] = [];
  for (const v of vs)
    for (const u of us) {
      P.push(map(u, v));
      const du = sub3(map(u + h, v), map(u - h, v));
      const dv = sub3(map(u, v + h), map(u, v - h));
      let n = norm3(cross3(du, dv));
      if (dot3(n, outward(u, v)) < 0) n = mul3(n, -1);
      N.push(n);
      UV.push([u, v]);
    }
  const w = us.length;
  return indexed(P, N, UV, (push) => {
    for (let j = 0; j < vs.length - 1; j++)
      for (let i = 0; i < w - 1; i++) {
        const a = j * w + i;
        const b = a + 1;
        const c = a + w;
        const d = c + 1;
        // Wind each triangle to face along its corners' normals.
        const fn = cross3(sub3(P[b], P[a]), sub3(P[d], P[a]));
        if (dot3(fn, N[a]) >= 0) push([a, b, d, a, d, c]);
        else push([a, d, b, a, c, d]);
      }
  });
}

/** `n` values from a to b, spaced evenly (or bunched at the ends with `ease`). */
export function steps(a: number, b: number, n: number, ease = 0): number[] {
  return Array.from({ length: n + 1 }, (_, i) => {
    let t = i / n;
    if (ease > 0) t = t - (ease * Math.sin(2 * Math.PI * t)) / (2 * Math.PI);
    return a + (b - a) * t;
  });
}

// ---- sweeps ------------------------------------------------------------------------------------------------------

/**
 * A profile swept along a path: at every path point the profile's (x, y) go along the frame's `side` and `up`
 * vectors. The profile is a closed loop (a seal, a strip) unless `openProfile`; the path's ends are capped when the
 * profile is closed and the path open.
 */
export function sweep(path: V3[], frames: { side: V3; up: V3 }[], profile: V2[], o: { closedPath?: boolean; openProfile?: boolean; caps?: boolean } = {}): THREE.BufferGeometry {
  const np = profile.length;
  const P: V3[] = [];
  const N: V3[] = [];
  // Profile normals (2D), outward of a counter-clockwise profile.
  const prof = o.openProfile ? profile : oriented(profile, true);
  const pn: V2[] = prof.map((_, i) => {
    const a = prof[(i + np - 1) % np];
    const b = prof[(i + 1) % np];
    const dx = b[0] - a[0];
    const dy = b[1] - a[1];
    const l = Math.hypot(dx, dy) || 1;
    return [dy / l, -dx / l];
  });
  for (let i = 0; i < path.length; i++) {
    const { side, up } = frames[i];
    for (let k = 0; k < np; k++) {
      const [x, y] = prof[k];
      P.push(add3(path[i], add3(mul3(side, x), mul3(up, y))));
      N.push(norm3(add3(mul3(side, pn[k][0]), mul3(up, pn[k][1]))));
    }
  }
  const idx: number[] = [];
  const rows = o.closedPath ? path.length : path.length - 1;
  const cols = o.openProfile ? np - 1 : np;
  for (let i = 0; i < rows; i++) {
    const i2 = (i + 1) % path.length;
    for (let k = 0; k < cols; k++) {
      const k2 = (k + 1) % np;
      const a = i * np + k;
      const b = i * np + k2;
      const c = i2 * np + k;
      const d = i2 * np + k2;
      const fn = cross3(sub3(P[c], P[a]), sub3(P[b], P[a]));
      if (dot3(fn, N[a]) >= 0) idx.push(a, c, b, b, c, d);
      else idx.push(a, b, c, b, d, c);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(P.flat(), 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(N.flat(), 3));
  g.setIndex(idx);
  if (o.closedPath || o.openProfile || o.caps === false) return g;
  // Caps: flat, facing back along the path at the start and forward at the end.
  const capPos: number[] = [];
  const capNor: number[] = [];
  const tri2 = THREE.ShapeUtils.triangulateShape(prof.map(([x, y]) => new THREE.Vector2(x, y)), []);
  for (const end of [0, path.length - 1]) {
    const dir = norm3(end === 0 ? sub3(path[0], path[1]) : sub3(path[end], path[end - 1]));
    for (const t of tri2) {
      const pts = t.map((k) => P[end * np + k]);
      const fn = cross3(sub3(pts[1], pts[0]), sub3(pts[2], pts[0]));
      const ordered = dot3(fn, dir) >= 0 ? pts : [pts[0], pts[2], pts[1]];
      for (const p of ordered) {
        capPos.push(...p);
        capNor.push(...dir);
      }
    }
  }
  const caps = new THREE.BufferGeometry();
  caps.setAttribute('position', new THREE.Float32BufferAttribute(capPos, 3));
  caps.setAttribute('normal', new THREE.Float32BufferAttribute(capNor, 3));
  return mergeInto(g, caps);
}

/** Two geometries as one (both made non-indexed). */
export function mergeInto(a: THREE.BufferGeometry, b: THREE.BufferGeometry): THREE.BufferGeometry {
  const A = a.index ? a.toNonIndexed() : a;
  const B = b.index ? b.toNonIndexed() : b;
  const g = new THREE.BufferGeometry();
  for (const name of ['position', 'normal'] as const) {
    const x = A.getAttribute(name).array as Float32Array;
    const y = B.getAttribute(name).array as Float32Array;
    const z = new Float32Array(x.length + y.length);
    z.set(x, 0);
    z.set(y, x.length);
    g.setAttribute(name, new THREE.Float32BufferAttribute(z, 3));
  }
  return g;
}

/** Frames for a path lying on a surface: `up` is the surface's normal there, `side` across the path along the surface. */
export function surfaceFrames(path: V3[], normalAt: (i: number) => V3, closed: boolean): { side: V3; up: V3 }[] {
  return path.map((_, i) => {
    const prev = path[closed ? (i + path.length - 1) % path.length : Math.max(0, i - 1)];
    const next = path[closed ? (i + 1) % path.length : Math.min(path.length - 1, i + 1)];
    const t = norm3(sub3(next, prev));
    const up = normalAt(i);
    const side = norm3(cross3(t, up));
    return { side, up: norm3(cross3(side, t)) };
  });
}
