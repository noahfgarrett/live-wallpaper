// @ts-check
import { createRenderer } from "./renderer.js";
import { flakeCount, speedMultiplier, sizeMultiplier, FallClock, WindModel, LAYERS } from "./snowModel.js";
import { History } from "./history.js";
import { createRegionEditor } from "./regionEditor.js";
import { buildMask } from "./mask.js";

/** @typedef {import("./scaling.js").ScalingMode} ScalingMode */

const params = new URLSearchParams(location.search);
const isDebug = params.get("debug") === "1";

/**
 * @template {HTMLElement} T
 * @param {string} id
 * @returns {T}
 */
function el(id) {
    const node = document.getElementById(id);
    if (!node) {
        throw new Error(`Missing element #${id}`);
    }
    return /** @type {T} */ (node);
}

const canvas = /** @type {HTMLCanvasElement} */ (el("stage"));
const dropzone = /** @type {HTMLLabelElement} */ (el("dropzone"));
const fileInput = /** @type {HTMLInputElement} */ (el("file"));
const panel = el("panel");
const panelToggle = /** @type {HTMLButtonElement} */ (el("panel-toggle"));
const readout = el("readout");
const readoutFps = el("readout-fps");
const readoutFlakes = el("readout-flakes");

const controls = {
    enabled: /** @type {HTMLInputElement} */ (el("snow-enabled")),
    density: /** @type {HTMLInputElement} */ (el("density")),
    speed: /** @type {HTMLInputElement} */ (el("speed")),
    wind: /** @type {HTMLInputElement} */ (el("wind")),
    size: /** @type {HTMLInputElement} */ (el("size")),
    motionBlur: /** @type {HTMLInputElement} */ (el("motion-blur")),
    fps: /** @type {HTMLSelectElement} */ (el("fps")),
    scaling: /** @type {HTMLSelectElement} */ (el("scaling")),
};

/** @type {ReturnType<typeof createRenderer>} */
const renderer = createRenderer(canvas);
const fallClock = new FallClock();
const wind = new WindModel(20261119);

/** @type {Set<() => void>} */
export const layoutListeners = new Set();

/** @returns {ScalingMode} */
function scalingMode() {
    const v = controls.scaling.value;
    return v === "fit" || v === "stretch" || v === "center" ? v : "fill";
}

/**
 * @param {Blob} blob
 * @returns {Promise<{ w: number, h: number }>}
 */
function readImageSize(blob) {
    const url = URL.createObjectURL(blob);
    const img = new Image();
    return new Promise((resolve, reject) => {
        img.onload = () => {
            URL.revokeObjectURL(url);
            resolve({ w: img.naturalWidth, h: img.naturalHeight });
        };
        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error("not an image"));
        };
        img.src = url;
    });
}

/**
 * Decodes straight to the texture size, so huge photos are never held at full resolution.
 * @param {Blob} blob
 * @returns {Promise<void>}
 */
async function loadBlob(blob) {
    const title = /** @type {HTMLElement} */ (dropzone.querySelector(".dropzone-title"));
    const hint = /** @type {HTMLElement} */ (dropzone.querySelector(".dropzone-hint"));
    title.textContent = "Opening photo…";
    try {
        const natural = await readImageSize(blob);
        const cap = Math.min(renderer.maxTextureSize(), 2 * Math.max(canvas.width, canvas.height));
        const scale = Math.min(1, cap / Math.max(natural.w, natural.h));
        const bitmap = scale < 1
            ? await createImageBitmap(blob, {
                resizeWidth: Math.max(1, Math.round(natural.w * scale)),
                resizeHeight: Math.max(1, Math.round(natural.h * scale)),
                resizeQuality: "high",
            })
            : await createImageBitmap(blob);
        renderer.setImage(bitmap, natural, scalingMode());
        bitmap.close();
        dropzone.hidden = true;
        readout.hidden = false;
        title.textContent = "Drop a photo here or choose one";
        hint.textContent = "Your photo stays on this computer.";
        if (isDebug) {
            const t = renderer.textureSize();
            console.debug(`[preview] image=${natural.w}x${natural.h} texture=${t.width}x${t.height}`);
        }
        notifyLayout();
    } catch {
        title.textContent = "That photo couldn't be opened";
        hint.textContent = "Try a JPG, PNG or WebP file.";
    }
}

/**
 * @param {File} file
 * @returns {Promise<void>}
 */
async function loadFile(file) {
    if (!file.type.startsWith("image/")) {
        return;
    }
    await loadBlob(file);
}

/** @returns {void} */
function notifyLayout() {
    for (const listener of layoutListeners) {
        listener();
    }
}

/** @returns {void} */
function resizeCanvas() {
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    canvas.height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    notifyLayout();
}

/**
 * @param {HTMLInputElement} input
 * @param {string} suffix
 * @param {number} [digits]
 * @returns {void}
 */
function bindRange(input, suffix, digits = 0) {
    const out = document.querySelector(`output[for="${input.id}"]`);
    const update = () => {
        const min = Number(input.min);
        const max = Number(input.max);
        const v = Number(input.value);
        input.style.setProperty("--fill", `${((v - min) / (max - min)) * 100}%`);
        if (out) {
            out.textContent = `${v.toFixed(digits)}${suffix}`;
        }
    };
    input.addEventListener("input", update);
    update();
}

for (const input of [controls.density, controls.speed, controls.wind, controls.size]) {
    bindRange(input, "%");
}
bindRange(/** @type {HTMLInputElement} */ (el("feather")), "%", 1);

controls.scaling.addEventListener("change", () => {
    renderer.setScaling(scalingMode());
    notifyLayout();
});

fileInput.addEventListener("change", () => {
    const file = fileInput.files?.[0];
    if (file) {
        void loadFile(file);
    }
});

for (const type of ["dragenter", "dragover"]) {
    window.addEventListener(type, (event) => {
        event.preventDefault();
        dropzone.classList.add("is-over");
    });
}
window.addEventListener("dragleave", () => dropzone.classList.remove("is-over"));
window.addEventListener("drop", (event) => {
    event.preventDefault();
    dropzone.classList.remove("is-over");
    const file = event.dataTransfer?.files?.[0];
    if (file) {
        void loadFile(file);
    }
});

panelToggle.addEventListener("click", () => {
    const collapsed = panel.classList.toggle("is-collapsed");
    panelToggle.setAttribute("aria-expanded", String(!collapsed));
    panelToggle.title = collapsed ? "Show panel" : "Hide panel";
});

/** @returns {Record<string, unknown>} */
export function currentSettings() {
    return {
        snow: {
            enabled: controls.enabled.checked,
            densityPct: Number(controls.density.value),
            speedPct: Number(controls.speed.value),
            windPct: Number(controls.wind.value),
            sizePct: Number(controls.size.value),
            motionBlur: controls.motionBlur.checked,
            fps: controls.fps.value,
        },
        scaling: scalingMode(),
        layers: LAYERS,
    };
}

window.addEventListener("resize", resizeCanvas);
resizeCanvas();

let lastTime = performance.now();
let lastDrawn = 0;
let frames = 0;
let fpsWindowStart = lastTime;

/**
 * @param {number} now
 * @returns {void}
 */
function frame(now) {
    requestAnimationFrame(frame);
    const target = controls.fps.value;
    if (target === "30" && now - lastDrawn < 1000 / 30 - 2) {
        return;
    }
    const dt = Math.min(0.1, (now - lastTime) / 1000);
    lastTime = now;
    lastDrawn = now;

    const speedPct = Number(controls.speed.value);
    const fallTime = fallClock.step(dt, speedPct);
    const w = wind.step(dt, Number(controls.wind.value), canvas.height);
    const count = flakeCount(Number(controls.density.value), (canvas.width * canvas.height) / 1e6);

    renderer.frame({
        fallTime,
        windOffsetPx: w.offsetPx,
        windPxPerSec: w.windPxPerSec,
        speedMul: speedMultiplier(speedPct),
        timeSec: now / 1000,
        count,
        sizeMul: sizeMultiplier(Number(controls.size.value)),
        motionBlur: controls.motionBlur.checked,
        enabled: controls.enabled.checked,
    });

    frames++;
    if (now - fpsWindowStart >= 500) {
        readoutFps.textContent = `${Math.round((frames * 1000) / (now - fpsWindowStart))} fps`;
        readoutFlakes.textContent = `${controls.enabled.checked ? count.toLocaleString() : 0} flakes`;
        frames = 0;
        fpsWindowStart = now;
    }
}
requestAnimationFrame(frame);

const svg = /** @type {SVGSVGElement} */ (/** @type {unknown} */ (el("regions")));
const regionButtons = {
    add: /** @type {HTMLButtonElement} */ (el("region-add")),
    remove: /** @type {HTMLButtonElement} */ (el("region-delete")),
    undo: /** @type {HTMLButtonElement} */ (el("undo")),
    redo: /** @type {HTMLButtonElement} */ (el("redo")),
};
const invertInput = /** @type {HTMLInputElement} */ (el("region-invert"));
const featherInput = /** @type {HTMLInputElement} */ (el("feather"));
const outlinesInput = /** @type {HTMLInputElement} */ (el("region-outlines"));
const copyButton = /** @type {HTMLButtonElement} */ (el("copy-settings"));
const copyStatus = el("copy-status");

/** @type {History<import("./regions.js").RegionSet>} */
const regionHistory = new History({ regions: [], invert: false, featherPct: Number(featherInput.value) });

/** @returns {import("./scaling.js").Rect | null} */
function cssDestination() {
    const d = renderer.destination();
    if (!d || canvas.clientWidth === 0) {
        return null;
    }
    const k = canvas.width / canvas.clientWidth;
    return { x: d.x / k, y: d.y / k, w: d.w / k, h: d.h / k };
}

/**
 * @param {import("./regions.js").RegionSet} set
 * @returns {void}
 */
function applyRegions(set) {
    const d = renderer.destination();
    renderer.setMask(d ? buildMask(set, d, canvas.width, canvas.height) : null);
    invertInput.checked = set.invert;
    featherInput.value = String(set.featherPct);
    featherInput.dispatchEvent(new Event("input"));
    regionButtons.remove.disabled = editor.selectedRegionId() === null;
    regionButtons.undo.disabled = !regionHistory.canUndo;
    regionButtons.redo.disabled = !regionHistory.canRedo;
}

const editor = createRegionEditor(svg, cssDestination, regionHistory, applyRegions);

layoutListeners.add(() => {
    editor.setEnabled(renderer.destination() !== null);
    editor.render();
    applyRegions(editor.state());
});

regionButtons.add.addEventListener("click", () => {
    if (renderer.destination()) {
        editor.addRegion();
    }
});
regionButtons.remove.addEventListener("click", () => editor.deleteSelected());
regionButtons.undo.addEventListener("click", () => editor.undo());
regionButtons.redo.addEventListener("click", () => editor.redo());
invertInput.addEventListener("change", () => editor.update({ invert: invertInput.checked }, true));
featherInput.addEventListener("input", (event) => {
    if (event.isTrusted) {
        editor.update({ featherPct: Number(featherInput.value) }, false);
    }
});
featherInput.addEventListener("change", () => editor.update({ featherPct: Number(featherInput.value) }, true));
outlinesInput.addEventListener("change", () => svg.classList.toggle("hide-outlines", !outlinesInput.checked));

copyButton.addEventListener("click", async () => {
    const json = JSON.stringify({ ...currentSettings(), regions: editor.state() }, null, 4);
    try {
        await navigator.clipboard.writeText(json);
        copyStatus.textContent = "Copied";
    } catch {
        copyStatus.textContent = "Couldn't copy. Clipboard access was blocked.";
    }
    setTimeout(() => {
        copyStatus.textContent = "";
    }, 2500);
});

const debugImage = params.get("image");
if (debugImage && /^\.local\/[\w.-]+$/.test(debugImage)) {
    void fetch(debugImage).then((r) => r.blob()).then(loadBlob);
}

export { renderer, canvas, scalingMode };
