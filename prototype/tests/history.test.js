// @ts-check
import { test } from "node:test";
import assert from "node:assert/strict";
import { History } from "../history.js";

test("undo returns to the previous state", () => {
    const h = new History(0);
    h.push(1);
    h.push(2);
    assert.equal(h.undo(), 1);
    assert.equal(h.current, 1);
});

test("redo restores an undone state", () => {
    const h = new History(0);
    h.push(1);
    h.undo();
    assert.equal(h.redo(), 1);
});

test("a push after undo clears redo", () => {
    const h = new History(0);
    h.push(1);
    h.undo();
    h.push(5);
    assert.equal(h.canRedo, false);
    assert.equal(h.redo(), 5);
});

test("undo at the start stays put", () => {
    const h = new History("a");
    assert.equal(h.canUndo, false);
    assert.equal(h.undo(), "a");
});

test("history keeps at most 100 entries", () => {
    const h = new History(0);
    for (let i = 1; i <= 150; i++) {
        h.push(i);
    }
    let steps = 0;
    while (h.canUndo) {
        h.undo();
        steps++;
    }
    assert.equal(steps, 99);
    assert.equal(h.current, 51);
});
