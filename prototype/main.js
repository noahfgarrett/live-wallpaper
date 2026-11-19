// @ts-check

const canvas = /** @type {HTMLCanvasElement} */ (document.getElementById("stage"));
const dropzone = /** @type {HTMLLabelElement} */ (document.getElementById("dropzone"));
const fileInput = /** @type {HTMLInputElement} */ (document.getElementById("file"));
const panel = /** @type {HTMLElement} */ (document.getElementById("panel"));
const panelToggle = /** @type {HTMLButtonElement} */ (document.getElementById("panel-toggle"));

/** @type {HTMLImageElement | null} */
let image = null;

/**
 * @param {File} file
 * @returns {Promise<void>}
 */
async function loadFile(file) {
    if (!file.type.startsWith("image/")) {
        return;
    }
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.src = url;
    try {
        await img.decode();
        image = img;
        dropzone.hidden = true;
    } finally {
        URL.revokeObjectURL(url);
    }
}

/** @returns {void} */
function resizeCanvas() {
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(canvas.clientWidth * dpr);
    canvas.height = Math.round(canvas.clientHeight * dpr);
}

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

window.addEventListener("resize", resizeCanvas);
resizeCanvas();

/** @returns {void} */
function frame() {
    requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
