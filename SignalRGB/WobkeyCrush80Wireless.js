/**
 * Wobkey Crush 80 (Wireless/2.4G Dongle) - SignalRGB Plugin
 *
 * Requires patched firmware (firmware_patched.bin) to enable hue control
 * via VIA USB. The stock firmware has a bug where the H byte is ignored.
 *
 * This plugin targets the 2.4G wireless dongle (PID 0x5088).
 * For wired USB mode, use WobkeyCrush80.js (PID 0x5055).
 *
 * VIA commands are transparently forwarded by the dongle to the keyboard
 * over the 2.4G link — same protocol, same channel/ID scheme.
 */

// ---------------------------------------------------------------------------
// Device identity
// ---------------------------------------------------------------------------

export function Name()        { return "Wobkey Crush 80 (Wireless)"; }
export function VendorId()    { return 0x320F; }
export function ProductId()   { return 0x5088; }
export function Publisher()   { return "Community"; }

// Canvas grid: 22x7 with LEDs spread across it so SignalRGB's effect engine
// has positions to render onto. We average all LED colors into one value
// since the keyboard only supports a single whole-board color.
const COLS = 22;
const ROWS = 7;

let vLedNames = [];
let vLedPositions = [];
for (let y = 0; y < ROWS; y++) {
    for (let x = 0; x < COLS; x++) {
        vLedNames.push(`Key ${y * COLS + x}`);
        vLedPositions.push([x, y]);
    }
}

export function Size()          { return [COLS, ROWS]; }
export function DefaultLayout() { return "Default"; }
export function LedNames()      { return vLedNames; }
export function LedPositions()  { return vLedPositions; }

// ---------------------------------------------------------------------------
// Endpoint selection
//
// Must target interface 1 (VIA, Usage Page 0xFF60).
// ---------------------------------------------------------------------------

export function Validate(endpoint) {
    return endpoint.interface    === 1
        && endpoint.usage_page   === 0xFF60
        && endpoint.usage        === 0x61;
}

// ---------------------------------------------------------------------------
// VIA protocol constants
// ---------------------------------------------------------------------------

const VIA_REPORT_SIZE     = 32;
const ID_CUSTOM_SET_VALUE = 0x07;

const RGB_CHANNEL         = 3;
const RGB_ID_BRIGHTNESS   = 1;   // 0–9
const RGB_ID_EFFECT       = 2;   // 0–18
const RGB_ID_COLOR        = 4;   // H, S (0–255 each)
const EFFECT_LIGHT        = 6;   // solid-colour static mode
const MAX_BRIGHTNESS      = 9;

// ---------------------------------------------------------------------------
// State — only send when values change
// ---------------------------------------------------------------------------

let lastH = -1;
let lastS = -1;
let lastBrightness = -1;

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

export function Initialize() {
    lastH = -1;
    lastS = -1;
    lastBrightness = -1;

    // Switch to solid-colour mode
    sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_EFFECT, EFFECT_LIGHT]);
}

export function Render() {
    // Average all LED positions on the canvas into a single RGB value.
    // device.color(x, y) returns [R, G, B] array.
    let totalR = 0, totalG = 0, totalB = 0;
    let count = COLS * ROWS;

    for (let y = 0; y < ROWS; y++) {
        for (let x = 0; x < COLS; x++) {
            let c = device.color(x, y);
            totalR += c[0];
            totalG += c[1];
            totalB += c[2];
        }
    }

    let r = Math.round(totalR / count);
    let g = Math.round(totalG / count);
    let b = Math.round(totalB / count);

    let [h, s, v] = rgbToHsv(r, g, b);
    let brightness = Math.round((v / 255) * MAX_BRIGHTNESS);

    if (h !== lastH || s !== lastS) {
        lastH = h;
        lastS = s;
        sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_COLOR, h, s]);
    }

    if (brightness !== lastBrightness) {
        lastBrightness = brightness;
        sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_BRIGHTNESS, brightness]);
    }
}

export function Shutdown() {
    sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_BRIGHTNESS, MAX_BRIGHTNESS]);
}

// ---------------------------------------------------------------------------
// RGB → HSV  (H and S in 0–255 to match VIA/QMK convention)
// ---------------------------------------------------------------------------

function rgbToHsv(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    let max = Math.max(r, g, b);
    let min = Math.min(r, g, b);
    let d = max - min;
    let h = 0;
    let s = max === 0 ? 0 : d / max;
    let v = max;

    if (d !== 0) {
        switch (max) {
            case r: h = ((g - b) / d + (g < b ? 6 : 0)) / 6; break;
            case g: h = ((b - r) / d + 2) / 6;               break;
            case b: h = ((r - g) / d + 4) / 6;               break;
        }
    }

    return [Math.round(h * 255), Math.round(s * 255), Math.round(v * 255)];
}

// ---------------------------------------------------------------------------
// HID write
// ---------------------------------------------------------------------------

function sendVIA(data) {
    // SignalRGB device.write() uses Windows HID API which requires report ID
    // as the first byte. VIA interface has no report ID, so prepend 0x00.
    let buf = new Array(1 + VIA_REPORT_SIZE).fill(0x00);
    buf[0] = 0x00;  // report ID (none)
    for (let i = 0; i < data.length && i < VIA_REPORT_SIZE; i++) {
        buf[i + 1] = data[i];
    }
    device.write(buf, buf.length);
}
