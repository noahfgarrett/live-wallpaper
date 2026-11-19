// @ts-check
import { LAYERS, REFERENCE_HEIGHT_PX, MAX_FLAKES, wrapMarginPx } from "./snowModel.js";
import { computeDestination } from "./scaling.js";
import { buildFlakeAtlas, ATLAS_CELLS } from "./flakeAtlas.js";

/** @typedef {import("./scaling.js").ScalingMode} ScalingMode */
/** @typedef {import("./scaling.js").Rect} Rect */

/**
 * @typedef {object} FrameState
 * @property {number} fallTime Accumulated fall time from FallClock.
 * @property {number} windOffsetPx Accumulated wind offset from WindModel.
 * @property {number} windPxPerSec Current wind speed, for motion blur.
 * @property {number} speedMul Current speed multiplier, for motion blur.
 * @property {number} timeSec Wall time, for sway and spin.
 * @property {number} count Flakes to draw.
 * @property {number} sizeMul
 * @property {boolean} motionBlur
 * @property {boolean} enabled
 */

/**
 * @typedef {object} Renderer
 * @property {(bitmap: ImageBitmap, natural: { w: number, h: number }, mode: ScalingMode) => void} setImage
 * @property {() => number} maxTextureSize
 * @property {(mode: ScalingMode) => void} setScaling
 * @property {(source: HTMLCanvasElement | null) => void} setMask
 * @property {() => Rect | null} destination
 * @property {(state: FrameState) => void} frame
 * @property {() => { width: number, height: number }} textureSize
 * @property {() => void} dispose
 */

const BG_VS = `#version 300 es
uniform vec4 uDest;
uniform vec2 uResolution;
out vec2 vUv;
void main() {
    vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
    vUv = corner;
    vec2 px = uDest.xy + corner * uDest.zw;
    gl_Position = vec4(px.x / uResolution.x * 2.0 - 1.0, 1.0 - px.y / uResolution.y * 2.0, 0.0, 1.0);
}`;

const BG_FS = `#version 300 es
precision mediump float;
uniform sampler2D uImage;
in vec2 vUv;
out vec4 outColor;
void main() {
    outColor = vec4(texture(uImage, vUv).rgb, 1.0);
}`;

const SNOW_VS = `#version 300 es
precision highp float;
uniform vec2 uResolution;
uniform float uFallTime;
uniform float uWindOffset;
uniform float uWindSpeed;
uniform float uSpeedMul;
uniform float uTime;
uniform float uSizeMul;
uniform float uScale;
uniform float uMargin;
uniform float uBlur;
uniform vec3 uShares;
uniform vec4 uLayerA[3];
uniform vec4 uLayerB[3];
uniform vec4 uLayerC[3];
out vec2 vUv;
out float vAlpha;
out float vSoft;
out float vSprite;

uint hash(uint x) {
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float rnd(uint id, uint k) {
    return float(hash(id * 0x9e3779b9u + k * 0x85ebca6bu) & 0xffffffu) / 16777216.0;
}

void main() {
    uint id = uint(gl_InstanceID);
    float r = rnd(id, 0u);
    int layer = r < uShares.x ? 0 : (r < uShares.y ? 1 : 2);
    vec4 a = uLayerA[layer];
    vec4 b = uLayerB[layer];
    vec4 c = uLayerC[layer];

    float size = mix(a.x, a.y, rnd(id, 1u)) * uSizeMul * uScale;
    float fall = mix(a.z, a.w, rnd(id, 2u)) * uScale;
    float swayAmp = mix(c.x, c.y, rnd(id, 4u)) * uScale;
    float swayHz = mix(c.z, c.w, rnd(id, 5u));
    float phase = rnd(id, 6u) * 6.2831853;
    float windResponse = b.w;

    vec2 span = uResolution + 2.0 * uMargin;
    float swayArg = 6.2831853 * swayHz * uTime + phase;
    float y = mod(rnd(id, 3u) * span.y + uFallTime * fall, span.y) - uMargin;
    float x = mod(rnd(id, 7u) * span.x + uWindOffset * windResponse + swayAmp * sin(swayArg), span.x) - uMargin;

    vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) * 2.0 - 1.0;
    vUv = corner * 0.5 + 0.5;

    vec2 offset;
    float alpha = mix(b.x, b.y, rnd(id, 8u));
    if (uBlur > 0.5) {
        vec2 velocity = vec2(uWindSpeed * windResponse + swayAmp * 6.2831853 * swayHz * cos(swayArg), fall * uSpeedMul);
        float speed = max(length(velocity), 1e-3);
        vec2 along = velocity / speed;
        vec2 across = vec2(-along.y, along.x);
        float streak = speed * 0.04;
        offset = across * corner.x * size * 0.5 + along * corner.y * (size + streak) * 0.5;
        alpha *= size / (size + streak * 0.6);
    } else {
        float angle = rnd(id, 9u) * 6.2831853 + (rnd(id, 10u) - 0.5) * 3.0 * uTime;
        float s = sin(angle);
        float co = cos(angle);
        vec2 local = corner * size * 0.5;
        offset = vec2(local.x * co - local.y * s, local.x * s + local.y * co);
    }

    vec2 px = vec2(x, y) + offset;
    vAlpha = alpha;
    vSoft = b.z;
    vSprite = floor(rnd(id, 11u) * ${ATLAS_CELLS.toFixed(1)});
    gl_Position = vec4(px.x / uResolution.x * 2.0 - 1.0, 1.0 - px.y / uResolution.y * 2.0, 0.0, 1.0);
}`;

const SNOW_FS = `#version 300 es
precision highp float;
uniform sampler2D uAtlas;
uniform sampler2D uMask;
uniform vec2 uResolution;
in vec2 vUv;
in float vAlpha;
in float vSoft;
in float vSprite;
out vec4 outColor;
void main() {
    float sprite = texture(uAtlas, vec2((vSprite + vUv.x) / ${ATLAS_CELLS.toFixed(1)}, vUv.y)).a;
    vec2 d = vUv * 2.0 - 1.0;
    float blob = exp(-dot(d, d) * 3.2) * (1.0 - smoothstep(0.85, 1.0, length(d)));
    float shape = mix(sprite, blob, vSoft);
    float mask = texture(uMask, gl_FragCoord.xy / uResolution).r;
    float a = shape * vAlpha * mask;
    outColor = vec4(vec3(0.95, 0.975, 1.0) * a, a);
}`;

/**
 * @param {HTMLCanvasElement} canvas
 * @returns {Renderer}
 */
export function createRenderer(canvas) {
    const gl = canvas.getContext("webgl2", { alpha: false, antialias: false, premultipliedAlpha: true });
    if (!gl) {
        throw new Error("WebGL2 is not available in this browser.");
    }
    const bgProgram = createProgram(gl, BG_VS, BG_FS);
    const snowProgram = createProgram(gl, SNOW_VS, SNOW_FS);
    const vao = gl.createVertexArray();

    const atlas = createTexture(gl);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, buildFlakeAtlas());
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);

    const whiteMask = createTexture(gl);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
    const mask = createTexture(gl);
    let hasMask = false;

    const imageTexture = createTexture(gl);
    /** @type {{ w: number, h: number } | null} */
    let imageSize = null;
    let texSize = { width: 0, height: 0 };
    /** @type {ScalingMode} */
    let scaling = "fill";

    const layerA = new Float32Array(12);
    const layerB = new Float32Array(12);
    const layerC = new Float32Array(12);
    let cumulative = 0;
    const shares = new Float32Array(3);
    LAYERS.forEach((layer, i) => {
        layerA.set([layer.sizePx[0], layer.sizePx[1], layer.fallPxPerSec[0], layer.fallPxPerSec[1]], i * 4);
        layerB.set([layer.opacity[0], layer.opacity[1], layer.softness, layer.windResponse], i * 4);
        layerC.set([layer.swayPx[0], layer.swayPx[1], layer.swayHz[0], layer.swayHz[1]], i * 4);
        cumulative += layer.share;
        shares[i] = cumulative;
    });
    shares[2] = 1.01;

    const u = uniformLocator(gl);

    /** @returns {Rect | null} */
    function destination() {
        if (!imageSize) {
            return null;
        }
        return computeDestination(imageSize, { w: canvas.width, h: canvas.height }, scaling);
    }

    return {
        setImage(bitmap, natural, mode) {
            scaling = mode;
            gl.bindTexture(gl.TEXTURE_2D, imageTexture);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, bitmap);
            imageSize = { w: natural.w, h: natural.h };
            texSize = { width: bitmap.width, height: bitmap.height };
        },
        maxTextureSize() {
            return gl.getParameter(gl.MAX_TEXTURE_SIZE);
        },
        setScaling(mode) {
            scaling = mode;
        },
        setMask(source) {
            hasMask = source !== null;
            if (source) {
                gl.bindTexture(gl.TEXTURE_2D, mask);
                gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
                gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
            }
        },
        destination,
        textureSize() {
            return texSize;
        },
        frame(state) {
            gl.viewport(0, 0, canvas.width, canvas.height);
            gl.clearColor(0, 0, 0, 1);
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.bindVertexArray(vao);

            const dest = destination();
            if (dest) {
                gl.disable(gl.BLEND);
                gl.useProgram(bgProgram);
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, imageTexture);
                gl.uniform1i(u(bgProgram, "uImage"), 0);
                gl.uniform4f(u(bgProgram, "uDest"), dest.x, dest.y, dest.w, dest.h);
                gl.uniform2f(u(bgProgram, "uResolution"), canvas.width, canvas.height);
                gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
            }

            const count = Math.min(MAX_FLAKES, Math.max(0, Math.floor(state.count)));
            if (!state.enabled || count === 0) {
                return;
            }
            const scale = canvas.height / REFERENCE_HEIGHT_PX;
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
            gl.useProgram(snowProgram);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, atlas);
            gl.uniform1i(u(snowProgram, "uAtlas"), 0);
            gl.activeTexture(gl.TEXTURE1);
            gl.bindTexture(gl.TEXTURE_2D, hasMask ? mask : whiteMask);
            gl.uniform1i(u(snowProgram, "uMask"), 1);
            gl.uniform2f(u(snowProgram, "uResolution"), canvas.width, canvas.height);
            gl.uniform1f(u(snowProgram, "uFallTime"), state.fallTime);
            gl.uniform1f(u(snowProgram, "uWindOffset"), state.windOffsetPx);
            gl.uniform1f(u(snowProgram, "uWindSpeed"), state.windPxPerSec);
            gl.uniform1f(u(snowProgram, "uSpeedMul"), state.speedMul);
            gl.uniform1f(u(snowProgram, "uTime"), state.timeSec);
            gl.uniform1f(u(snowProgram, "uSizeMul"), state.sizeMul);
            gl.uniform1f(u(snowProgram, "uScale"), scale);
            gl.uniform1f(u(snowProgram, "uMargin"), wrapMarginPx(scale));
            gl.uniform1f(u(snowProgram, "uBlur"), state.motionBlur ? 1 : 0);
            gl.uniform3fv(u(snowProgram, "uShares"), shares);
            gl.uniform4fv(u(snowProgram, "uLayerA"), layerA);
            gl.uniform4fv(u(snowProgram, "uLayerB"), layerB);
            gl.uniform4fv(u(snowProgram, "uLayerC"), layerC);
            gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, count);
        },
        dispose() {
            gl.deleteProgram(bgProgram);
            gl.deleteProgram(snowProgram);
            gl.deleteTexture(atlas);
            gl.deleteTexture(mask);
            gl.deleteTexture(whiteMask);
            gl.deleteTexture(imageTexture);
            gl.deleteVertexArray(vao);
        },
    };
}

/**
 * @param {WebGL2RenderingContext} gl
 * @returns {WebGLTexture}
 */
function createTexture(gl) {
    const tex = gl.createTexture();
    if (!tex) {
        throw new Error("Could not create a texture.");
    }
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return tex;
}

/**
 * @param {WebGL2RenderingContext} gl
 * @param {string} vsSource
 * @param {string} fsSource
 * @returns {WebGLProgram}
 */
function createProgram(gl, vsSource, fsSource) {
    const program = gl.createProgram();
    if (!program) {
        throw new Error("Could not create a shader program.");
    }
    for (const [type, source] of /** @type {[number, string][]} */ ([[gl.VERTEX_SHADER, vsSource], [gl.FRAGMENT_SHADER, fsSource]])) {
        const shader = gl.createShader(type);
        if (!shader) {
            throw new Error("Could not create a shader.");
        }
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            throw new Error(`Shader failed to compile: ${gl.getShaderInfoLog(shader)}`);
        }
        gl.attachShader(program, shader);
        gl.deleteShader(shader);
    }
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error(`Shader program failed to link: ${gl.getProgramInfoLog(program)}`);
    }
    return program;
}

/**
 * @param {WebGL2RenderingContext} gl
 * @returns {(program: WebGLProgram, name: string) => WebGLUniformLocation | null}
 */
function uniformLocator(gl) {
    /** @type {Map<WebGLProgram, Map<string, WebGLUniformLocation | null>>} */
    const cache = new Map();
    return (program, name) => {
        let byName = cache.get(program);
        if (!byName) {
            byName = new Map();
            cache.set(program, byName);
        }
        if (!byName.has(name)) {
            byName.set(name, gl.getUniformLocation(program, name));
        }
        return byName.get(name) ?? null;
    };
}
