/**
 * Wobkey Crush 80 - SignalRGB Plugin
 *
 * Firmware limitation: the stock firmware ignores the hue byte entirely.
 * All effects are palette-based (rainbow / multi-colour). True per-colour
 * sync is not achievable without replacing the firmware.
 *
 * What this plugin CAN do:
 *   - Sync brightness with the rest of your SignalRGB setup.
 *     When SignalRGB dims or turns off the canvas, the keyboard follows.
 *   - The keyboard keeps whatever animation/effect you last set in VIA.
 *     This plugin does not override it.
 *
 * Confirmed working via WebHID testing:
 *   VIA channel 3, ID 1  Brightness  0–9          ✓
 *   VIA channel 3, ID 2  Effect      0–18          ✓ (not used here)
 *   VIA channel 3, ID 4  Color H,S   H ignored, S works  ✗/✓
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
const MAX_BRIGHTNESS      = 9;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let lastBrightness = -1;

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

export function Initialize() {
    lastBrightness = -1;   // force send on first Render()
}

export function Render() {
    // Sample the single LED zone. device.color(x, y) returns a packed 0xRRGGBB int.
    let c = device.color(0, 0);
    let r = (c >> 16) & 0xFF;
    let g = (c >>  8) & 0xFF;
    let b =  c        & 0xFF;

    // Derive perceived brightness (0–255) from the colour SignalRGB has chosen,
    // then map it to the firmware's 0–9 range.
    let v = Math.max(r, g, b);
    let brightness = Math.round((v / 255) * MAX_BRIGHTNESS);

    if (brightness === lastBrightness) return;
    lastBrightness = brightness;

    sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_BRIGHTNESS, brightness]);
}

export function Shutdown() {
    // Restore full brightness when SignalRGB exits so the keyboard isn't left dark.
    sendVIA([ID_CUSTOM_SET_VALUE, RGB_CHANNEL, RGB_ID_BRIGHTNESS, MAX_BRIGHTNESS]);
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
