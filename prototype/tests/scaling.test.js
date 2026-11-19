// @ts-check
import { test } from "node:test";
import assert from "node:assert/strict";
import { computeDestination, imageToScreen, screenToImage } from "../scaling.js";

const square = { w: 1000, h: 1000 };
const hd = { w: 1920, h: 1080 };

test("fill 1000x1000 into 1920x1080", () =>
    assert.deepEqual(computeDestination(square, hd, "fill"), { x: 0, y: -420, w: 1920, h: 1920 }));

test("fit 1000x1000 into 1920x1080", () =>
    assert.deepEqual(computeDestination(square, hd, "fit"), { x: 420, y: 0, w: 1080, h: 1080 }));

test("stretch fills the target exactly", () =>
    assert.deepEqual(computeDestination(square, hd, "stretch"), { x: 0, y: 0, w: 1920, h: 1080 }));

test("center keeps native size", () =>
    assert.deepEqual(computeDestination(square, hd, "center"), { x: 460, y: 40, w: 1000, h: 1000 }));

test("center crops a larger image symmetrically", () =>
    assert.deepEqual(computeDestination({ w: 4000, h: 3000 }, hd, "center"), { x: -1040, y: -960, w: 4000, h: 3000 }));

test("screen/image round trip is stable under fill", () => {
    const dest = computeDestination({ w: 4000, h: 3000 }, { w: 1280, h: 720 }, "fill");
    const p = screenToImage(imageToScreen({ x: 0.3, y: 0.7 }, dest), dest);
    assert.ok(Math.abs(p.x - 0.3) < 1e-9 && Math.abs(p.y - 0.7) < 1e-9);
});

test("screen/image round trip is stable under fit", () => {
    const dest = computeDestination({ w: 3000, h: 4000 }, { w: 1280, h: 720 }, "fit");
    const p = screenToImage(imageToScreen({ x: 0.9, y: 0.1 }, dest), dest);
    assert.ok(Math.abs(p.x - 0.9) < 1e-9 && Math.abs(p.y - 0.1) < 1e-9);
});

test("zero-size image throws RangeError", () =>
    assert.throws(() => computeDestination({ w: 0, h: 10 }, { w: 10, h: 10 }, "fill"), RangeError));
