// @ts-check

export const ATLAS_CELLS = 4;
export const ATLAS_CELL_PX = 64;

/**
 * Four soft white flake sprites side by side on a transparent background.
 * @returns {HTMLCanvasElement}
 */
export function buildFlakeAtlas() {
    const canvas = document.createElement("canvas");
    canvas.width = ATLAS_CELL_PX * ATLAS_CELLS;
    canvas.height = ATLAS_CELL_PX;
    const ctx = /** @type {CanvasRenderingContext2D} */ (canvas.getContext("2d"));
    const c = ATLAS_CELL_PX / 2;

    drawRadial(ctx, c, c, [[0, 1], [0.35, 0.8], [1, 0]]);
    drawRadial(ctx, ATLAS_CELL_PX + c, c, [[0, 1], [0.5, 0.92], [0.78, 0.25], [1, 0]]);

    ctx.save();
    ctx.translate(ATLAS_CELL_PX * 2 + c, c);
    ctx.filter = "blur(1.6px)";
    ctx.strokeStyle = "white";
    ctx.lineCap = "round";
    ctx.lineWidth = 4.5;
    for (let i = 0; i < 6; i++) {
        const a = (i / 6) * Math.PI * 2;
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(Math.cos(a) * 22, Math.sin(a) * 22);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(Math.cos(a) * 12, Math.sin(a) * 12);
        ctx.lineTo(Math.cos(a + 0.5) * 17, Math.sin(a + 0.5) * 17);
        ctx.moveTo(Math.cos(a) * 12, Math.sin(a) * 12);
        ctx.lineTo(Math.cos(a - 0.5) * 17, Math.sin(a - 0.5) * 17);
        ctx.stroke();
    }
    ctx.restore();

    ctx.save();
    ctx.filter = "blur(2.5px)";
    ctx.fillStyle = "white";
    const blobs = [[0, 0, 11], [-9, -5, 7], [8, -7, 6], [6, 9, 7], [-7, 8, 5]];
    for (const [dx, dy, r] of blobs) {
        ctx.beginPath();
        ctx.arc(ATLAS_CELL_PX * 3 + c + dx, c + dy, r, 0, Math.PI * 2);
        ctx.fill();
    }
    ctx.restore();

    return canvas;
}

/**
 * @param {CanvasRenderingContext2D} ctx
 * @param {number} x
 * @param {number} y
 * @param {[number, number][]} stops Offset and alpha pairs.
 * @returns {void}
 */
function drawRadial(ctx, x, y, stops) {
    const r = ATLAS_CELL_PX / 2 - 2;
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    for (const [offset, alpha] of stops) {
        g.addColorStop(offset, `rgba(255,255,255,${alpha})`);
    }
    ctx.fillStyle = g;
    ctx.fillRect(x - r, y - r, r * 2, r * 2);
}
