// @ts-check
import { test } from "node:test";
import assert from "node:assert/strict";
import { createQuad, moveVertex, insertVertex, removeVertex, translate, nearestEdge, isPlane } from "../regions.js";

/**
 * @param {{x:number,y:number}[]} points
 * @returns {number[][]}
 */
const rounded = (points) => points.map((p) => [Math.round(p.x * 1e6) / 1e6, Math.round(p.y * 1e6) / 1e6]);

test("createQuad makes a centred 4-corner plane", () => {
    const q = createQuad("a");
    assert.deepEqual(rounded(q.points), [[0.35, 0.35], [0.65, 0.35], [0.65, 0.65], [0.35, 0.65]]);
    assert.equal(isPlane(q), true);
});

test("insertVertex adds a point after the edge start", () => {
    const q = insertVertex(createQuad("a"), 0, { x: 0.5, y: 0.35 });
    assert.equal(q.points.length, 5);
    assert.deepEqual(q.points[1], { x: 0.5, y: 0.35 });
    assert.equal(isPlane(q), false);
});

test("removeVertex keeps at least 3 points", () => {
    const tri = removeVertex(createQuad("a"), 0);
    assert.equal(tri.points.length, 3);
    assert.equal(removeVertex(tri, 0).points.length, 3);
});

test("moveVertex clamps to the image", () => {
    const q = moveVertex(createQuad("a"), 0, { x: -0.2, y: 1.4 });
    assert.deepEqual(q.points[0], { x: 0, y: 1 });
});

test("translate stops at the image edge and keeps the shape", () => {
    const q = translate(createQuad("a"), 0.5, 0);
    const xs = q.points.map((p) => p.x);
    assert.equal(Math.max(...xs), 1);
    assert.ok(Math.abs(Math.max(...xs) - Math.min(...xs) - 0.3) < 1e-9);
});

test("regions are immutable", () => {
    const q = createQuad("a");
    moveVertex(q, 0, { x: 0, y: 0 });
    assert.deepEqual(rounded([q.points[0]]), [[0.35, 0.35]]);
});

test("nearestEdge finds the edge within reach", () => {
    const pts = [{ x: 100, y: 100 }, { x: 200, y: 100 }, { x: 200, y: 200 }, { x: 100, y: 200 }];
    const hit = nearestEdge(pts, { x: 150, y: 98 }, 6);
    assert.equal(hit?.edgeIndex, 0);
    assert.ok(Math.abs((hit?.distance ?? 0) - 2) < 1e-9);
});

test("nearestEdge returns null when out of reach", () => {
    const pts = [{ x: 100, y: 100 }, { x: 200, y: 100 }, { x: 200, y: 200 }, { x: 100, y: 200 }];
    assert.equal(nearestEdge(pts, { x: 150, y: 98 }, 1), null);
});

test("nearestEdge includes the closing edge", () => {
    const pts = [{ x: 100, y: 100 }, { x: 200, y: 100 }, { x: 200, y: 200 }, { x: 100, y: 200 }];
    assert.equal(nearestEdge(pts, { x: 97, y: 150 }, 6)?.edgeIndex, 3);
});
