// Generates the AI placeholder content pack into data/ai/.
// Every generated file is prefixed with "ai_" so it can be told apart from (and later
// replaced by) assets imported from the original game.
//
//   npm run gen:assets

import { mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { AssetManifest, ContentPack } from './types.ts';
import { writeGlb } from './glb.ts';
import { Registry, buildCars } from './cars.ts';
import { DECALS, FASTENER_KINDS, JOBS, NAMES, PAINTS, RULES, TOOLS } from './content.ts';
import { decals, fastenerModels, icons, portraits, props, sounds, textures } from './media.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = join(root, 'data', 'ai');
mkdirSync(outDir, { recursive: true });
for (const f of readdirSync(outDir)) if (f.startsWith('ai_')) rmSync(join(outDir, f));

const fileName = (prefix: string, id: string, ext: string) => `ai_${prefix}_${id.replace(/[^a-z0-9]+/gi, '_')}.${ext}`;
const t0 = Date.now();

const reg = new Registry();
const cars = buildCars(reg);
for (const [id, b] of Object.entries(fastenerModels())) reg.model(id, b);
for (const [id, b] of Object.entries(props())) reg.model(id, b);

const manifest: AssetManifest = { pack: 'ai', name: 'AI-generated placeholder content', version: 1, data: {}, models: {}, textures: {}, sounds: {}, icons: {} };
let bytes = 0;
const write = (name: string, data: Uint8Array | string) => {
  writeFileSync(join(outDir, name), data);
  bytes += typeof data === 'string' ? Buffer.byteLength(data) : data.byteLength;
};

for (const [id, builder] of reg.models) {
  const name = fileName('model', id, 'glb');
  write(name, writeGlb(id, builder.build()));
  manifest.models[id] = name;
}
for (const [id, png] of Object.entries({ ...textures(), ...decals() })) {
  const name = fileName('tex', id.replace(/^tex\./, ''), 'png');
  write(name, png);
  manifest.textures[id] = name;
}
for (const [id, wav] of Object.entries(sounds())) {
  const name = fileName('snd', id.replace(/^snd\./, ''), 'wav');
  write(name, wav);
  manifest.sounds[id] = name;
}
for (const [id, text] of Object.entries(icons())) {
  const name = fileName('icon', id.replace(/^icon\./, ''), 'svg');
  write(name, text);
  manifest.icons[id] = name;
}
const portraitIds: string[] = [];
for (const [id, text] of Object.entries(portraits())) {
  const name = fileName('portrait', id.replace(/^portrait\./, ''), 'svg');
  write(name, text);
  manifest.icons[id] = name;
  portraitIds.push(id);
}

// The jobs as far as the cars built have their parts and body styles (the beta builds one car, see PACK_CARS).
const styles = new Set(cars.map((c) => c.bodyStyle));
const jobs = JOBS
  .map((j) => ({ ...j, requirements: j.requirements.map((r) => (r.parts ? { ...r, parts: r.parts.filter((p) => reg.parts.has(p)) } : r)) }))
  .filter((j) => (!j.bodyStyles || j.bodyStyles.some((s) => styles.has(s))) && j.requirements.every((r) => !r.parts || r.parts.length > 0));
// Start Engine's diagnosis, as far as the cars built have those part families (the pickup's V8 has a distributor and a
// carburettor, the sedan's I4 neither).
const families = new Set(cars.flatMap((c) => c.slots.map((s) => s.family)));
const d = RULES.diagnosis;
const has = (list: string[]) => list.filter((f) => families.has(f));
const rules = { ...RULES, diagnosis: { ...d, dead: has(d.dead), noCrank: has(d.noCrank), noStart: has(d.noStart), rough: has(d.rough), loud: has(d.loud) } };

const pack: ContentPack = {
  id: 'ai',
  name: 'AI placeholder content',
  tools: TOOLS,
  fastenerKinds: FASTENER_KINDS,
  parts: [...reg.parts.values()],
  cars,
  jobs,
  names: NAMES,
  portraits: portraitIds,
  decals: DECALS,
  paints: PAINTS,
  rules,
};
const dataFiles: Record<string, unknown> = {
  tools: pack.tools,
  fasteners: pack.fastenerKinds,
  parts: pack.parts,
  cars: pack.cars,
  jobs: pack.jobs,
  names: pack.names,
  portraits: pack.portraits,
  decals: pack.decals,
  paints: pack.paints,
  rules: pack.rules,
};
for (const [key, value] of Object.entries(dataFiles)) {
  const name = `ai_${key}.json`;
  write(name, JSON.stringify(value, null, 1));
  manifest.data[key] = name;
}
write('ai_manifest.json', JSON.stringify(manifest, null, 1));

const files = readdirSync(outDir).filter((f) => f.startsWith('ai_')).length;
console.log(`Generated ${files} files (${(bytes / 1024 / 1024).toFixed(2)} MB) in ${Date.now() - t0} ms`);
console.log(`  ${reg.models.size} models, ${pack.parts.length} parts, ${cars.length} cars (${cars.map((c) => `${c.id}: ${c.slots.length} slots`).join(', ')})`);
