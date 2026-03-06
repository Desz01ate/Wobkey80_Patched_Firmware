/**
 * Wobkey Crush 80 - SignalRGB Plugin
 *
 * Requires patched firmware (firmware_patched.bin) to enable hue control
 * via VIA USB. The stock firmware has a bug where the H byte is ignored.
 *
 * What this plugin does:
 *   - Sets the keyboard to solid-colour mode (Effect 6 / LIGHT_MODE)
 *   - Syncs the colour from SignalRGB canvas → keyboard via VIA channel 3
 *   - Maps HSV hue + saturation for colour, brightness for intensity
 *   - Restores full brightness on shutdown
 *
 * VIA channel 3 controls:
 *   ID 1  Brightness  0–9   ✓
 *   ID 2  Effect      0–18  ✓
 *   ID 4  Color H,S         ✓ (with patched firmware)
 */

// ---------------------------------------------------------------------------
// Device identity
// ---------------------------------------------------------------------------

export function Name()        { return "Wobkey Crush 80"; }
export function VendorId()    { return 0x320F; }
export function ProductId()   { return 0x5055; }
export function Publisher()   { return "Community"; }

export function Size()          { return [1, 1]; }
export function DefaultLayout() { return "Default"; }
export function LedNames()      { return ["Keyboard"]; }
export function LedPositions()  { return [[0, 0]]; }

// ---------------------------------------------------------------------------
// Endpoint selection
//
// Must target interface 1 (VIA, Usage Page 0xFF60).
// Interface 0 = standard keyboard HID  — ignores VIA commands
// Interface 3 = wireless mode switch   — writing disconnects keyboard from USB
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
    let c = device.color(0, 0);
    let r = (c >> 16) & 0xFF;
    let g = (c >>  8) & 0xFF;
    let b =  c        & 0xFF;

    let [h, s, v] = rgbToHsv(r, g, b);

    // Map V to the firmware's coarse 0–9 brightness range
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
    let buf = new Array(VIA_REPORT_SIZE).fill(0x00);
    for (let i = 0; i < data.length && i < VIA_REPORT_SIZE; i++) {
        buf[i] = data[i];
    }
    device.write(buf);
}
