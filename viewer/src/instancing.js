import * as THREE from "three";
import { mergeGeometries } from "three/examples/jsm/utils/BufferGeometryUtils.js";

/** Per-instance geometry must be this large before InstancedMesh pays off (Speckle-style). */
export const MIN_INSTANCED_BATCH_VERTICES = 10_000;

/** Split material batches so merged vertex buffers stay under this (Speckle-style). */
export const MAX_BATCH_VERTICES = 500_000;

/** Yield to the event loop every N material buckets processed (AsyncPause-style). */
export const ASYNC_YIELD_EVERY_MATERIALS = 20;

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

function meshPositionVertexCount(mesh) {
  const g = mesh.geometry;
  if (!g?.attributes?.position) return 0;
  return g.attributes.position.count;
}

function isEligibleMesh(o) {
  if (!o.isMesh || o.isInstancedMesh || o.isSkinnedMesh) return false;
  if (o.name?.startsWith("gia_detail") || o.name?.startsWith("gia_hull")) return false;
  if (o.geometry?.morphAttributes && Object.keys(o.geometry.morphAttributes).length)
    return false;
  if (Array.isArray(o.material)) return false;
  if (!o.material || !o.geometry?.attributes?.position) return false;
  return true;
}

/**
 * Collapse identical geometry+material into InstancedMesh only when the shared geometry is large enough.
 * Matrices are relative to `root` (same as before).
 *
 * @param {THREE.Object3D} root
 * @param {{ minGroupSize?: number; minVertices?: number }} [options]
 */
export function mergeIdenticalMeshesToInstanced(root, options = {}) {
  const minGroupSize = options.minGroupSize ?? 2;
  const minVertices = options.minVertices ?? MIN_INSTANCED_BATCH_VERTICES;

  /** @type {THREE.Mesh[]} */
  const meshes = [];
  root.updateMatrixWorld(true);
  root.traverse((o) => {
    if (!isEligibleMesh(o)) return;
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

    const vertCount = geometry.attributes.position.count;
    if (vertCount < minVertices) continue;

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
    instanced.geometry.computeBoundingBox();
    instanced.geometry.computeBoundingSphere();
    instanced.computeBoundingSphere();
    instanced.computeBoundingBox();

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

/** Force single-material draw groups after mergeGeometries(..., true). */
function normalizeBatchMaterialIndex(geometry) {
  if (!geometry.groups?.length) return;
  for (const grp of geometry.groups) {
    grp.materialIndex = 0;
  }
}

/**
 * Speckle-style MeshBatch: one BufferGeometry per material (optionally split at MAX_BATCH_VERTICES),
 * with mergeGeometries(..., true) so each source mesh keeps a draw range (future per-object visibility).
 *
 * @param {THREE.Object3D} root
 * @param {{ onProgress?: (done: number, totalMaterials: number) => void }} [options]
 * @returns {Promise<number>} Number of batch meshes created
 */
export async function mergeMeshesByMaterialBatch(root, options = {}) {
  const onProgress = options.onProgress;
  root.updateMatrixWorld(true);
  const invRoot = new THREE.Matrix4().copy(root.matrixWorld).invert();
  const tmp = new THREE.Matrix4();

  /** @type {Map<string, { material: THREE.Material; meshes: THREE.Mesh[] }>} */
  const byMaterial = new Map();

  root.traverse((o) => {
    if (!isEligibleMesh(o)) return;
    const mat = o.material;
    const key = mat.uuid;
    let bucket = byMaterial.get(key);
    if (!bucket) {
      bucket = { material: mat, meshes: [] };
      byMaterial.set(key, bucket);
    }
    bucket.meshes.push(o);
  });

  const totalMaterials = byMaterial.size;
  let processedMaterials = 0;
  let batchMeshesCreated = 0;

  for (const { material, meshes } of byMaterial.values()) {
    if (processedMaterials > 0 && processedMaterials % ASYNC_YIELD_EVERY_MATERIALS === 0) {
      await new Promise((r) => setTimeout(r, 0));
    }

    if (meshes.length < 2) {
      processedMaterials++;
      onProgress?.(processedMaterials, totalMaterials);
      continue;
    }

    /** @type {THREE.Mesh[][]} */
    const subBatches = [];
    let cur = [];
    let curVerts = 0;
    for (const m of meshes) {
      const v = meshPositionVertexCount(m);
      if (cur.length > 0 && curVerts + v > MAX_BATCH_VERTICES) {
        subBatches.push(cur);
        cur = [];
        curVerts = 0;
      }
      cur.push(m);
      curVerts += v;
    }
    if (cur.length) subBatches.push(cur);

    for (const batch of subBatches) {
      if (batch.length < 2) continue;

      const geos = [];
      for (const m of batch) {
        m.updateWorldMatrix(true, false);
        const g = m.geometry.clone();
        tmp.copy(invRoot).multiply(m.matrixWorld);
        g.applyMatrix4(tmp);
        geos.push(g);
      }

      const merged = mergeGeometries(geos, true);
      if (!merged) {
        geos.forEach((g) => g.dispose());
        continue;
      }

      normalizeBatchMaterialIndex(merged);
      merged.computeBoundingBox();
      merged.computeBoundingSphere();

      const mesh = new THREE.Mesh(merged, material);
      mesh.name = `gia-mesh-batch-${batchMeshesCreated}`;
      mesh.frustumCulled = false;
      mesh.castShadow = batch.some((m) => m.castShadow);
      mesh.receiveShadow = batch.some((m) => m.receiveShadow);
      mesh.userData.giaBatchSubMeshCount = batch.length;
      /** Aligns with `geometry.groups` after merge (Speckle-style per-object draw ranges). */
      mesh.userData.giaBatchEntries = batch.map((m, i) => ({
        sourceUuid: m.uuid,
        start: merged.groups[i].start,
        count: merged.groups[i].count,
      }));

      root.add(mesh);

      const disposedGeo = new Set();
      for (const m of batch) {
        const g = m.geometry;
        m.parent?.remove(m);
        if (g && !disposedGeo.has(g.uuid)) {
          disposedGeo.add(g.uuid);
          g.dispose();
        }
      }
      geos.forEach((g) => g.dispose());
      batchMeshesCreated++;
    }

    processedMaterials++;
    onProgress?.(processedMaterials, totalMaterials);
  }

  return batchMeshesCreated;
}
