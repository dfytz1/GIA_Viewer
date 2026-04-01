import * as THREE from "three";
import { mergeGeometries } from "three/examples/jsm/utils/BufferGeometryUtils.js";

/**
 * Stable key for grouping meshes that should share one draw call.
 * Uses geometry layout + a light hash of positions (same as GLB duplicates of one block).
 */
function geometrySignature(geometry) {
  if (!geometry?.attributes?.position) return "none";
  const pos = geometry.attributes.position;
  const arr = pos.array;
  const vCount = pos.count;
  const iCount = geometry.index ? geometry.index.count : vCount;
  let h = (vCount * 73856093) ^ (iCount * 19349663);
  const len = arr.length;
  const step = Math.max(4, Math.floor(len / 128));
  for (let i = 0; i < len; i += step) {
    h = Math.imul(h ^ (arr[i] * 1000) | 0, 2654435761);
  }
  return `${vCount}|${geometry.index?.count ?? "ni"}|${h}`;
}

function materialKey(material) {
  if (!material) return "null";
  if (Array.isArray(material))
    return material.map((m) => m?.uuid ?? "?").join(",");
  return material.uuid;
}

function bucketKey(mesh) {
  return `${geometrySignature(mesh.geometry)}#${materialKey(mesh.material)}`;
}

/**
 * Collapse many identical Mesh draw calls into InstancedMesh (same geometry+material).
 * Matrices are converted to be local to `root` (the loaded glTF root group).
 *
 * @param {THREE.Object3D} root
 * @param {{ minGroupSize?: number }} [options]
 * @returns {{ mergedGroups: number, meshCountBefore: number, meshCountAfter: number }}
 */
export function mergeIdenticalMeshesToInstanced(root, options = {}) {
  const minGroupSize = options.minGroupSize ?? 2;

  /** @type {THREE.Mesh[]} */
  const meshes = [];
  root.updateMatrixWorld(true);
  root.traverse((o) => {
    if (!o.isMesh || o.isInstancedMesh) return;
    if (o.isSkinnedMesh) return;
    if (
      o.name?.startsWith("gia_detail") ||
      o.name?.startsWith("gia_hull")
    )
      return;
    if (o.geometry?.morphAttributes && Object.keys(o.geometry.morphAttributes).length)
      return;
    if (Array.isArray(o.material)) return;
    meshes.push(o);
  });

  const meshCountBefore = meshes.length;
  if (meshes.length < minGroupSize) {
    return { mergedGroups: 0, meshCountBefore, meshCountAfter: meshCountBefore };
  }

  /** @type {Map<string, THREE.Mesh[]>} */
  const buckets = new Map();
  for (const m of meshes) {
    const k = bucketKey(m);
    let g = buckets.get(k);
    if (!g) {
      g = [];
      buckets.set(k, g);
    }
    g.push(m);
  }

  const invRoot = new THREE.Matrix4().copy(root.matrixWorld).invert();
  const tmp = new THREE.Matrix4();
  let mergedGroups = 0;
  let drawsSaved = 0;

  for (const [, group] of buckets) {
    if (group.length < minGroupSize) continue;

    const template = group[0];
    const geometry = template.geometry;
    const material = template.material;
    if (!geometry || !material) continue;

    const count = group.length;
    const instanced = new THREE.InstancedMesh(geometry, material, count);
    instanced.name = `gia-instanced-${mergedGroups}`;
    instanced.castShadow = template.castShadow;
    instanced.receiveShadow = template.receiveShadow;
    instanced.frustumCulled = true;

    for (let i = 0; i < count; i++) {
      const m = group[i];
      m.updateWorldMatrix(true, false);
      tmp.copy(invRoot).multiply(m.matrixWorld);
      instanced.setMatrixAt(i, tmp);
    }
    instanced.instanceMatrix.needsUpdate = true;
    instanced.computeBoundingSphere();

    for (const m of group) {
      m.parent?.remove(m);
      if (m.geometry && m.geometry !== geometry) {
        m.geometry.dispose();
      }
      if (m.material && m.material !== material) {
        const mats = Array.isArray(m.material) ? m.material : [m.material];
        const keep = Array.isArray(material) ? material : [material];
        for (const mm of mats) {
          if (mm && !keep.includes(mm)) mm.dispose?.();
        }
      }
    }

    root.add(instanced);
    mergedGroups += 1;
    drawsSaved += count - 1;
  }

  const meshCountAfter = meshCountBefore - drawsSaved;
  return { mergedGroups, meshCountBefore, meshCountAfter };
}

/**
 * Merge static meshes that share one material into a single draw call (distinct geometries baked to root space).
 * Excludes LOD nodes and instanced/skinned/multi-material meshes. Frustum culling is off on merged meshes (scene-spanning bounds).
 *
 * @param {THREE.Object3D} root Loaded glTF root (same as instancing).
 * @returns {number} Number of material groups merged (0 if none)
 */
export function mergeByMaterial(root) {
  root.updateMatrixWorld(true);
  const invRoot = new THREE.Matrix4().copy(root.matrixWorld).invert();
  const tmp = new THREE.Matrix4();

  /** @type {Map<string, { material: THREE.Material; meshes: THREE.Mesh[] }>} */
  const byMaterial = new Map();

  root.traverse((o) => {
    if (!o.isMesh || o.isInstancedMesh || o.isSkinnedMesh) return;
    if (o.name?.startsWith("gia_detail") || o.name?.startsWith("gia_hull")) return;
    if (Array.isArray(o.material)) return;
    const mat = o.material;
    if (!mat) return;
    if (o.geometry?.morphAttributes && Object.keys(o.geometry.morphAttributes).length)
      return;
    if (!o.geometry?.attributes?.position) return;

    const key = mat.uuid;
    let bucket = byMaterial.get(key);
    if (!bucket) {
      bucket = { material: mat, meshes: [] };
      byMaterial.set(key, bucket);
    }
    bucket.meshes.push(o);
  });

  let merged = 0;
  for (const { material, meshes } of byMaterial.values()) {
    if (meshes.length < 2) continue;

    const geos = [];
    for (const m of meshes) {
      m.updateWorldMatrix(true, false);
      const g = m.geometry.clone();
      tmp.copy(invRoot).multiply(m.matrixWorld);
      g.applyMatrix4(tmp);
      geos.push(g);
    }

    const combined = mergeGeometries(geos, false);
    if (!combined) {
      geos.forEach((g) => g.dispose());
      continue;
    }

    combined.computeBoundingSphere();

    const mergedMesh = new THREE.Mesh(combined, material);
    mergedMesh.name = `gia-merged-${merged}`;
    mergedMesh.frustumCulled = false;
    mergedMesh.castShadow = meshes.some((m) => m.castShadow);
    mergedMesh.receiveShadow = meshes.some((m) => m.receiveShadow);

    root.add(mergedMesh);

    const disposedGeo = new Set();
    for (const m of meshes) {
      const g = m.geometry;
      m.parent?.remove(m);
      if (g && !disposedGeo.has(g.uuid)) {
        disposedGeo.add(g.uuid);
        g.dispose();
      }
    }
    geos.forEach((g) => g.dispose());
    merged += 1;
  }

  return merged;
}
