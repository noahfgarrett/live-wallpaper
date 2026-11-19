// @ts-check

/** @typedef {{ x: number, y: number }} Point */
/** @typedef {{ id: string, points: Point[] }} Region */
/** @typedef {{ regions: Region[], invert: boolean, featherPct: number }} RegionSet */

export const MIN_POINTS = 3;

/**
 * @param {string} id
 * @param {Point} [center]
 * @param {number} [size]
 * @returns {Region} Corners in order TL, TR, BR, BL.
 */
export function createQuad(id, center = { x: 0.5, y: 0.5 }, size = 0.3) {
    const h = size / 2;
    return {
        id,
        points: [
            clampPoint({ x: center.x - h, y: center.y - h }),
            clampPoint({ x: center.x + h, y: center.y - h }),
            clampPoint({ x: center.x + h, y: center.y + h }),
            clampPoint({ x: center.x - h, y: center.y + h }),
        ],
    };
}

/**
 * @param {Region} region
 * @param {number} index
 * @param {Point} p
 * @returns {Region}
 */
export function moveVertex(region, index, p) {
    return { ...region, points: region.points.map((q, i) => (i === index ? clampPoint(p) : q)) };
}

/**
 * @param {Region} region
 * @param {number} edgeIndex The new point goes after points[edgeIndex].
 * @param {Point} p
 * @returns {Region}
 */
export function insertVertex(region, edgeIndex, p) {
    const points = region.points.slice();
    points.splice(edgeIndex + 1, 0, clampPoint(p));
    return { ...region, points };
}

/**
 * @param {Region} region
 * @param {number} index
 * @returns {Region}
 */
export function removeVertex(region, index) {
    if (region.points.length <= MIN_POINTS) {
        return region;
    }
    return { ...region, points: region.points.filter((_, i) => i !== index) };
}

/**
 * Moves the whole region, stopping when any point would leave the image.
 * @param {Region} region
 * @param {number} dx
 * @param {number} dy
 * @returns {Region}
 */
export function translate(region, dx, dy) {
    const xs = region.points.map((p) => p.x);
    const ys = region.points.map((p) => p.y);
    const cdx = Math.min(1 - Math.max(...xs), Math.max(-Math.min(...xs), dx));
    const cdy = Math.min(1 - Math.max(...ys), Math.max(-Math.min(...ys), dy));
    return { ...region, points: region.points.map((p) => ({ x: p.x + cdx, y: p.y + cdy })) };
}

/**
 * @param {Point[]} points Polygon in any coordinate space.
 * @param {Point} p
 * @param {number} maxDist
 * @returns {{ edgeIndex: number, distance: number } | null}
 */
export function nearestEdge(points, p, maxDist) {
    /** @type {{ edgeIndex: number, distance: number } | null} */
    let best = null;
    for (let i = 0; i < points.length; i++) {
        const d = distanceToSegment(p, points[i], points[(i + 1) % points.length]);
        if (d <= maxDist && (best === null || d < best.distance)) {
            best = { edgeIndex: i, distance: d };
        }
    }
    return best;
}

/**
 * @param {Region} region
 * @returns {boolean}
 */
export function isPlane(region) {
    return region.points.length === 4;
}

/**
 * @param {Point} p
 * @param {Point[]} points
 * @returns {boolean}
 */
export function containsPoint(points, p) {
    let inside = false;
    for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
        const a = points[i];
        const b = points[j];
        if ((a.y > p.y) !== (b.y > p.y) && p.x < ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x) {
            inside = !inside;
        }
    }
    return inside;
}

/**
 * @param {Point} p
 * @returns {Point}
 */
function clampPoint(p) {
    return { x: Math.min(1, Math.max(0, p.x)), y: Math.min(1, Math.max(0, p.y)) };
}

/**
 * @param {Point} p
 * @param {Point} a
 * @param {Point} b
 * @returns {number}
 */
function distanceToSegment(p, a, b) {
    const vx = b.x - a.x;
    const vy = b.y - a.y;
    const len2 = vx * vx + vy * vy;
    const t = len2 === 0 ? 0 : Math.min(1, Math.max(0, ((p.x - a.x) * vx + (p.y - a.y) * vy) / len2));
    return Math.hypot(p.x - (a.x + t * vx), p.y - (a.y + t * vy));
}
