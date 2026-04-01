import * as THREE from "three";
import {
  computeBoundsTree,
  disposeBoundsTree,
  acceleratedRaycast,
} from "three-mesh-bvh";

let installed = false;

/** Patch Three so Mesh.raycast uses BVH when geometry.boundsTree exists. */
export function installMeshBvhRaycastExtensions() {
  if (installed) return;
  installed = true;
  THREE.BufferGeometry.prototype.computeBoundsTree = computeBoundsTree;
  THREE.BufferGeometry.prototype.disposeBoundsTree = disposeBoundsTree;
  THREE.Mesh.prototype.raycast = acceleratedRaycast;
}

installMeshBvhRaycastExtensions();

/**
 * Build a BVH per BufferGeometry under root (shared geometries only built once).
 * Call after instancing merge so dense scenes get one tree per shared geometry.
 */
export function buildBoundsTreesForModelRoot(root) {
  root.updateMatrixWorld(true);
  root.traverse((obj) => {
    if (!obj.isMesh || obj.isSkinnedMesh) return;
    const g = obj.geometry;
    if (!g?.isBufferGeometry) return;
    if (g.boundsTree) return;
    const pos = g.attributes.position;
    if (!pos || pos.count < 3) return;
    try {
      g.computeBoundsTree();
    } catch (e) {
      console.warn("[GIA] BVH build skipped:", obj.name || obj.type, e);
    }
  });
}

/** Remove BVHs before unloading geometry (pairs with GLB reload / clear). */
export function disposeBoundsTreesUnderRoot(root) {
  root.traverse((obj) => {
    if (!obj.isMesh) return;
    const g = obj.geometry;
    if (g?.boundsTree && typeof g.disposeBoundsTree === "function") {
      g.disposeBoundsTree();
    }
  });
}
