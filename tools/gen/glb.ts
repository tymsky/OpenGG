// Minimal glTF 2.0 binary (.glb) writer for generated placeholder models.
// One node, one mesh, one primitive per material.

import type * as THREE from 'three';
import type { MatDef } from './geo.ts';

export interface GlbPrimitive {
  mat: MatDef;
  geo: THREE.BufferGeometry;
}

interface BufferViewJson {
  buffer: number;
  byteOffset: number;
  byteLength: number;
  target?: number;
}

const ARRAY_BUFFER = 34962;
const ELEMENT_ARRAY_BUFFER = 34963;
const FLOAT = 5126;
const UNSIGNED_INT = 5125;

export function writeGlb(name: string, prims: GlbPrimitive[]): Uint8Array {
  const chunks: Uint8Array[] = [];
  let byteLength = 0;
  const bufferViews: BufferViewJson[] = [];
  const accessors: object[] = [];
  const materials: object[] = [];
  const primitives: object[] = [];

  const pushView = (data: ArrayBufferView, target: number): number => {
    const bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
    const offset = byteLength;
    chunks.push(bytes);
    byteLength += bytes.byteLength;
    const pad = (4 - (byteLength % 4)) % 4;
    if (pad) {
      chunks.push(new Uint8Array(pad));
      byteLength += pad;
    }
    bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: bytes.byteLength, target });
    return bufferViews.length - 1;
  };

  for (const { mat, geo } of prims) {
    const pos = geo.getAttribute('position');
    if (!pos || pos.count === 0) continue;
    const posArr = new Float32Array(pos.array as ArrayLike<number>);
    const min = [Infinity, Infinity, Infinity];
    const max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < posArr.length; i += 3) {
      for (let k = 0; k < 3; k++) {
        const v = posArr[i + k];
        if (v < min[k]) min[k] = v;
        if (v > max[k]) max[k] = v;
      }
    }
    const attributes: Record<string, number> = {};
    accessors.push({
      bufferView: pushView(posArr, ARRAY_BUFFER),
      componentType: FLOAT,
      count: pos.count,
      type: 'VEC3',
      min,
      max,
    });
    attributes.POSITION = accessors.length - 1;

    const nor = geo.getAttribute('normal');
    if (nor) {
      const arr = new Float32Array(nor.array as ArrayLike<number>);
      accessors.push({ bufferView: pushView(arr, ARRAY_BUFFER), componentType: FLOAT, count: nor.count, type: 'VEC3' });
      attributes.NORMAL = accessors.length - 1;
    }
    const uv = geo.getAttribute('uv');
    if (uv) {
      const arr = new Float32Array(uv.array as ArrayLike<number>);
      accessors.push({ bufferView: pushView(arr, ARRAY_BUFFER), componentType: FLOAT, count: uv.count, type: 'VEC2' });
      attributes.TEXCOORD_0 = accessors.length - 1;
    }

    const prim: Record<string, unknown> = { attributes, mode: 4, material: materials.length };
    const index = geo.getIndex();
    if (index) {
      const arr = new Uint32Array(index.array as ArrayLike<number>);
      accessors.push({ bufferView: pushView(arr, ELEMENT_ARRAY_BUFFER), componentType: UNSIGNED_INT, count: index.count, type: 'SCALAR' });
      prim.indices = accessors.length - 1;
    }
    primitives.push(prim);

    const opacity = mat.opacity ?? 1;
    const matJson: Record<string, unknown> = {
      name: mat.name,
      doubleSided: true,
      pbrMetallicRoughness: {
        baseColorFactor: [...mat.color, opacity],
        metallicFactor: mat.metallic,
        roughnessFactor: mat.roughness,
      },
    };
    if (mat.emissive) matJson.emissiveFactor = mat.emissive;
    if (opacity < 1) matJson.alphaMode = 'BLEND';
    materials.push(matJson);
  }

  const json = {
    asset: { version: '2.0', generator: 'opengg ai asset generator' },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ name, mesh: 0 }],
    meshes: [{ name, primitives }],
    materials,
    accessors,
    bufferViews,
    buffers: [{ byteLength }],
  };

  const bin = new Uint8Array(byteLength);
  let o = 0;
  for (const c of chunks) {
    bin.set(c, o);
    o += c.byteLength;
  }

  let jsonText = JSON.stringify(json);
  while (jsonText.length % 4 !== 0) jsonText += ' ';
  const jsonBytes = new TextEncoder().encode(jsonText);

  const total = 12 + 8 + jsonBytes.byteLength + 8 + bin.byteLength;
  const out = new Uint8Array(total);
  const dv = new DataView(out.buffer);
  dv.setUint32(0, 0x46546c67, true); // 'glTF'
  dv.setUint32(4, 2, true);
  dv.setUint32(8, total, true);
  dv.setUint32(12, jsonBytes.byteLength, true);
  dv.setUint32(16, 0x4e4f534a, true); // 'JSON'
  out.set(jsonBytes, 20);
  const binHeader = 20 + jsonBytes.byteLength;
  dv.setUint32(binHeader, bin.byteLength, true);
  dv.setUint32(binHeader + 4, 0x004e4942, true); // 'BIN\0'
  out.set(bin, binHeader + 8);
  return out;
}
