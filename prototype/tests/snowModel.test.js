// @ts-check
import { test } from "node:test";
import assert from "node:assert/strict";
import { flakeCount, speedMultiplier, sizeMultiplier, FallClock, WindModel, LAYERS } from "../snowModel.js";

test("density 0% draws no flakes", () => assert.equal(flakeCount(0, 2.0736), 0));

test("density 50% at 1080p is 1100 flakes", () => assert.equal(flakeCount(50, 2.0736), 1100));

test("density 100% at 1080p is 3110 flakes", () => assert.equal(flakeCount(100, 2.0736), 3110));

test("flake count is capped at MAX_FLAKES", () => assert.equal(flakeCount(100, 33.2), 12000));

test("speed 0/50/100% maps to 0.25/1.0/1.75", () => {
    assert.equal(speedMultiplier(0), 0.25);
    assert.equal(speedMultiplier(50), 1);
    assert.equal(speedMultiplier(100), 1.75);
});

test("size 0/50/100% maps to 0.5/1.0/1.5", () => {
    assert.equal(sizeMultiplier(0), 0.5);
    assert.equal(sizeMultiplier(50), 1);
    assert.equal(sizeMultiplier(100), 1.5);
});

test("layer shares sum to 1", () => {
    const total = LAYERS.reduce((sum, layer) => sum + layer.share, 0);
    assert.ok(Math.abs(total - 1) < 1e-9);
});

test("fall clock does not jump when speed changes", () => {
    const clock = new FallClock();
    let t = 0;
    for (let i = 0; i < 60; i++) {
        t = clock.step(1 / 60, 10);
    }
    const next = clock.step(1 / 60, 100);
    assert.ok(next - t <= (1 / 60) * 1.75 + 1e-9);
});

test("wind 0% never moves flakes sideways, gusts included", () => {
    const wind = new WindModel(42);
    for (let i = 0; i < 600; i++) {
        assert.equal(wind.step(1 / 60, 0, 1080).offsetPx, 0);
    }
});

test("wind offset changes smoothly between frames", () => {
    const wind = new WindModel(7);
    let prev = wind.step(1 / 60, 100, 1080);
    for (let i = 0; i < 3600; i++) {
        const cur = wind.step(1 / 60, 100, 1080);
        assert.ok(Math.abs(cur.offsetPx - prev.offsetPx) <= Math.abs(cur.windPxPerSec) / 60 + 1e-6);
        prev = cur;
    }
});

test("wind model is deterministic for a seed", () => {
    const a = new WindModel(3);
    const b = new WindModel(3);
    for (let i = 0; i < 100; i++) {
        assert.deepEqual(a.step(0.016, 60, 1080), b.step(0.016, 60, 1080));
    }
});

test("gusts vary the wind over time", () => {
    const wind = new WindModel(11);
    const speeds = new Set();
    for (let i = 0; i < 6000; i++) {
        speeds.add(Math.round(wind.step(1 / 60, 100, 1080).windPxPerSec));
    }
    assert.ok(speeds.size > 10);
});
