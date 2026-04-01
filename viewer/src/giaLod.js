import * as THREE from "three";

const _lodPivotPos = new THREE.Vector3();

/**
 * Find gia_detail / gia_hull pairs exported from Grasshopper (GlbExporter).
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
  return pairs;
}

/**
 * @param {{ detail: THREE.Mesh; hull: THREE.Mesh; pivot: THREE.Object3D }[]} pairs
 * @param {THREE.Camera} camera
 * @param {number} distanceWorld 0 = disabled (keep full detail)
 */
export function updateGiaLodVisibility(pairs, camera, distanceWorld) {
  if (!pairs.length || distanceWorld <= 0) {
    for (const { detail, hull } of pairs) {
      detail.visible = true;
      hull.visible = false;
    }
    return;
  }

  const camPos = camera.position;
  for (const { detail, hull, pivot } of pairs) {
    pivot.getWorldPosition(_lodPivotPos);
    const d = camPos.distanceTo(_lodPivotPos);
    const useHull = d > distanceWorld;
    detail.visible = !useHull;
    hull.visible = useHull;
  }
}
