// Tiny RGBA raster + PNG encoder for generated textures and decals.

import { deflateSync } from 'node:zlib';

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Uint8Array): Uint8Array {
  const out = new Uint8Array(12 + data.length);
  const dv = new DataView(out.buffer);
  dv.setUint32(0, data.length);
  for (let i = 0; i < 4; i++) out[4 + i] = type.charCodeAt(i);
  out.set(data, 8);
  dv.setUint32(8 + data.length, crc32(out.subarray(4, 8 + data.length)));
  return out;
}

export function encodePng(w: number, h: number, rgba: Uint8Array): Uint8Array {
  const ihdr = new Uint8Array(13);
  const dv = new DataView(ihdr.buffer);
  dv.setUint32(0, w);
  dv.setUint32(4, h);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  const raw = new Uint8Array(h * (w * 4 + 1));
  for (let y = 0; y < h; y++) {
    raw[y * (w * 4 + 1)] = 0;
    raw.set(rgba.subarray(y * w * 4, (y + 1) * w * 4), y * (w * 4 + 1) + 1);
  }
  const idat = deflateSync(raw, { level: 9 });
  const sig = new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10]);
  const parts = [sig, chunk('IHDR', ihdr), chunk('IDAT', new Uint8Array(idat)), chunk('IEND', new Uint8Array(0))];
  const total = parts.reduce((a, p) => a + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.length;
  }
  return out;
}

export type RGBA = [number, number, number, number];

export function hexColor(hex: number, a = 1): RGBA {
  return [((hex >> 16) & 255) / 255, ((hex >> 8) & 255) / 255, (hex & 255) / 255, a];
}

/** Float RGBA raster (0..1, straight alpha). */
export class Raster {
  data: Float32Array;
  w: number;
  h: number;
  constructor(w: number, h: number, fill: RGBA = [0, 0, 0, 0]) {
    this.w = w;
    this.h = h;
    this.data = new Float32Array(w * h * 4);
    for (let i = 0; i < w * h; i++) this.data.set(fill, i * 4);
  }

  get(x: number, y: number): RGBA {
    const i = (y * this.w + x) * 4;
    return [this.data[i], this.data[i + 1], this.data[i + 2], this.data[i + 3]];
  }

  set(x: number, y: number, c: RGBA): void {
    this.data.set(c, (y * this.w + x) * 4);
  }

  /** Alpha-blend color c with coverage a onto pixel (x, y). */
  blend(x: number, y: number, c: RGBA, a: number): void {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h || a <= 0) return;
    const i = (y * this.w + x) * 4;
    const sa = c[3] * a;
    const da = this.data[i + 3];
    const oa = sa + da * (1 - sa);
    if (oa <= 0) return;
    for (let k = 0; k < 3; k++) {
      this.data[i + k] = (c[k] * sa + this.data[i + k] * da * (1 - sa)) / oa;
    }
    this.data[i + 3] = oa;
  }

  /** Per-pixel shader. */
  shade(fn: (x: number, y: number, cur: RGBA) => RGBA): void {
    for (let y = 0; y < this.h; y++) for (let x = 0; x < this.w; x++) this.set(x, y, fn(x, y, this.get(x, y)));
  }

  /** Fill an antialiased shape given by an inside() test (3x3 supersampling). */
  fillShape(inside: (x: number, y: number) => boolean, c: RGBA, bbox?: [number, number, number, number]): void {
    const [x0, y0, x1, y1] = bbox ?? [0, 0, this.w - 1, this.h - 1];
    const ss = 3;
    for (let y = Math.max(0, Math.floor(y0)); y <= Math.min(this.h - 1, Math.ceil(y1)); y++) {
      for (let x = Math.max(0, Math.floor(x0)); x <= Math.min(this.w - 1, Math.ceil(x1)); x++) {
        let hits = 0;
        for (let sy = 0; sy < ss; sy++)
          for (let sx = 0; sx < ss; sx++) if (inside(x + (sx + 0.5) / ss, y + (sy + 0.5) / ss)) hits++;
        if (hits) this.blend(x, y, c, hits / (ss * ss));
      }
    }
  }

  fillPolygon(pts: [number, number][], c: RGBA): void {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const [x, y] of pts) {
      x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y);
    }
    this.fillShape((x, y) => pointInPolygon(x, y, pts), c, [x0, y0, x1, y1]);
  }

  fillCircle(cx: number, cy: number, r: number, c: RGBA): void {
    this.fillShape((x, y) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r, c, [cx - r, cy - r, cx + r, cy + r]);
  }

  fillRect(x: number, y: number, w: number, h: number, c: RGBA): void {
    this.fillShape((px, py) => px >= x && px <= x + w && py >= y && py <= y + h, c, [x, y, x + w, y + h]);
  }

  toPng(): Uint8Array {
    const out = new Uint8Array(this.w * this.h * 4);
    for (let i = 0; i < out.length; i++) out[i] = Math.max(0, Math.min(255, Math.round(this.data[i] * 255)));
    return encodePng(this.w, this.h, out);
  }
}

export function pointInPolygon(x: number, y: number, pts: [number, number][]): boolean {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i];
    const [xj, yj] = pts[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

// ---- tileable value noise -------------------------------------------------

function hash2(x: number, y: number, seed: number): number {
  let h = (x * 374761393 + y * 668265263 + seed * 2147483647) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967295;
}

/** Tileable value noise in [0,1]. `period` is the lattice size (in cells) that wraps. */
export function valueNoise(u: number, v: number, period: number, seed: number): number {
  const x = u * period;
  const y = v * period;
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const fx = x - xi;
  const fy = y - yi;
  const w = (i: number) => ((i % period) + period) % period;
  const a = hash2(w(xi), w(yi), seed);
  const b = hash2(w(xi + 1), w(yi), seed);
  const c = hash2(w(xi), w(yi + 1), seed);
  const d = hash2(w(xi + 1), w(yi + 1), seed);
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
}

export function fbm(u: number, v: number, basePeriod: number, octaves: number, seed: number): number {
  let sum = 0;
  let amp = 0.5;
  let norm = 0;
  let period = basePeriod;
  for (let o = 0; o < octaves; o++) {
    sum += amp * valueNoise(u, v, period, seed + o * 17);
    norm += amp;
    amp *= 0.5;
    period *= 2;
  }
  return sum / norm;
}
