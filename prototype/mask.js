// @ts-check
import { imageToScreen } from "./scaling.js";

/** @typedef {import("./regions.js").RegionSet} RegionSet */
/** @typedef {import("./scaling.js").Rect} Rect */

/** @type {HTMLCanvasElement | null} */
let maskCanvas = null;

/**
 * Rasterizes regions into a white-on-black mask at render resolution.
 * @param {RegionSet} set
 * @param {Rect} dest Image destination in canvas pixels.
 * @param {number} width
 * @param {number} height
 * @returns {HTMLCanvasElement | null} null when there are no regions (effect shows everywhere).
 */
export function buildMask(set, dest, width, height) {
    if (set.regions.length === 0) {
        return null;
    }
    maskCanvas ??= document.createElement("canvas");
    if (maskCanvas.width !== width || maskCanvas.height !== height) {
        maskCanvas.width = width;
        maskCanvas.height = height;
    }
    const ctx = /** @type {CanvasRenderingContext2D} */ (maskCanvas.getContext("2d"));
    ctx.save();
    ctx.globalCompositeOperation = "source-over";
    ctx.filter = "none";
    ctx.fillStyle = set.invert ? "white" : "black";
    ctx.fillRect(0, 0, width, height);

    const featherPx = (Math.max(0, set.featherPct) / 100) * dest.w;
    ctx.filter = featherPx > 0.5 ? `blur(${featherPx / 2}px)` : "none";
    ctx.globalCompositeOperation = set.invert ? "destination-out" : "source-over";
    ctx.fillStyle = "white";
    ctx.beginPath();
    for (const region of set.regions) {
        region.points.forEach((p, i) => {
            const s = imageToScreen(p, dest);
            if (i === 0) {
                ctx.moveTo(s.x, s.y);
            } else {
                ctx.lineTo(s.x, s.y);
            }
        });
        ctx.closePath();
    }
    ctx.fill("nonzero");
    ctx.restore();

    if (set.invert) {
        ctx.save();
        ctx.globalCompositeOperation = "destination-over";
        ctx.fillStyle = "black";
        ctx.fillRect(0, 0, width, height);
        ctx.restore();
    }
    return maskCanvas;
}
