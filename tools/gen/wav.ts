// 16-bit mono WAV encoder + a few synth helpers for placeholder sound effects.

export const RATE = 22050;

export function encodeWav(samples: Float32Array, rate = RATE): Uint8Array {
  const n = samples.length;
  const out = new Uint8Array(44 + n * 2);
  const dv = new DataView(out.buffer);
  const str = (o: number, s: string) => {
    for (let i = 0; i < s.length; i++) out[o + i] = s.charCodeAt(i);
  };
  str(0, 'RIFF');
  dv.setUint32(4, 36 + n * 2, true);
  str(8, 'WAVE');
  str(12, 'fmt ');
  dv.setUint32(16, 16, true);
  dv.setUint16(20, 1, true); // PCM
  dv.setUint16(22, 1, true); // mono
  dv.setUint32(24, rate, true);
  dv.setUint32(28, rate * 2, true);
  dv.setUint16(32, 2, true);
  dv.setUint16(34, 16, true);
  str(36, 'data');
  dv.setUint32(40, n * 2, true);
  for (let i = 0; i < n; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    dv.setInt16(44 + i * 2, Math.round(s * 32767), true);
  }
  return out;
}

/** Deterministic noise source. */
export function makeNoise(seed = 1): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6d2b79f5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return (((t ^ (t >>> 14)) >>> 0) / 4294967296) * 2 - 1;
  };
}

export function buffer(seconds: number): Float32Array {
  return new Float32Array(Math.ceil(seconds * RATE));
}

/** Add a decaying sine partial. */
export function addTone(buf: Float32Array, start: number, freq: number, amp: number, decay: number, dur = 1, freqEnd?: number): void {
  const s0 = Math.floor(start * RATE);
  const n = Math.min(buf.length - s0, Math.floor(dur * RATE));
  let phase = 0;
  for (let i = 0; i < n; i++) {
    const t = i / RATE;
    const f = freqEnd === undefined ? freq : freq + (freqEnd - freq) * (i / n);
    phase += (2 * Math.PI * f) / RATE;
    buf[s0 + i] += Math.sin(phase) * amp * Math.exp(-t / decay);
  }
}

/** Add a burst of filtered noise. `tone` in 0..1 is a crude low-pass amount (1 = bright). */
export function addNoise(buf: Float32Array, start: number, dur: number, amp: number, decay: number, tone: number, seed = 7, highpass = false): void {
  const rnd = makeNoise(seed);
  const s0 = Math.floor(start * RATE);
  const n = Math.min(buf.length - s0, Math.floor(dur * RATE));
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const t = i / RATE;
    const x = rnd();
    lp += (x - lp) * tone;
    const v = highpass ? x - lp : lp;
    buf[s0 + i] += v * amp * Math.exp(-t / decay);
  }
}

export function normalize(buf: Float32Array, peak = 0.85): Float32Array {
  let m = 0;
  for (const v of buf) m = Math.max(m, Math.abs(v));
  if (m > 0) for (let i = 0; i < buf.length; i++) buf[i] = (buf[i] / m) * peak;
  // tiny fade-out to avoid clicks
  const fade = Math.min(buf.length, Math.floor(0.01 * RATE));
  for (let i = 0; i < fade; i++) buf[buf.length - 1 - i] *= i / fade;
  return buf;
}
