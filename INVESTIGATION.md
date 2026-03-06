# Wobkey Crush 80 — SignalRGB Integration Research

## Device

| Field | Value |
|---|---|
| Name | RDR Crush 80 |
| MCU | Telink (TLSR-series, confirmed from OTA flasher internals) |
| Firmware | Custom (not standard QMK) |
| VIA Protocol Version | 11 (0x000B) |

### Connection Modes

| Mode | VID | PID | BT Name | VIA Support |
|---|---|---|---|---|
| USB wired | `0x320F` | `0x5055` | — | ✅ Full |
| 2.4 GHz dongle | `0x320F` | `0x5088` | — | ✅ Full (forwarded) |
| Bluetooth | `0x245A` | `0x8276` | Crush 80-1 | ❌ None |

---

## USB Interfaces — Wired (VID 0x320F, PID 0x5055)

| hidraw | Interface | Usage Page | Usage | What it is | Notes |
|---|---|---|---|---|---|
| hidraw2 | 0 | `0x0001` | — | Standard keyboard HID | Read-only for our purposes |
| hidraw3 | 1 | `0xFF60` | `0x61` | **VIA raw HID** | RGB control lives here |
| hidraw4 | 2 | `0xFFEF` | — | Telink OTA firmware flasher | Report ID 5; don't use |
| hidraw5 | 3 | `0xFF1C` | — | **Wireless mode switch** | Writing anything here immediately switches keyboard to 2.4G and disconnects from USB |

## USB Interfaces — 2.4 GHz Dongle (VID 0x320F, PID 0x5088)

| hidraw | Interface | Usage Page | Report ID | What it is | Notes |
|---|---|---|---|---|---|
| hidraw2 | 0 | `0x0001` | none | Standard keyboard HID | Forwarded keypresses |
| hidraw3 | 1 | `0xFF60` | none | **VIA raw HID** | RGB control — identical to wired |
| hidraw4 | 2 | `0xFF1C` | 4 | Wireless mode control | 63-byte reports |
| hidraw4 | 2 | `0xFFEF` | 5 | Telink OTA flasher | Dongle firmware update |
| hidraw4 | 2 | `0x0001` | 6 | Mouse | Shared interface |

The dongle transparently forwards all VIA commands to the keyboard over the 2.4 GHz link. GET and SET operations work identically to the wired path, including the patched hue control.

## Bluetooth HID Profile (VID 0x245A, PID 0x8276)

| Report ID | Usage Page | Type | Purpose |
|---|---|---|---|
| 1 | `0x0001` / `0x0007` | In/Out | Standard keyboard |
| 2 | `0x000C` | Input | Consumer controls (media keys) |
| 3 | `0x0001` | Input | System power controls |
| 4 | `0x0001` / `0x0009` | Input | Mouse |
| 5 | `0xFF01` | Feature (4 bytes) | Vendor-specific (likely battery/pairing) |
| 6 | `0x0007` | Input | Extended keyboard (NKRO) |

**No VIA interface (`0xFF60`) is present over Bluetooth.** The only vendor report is a 4-byte Feature on `0xFF01`, far too small for VIA's 32-byte protocol. BLE HID bandwidth and descriptor constraints typically prevent keyboards from exposing vendor channels over Bluetooth.

---

## VIA Protocol (interface 1, Usage Page 0xFF60)

Report size: 32 bytes, no Report ID. On Windows, `sendReport(0, data)` via WebHID.

### Channel 3 — RGB Matrix

| ID | Name | Range | Status |
|---|---|---|---|
| 1 | Brightness | 0–9 | ✅ Works |
| 2 | Effect | 0–18 | ✅ Works |
| 3 | Effect speed | 0–4 | ✅ Works |
| 4 | Color (H, S) | 0–255 each | ⚠️ Partial — S works, H ignored |

### Standard QMK keyboard-value path (`cmd=0x02/0x03`)

Tested `id=0x04` (hue) through `id=0x08` (effect). No response to GET; SET had no visible effect. This path is not implemented by the firmware.

---

## Effects (channel 3, ID 2)

All 19 effects (0–18) switch successfully via VIA. Effect names from VIA config:

| ID | Name | Notes |
|---|---|---|
| 0 | OFF_MODE | |
| 1 | WAVE_MODE | |
| 2 | COLOUR_CLOUD_MODE | |
| 3 | VORTEX_MODE | |
| 4 | MIX_COLOUR_MODE | |
| 5 | BREATHE_MODE | |
| 6 | LIGHT_MODE | Static colour cycle — hue changes via `RGB_HUI`/`RGB_HUD` keys only |
| 7 | SLOWLY_OFF_MODE | |
| 8 | STONE_MODE | |
| 9 | LASER_MODE | |
| 10 | STARRY_MODE | |
| 11 | FLOWERS_OPEN_MODE | |
| 12 | TRAVERSE_MODE | |
| 13 | WAVE_BAR_MODE | |
| 14 | METEOR_MODE | |
| 15 | RAIN_MODE | |
| 16 | SCAN_MODE | |
| 17 | TRIGGER_COLOUR_MODE | |
| 18 | CENTER_SPREAD_MODE | |

---

## Hue Control — Root Cause (Confirmed via Firmware Reverse Engineering)

Effect 6 (LIGHT_MODE) is a static colour display whose hue can be changed by pressing `RGB_HUI` / `RGB_HUD` on the keyboard. This confirms the firmware **does** maintain a hue register internally.

However, all USB paths to that register are broken or unimplemented:

| Path tried | Result |
|---|---|
| VIA custom channel 3, ID 4 — `[0x07, 0x03, 0x04, H, S]` | S applies, H ignored |
| Byte order swapped — `[0x07, 0x03, 0x04, S, H]` | Produces white; S now ignored |
| Three-byte colour — `[0x07, 0x03, 0x04, H, S, V]` | Same as H,S; V has no effect |
| QMK `SET_KEYBOARD_VALUE` — `[0x03, 0x04, H]` | No effect |
| QMK `GET_KEYBOARD_VALUE` — `[0x02, 0x04]` | No response |

### Firmware Bug — Decompiled Root Cause

The firmware was extracted from the .NET OTA flasher (`code_2M.bin` → `firmware.bin`, 121,332 bytes, Telink TLSR RISC-V RV32IC). Ghidra decompilation reveals the exact bug in the VIA color SET handler at `FUN_ram_0000da20` (firmware offset `0xDA20`, 302 bytes):

**The state struct** (pointed to by register `a5`) stores LED parameters:

| Offset | Field | Written by USB? | Written by keypress? |
|---|---|---|---|
| `+1` | Effect ID (0–18) | Yes | Yes |
| `+2` | Brightness (0–9) | Yes | Yes |
| `+3` | Speed (inverted) | Yes | Yes |
| `+6` | Red component | **NO — overwritten with stale RAM global** | Yes |
| `+7` | Green component | **NO — overwritten with stale RAM global** | Yes |
| `+8` | Blue component | **NO — overwritten with stale RAM global** | Yes |
| `+9` | Hue (H byte) | Yes (stored but unused for rendering) | Yes |
| `+0x1e` | Inverted Saturation (~S) | Yes | Yes |

**What happens when VIA sends `[0x07, 0x03, 0x04, H, S]`** (the SET handler at `0xDA20`):
```c
// Pseudocode from decompilation
case 0x83:  // COLOR command
    state[9] = usb_packet[2];           // H byte → stored to hue field ✓
    state[6] = RAM[0x2001BAAF];         // R ← stale global (BUG!)
    state[7] = RAM[0x2001BAB0];         // G ← stale global (BUG!)
    state[8] = RAM[0x2001BAB1];         // B ← stale global (BUG!)
    state[0x1e] = ~usb_packet[3];       // ~S → inverted saturation ✓
```

**What happens when a physical keypress changes hue** (the keypress handler at `FUN_ram_0000d8c8`, offset `0xD8C8`):
```c
// Keypress handler receives pre-converted RGB from upstream HSV→RGB conversion
void keypress_color_handler(hue, rgb_array[3], state) {
    state[9] = hue;                     // H byte ✓
    state[6] = rgb_array[0];            // R ← freshly converted ✓
    state[7] = rgb_array[1];            // G ← freshly converted ✓
    state[8] = rgb_array[2];            // B ← freshly converted ✓
    state[0x1e] = ~saturation;          // ~S ✓
}
```

**Summary**: The VIA USB handler stores the H byte correctly but then overwrites the RGB fields (`state[6-8]`) with stale cached values from global RAM (`0x2001BAAF-B1`) instead of converting H to RGB. The keypress path works because an upstream function does HSV→RGB conversion *before* calling the color handler. The VIA path skips this conversion entirely — **this is a firmware bug**.

The GET handler (`FUN_ram_0000d9ec` at `0xD9EC`) correctly reads H back from `state[9]` and S from `~state[0x1e]`, which is why VIA can *read* the hue value even though *setting* it has no visual effect.

### Globals at 0x2001BAAF–B1

These three bytes hold the last-known RGB values, likely updated only by the physical keypress HSV→RGB conversion path. They are loaded unconditionally at the top of the SET handler and stuffed into `state[6-8]` regardless of the H value sent via USB.

---

## SignalRGB Plugin — What Is Achievable

| Feature | Wired USB | 2.4G Dongle | Bluetooth |
|---|---|---|---|
| Solid single colour | ✅ H+S via VIA ch3 ID4 | ✅ Same (forwarded) | ❌ No VIA |
| Brightness sync | ✅ VIA ch3 ID1 (0–9) | ✅ Same | ❌ No VIA |
| Effect selection | ✅ VIA ch3 ID2 (0–18) | ✅ Same | ❌ No VIA |
| Speed control | ✅ VIA ch3 ID3 (0–4) | ✅ Same | ❌ No VIA |
| On/off with SignalRGB | ✅ Brightness = 0 | ✅ Same | ❌ No VIA |
| Per-key RGB | ❌ Not exposed | ❌ Not exposed | ❌ Not exposed |

With the firmware patch applied, the SignalRGB plugin can set solid colors via VIA channel 3, ID 4 (H, S) over both USB wired and 2.4G wireless. Bluetooth does not expose the VIA interface and cannot be used for RGB control.

### SignalRGB Plugin Implementation Notes

The plugin (`SignalRGB/WobkeyCrush80.js`) required several non-obvious fixes to work:

| Issue | Root Cause | Fix |
|---|---|---|
| Canvas returns all black | Single-LED layout causes SignalRGB rendering engine to skip device | Define a 22×7 grid (154 LEDs) so the effect engine has positions to render onto |
| `device.color(x, y)` returns 0 | Return value is `[R, G, B]` array, not packed integer | Access as `c[0]`, `c[1]`, `c[2]` instead of bit-shifting |
| VIA writes ignored | Windows HID API requires report ID as first byte | Prepend `0x00` (no report ID) to every `device.write()` call |
| "Unknown plugin type" error | `Type()` export returning `"keyboard"` | Remove `Type()` export entirely — SignalRGB infers it |

**How it works:** On each frame, the plugin averages all 154 LED positions from the SignalRGB canvas into a single RGB value, converts to HSV (H and S in 0–255 QMK convention), and sends H+S via VIA channel 3 ID 4. Brightness is mapped from V (0–255) to the keyboard's 0–9 range via channel 3 ID 1. Values are only sent when they change to minimize USB traffic. On initialize, the plugin switches the keyboard to solid-colour mode (Effect 6). On shutdown, brightness is restored to maximum.

---

## Potential Workarounds

### 1. Firmware Binary Patch (IMPLEMENTED & VERIFIED)

The VIA color handler at `0xDA4E` originally jumps to `0xD8D0` (the keypress color store routine) with register `a4` pointing to stale RGB globals at `0x2001BAAF`. The patch redirects this jump through a code cave at `0x10A4` that converts the H byte (in `a2`) to RGB using a 6-sector color wheel algorithm, writes the result to `0x2001BAAF-B1` (updating the stale cache), then continues to `0xD8D0` as normal.

**Patch details:**
- **Code cave at `0x10A4`** (276 bytes, in unused padding): HSV→RGB color wheel conversion. Divides hue into 6 sectors of 43, computes fractional RGB, stores to `a4[0-2]`.
- **Patch at `0xDA4E`** (6 bytes): Original `lb a3,3(s1)` + `c.j 0xD8D0` replaced with `j 0x10A4` + `c.nop`. The displaced `lb` instruction is the first instruction in the code cave.
- **CRC updated**: `0xA7DA1601` → `0x735D569A` (stored CRC = ~CRC32 of data excluding last 4 bytes).

**Files produced:**
- `firmware_patched.bin` — patched firmware binary
- `code_2M_patched.bin` — patched OTA image (replace in flasher's .resx resources)
- `patch_firmware.py` — reproducible patch script

**To flash:** Replace the `code_2M` resource in the .NET OTA flasher with `code_2M_patched.bin` and run the flasher.

**OTA encryption: None.** The `enc_key[16]` field loaded from `param_128K.bin` is a dead variable — assigned but never used. Firmware data is sent as plaintext 16-byte blocks with CRC16 integrity checks. The only host-side validation is `bin_crc != 0 && bin_crc != 0xFFFFFFFF`, which the patched firmware passes.

**Risk:** Low — the OTA bootloader is separate from the application firmware. If the patched firmware crashes, the bootloader should still present the OTA USB interface for recovery with the original `code_2M.bin`.

### 2. Manufacturer Bug Report (Safest)

The decompiled evidence clearly shows this is a firmware bug. Providing Wobkey/RDR with the exact root cause (SET handler at `0xDA20` loads stale RGB globals instead of converting H) may result in an official fix. This is the lowest-risk path.

### 3. Simulating Keypresses via USB

If there's a way to inject `RGB_HUI`/`RGB_HUD` keypress events via the VIA `dynamic_keymap_macro` system or by writing to the key matrix, the firmware's own keypress path would handle the HSV→RGB conversion correctly.

### 4. Custom QMK Firmware

The MCU is Telink (TLSR-series, RISC-V), not the typical STM32/AVR. A Telink-compatible QMK port would need to expose per-key LED control. Non-trivial but would give full control.

## OTA Protocol (No Encryption)

The .NET OTA flasher sends firmware in plaintext over USB HID (interface 0xFFEF, report_id=6).

| Phase | Packet | Description |
|---|---|---|
| Start | `[02][02][00][01][FF]` | Handshake / begin OTA |
| Data | 3× `[idx_lo][idx_hi][16B data][crc16_lo][crc16_hi]` | 20-byte chunks, 3 per packet |
| End | `[02][06][00][02][FF][cnt_lo][cnt_hi][~cnt_lo][~cnt_hi]` | Total chunk count + complement |

- `enc_key[16]` from `param_128K.bin` offset 0x30 is loaded but **never used** — dead code
- CRC16 uses polynomial 0xA001 (standard Telink OTA)
- Host validation: `bin_crc != 0 && bin_crc != 0xFFFFFFFF` only
- Device validation: likely CRC32 check on received firmware header

---

## Firmware Extraction Details

| Item | Value |
|---|---|
| Source | `code_2M.bin` resource from .NET OTA flasher `.resx` |
| Extracted binary | `firmware.bin` (121,332 bytes) |
| Architecture | Telink TLSR RISC-V (RV32IC) |
| Boot vector | `c.j 0x28` at offset 0x00 |
| Telink marker | "KNLT" at offset 0x20 |
| Firmware version | `0x56565656` |
| CRC | `0xA7DA1601` (at header offset 8) |
| OTA parameter file | `param_128K.bin` (AES key at 0x30, VID/PID at 0x40/0x44) |
| Analysis tool | Ghidra headless with Java GhidraScript (`GhidraDeep.java`) |
| Decompilation output | `/tmp/ghidra_decomp.txt` (4244 lines, all functions >40 bytes) |

### Key Functions Identified

| Address | Size | Role |
|---|---|---|
| `0x0DA20` | 302 | **VIA Custom Channel SET handler** — contains the hue bug |
| `0x0D9EC` | 140 | VIA Custom Channel GET handler |
| `0x0D8C8` | 48 | Keypress color handler (receives pre-converted RGB) |
| `0x0D500` | 80 | Higher-level dispatcher (switch on command type) |
| `0x0D57C` | 164 | Flash/EEPROM write handler |
| `0x0EF88` | 768 | LED initialization (GPIO register writes to `0x80140xxx`) |
| `0x0C850` | — | Called when effect changes (referenced but not fully decompiled) |

---

## Files

| File | Description |
|---|---|
| `Crush80-RGB-USB.JSON` | VIA configuration (layout, keymap, channel map) |
| `Crush80-RGB-Firmware.exe` | Telink USB OTA firmware flasher (.NET 4.0, decompilable) |
| `decompiled/` | ILSpy decompile output of the firmware flasher |
| `SignalRGB/WobkeyCrush80.js` | SignalRGB plugin — wired USB (PID 0x5055) |
| `SignalRGB/WobkeyCrush80Wireless.js` | SignalRGB plugin — 2.4G dongle (PID 0x5088) |
| `SignalRGB/via-test.html` | WebHID test app used to probe the VIA interface |
| `99-wobkey-crush80.rules` | udev rule granting plugdev access (wired + dongle) |
| `firmware.bin` | Extracted TLSR RISC-V firmware binary (121,332 bytes) |
| `code_2M.bin` | Raw firmware image from OTA flasher resources |
| `param_128K.bin` | OTA parameter file (AES key, VID/PID, interface config) |
| `extract_firmware.py` | Script to extract binaries from .resx resource file |
| `extract_fw_code.py` | Script to parse firmware header and extract code section |
| `analyze_firmware.py` | Initial firmware analysis (strings, patterns, boot vector) |
| `disasm_targets.py` | Targeted capstone disassembly around VIA handler areas |
| `GhidraDeep.java` | Ghidra headless script — full decompilation to `/tmp/ghidra_decomp.txt` |
| `GhidraAnalyze.java` | Ghidra script — function scoring for VIA-like patterns |
| `GhidraVIA.java` | Ghidra script — targeted VIA area analysis |
| `GhidraHSV.java` | Ghidra script — full C000-E000 decompilation |
| `patch_firmware.py` | Reproducible firmware patch script |
| `detail.md` | Comprehensive technical write-up of the full reverse engineering process |
| `flash_ota.py` | Python OTA flasher (replaces .NET flasher, Linux hidraw, Report ID 5) |
| `firmware_patched.bin` | Patched firmware with hue fix (flashed & verified working) |
| `code_2M_patched.bin` | Patched OTA image for .NET flasher |
