// @ts-check

/** @typedef {{ w: number, h: number }} Size */
/** @typedef {{ x: number, y: number, w: number, h: number }} Rect */
/** @typedef {{ x: number, y: number }} Point */
/** @typedef {"fill" | "fit" | "stretch" | "center"} ScalingMode */

/**
 * Where the image lands inside the target. May extend past the target (Fill, Center).
 * @param {Size} image
 * @param {Size} target
 * @param {ScalingMode} mode
 * @returns {Rect}
 */
export function computeDestination(image, target, mode) {
    if (!(image.w > 0 && image.h > 0 && target.w > 0 && target.h > 0)) {
        throw new RangeError("Image and target sizes must be positive.");
    }
    switch (mode) {
        case "stretch":
            return { x: 0, y: 0, w: target.w, h: target.h };
        case "center":
            return centered(image.w, image.h, target);
        case "fill": {
            const scale = Math.max(target.w / image.w, target.h / image.h);
            return centered(image.w * scale, image.h * scale, target);
        }
        case "fit": {
            const scale = Math.min(target.w / image.w, target.h / image.h);
            return centered(image.w * scale, image.h * scale, target);
        }
        default: {
            /** @type {never} */
            const unreachable = mode;
            throw new RangeError(`Unknown scaling mode: ${String(unreachable)}`);
        }
    }
}

/**
 * @param {Point} p Normalized image coordinates (0–1).
 * @param {Rect} dest
 * @returns {Point} Target pixels.
 */
export function imageToScreen(p, dest) {
    return { x: dest.x + p.x * dest.w, y: dest.y + p.y * dest.h };
}

/**
 * @param {Point} p Target pixels.
 * @param {Rect} dest
 * @returns {Point} Normalized image coordinates, not clamped.
 */
export function screenToImage(p, dest) {
    return { x: (p.x - dest.x) / dest.w, y: (p.y - dest.y) / dest.h };
}

/**
 * @param {number} w
 * @param {number} h
 * @param {Size} target
 * @returns {Rect}
 */
function centered(w, h, target) {
    return { x: (target.w - w) / 2, y: (target.h - h) / 2, w, h };
}
