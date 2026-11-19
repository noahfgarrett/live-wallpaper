// @ts-check
import { createQuad, moveVertex, insertVertex, removeVertex, translate, nearestEdge, containsPoint } from "./regions.js";
import { imageToScreen, screenToImage } from "./scaling.js";

/** @typedef {import("./regions.js").Region} Region */
/** @typedef {import("./regions.js").RegionSet} RegionSet */
/** @typedef {import("./regions.js").Point} Point */
/** @typedef {import("./scaling.js").Rect} Rect */
/** @template T @typedef {import("./history.js").History<T>} History */

const SVG_NS = "http://www.w3.org/2000/svg";
const HANDLE_RADIUS = 6;
const EDGE_REACH_PX = 6;
const VERTEX_CLEARANCE_PX = 12;

/**
 * @typedef {object} RegionEditor
 * @property {() => void} render
 * @property {(on: boolean) => void} setEnabled
 * @property {() => RegionSet} state
 * @property {() => string | null} selectedRegionId
 * @property {() => void} addRegion
 * @property {() => void} deleteSelected
 * @property {(patch: Partial<Pick<RegionSet, "invert" | "featherPct">>, commit: boolean) => void} update
 * @property {() => void} undo
 * @property {() => void} redo
 */

/**
 * @typedef {{ kind: "vertex", regionId: string, index: number }
 *   | { kind: "body", regionId: string, start: Point, origin: Region }} Drag
 */

/**
 * @param {SVGSVGElement} svg
 * @param {() => Rect | null} getDest Image destination in CSS pixels.
 * @param {History<RegionSet>} history
 * @param {(set: RegionSet) => void} onChange
 * @returns {RegionEditor}
 */
export function createRegionEditor(svg, getDest, history, onChange) {
    /** @type {RegionSet} */
    let working = history.current;
    /** @type {string | null} */
    let selectedId = null;
    /** @type {number | null} */
    let selectedVertex = null;
    /** @type {Drag | null} */
    let drag = null;
    let dragChanged = false;
    /** @type {{ edgeIndex: number, point: Point } | null} */
    let ghost = null;
    let nextId = 1;

    /**
     * @param {RegionSet} set
     * @param {boolean} commit
     * @returns {void}
     */
    function setState(set, commit) {
        working = set;
        if (commit) {
            history.push(set);
        }
        render();
        onChange(working);
    }

    /**
     * @param {string} id
     * @param {(r: Region) => Region} fn
     * @returns {RegionSet}
     */
    function mapRegion(id, fn) {
        return { ...working, regions: working.regions.map((r) => (r.id === id ? fn(r) : r)) };
    }

    /**
     * @param {PointerEvent} event
     * @returns {Point}
     */
    function pointerPos(event) {
        const rect = svg.getBoundingClientRect();
        return { x: event.clientX - rect.left, y: event.clientY - rect.top };
    }

    /**
     * @param {Region} region
     * @param {Rect} dest
     * @returns {Point[]}
     */
    function screenPoints(region, dest) {
        return region.points.map((p) => imageToScreen(p, dest));
    }

    /** @returns {void} */
    function render() {
        svg.replaceChildren();
        const dest = getDest();
        if (!dest) {
            return;
        }
        for (const region of working.regions) {
            const pts = screenPoints(region, dest);
            const isSelected = region.id === selectedId;
            const poly = document.createElementNS(SVG_NS, "polygon");
            poly.setAttribute("points", pts.map((p) => `${p.x},${p.y}`).join(" "));
            poly.setAttribute("class", `region-shape${isSelected ? " is-selected" : ""}`);
            poly.dataset.regionId = region.id;
            svg.append(poly);
        }
        const selected = working.regions.find((r) => r.id === selectedId);
        if (selected) {
            screenPoints(selected, dest).forEach((p, i) => {
                const handle = document.createElementNS(SVG_NS, "circle");
                handle.setAttribute("cx", String(p.x));
                handle.setAttribute("cy", String(p.y));
                handle.setAttribute("r", String(HANDLE_RADIUS));
                handle.setAttribute("class", `region-handle${i === selectedVertex ? " is-selected" : ""}`);
                handle.dataset.vertex = String(i);
                svg.append(handle);
            });
            if (ghost && !drag) {
                const g = imageToScreen(ghost.point, dest);
                const plus = document.createElementNS(SVG_NS, "circle");
                plus.setAttribute("cx", String(g.x));
                plus.setAttribute("cy", String(g.y));
                plus.setAttribute("r", String(HANDLE_RADIUS - 1));
                plus.setAttribute("class", "region-ghost");
                plus.dataset.ghost = "1";
                svg.append(plus);
            }
        }
    }

    /**
     * @param {Point} pos
     * @param {Rect} dest
     * @returns {Region | null}
     */
    function regionAt(pos, dest) {
        for (let i = working.regions.length - 1; i >= 0; i--) {
            if (containsPoint(screenPoints(working.regions[i], dest), pos)) {
                return working.regions[i];
            }
        }
        return null;
    }

    svg.addEventListener("pointerdown", (event) => {
        const dest = getDest();
        if (!dest || event.button !== 0) {
            return;
        }
        const pos = pointerPos(event);
        const target = /** @type {SVGElement} */ (event.target);
        if (target.dataset.vertex !== undefined && selectedId) {
            selectedVertex = Number(target.dataset.vertex);
            drag = { kind: "vertex", regionId: selectedId, index: selectedVertex };
        } else if (target.dataset.ghost && ghost && selectedId) {
            const at = ghost;
            working = mapRegion(selectedId, (r) => insertVertex(r, at.edgeIndex, at.point));
            selectedVertex = at.edgeIndex + 1;
            drag = { kind: "vertex", regionId: selectedId, index: selectedVertex };
            dragChanged = true;
            ghost = null;
        } else {
            const hit = regionAt(pos, dest);
            selectedId = hit?.id ?? null;
            selectedVertex = null;
            drag = hit ? { kind: "body", regionId: hit.id, start: screenToImage(pos, dest), origin: hit } : null;
        }
        svg.setPointerCapture(event.pointerId);
        render();
        onChange(working);
    });

    svg.addEventListener("pointermove", (event) => {
        const dest = getDest();
        if (!dest) {
            return;
        }
        const pos = pointerPos(event);
        if (drag) {
            const current = drag;
            if (current.kind === "vertex") {
                working = mapRegion(current.regionId, (r) => moveVertex(r, current.index, screenToImage(pos, dest)));
            } else {
                const p = screenToImage(pos, dest);
                working = mapRegion(current.regionId, () => translate(current.origin, p.x - current.start.x, p.y - current.start.y));
            }
            dragChanged = true;
            render();
            onChange(working);
            return;
        }
        const selected = working.regions.find((r) => r.id === selectedId);
        let nextGhost = null;
        if (selected) {
            const pts = screenPoints(selected, dest);
            const nearVertex = pts.some((p) => Math.hypot(p.x - pos.x, p.y - pos.y) < VERTEX_CLEARANCE_PX);
            const hit = nearVertex ? null : nearestEdge(pts, pos, EDGE_REACH_PX);
            if (hit) {
                const a = pts[hit.edgeIndex];
                const b = pts[(hit.edgeIndex + 1) % pts.length];
                const vx = b.x - a.x;
                const vy = b.y - a.y;
                const t = Math.min(1, Math.max(0, ((pos.x - a.x) * vx + (pos.y - a.y) * vy) / (vx * vx + vy * vy || 1)));
                nextGhost = { edgeIndex: hit.edgeIndex, point: screenToImage({ x: a.x + t * vx, y: a.y + t * vy }, dest) };
            }
        }
        if ((ghost === null) !== (nextGhost === null) || nextGhost) {
            ghost = nextGhost;
            render();
        }
    });

    /** @returns {void} */
    function endDrag() {
        if (drag && dragChanged) {
            history.push(working);
            onChange(working);
        }
        drag = null;
        dragChanged = false;
        render();
    }
    svg.addEventListener("pointerup", endDrag);
    svg.addEventListener("pointercancel", endDrag);

    svg.addEventListener("contextmenu", (event) => {
        const target = /** @type {SVGElement} */ (event.target);
        if (target.dataset.vertex !== undefined && selectedId) {
            event.preventDefault();
            const index = Number(target.dataset.vertex);
            const id = selectedId;
            selectedVertex = null;
            setState(mapRegion(id, (r) => removeVertex(r, index)), true);
        }
    });

    window.addEventListener("keydown", (event) => {
        const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement;
        if ((event.key === "Delete" || event.key === "Backspace") && !typing && selectedId && selectedVertex !== null) {
            event.preventDefault();
            const index = selectedVertex;
            const id = selectedId;
            selectedVertex = null;
            setState(mapRegion(id, (r) => removeVertex(r, index)), true);
        } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "z") {
            event.preventDefault();
            if (event.shiftKey) {
                api.redo();
            } else {
                api.undo();
            }
        }
    });

    /** @type {RegionEditor} */
    const api = {
        render,
        setEnabled(on) {
            svg.classList.toggle("is-editing", on);
        },
        state() {
            return working;
        },
        selectedRegionId() {
            return selectedId;
        },
        addRegion() {
            const region = createQuad(`r${nextId++}`);
            selectedId = region.id;
            selectedVertex = null;
            setState({ ...working, regions: [...working.regions, region] }, true);
        },
        deleteSelected() {
            if (!selectedId) {
                return;
            }
            const id = selectedId;
            selectedId = null;
            selectedVertex = null;
            setState({ ...working, regions: working.regions.filter((r) => r.id !== id) }, true);
        },
        update(patch, commit) {
            setState({ ...working, ...patch }, commit);
        },
        undo() {
            selectedVertex = null;
            setState(history.undo(), false);
        },
        redo() {
            selectedVertex = null;
            setState(history.redo(), false);
        },
    };
    return api;
}
