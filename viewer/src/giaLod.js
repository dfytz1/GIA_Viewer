import * as THREE from "three";

const _sphereCenter = new THREE.Vector3();

function maxScaleOnAxis(matrixWorld) {
  const e = matrixWorld.elements;
  const sx = Math.hypot(e[0], e[1], e[2]);
  const sy = Math.hypot(e[4], e[5], e[6]);
  const sz = Math.hypot(e[8], e[9], e[10]);
  return Math.max(sx, sy, sz, 1e-20);
}

/**
 * Approximate diameter of the mesh's bounding sphere on screen (pixels), vertical FOV basis.
 * Matches how {@link THREE.PerspectiveCamera} projects (fov is full vertical view).
 * @param {THREE.Mesh} mesh
 * @param {THREE.PerspectiveCamera} camera
 * @param {number} viewportHeightPx  CSS / logical pixels (same basis as camera.aspect height)
 * @returns {number} Infinity if unusable (keeps full detail)
 */
function projectedBoundingSphereDiameterPx(mesh, camera, viewportHeightPx) {
  const geom = mesh.geometry;
  if (!geom) return Infinity;
  if (!geom.boundingSphere) geom.computeBoundingSphere();
  const bs = geom.boundingSphere;
  if (!bs || !Number.isFinite(bs.radius) || bs.radius <= 0) return Infinity;

  _sphereCenter.copy(bs.center).applyMatrix4(mesh.matrixWorld);
  const rWorld = bs.radius * maxScaleOnAxis(mesh.matrixWorld);
  const dist = camera.position.distanceTo(_sphereCenter);
  const safeDist = Math.max(dist, 1e-9);

  const h = Math.max(viewportHeightPx, 1);

  if (camera.isPerspectiveCamera) {
    const vFovRad = (camera.fov * Math.PI) / 180;
    const tanHalf = Math.tan(vFovRad / 2);
    const worldPerPxVert = (2 * safeDist * tanHalf) / h;
    return (2 * rWorld) / worldPerPxVert;
  }

  if (camera.isOrthographicCamera) {
    const viewH = Math.abs(camera.top - camera.bottom);
    const worldPerPx = viewH / h;
    return (2 * rWorld) / worldPerPx;
  }

  return Infinity;
}

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
      const g = o.geometry;
      if (g && !g.boundingSphere) g.computeBoundingSphere();
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
 * @param {{ width: number; height: number }} viewportPx  Logical viewport (match camera.aspect)
 * @param {number | null} lodDetailMinPx
 *   `null` — LOD off: full mesh only (hull hidden).
 *   `0` — always convex hull (detail hidden).
 *   `> 0` — full detail while projected bounding-sphere diameter (px) ≥ this; hull when smaller.
 */
export function updateGiaLodVisibility(pairs, camera, viewportPx, lodDetailMinPx) {
  if (!pairs.length) return;

  const off =
    lodDetailMinPx == null ||
    !Number.isFinite(lodDetailMinPx) ||
    lodDetailMinPx < 0;

  if (off) {
    for (const { detail, hull } of pairs) {
      detail.visible = true;
      hull.visible = false;
    }
    return;
  }

  if (lodDetailMinPx === 0) {
    for (const { detail, hull } of pairs) {
      detail.visible = false;
      hull.visible = true;
    }
    return;
  }

  const vh = viewportPx?.height ?? 1;

  for (const { detail, hull } of pairs) {
    const diamPx = projectedBoundingSphereDiameterPx(detail, camera, vh);
    const useHull = diamPx < lodDetailMinPx;
    detail.visible = !useHull;
    hull.visible = useHull;
  }
}
