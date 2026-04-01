import * as THREE from "three";

const _lodPivotPos = new THREE.Vector3();

/**
 * Rhino/Grasshopper exports vertex coordinates in document units; typical AEC is mm.
 * If your GLB is in meters, set this to 0.001 so LOD distances stay in mm.
 */
export const MM_TO_SCENE_UNIT = 1;

/**
 * Find gia_detail / gia_hull pairs exported from Grasshopper (GlbExporter).
 * Hulls start hidden and detail visible until {@link updateGiaLodVisibility} runs.
 * @param {THREE.Object3D} root
 * @returns {{ detail: THREE.Mesh; hull: THREE.Mesh; pivot: THREE.Object3D }[]}
 */
export function collectGiaLodPairs(root) {
  /** @type {{ detail: THREE.Mesh; hull: THREE.Mesh; pivot: THREE.Object3D }[]} */
  const pairs = [];
  root.traverse((o) => {
    if (o.name !== "gia_detail" || !o.isMesh) return;
    const hull = o.parent?.getObjectByName("gia_hull");
    if (hull?.isMesh && o.parent) {
      o.userData.giaLodDetail = true;
      hull.userData.giaLodHull = true;
      pairs.push({ detail: o, hull, pivot: o.parent });
    }
  });
  for (const { detail, hull } of pairs) {
    detail.visible = true;
    hull.visible = false;
  }
  return pairs;
}

/**
 * @param {{ detail: THREE.Mesh; hull: THREE.Mesh; pivot: THREE.Object3D }[]} pairs
 * @param {THREE.Camera} camera
 * @param {number | null} lodDistanceMm
 *   `null` — LOD off: full mesh only (hull hidden).
 *   `0` — always convex hull (detail hidden).
 *   `> 0` — full mesh while camera–pivot distance ≤ this many mm, hull beyond.
 */
export function updateGiaLodVisibility(pairs, camera, lodDistanceMm) {
  if (!pairs.length) return;

  const off =
    lodDistanceMm == null ||
    !Number.isFinite(lodDistanceMm) ||
    lodDistanceMm < 0;

  if (off) {
    for (const { detail, hull } of pairs) {
      detail.visible = true;
      hull.visible = false;
    }
    return;
  }

  if (lodDistanceMm === 0) {
    for (const { detail, hull } of pairs) {
      detail.visible = false;
      hull.visible = true;
    }
    return;
  }

  const thresholdWorld = lodDistanceMm * MM_TO_SCENE_UNIT;
  const camPos = camera.position;
  for (const { detail, hull, pivot } of pairs) {
    pivot.getWorldPosition(_lodPivotPos);
    const d = camPos.distanceTo(_lodPivotPos);
    const useHull = d > thresholdWorld;
    detail.visible = !useHull;
    hull.visible = useHull;
  }
}
