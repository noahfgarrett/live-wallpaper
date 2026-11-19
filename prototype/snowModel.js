// @ts-check

/**
 * @typedef {object} LayerSpec
 * @property {string} name
 * @property {number} share Fraction of all flakes in this layer.
 * @property {[number, number]} sizePx Flake diameter range at 1080 px height.
 * @property {[number, number]} fallPxPerSec Fall speed range at 1080 px height.
 * @property {[number, number]} opacity
 * @property {number} softness 0 = crisp edge, 1 = very soft (bokeh).
 * @property {number} windResponse Multiplier on the shared wind offset.
 * @property {[number, number]} swayPx Horizontal sway amplitude range.
 * @property {[number, number]} swayHz Sway frequency range.
 */

export const REFERENCE_HEIGHT_PX = 1080;
export const MAX_FLAKES = 12000;
export const MAX_FLAKES_PER_MEGAPIXEL = 1500;

/** @type {readonly LayerSpec[]} */
export const LAYERS = Object.freeze([
    {
        name: "far",
        share: 0.55,
        sizePx: [1.2, 2.4],
        fallPxPerSec: [30, 55],
        opacity: [0.35, 0.6],
        softness: 0.35,
        windResponse: 0.5,
        swayPx: [4, 10],
        swayHz: [0.1, 0.25],
    },
    {
        name: "mid",
        share: 0.35,
        sizePx: [2.5, 4.5],
        fallPxPerSec: [60, 95],
        opacity: [0.6, 0.85],
        softness: 0.5,
        windResponse: 0.8,
        swayPx: [8, 18],
        swayHz: [0.15, 0.35],
    },
    {
        name: "near",
        share: 0.1,
        sizePx: [6, 12],
        fallPxPerSec: [110, 170],
        opacity: [0.35, 0.6],
        softness: 0.9,
        windResponse: 1.2,
        swayPx: [14, 30],
        swayHz: [0.2, 0.45],
    },
]);

/**
 * @param {number} densityPct 0–100
 * @param {number} megapixels Render area in millions of pixels.
 * @returns {number}
 */
export function flakeCount(densityPct, megapixels) {
    const d = clampPct(densityPct) / 100;
    return Math.min(MAX_FLAKES, Math.round(MAX_FLAKES_PER_MEGAPIXEL * d ** 1.5 * Math.max(0, megapixels)));
}

/**
 * @param {number} speedPct 0–100
 * @returns {number}
 */
export function speedMultiplier(speedPct) {
    return 0.25 + 1.5 * (clampPct(speedPct) / 100);
}

/**
 * @param {number} sizePct 0–100
 * @returns {number}
 */
export function sizeMultiplier(sizePct) {
    return 0.5 + clampPct(sizePct) / 100;
}

/**
 * Off-screen band flakes wrap through. It must not depend on any setting:
 * the shader wraps with mod(…, span), so a changing span would teleport every flake.
 * @param {number} scale Canvas height / reference height.
 * @returns {number}
 */
export function wrapMarginPx(scale) {
    const largest = Math.max(...LAYERS.map((l) => l.sizePx[1])) * sizeMultiplier(100);
    return (largest * 1.5 + 40) * scale;
}

/**
 * Accumulates fall time so a speed change alters velocity, never position.
 */
export class FallClock {
    #time = 0;

    /**
     * @param {number} dtSec
     * @param {number} speedPct
     * @returns {number} Accumulated fall time in seconds at 1× speed.
     */
    step(dtSec, speedPct) {
        this.#time += Math.max(0, dtSec) * speedMultiplier(speedPct);
        return this.#time;
    }
}

const GUST_FREQUENCIES_HZ = [0.071, 0.113, 0.029];
const GUST_WEIGHTS = [1, 0.6, 0.4];
const GUST_STRENGTH = 0.8;
const BASE_WIND_PX_PER_SEC = 120;

/**
 * Smooth wind with occasional gusts, integrated into a horizontal offset.
 */
export class WindModel {
    #time = 0;
    #offsetPx = 0;
    /** @type {number[]} */
    #phases;

    /** @param {number} seed */
    constructor(seed) {
        const random = mulberry32(seed);
        this.#phases = GUST_FREQUENCIES_HZ.map(() => random() * Math.PI * 2);
    }

    /**
     * @param {number} dtSec
     * @param {number} windPct 0–100
     * @param {number} heightPx Canvas height; wind scales with it.
     * @returns {{ windPxPerSec: number, offsetPx: number }}
     */
    step(dtSec, windPct, heightPx) {
        this.#time += Math.max(0, dtSec);
        const base = BASE_WIND_PX_PER_SEC * (clampPct(windPct) / 100) * (heightPx / REFERENCE_HEIGHT_PX);
        let g = 0;
        for (let i = 0; i < GUST_FREQUENCIES_HZ.length; i++) {
            g += GUST_WEIGHTS[i] * Math.sin(2 * Math.PI * GUST_FREQUENCIES_HZ[i] * this.#time + this.#phases[i]);
        }
        g /= 2;
        const gust = Math.max(0, g) ** 2 * GUST_STRENGTH * base;
        const windPxPerSec = base + gust;
        this.#offsetPx += windPxPerSec * Math.max(0, dtSec);
        return { windPxPerSec, offsetPx: this.#offsetPx };
    }
}

/**
 * @param {number} pct
 * @returns {number}
 */
function clampPct(pct) {
    return Math.min(100, Math.max(0, Number.isFinite(pct) ? pct : 0));
}

/**
 * @param {number} seed
 * @returns {() => number}
 */
function mulberry32(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}
