// Placeholder textures, decals, sounds, icons, fastener models and props.

import * as THREE from 'three';
import { Raster, hexColor, fbm, valueNoise, type RGBA } from './png.ts';
import { buffer, addTone, addNoise, normalize, encodeWav, makeNoise, RATE } from './wav.ts';
import { ModelBuilder, M } from './geo.ts';
import { EXTRA } from './parts.ts';

// ---------------------------------------------------------------------------
// Textures
// ---------------------------------------------------------------------------

const mix = (a: RGBA, b: RGBA, t: number): RGBA => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3] + (b[3] - a[3]) * t];
const shadeC = (c: RGBA, k: number): RGBA => [c[0] * k, c[1] * k, c[2] * k, c[3]];

export function textures(): Record<string, Uint8Array> {
  const N = 512;
  const out: Record<string, Uint8Array> = {};

  const concrete = new Raster(N, N);
  concrete.shade((x, y) => {
    const u = x / N, v = y / N;
    const n = fbm(u, v, 8, 5, 3);
    const stain = fbm(u, v, 3, 3, 11);
    const speck = valueNoise(u, v, 128, 5) > 0.93 ? 0.08 : 0;
    let c = mix(hexColor(0x8d8c87), hexColor(0x6f6e69), n);
    if (stain > 0.62) c = mix(c, hexColor(0x3a3833), Math.min(1, (stain - 0.62) * 4));
    c = shadeC(c, 1 - speck);
    const edge = Math.min(x, y, N - 1 - x, N - 1 - y);
    if (edge < 2) c = shadeC(c, 0.72);
    return c;
  });
  out['tex.concrete'] = concrete.toPng();

  const wall = new Raster(N, N);
  wall.shade((x, y) => {
    const u = x / N, v = y / N;
    const row = Math.floor(y / 64);
    const bx = (x + (row % 2) * 64) % 128;
    const by = y % 64;
    const mortar = bx < 4 || by < 4;
    const n = fbm(u, v, 16, 4, 21);
    const base = mortar ? hexColor(0x9ea39a) : hexColor(0xb9c2b4);
    return shadeC(base, 0.9 + n * 0.14);
  });
  out['tex.wall'] = wall.toPng();

  const asphalt = new Raster(N, N);
  asphalt.shade((x, y) => {
    const u = x / N, v = y / N;
    const n = fbm(u, v, 32, 4, 41);
    const speck = valueNoise(u, v, 256, 9) > 0.9 ? 0.12 : 0;
    const c = mix(hexColor(0x2e2f31), hexColor(0x3d3e40), n);
    return [c[0] + speck, c[1] + speck, c[2] + speck, 1];
  });
  out['tex.asphalt'] = asphalt.toPng();

  const peg = new Raster(256, 256, hexColor(0xa0835e));
  peg.shade((x, y) => shadeC(hexColor(0xa0835e), 0.92 + fbm(x / 256, y / 256, 8, 3, 7) * 0.12));
  for (let y = 16; y < 256; y += 32) for (let x = 16; x < 256; x += 32) peg.fillCircle(x, y, 4, hexColor(0x2a2118));
  out['tex.pegboard'] = peg.toPng();

  return out;
}

// ---------------------------------------------------------------------------
// Decals (RGBA, transparent background)
// ---------------------------------------------------------------------------

function polyStar(cx: number, cy: number, r0: number, r1: number, n: number, rot = -Math.PI / 2): [number, number][] {
  const pts: [number, number][] = [];
  for (let i = 0; i < n * 2; i++) {
    const r = i % 2 === 0 ? r0 : r1;
    const a = rot + (i * Math.PI) / n;
    pts.push([cx + Math.cos(a) * r, cy + Math.sin(a) * r]);
  }
  return pts;
}

export function decals(): Record<string, Uint8Array> {
  const out: Record<string, Uint8Array> = {};

  // Flames: tongues licking to the right
  const fl = new Raster(512, 256);
  const tongues = [
    { y: 128, len: 470, w: 60, ph: 0 },
    { y: 70, len: 360, w: 42, ph: 1.3 },
    { y: 186, len: 380, w: 44, ph: 2.1 },
    { y: 30, len: 230, w: 26, ph: 0.6 },
    { y: 226, len: 250, w: 28, ph: 2.8 },
  ];
  const layers: [number, RGBA][] = [[1, hexColor(0xc41a10)], [0.72, hexColor(0xf06a12)], [0.42, hexColor(0xffd23a)]];
  for (const [scale, color] of layers) {
    fl.fillShape((x, y) => {
      for (const t of tongues) {
        if (x > t.len) continue;
        const k = x / t.len;
        const center = t.y + Math.sin(k * 5 + t.ph) * 18 * k;
        const half = t.w * scale * Math.pow(1 - k, 0.7) * (1 + 0.25 * Math.sin(k * 14 + t.ph));
        if (Math.abs(y - center) < half) return true;
      }
      return false;
    }, color);
  }
  out['tex.decal.flames'] = fl.toPng();

  const st = new Raster(512, 256);
  st.fillRect(0, 70, 512, 42, hexColor(0xf4f4f0));
  st.fillRect(0, 144, 512, 42, hexColor(0xf4f4f0));
  st.fillRect(0, 124, 512, 8, hexColor(0xc41a10));
  out['tex.decal.stripes'] = st.toPng();

  const star = new Raster(256, 256);
  star.fillPolygon(polyStar(128, 134, 120, 50, 5), hexColor(0x1b1b1b));
  star.fillPolygon(polyStar(128, 134, 104, 43, 5), hexColor(0xf2c230));
  out['tex.decal.star'] = star.toPng();

  const num = new Raster(256, 256);
  num.fillCircle(128, 128, 122, hexColor(0x111111));
  num.fillCircle(128, 128, 112, hexColor(0xf4f4f0));
  num.fillPolygon([[70, 60], [190, 60], [190, 88], [130, 206], [96, 206], [152, 90], [70, 90]], hexColor(0x111111));
  out['tex.decal.number'] = num.toPng();

  const ch = new Raster(384, 256);
  ch.fillShape((x, y) => {
    const wave = Math.sin((x / 384) * Math.PI * 2) * 14;
    const yy = y - wave;
    if (yy < 20 || yy > 236) return false;
    const cx = Math.floor(x / 48);
    const cy = Math.floor((yy - 20) / 54);
    return (cx + cy) % 2 === 0;
  }, hexColor(0x111111));
  ch.fillShape((x, y) => {
    const wave = Math.sin((x / 384) * Math.PI * 2) * 14;
    const yy = y - wave;
    if (yy < 20 || yy > 236) return false;
    const cx = Math.floor(x / 48);
    const cy = Math.floor((yy - 20) / 54);
    return (cx + cy) % 2 === 1;
  }, hexColor(0xf4f4f0));
  out['tex.decal.checker'] = ch.toPng();

  const bolt = new Raster(512, 256);
  const bp: [number, number][] = [[20, 150], [250, 60], [230, 120], [490, 60], [260, 200], [290, 140], [20, 150]];
  bolt.fillPolygon(bp.map(([x, y]) => [x, y + 4]), hexColor(0x1b1b1b));
  bolt.fillPolygon(bp, hexColor(0xf2d21a));
  out['tex.decal.bolt'] = bolt.toPng();

  return out;
}

// ---------------------------------------------------------------------------
// Sounds
// ---------------------------------------------------------------------------

export function sounds(): Record<string, Uint8Array> {
  const out: Record<string, Uint8Array> = {};
  const put = (id: string, buf: Float32Array) => (out[id] = encodeWav(normalize(buf)));

  {
    const b = buffer(0.42);
    for (let i = 0; i < 8; i++) {
      addNoise(b, i * 0.048, 0.02, 0.8, 0.004, 0.9, 10 + i, true);
      addTone(b, i * 0.048, 3200 + (i % 2) * 400, 0.25, 0.006, 0.02);
    }
    put('snd.ratchet', b);
  }
  {
    const b = buffer(0.65);
    const rnd = makeNoise(3);
    for (let i = 0; i < b.length; i++) {
      const t = i / RATE;
      const env = Math.min(1, t * 30) * Math.min(1, (0.65 - t) * 12);
      const hammer = Math.pow(0.5 + 0.5 * Math.sin(2 * Math.PI * 28 * t), 6);
      b[i] = env * (0.5 * Math.sin(2 * Math.PI * (180 + 40 * Math.sin(t * 6)) * t) * (0.4 + hammer) + 0.35 * rnd() * (0.3 + hammer));
    }
    put('snd.impact', b);
  }
  {
    const b = buffer(0.4);
    const rnd = makeNoise(4);
    let lp = 0;
    for (let i = 0; i < b.length; i++) {
      const t = i / RATE;
      lp += (rnd() - lp) * 0.2;
      b[i] = lp * (0.6 + 0.4 * Math.sin(2 * Math.PI * 9 * t)) * Math.min(1, (0.4 - t) * 10);
    }
    addTone(b, 0, 900, 0.1, 0.3, 0.4, 700);
    put('snd.screwdriver', b);
  }
  {
    const b = buffer(0.16);
    addNoise(b, 0, 0.03, 1, 0.005, 0.95, 5, true);
    addTone(b, 0, 2400, 0.4, 0.01, 0.05);
    addTone(b, 0.02, 180, 0.5, 0.03, 0.1);
    put('snd.clip', b);
  }
  {
    const b = buffer(0.4);
    addTone(b, 0, 95, 1, 0.08, 0.4, 70);
    addNoise(b, 0, 0.1, 0.6, 0.03, 0.2, 8);
    addTone(b, 0.01, 1300, 0.15, 0.06, 0.2);
    put('snd.part_off', b);
  }
  {
    const b = buffer(0.35);
    addTone(b, 0, 140, 0.9, 0.06, 0.3, 110);
    addNoise(b, 0, 0.06, 0.5, 0.02, 0.3, 9);
    addNoise(b, 0.12, 0.03, 0.6, 0.006, 0.9, 12, true);
    put('snd.part_on', b);
  }
  {
    const b = buffer(0.7);
    for (const [f, a, d] of [[2100, 0.6, 0.25], [3420, 0.4, 0.18], [5230, 0.25, 0.12], [7100, 0.15, 0.08]] as const) addTone(b, 0, f, a, d, 0.7);
    addNoise(b, 0, 0.01, 0.5, 0.003, 0.9, 2, true);
    put('snd.bolt_drop', b);
  }
  {
    const b = buffer(0.3);
    for (const s of [0, 0.15]) {
      const n0 = Math.floor(s * RATE);
      for (let i = 0; i < 0.11 * RATE; i++) b[n0 + i] += Math.sign(Math.sin((2 * Math.PI * 150 * i) / RATE)) * 0.4;
    }
    put('snd.error', b);
  }
  {
    const b = buffer(0.9);
    addNoise(b, 0, 0.05, 0.6, 0.015, 0.7, 13, true);
    addTone(b, 0.05, 1760, 0.6, 0.35, 0.85);
    addTone(b, 0.05, 2640, 0.4, 0.3, 0.85);
    addTone(b, 0.05, 3520, 0.2, 0.2, 0.85);
    put('snd.cash', b);
  }
  {
    const b = buffer(0.7);
    const rnd = makeNoise(6);
    let lp = 0;
    for (let i = 0; i < b.length; i++) {
      const t = i / RATE;
      const x = rnd();
      lp += (x - lp) * 0.35;
      b[i] = (x - lp) * Math.min(1, t * 20) * Math.min(1, (0.7 - t) * 8);
    }
    put('snd.spray', b);
  }
  {
    const b = buffer(0.05);
    addNoise(b, 0, 0.01, 1, 0.002, 0.9, 1, true);
    addTone(b, 0, 1800, 0.3, 0.01, 0.03);
    put('snd.click', b);
  }
  {
    const b = buffer(0.7);
    addTone(b, 0, 320, 0.35, 0.3, 0.45, 210);
    addNoise(b, 0, 0.45, 0.15, 0.3, 0.3, 17);
    addTone(b, 0.45, 110, 0.9, 0.05, 0.25);
    addNoise(b, 0.45, 0.08, 0.6, 0.02, 0.3, 18);
    put('snd.hood', b);
  }
  {
    const b = buffer(1.1);
    for (let i = 0; i < b.length; i++) {
      const t = i / RATE;
      const gate = t < 0.45 || (t > 0.6 && t < 1.05) ? 1 : 0;
      const am = 0.5 + 0.5 * Math.sign(Math.sin(2 * Math.PI * 20 * t));
      b[i] = gate * am * (Math.sin(2 * Math.PI * 440 * t) + Math.sin(2 * Math.PI * 480 * t)) * 0.4;
    }
    put('snd.phone', b);
  }
  {
    const b = buffer(0.55);
    for (const s of [0, 0.22]) {
      addTone(b, s, 820, 0.7, 0.03, 0.2);
      addTone(b, s, 1350, 0.3, 0.02, 0.2);
      addNoise(b, s, 0.03, 0.8, 0.01, 0.5, 20, true);
    }
    put('snd.gavel', b);
  }
  // ---- engine: starter and running sounds (Start Engine diagnosis) ----
  const cranking = (b: Float32Array, start: number, dur: number, rate: number, strength: number) => {
    const rnd = makeNoise(31);
    const s0 = Math.floor(start * RATE);
    for (let i = 0; i < dur * RATE && s0 + i < b.length; i++) {
      const t = i / RATE;
      const pulse = Math.pow(0.5 + 0.5 * Math.sin(2 * Math.PI * rate * t), 3);
      b[s0 + i] += (0.5 * Math.sin(2 * Math.PI * 110 * t) + 0.4 * rnd()) * (0.3 + 0.7 * pulse) * strength;
    }
  };
  const idle = (b: Float32Array, start: number, dur: number, f: number, rough: number, loud: number, seed: number) => {
    const rnd = makeNoise(seed);
    const s0 = Math.floor(start * RATE);
    let lp = 0;
    for (let i = 0; i < dur * RATE && s0 + i < b.length; i++) {
      const t = i / RATE;
      const fire = Math.pow(0.5 + 0.5 * Math.sin(2 * Math.PI * f * t), 4);
      const miss = rough > 0 && Math.sin(2 * Math.PI * 1.7 * t) > 1 - rough ? 0.1 : 1;
      const n = rnd();
      lp += (n - lp) * (loud ? 0.6 : 0.15);
      const env = Math.min(1, t * 6) * Math.min(1, (dur - t) * 4);
      b[s0 + i] += env * miss * (0.6 * fire * Math.sin(2 * Math.PI * f * 2 * t) + (0.35 + loud * 0.5) * lp * fire);
      if (rough > 0 && Math.sin(2 * Math.PI * 3.1 * t) > 0.97) b[s0 + i] += 0.5 * Math.sin(2 * Math.PI * 600 * t);
    }
  };
  {
    const b = buffer(0.5);
    addNoise(b, 0, 0.02, 1, 0.005, 0.9, 40, true);
    addTone(b, 0, 1900, 0.4, 0.01, 0.05);
    addNoise(b, 0.25, 0.02, 1, 0.005, 0.9, 41, true);
    put('snd.engine_click', b);
  }
  {
    const b = buffer(1.8);
    addNoise(b, 0, 0.02, 1, 0.005, 0.9, 42, true);
    cranking(b, 0.05, 1.7, 5, 0.8);
    put('snd.engine_crank', b);
  }
  {
    const b = buffer(3.2);
    cranking(b, 0.05, 0.8, 5.5, 0.8);
    idle(b, 0.75, 2.4, 13, 0, 0, 43);
    put('snd.engine_start', b);
  }
  {
    const b = buffer(3.2);
    cranking(b, 0.05, 0.9, 5, 0.8);
    idle(b, 0.85, 2.3, 11, 0.25, 0, 44);
    put('snd.engine_rough', b);
  }
  {
    const b = buffer(3.2);
    cranking(b, 0.05, 0.7, 5.5, 0.8);
    idle(b, 0.7, 2.5, 13, 0, 1, 45);
    put('snd.engine_loud', b);
  }
  {
    const b = buffer(0.35);
    addNoise(b, 0, 0.05, 1, 0.01, 0.8, 46, true);
    addNoise(b, 0.12, 0.08, 0.6, 0.02, 0.5, 47, true);
    addTone(b, 0.12, 2400, 0.2, 0.02, 0.05);
    put('snd.camera', b);
  }
  {
    const b = buffer(1.2);
    [523, 659, 784, 1047].forEach((f, i) => addTone(b, i * 0.12, f, 0.5, 0.35, 0.8));
    put('snd.fanfare', b);
  }
  return out;
}

// ---------------------------------------------------------------------------
// Icons (SVG, 64x64)
// ---------------------------------------------------------------------------

const svg = (body: string) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="#ece8df" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;
const Y = '#f2b01e';

export function icons(): Record<string, string> {
  return {
    'icon.tool.hands': svg(`<path d="M22 34V16a4 4 0 0 1 8 0v14M30 28V12a4 4 0 0 1 8 0v18M38 28V16a4 4 0 0 1 8 0v20c0 12-7 20-17 20-8 0-12-4-16-12l-5-9a4 4 0 0 1 7-4l4 6"/>`),
    'icon.tool.ratchet': svg(`<circle cx="18" cy="18" r="9"/><circle cx="18" cy="18" r="3" fill="${Y}" stroke="none"/><path d="M25 25l27 27" stroke-width="7"/><path d="M25 25l27 27" stroke="${Y}" stroke-width="2"/>`),
    'icon.tool.impact': svg(`<path d="M10 22h26v14H10z"/><path d="M36 26h10v6H36z" fill="${Y}"/><path d="M18 36l-4 18h12l2-18"/><path d="M46 29h8"/><path d="M4 26h6M4 32h6"/>`),
    'icon.tool.screwdriver': svg(`<path d="M40 10l14 14-8 8-14-14z" fill="${Y}" stroke="${Y}"/><path d="M32 26L12 46l-2 8 8-2 20-20"/>`),
    'icon.tool.pliers': svg(`<path d="M24 24l-10-14M40 24l10-14"/><path d="M24 24c-2 8-4 16-12 30M40 24c2 8 4 16 12 30"/><circle cx="32" cy="26" r="4" fill="${Y}" stroke="none"/>`),
    'icon.tool.plug_socket': svg(`<rect x="24" y="6" width="16" height="20" rx="2"/><path d="M26 26h12v8H26z" fill="${Y}"/><path d="M32 34v22"/><path d="M28 56h8"/>`),
    'icon.tool.inspect': svg(`<circle cx="26" cy="26" r="15"/><path d="M37 37l17 17" stroke-width="6"/><path d="M20 22a8 8 0 0 1 8-5" stroke="${Y}"/>`),
    'icon.tool.paint': svg(`<rect x="18" y="20" width="24" height="36" rx="4"/><path d="M24 20v-6h12v6"/><path d="M30 8h6" stroke="${Y}"/><path d="M42 10l8-4M42 14l10 0M42 18l8 4" stroke="${Y}" stroke-width="2.5"/>`),
    'icon.tool.decal': svg(`<path d="M12 12h40v28L40 52H12z"/><path d="M40 52V40h12" fill="${Y}"/><path d="M22 24l6 6 12-12" stroke="${Y}"/>`),
    'icon.nav.workshop': svg(`<path d="M6 30L32 10l26 20"/><path d="M12 26v30h40V26"/><path d="M20 56V38h24v18" stroke="${Y}"/><path d="M20 44h24M20 50h24" stroke="${Y}" stroke-width="2"/>`),
    'icon.nav.lot': svg(`<path d="M8 40l6-14h36l6 14v10H8z"/><circle cx="18" cy="50" r="5" fill="${Y}" stroke="${Y}"/><circle cx="46" cy="50" r="5" fill="${Y}" stroke="${Y}"/><path d="M18 26l4-8h20l4 8"/>`),
    'icon.nav.auction': svg(`<path d="M30 12l14 14-8 8-14-14z" fill="${Y}" stroke="${Y}"/><path d="M34 30L14 50"/><path d="M8 58h28"/>`),
    'icon.nav.catalog': svg(`<path d="M12 10h30a8 8 0 0 1 8 8v36H20a8 8 0 0 1-8-8z"/><path d="M12 46a8 8 0 0 1 8-8h30"/><path d="M22 20h18M22 28h12" stroke="${Y}"/>`),
    'icon.nav.office': svg(`<path d="M14 12c-4 4-4 12 4 24s16 16 22 16l6-6-8-8-6 4c-4-2-10-8-12-12l4-6-8-8z" fill="${Y}" stroke="${Y}"/><path d="M40 10a14 14 0 0 1 14 14M40 18a6 6 0 0 1 6 6"/>`),
    'icon.nav.bench': svg(`<path d="M6 24h52v8H6z"/><path d="M12 32v24M52 32v24"/><path d="M18 24v-8h10v8" stroke="${Y}"/><path d="M36 24l6-10 6 10" stroke="${Y}"/>`),
    'icon.ui.money': svg(`<rect x="6" y="16" width="52" height="32" rx="4"/><circle cx="32" cy="32" r="8" stroke="${Y}"/><path d="M14 24v16M50 24v16"/>`),
    'icon.ui.day': svg(`<circle cx="32" cy="32" r="12" fill="${Y}" stroke="${Y}"/><path d="M32 6v8M32 50v8M6 32h8M50 32h8M13 13l6 6M45 45l6 6M13 51l6-6M45 19l6-6"/>`),
    'icon.ui.save': svg(`<path d="M10 10h36l8 8v36H10z"/><path d="M20 10v14h22V10"/><rect x="18" y="34" width="28" height="14" stroke="${Y}"/>`),
    'icon.tool.camera': svg(`<rect x="8" y="20" width="48" height="32" rx="4"/><path d="M22 20l4-7h12l4 7"/><circle cx="32" cy="36" r="9" stroke="${Y}"/>`),
    'icon.tool.key': svg(`<circle cx="20" cy="32" r="10"/><circle cx="20" cy="32" r="3" fill="${Y}" stroke="none"/><path d="M30 32h26M48 32v8M54 32v6"/>`),
    'icon.ui.repair': svg(`<path d="M40 8a12 12 0 0 0-10 17L10 45a5 5 0 0 0 7 7l20-20a12 12 0 0 0 17-10l-7 7-6-1-1-6z" fill="${Y}" stroke="${Y}" stroke-width="2"/>`),
    'icon.ui.scrap': svg(`<path d="M10 22h44l-4 32H14z"/><path d="M6 22h52M24 22v-8h16v8"/><path d="M24 32l4 14M40 32l-4 14" stroke="${Y}"/>`),
    'icon.ui.condition': svg(`<circle cx="22" cy="22" r="9" fill="#5dd67a" stroke="none"/><circle cx="42" cy="22" r="9" fill="#e8c547" stroke="none"/><circle cx="22" cy="42" r="9" fill="#e0483a" stroke="none"/><circle cx="42" cy="42" r="9" fill="#222" stroke="#ece8df" stroke-width="2"/>`),
    'icon.nav.junkyard': svg(`<path d="M6 54h52"/><path d="M10 54l6-18 10 6 8-16 8 12 8-6 6 22" fill="${Y}" fill-opacity="0.25"/><circle cx="20" cy="48" r="5"/><circle cx="44" cy="48" r="5"/>`),
    'icon.nav.job': svg(`<rect x="14" y="10" width="36" height="46" rx="3"/><rect x="24" y="6" width="16" height="8" rx="2" fill="${Y}" stroke="${Y}"/><path d="M22 26h20M22 34h20M22 42h12"/>`),
    'icon.nav.exit': svg(`<path d="M26 10H12v44h14"/><path d="M28 32h26M44 22l10 10-10 10" stroke="${Y}"/>`),
  };
}

// ---------------------------------------------------------------------------
// Fastener models (unit size: head radius 1, +Y = unscrew direction)
// ---------------------------------------------------------------------------

export function fastenerModels(): Record<string, ModelBuilder> {
  // The original draws bolts as small white cubes. Ours: a white cube with a washer.
  const boltWhite = { name: 'bolt_white', color: [0.85, 0.85, 0.82] as [number, number, number], metallic: 0.2, roughness: 0.45 };
  // Plain geometry, not the builder's bevelled boxes and chamfered cylinders: the original's look draws these too, and
  // its cubes stay as they were.
  const cyl = (r: number, h: number, segs: number) => new THREE.CylinderGeometry(r, r, h, segs);
  const cube = new ModelBuilder().add(new THREE.BoxGeometry(1.6, 1.4, 1.6), boltWhite, { p: [0, 0.7, 0] }).add(cyl(1.25, 0.15, 20), M.steel, { p: [0, 0.075, 0] });
  const hex = new ModelBuilder().add(cyl(1, 0.7, 6), M.steel, { p: [0, 0.35, 0] }).add(cyl(1.25, 0.12, 20), M.steel, { p: [0, 0.06, 0] });
  const lug = new ModelBuilder().add(cyl(1, 0.9, 6), M.chrome, { p: [0, 0.45, 0] }).lathe([[0.95, 0.9], [0.7, 1.2], [0.0, 1.3]], M.chrome, {}, { segs: 12 });
  return { 'fastener.cube': cube, 'fastener.hex': hex, 'fastener.lug': lug };
}

/** Cartoon customer portraits (our own), as SVG. */
export function portraits(): Record<string, string> {
  const skins = ['#f1c9a5', '#e0ac7e', '#c68642', '#8d5524', '#f5d6c6', '#d9a066'];
  const hairs = ['#2b1d12', '#6b4a2b', '#c9a45c', '#9a9a9a', '#b5451b', '#111111'];
  const shirts = ['#2f6db5', '#b53a2f', '#3a8a4a', '#8a6a2f', '#6a3a8a', '#2f8a8a'];
  const out: Record<string, string> = {};
  for (let i = 0; i < 12; i++) {
    const skin = skins[i % skins.length];
    const hair = hairs[(i * 5) % hairs.length];
    const shirt = shirts[(i * 7) % shirts.length];
    const style = i % 4;
    const glasses = i % 3 === 1;
    const hat = i % 5 === 3;
    const beard = i % 4 === 2;
    const hairSvg =
      style === 0 ? `<path d="M22 30c0-14 8-20 18-20s18 6 18 20c-4-6-10-8-18-8s-14 2-18 8z" fill="${hair}"/>`
      : style === 1 ? `<path d="M20 34c-2-16 6-26 20-26s22 10 20 26c-2-2-3-10-6-12-8 4-20 4-28 0-3 2-4 10-6 12z" fill="${hair}"/><path d="M20 34c-2 10 0 18 4 24M60 34c2 10 0 18-4 24" stroke="${hair}" stroke-width="6"/>`
      : style === 2 ? `<path d="M24 26c2-10 8-14 16-14s14 4 16 14c-6-3-26-3-32 0z" fill="${hair}"/>`
      : `<circle cx="40" cy="14" r="8" fill="${hair}"/><path d="M22 30c0-12 8-18 18-18s18 6 18 18c-5-5-11-7-18-7s-13 2-18 7z" fill="${hair}"/>`;
    out[`portrait.${i + 1}`] = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 80 80"><rect width="80" height="80" rx="8" fill="#d8cfb8"/>`
      + `<path d="M8 80c2-16 14-22 32-22s30 6 32 22z" fill="${shirt}"/>`
      + `<rect x="34" y="48" width="12" height="12" fill="${skin}"/>`
      + `<ellipse cx="40" cy="36" rx="17" ry="20" fill="${skin}"/>`
      + hairSvg
      + `<ellipse cx="33" cy="36" rx="2.2" ry="2.8" fill="#222"/><ellipse cx="47" cy="36" rx="2.2" ry="2.8" fill="#222"/>`
      + `<path d="M40 38l-2 6h4" fill="none" stroke="#a0704a" stroke-width="1.5"/>`
      + (beard ? `<path d="M24 40c2 14 10 18 16 18s14-4 16-18c-4 6-10 8-16 8s-12-2-16-8z" fill="${hair}"/>` : `<path d="M34 48c4 3 8 3 12 0" fill="none" stroke="#7a3a2a" stroke-width="2" stroke-linecap="round"/>`)
      + (glasses ? `<circle cx="33" cy="36" r="5.5" fill="none" stroke="#333" stroke-width="1.6"/><circle cx="47" cy="36" r="5.5" fill="none" stroke="#333" stroke-width="1.6"/><path d="M38.5 36h3" stroke="#333" stroke-width="1.6"/>` : '')
      + (hat ? `<path d="M20 24h40v-6c0-6-8-10-20-10s-20 4-20 10z" fill="#c23a2a"/><path d="M16 24h48" stroke="#8a2a1f" stroke-width="4" stroke-linecap="round"/>` : '')
      + `</svg>`;
  }
  return out;
}

// ---------------------------------------------------------------------------
// Props (garage + car lot)
// ---------------------------------------------------------------------------

export function props(): Record<string, ModelBuilder> {
  const out: Record<string, ModelBuilder> = {};

  // Two-post lift. Origin on the floor between the posts. Arms at y = 0 relative to `armY` (set by the engine).
  const lift = new ModelBuilder();
  for (const s of [-1, 1]) {
    lift.box([0.28, 2.6, 0.28], M.liftYellow, { p: [0, 1.3, s * 1.45] });
    lift.box([0.6, 0.02, 0.6], M.darkSteel, { p: [0, 0.01, s * 1.45] });
    lift.box([0.12, 0.3, 0.2], M.darkSteel, { p: [0.18, 1.0, s * 1.45] });
    lift.cyl(0.04, 2.4, M.chrome, { p: [-0.18, 1.2, s * 1.45] });
  }
  lift.box([0.25, 0.12, 3.18], M.liftYellow, { p: [0, 2.66, 0] });
  out['prop.lift'] = lift;

  const arms = new ModelBuilder();
  for (const s of [-1, 1]) for (const dx of [-1, 1]) {
    arms.box([0.8, 0.07, 0.12], M.liftYellow, { p: [dx * 0.4, 0, s * 1.0], r: [0, dx * s * 0.45, 0] });
    arms.cyl(0.08, 0.06, M.rubber, { p: [dx * 0.72, 0.06, s * 0.62] });
  }
  for (const s of [-1, 1]) arms.box([0.24, 0.24, 0.3], M.liftYellow, { p: [0, 0, s * 1.33] });
  out['prop.lift_arms'] = arms;

  const bench = new ModelBuilder();
  bench.box([2.0, 0.06, 0.8], M.wood, { p: [0, 0.9, 0] });
  for (const x of [-0.95, 0.95]) for (const z of [-0.35, 0.35]) bench.box([0.05, 0.9, 0.05], M.darkSteel, { p: [x, 0.45, z] });
  bench.box([1.9, 0.03, 0.7], M.darkSteel, { p: [0, 0.2, 0] });
  bench.box([0.2, 0.12, 0.14], M.blue, { p: [-0.75, 0.99, 0.28] });
  bench.box([0.05, 0.06, 0.3], M.steel, { p: [-0.75, 0.99, 0.46] });
  out['prop.workbench'] = bench;

  const chest = new ModelBuilder();
  chest.box([0.75, 1.0, 0.5], M.toolRed, { p: [0, 0.55, 0] });
  for (let i = 0; i < 6; i++) {
    chest.box([0.7, 0.005, 0.51], M.darkSteel, { p: [0, 0.15 + i * 0.16, 0] });
    chest.box([0.4, 0.025, 0.03], M.chrome, { p: [0, 0.22 + i * 0.16, 0.26] });
  }
  chest.box([0.75, 0.4, 0.45], M.toolRed, { p: [0, 1.28, -0.02] });
  for (const x of [-0.3, 0.3]) for (const z of [-0.2, 0.2]) chest.cyl(0.04, 0.03, M.rubber, { p: [x, 0.03, z] }, { axis: 'z' });
  out['prop.toolchest'] = chest;

  const shelf = new ModelBuilder();
  for (const x of [-0.9, 0.9]) for (const z of [-0.25, 0.25]) shelf.box([0.04, 2.0, 0.04], M.steel, { p: [x, 1.0, z] });
  for (let i = 0; i < 4; i++) {
    const y = 0.15 + i * 0.6;
    shelf.box([1.84, 0.03, 0.54], M.steel, { p: [0, y, 0] });
    for (let k = 0; k < 3; k++) {
      const w = 0.3 + ((i * 3 + k) % 3) * 0.1;
      shelf.box([w, 0.22 + ((k + i) % 2) * 0.08, 0.4], EXTRA.cardboard, { p: [-0.6 + k * 0.6, y + 0.14, 0] });
    }
  }
  out['prop.shelf'] = shelf;

  const tires = new ModelBuilder();
  for (let i = 0; i < 4; i++) tires.torus(0.26, 0.1, M.rubber, { p: [0, 0.1 + i * 0.2, 0] }, { axis: 'y' });
  out['prop.tires'] = tires;

  const drum = new ModelBuilder();
  drum.cyl(0.29, 0.88, M.blue, { p: [0, 0.44, 0] });
  for (const y of [0.3, 0.6]) drum.torus(0.29, 0.015, M.blue, { p: [0, y, 0] }, { axis: 'y' });
  drum.cyl(0.04, 0.02, M.darkSteel, { p: [0.15, 0.89, 0] });
  out['prop.drum'] = drum;

  const lamp = new ModelBuilder();
  lamp.box([1.3, 0.08, 0.25], M.steel, {});
  lamp.box([1.2, 0.04, 0.06], M.headlight, { p: [0, -0.05, -0.05] });
  lamp.box([1.2, 0.04, 0.06], M.headlight, { p: [0, -0.05, 0.05] });
  out['prop.lamp'] = lamp;

  const pole = new ModelBuilder();
  pole.cyl(0.08, 6, M.darkSteel, { p: [0, 3, 0] });
  pole.box([1.2, 0.08, 0.08], M.darkSteel, { p: [0.5, 5.9, 0] });
  pole.box([0.5, 0.12, 0.3], M.darkSteel, { p: [1.0, 5.85, 0] });
  pole.box([0.44, 0.02, 0.24], M.headlight, { p: [1.0, 5.78, 0] });
  out['prop.lightpole'] = pole;

  const booth = new ModelBuilder();
  booth.box([3, 2.6, 2.4], M.paper, { p: [0, 1.3, 0] });
  booth.box([3.3, 0.15, 2.7], M.red, { p: [0, 2.68, 0] });
  booth.box([1.6, 0.9, 0.02], M.glass, { p: [0, 1.5, 1.21] });
  booth.box([0.9, 2.0, 0.03], M.wood, { p: [-1.51, 1.0, 0.3], r: [0, Math.PI / 2, 0] });
  out['prop.booth'] = booth;

  const fence = new ModelBuilder();
  for (let i = 0; i <= 4; i++) fence.cyl(0.04, 1.2, M.darkSteel, { p: [i * 1.0 - 2, 0.6, 0] });
  fence.cyl(0.02, 4.0, M.darkSteel, { p: [0, 1.15, 0] }, { axis: 'x' });
  fence.cyl(0.02, 4.0, M.darkSteel, { p: [0, 0.35, 0] }, { axis: 'x' });
  for (let i = 0; i < 40; i++) fence.box([0.005, 0.8, 0.005], M.steel, { p: [-2 + i * 0.1, 0.75, 0], r: [0, 0, 0.5] });
  out['prop.fence'] = fence;

  const chair = new ModelBuilder();
  chair.box([0.45, 0.03, 0.42], M.wood, { p: [0, 0.46, 0] });
  chair.box([0.03, 0.4, 0.42], M.wood, { p: [-0.22, 0.72, 0], r: [0, 0, -0.1] });
  chair.box([0.02, 0.28, 0.44], EXTRA.cardboard, { p: [-0.23, 0.78, 0] });
  for (const z of [-0.2, 0.2]) {
    chair.box([0.5, 0.025, 0.025], M.wood, { p: [0, 0.25, z], r: [0, 0, 0.9] });
    chair.box([0.5, 0.025, 0.025], M.wood, { p: [0, 0.25, z], r: [0, 0, -0.9] });
  }
  out['prop.chair'] = chair;

  const stage = new ModelBuilder();
  stage.box([7, 0.5, 4.5], M.wood, { p: [0, 0.25, 0] });
  for (let i = 0; i < 14; i++) stage.box([0.02, 0.005, 4.5], M.darkSteel, { p: [-3.5 + i * 0.5, 0.503, 0] });
  stage.box([7.2, 0.08, 0.1], M.red, { p: [0, 0.46, 2.26] });
  stage.box([0.8, 1.1, 0.5], M.wood, { p: [2.8, 1.05, -1.6] }); // podium
  stage.box([0.9, 0.05, 0.6], M.wood, { p: [2.8, 1.62, -1.6] });
  out['prop.stage'] = stage;

  const board = new ModelBuilder();
  board.box([0.5, 0.02, 0.05], M.steel, { p: [0, 0, 0] });
  for (let i = 0; i < 7; i++) board.box([0.02, 0.18 + i * 0.03, 0.01], M.chrome, { p: [-0.2 + i * 0.065, -0.1 - i * 0.015, 0.02] });
  out['prop.wrenches'] = board;

  return out;
}
