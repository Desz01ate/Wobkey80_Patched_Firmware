# Wobkey Crush 80 — Firmware Hue Bug: Reverse Engineering, Patching & Flashing

A technical deep-dive into finding and fixing a firmware bug in a keyboard's RGB
controller via binary reverse engineering, RISC-V code injection, and USB
protocol analysis — all without source code, documentation, or manufacturer
cooperation.

---

## Table of Contents

1. [The Problem](#1-the-problem)
2. [Reconnaissance — USB Interface Mapping](#2-reconnaissance--usb-interface-mapping)
3. [VIA Protocol Probing](#3-via-protocol-probing)
4. [Firmware Extraction](#4-firmware-extraction)
5. [Reverse Engineering with Ghidra](#5-reverse-engineering-with-ghidra)
6. [Root Cause — The Bug at Register Level](#6-root-cause--the-bug-at-register-level)
7. [The Fix — RISC-V Code Cave Injection](#7-the-fix--risc-v-code-cave-injection)
8. [OTA Protocol Reverse Engineering](#8-ota-protocol-reverse-engineering)
9. [Building a Linux OTA Flasher](#9-building-a-linux-ota-flasher)
10. [Verification & SignalRGB Integration](#10-verification--signalrgb-integration)
11. [Lessons Learned](#11-lessons-learned)

---

## 1. The Problem

The **Wobkey Crush 80** (VID `0x320F`, PID `0x5055`) is a mechanical keyboard
with per-key RGB LEDs running on a Telink TLSR-series RISC-V microcontroller.
It supports the [VIA](https://www.caniusevia.com/) protocol for configuration
over USB — including an RGB control channel that should let software set the
LED color.

The goal was to write a [SignalRGB](https://www.signalrgb.com/) plugin to sync
the keyboard's LEDs with the rest of a desktop lighting setup.

**The symptom**: sending a color command via VIA changes the **saturation**
correctly, but the **hue** byte is silently ignored. Physical keypresses
(`RGB_HUI` / `RGB_HUD`) change the hue just fine — only the USB path is broken.

This meant the keyboard couldn't display an arbitrary color commanded over USB,
making SignalRGB integration impossible beyond simple brightness sync.

---

## 2. Reconnaissance — USB Interface Mapping

The first step was understanding what USB interfaces the keyboard exposes.
Using `usbhid-dump` and inspecting `/sys/class/hidraw/*/device/report_descriptor`,
four HID interfaces were identified:

| hidraw  | Interface | Usage Page | Purpose                  | Safety         |
|---------|-----------|------------|--------------------------|----------------|
| hidraw2 | 0         | `0x0001`   | Standard keyboard HID    | Read-only      |
| hidraw3 | 1         | `0xFF60`   | **VIA raw HID**          | RGB control    |
| hidraw4 | 2         | `0xFFEF`   | Telink OTA flasher       | Firmware flash |
| hidraw5 | 3         | `0xFF1C`   | Wireless mode switch     | **Dangerous**  |

**Critical discovery**: writing *anything* to interface 3 (`0xFF1C`) immediately
switches the keyboard to 2.4 GHz wireless mode and disconnects USB. This was
found by accident and documented to prevent others from bricking their setup.

A **udev rule** (`99-wobkey-crush80.rules`) was written to grant the `plugdev`
group access to all four hidraw devices without requiring root.

---

## 3. VIA Protocol Probing

A **WebHID test app** (`via-test.html`) was built to interactively probe the
VIA interface from a browser. VIA uses 32-byte HID reports with no report ID
on interface 1.

The VIA custom channel map (from the keyboard's JSON config) exposes:

```
Channel 3 — RGB Matrix:
  ID 1: Brightness  (0–9)    → Works ✓
  ID 2: Effect      (0–18)   → Works ✓
  ID 3: Speed       (0–4)    → Works ✓
  ID 4: Color       (H, S)   → S works, H ignored ✗
```

The SET command format: `[0x07, channel, id, value...]`
The GET command format: `[0x08, channel, id]` → device echoes with values

**Key observation**: GET for color correctly returns the H value that was SET —
the firmware *stores* the hue, it just doesn't *use* it for rendering. This
pointed toward a data-flow bug rather than a parsing bug.

Every conceivable byte ordering was tested:

| Attempt                              | Result                           |
|--------------------------------------|----------------------------------|
| `[0x07, 0x03, 0x04, H, S]`          | S works, H ignored               |
| `[0x07, 0x03, 0x04, S, H]`          | White; S now ignored             |
| `[0x07, 0x03, 0x04, H, S, V]`       | Same as H,S; V no effect         |
| QMK `SET_KEYBOARD_VALUE` `[0x03, 0x04, H]` | No effect                 |
| QMK `GET_KEYBOARD_VALUE` `[0x02, 0x04]`    | No response                |

The standard QMK keyboard-value path (`cmd=0x02/0x03`) is completely
unimplemented — the firmware only responds to the custom channel path.

---

## 4. Firmware Extraction

### Source: the .NET OTA Flasher

The keyboard ships with a Windows firmware updater (`Crush80-RGB-Firmware.exe`),
a .NET Framework 4.0 application. Decompiling it with **ILSpy** revealed:

- Firmware binary stored as a .resx resource named `code_2M` (2 MB file)
- OTA parameters in `param_128K` resource (AES key, VID/PID, report ID)

### Extracting the Firmware Binary

The `code_2M.bin` file has a 256-byte OTA wrapper header followed by the raw
firmware. A Python script (`extract_fw_code.py`) parses this:

```
code_2M.bin layout:
  [0x000 – 0x0FF]  OTA wrapper header (256 bytes)
    Offset 0x08:   CRC32 (inverted)
    Offset 0x18:   Firmware size (LE uint32)
    Offset 0x30:   Firmware size (BE uint32, redundant)
  [0x100 – ...]    Firmware binary
  [remainder]      0xFF padding to 2 MB
```

**Extracted firmware**: `firmware.bin`, 121,332 bytes.

### Identifying the Architecture

```
Offset 0x00: c.j 0x28          ← RISC-V compressed jump (boot vector)
Offset 0x20: "KNLT"            ← Telink marker (reversed "TLNK")
Offset 0x02: 0x56565656        ← Firmware version
Offset 0x08: 0xA7DA1601        ← CRC (stored as ~CRC32)
```

Architecture: **Telink TLSR RISC-V (RV32IC)** — a 32-bit RISC-V core with
compressed instruction extension. This is not the typical STM32/AVR found in
most keyboards, which means standard QMK toolchains don't apply.

### CRC Algorithm

The firmware uses an inverted CRC32:

```
stored_crc = ~CRC32(firmware_data[0 : size-4])
```

Verification property: `CRC32(entire_firmware_including_stored_crc) == 0xFFFFFFFF`

This was confirmed by computing the CRC32 of the full 121,332 bytes and
checking the result equals `0xFFFFFFFF`.

---

## 5. Reverse Engineering with Ghidra

### Setup

Ghidra doesn't have native Telink TLSR support, but the RISC-V:LE:32:RV32IC
processor module works. The firmware was loaded at RAM address `0x00000000`
(the Telink bootloader copies firmware from flash to RAM before execution).

Four custom Ghidra scripts were written to automate analysis:

| Script             | Purpose                                             |
|--------------------|-----------------------------------------------------|
| `GhidraDeep.java`  | Full decompilation of all functions >40 bytes       |
| `GhidraAnalyze.java`| Score functions for VIA-like patterns (switch/case) |
| `GhidraVIA.java`   | Targeted analysis around suspected VIA handlers     |
| `GhidraHSV.java`   | Full decompilation of 0xC000–0xE000 (LED area)      |

All scripts were run **headless** via:

```bash
analyzeHeadless /tmp/GhidraProject wobkey_fw \
  -import firmware.bin \
  -processor "RISCV:LE:32:RV32IC" \
  -postScript GhidraDeep.java
```

### Key Functions Identified

| Address   | Size | Role                                    |
|-----------|------|-----------------------------------------|
| `0x0DA20` | 302  | **VIA Custom Channel SET handler**      |
| `0x0D9EC` | 140  | VIA Custom Channel GET handler          |
| `0x0D8C8` | 48   | Keypress color handler                  |
| `0x0D500` | 80   | Command-type dispatcher                 |
| `0x0EF88` | 768  | LED initialization (GPIO writes)        |

The SET handler at `0xDA20` is a large switch statement dispatching on
`(channel << 8) | id`. Case `0x83` (channel 3, OR'd with 0x80) handles the
color command.

---

## 6. Root Cause — The Bug at Register Level

### The State Struct

The firmware maintains an LED state struct pointed to by register `a5`:

```
Offset  Field                Written by USB?   Written by keypress?
+1      Effect ID (0–18)     Yes               Yes
+2      Brightness (0–9)     Yes               Yes
+3      Speed (inverted)     Yes               Yes
+6      Red component        NO (stale!)       Yes
+7      Green component      NO (stale!)       Yes
+8      Blue component       NO (stale!)       Yes
+9      Hue (H byte)         Yes               Yes
+0x1E   ~Saturation          Yes               Yes
```

### Two Code Paths — One Broken

**Keypress path** (working) — at `0xD8C8`:

```
Physical keypress (RGB_HUI/HUD)
  → upstream HSV→RGB conversion runs
  → keypress_color_handler(hue, rgb_array, state):
      state[9]    = hue
      state[6]    = rgb_array[0]    // R ← freshly converted
      state[7]    = rgb_array[1]    // G ← freshly converted
      state[8]    = rgb_array[2]    // B ← freshly converted
      state[0x1E] = ~saturation
```

**VIA USB path** (broken) — at `0xDA20`:

```
USB VIA command [0x07, 0x03, 0x04, H, S]
  → VIA SET handler case 0x83:
      state[9]    = H               // stored correctly ✓
      state[6]    = RAM[0x2001BAAF] // R ← STALE GLOBAL ✗
      state[7]    = RAM[0x2001BAB0] // G ← STALE GLOBAL ✗
      state[8]    = RAM[0x2001BAB1] // B ← STALE GLOBAL ✗
      state[0x1E] = ~S              // stored correctly ✓
      → jump to 0xD8D0 (mid-function in keypress handler)
```

### The Exact Bug

At address `0xDA4E`, the VIA handler jumps to `0xD8D0` (8 bytes into the
keypress color handler). At this point:

- Register `a2` = the H byte from the USB packet
- Register `a4` = pointer to `0x2001BAAF` (three bytes: R, G, B)
- Register `a5` = pointer to the LED state struct

The jump target at `0xD8D0` reads RGB from `a4[0-2]` and writes them to
`state[6-8]`. The problem: `a4` points to a **global RAM cache** at
`0x2001BAAF-B1` that holds whatever RGB values were last set by a *physical
keypress*. The VIA handler never converts the new H value to RGB — it just
loads the stale cached values.

The register `a4` was computed earlier in the handler as `0x2001BA2C + 0x83`,
where `0x83` was the case value still sitting in register `a3`. This arithmetic
coincidentally produces `0x2001BAAF` — the address of the RGB cache. A classic
case of register reuse leading to a subtle data-flow bug.

### Why the Keypress Path Works

The keypress path calls a function *upstream* that performs HSV→RGB conversion
and passes the fresh RGB array as a parameter. The VIA path skips this
upstream function entirely and jumps directly to the store routine, so the
conversion never happens.

```
Keypress:  HSV→RGB conversion → color_handler(hue, fresh_rgb, state)  ✓
VIA USB:   (no conversion)    → color_handler(hue, stale_rgb, state)  ✗
```

---

## 7. The Fix — RISC-V Code Cave Injection

### Strategy

The fix intercepts the jump at `0xDA4E` and routes it through a **code cave**
— a block of new code placed in unused firmware padding. The code cave
performs the missing HSV→RGB conversion, writes the result to the global RGB
cache at `0x2001BAAF`, then continues to the original jump target.

```
BEFORE:
  0xDA4E: lb a3, 3(s1)     ← load speed field
  0xDA52: c.j 0xD8D0       ← jump to color store (with stale RGB)

AFTER:
  0xDA4E: j 0x10A4         ← jump to code cave
  0xDA52: c.nop            ← pad to same size (6 bytes)

CODE CAVE at 0x10A4:
  lb a3, 3(s1)             ← displaced instruction
  [HSV→RGB conversion]     ← new code: converts H in a2 to RGB in a4[0-2]
  j 0xD8D0                 ← continue to original target
```

### Finding the Code Cave

The firmware has large regions of zero-padding between code sections. A scan
found 527 consecutive zero bytes at offset `0x10A4`. Cross-referencing with
Ghidra confirmed no functions reference this area. The HSV→RGB conversion
needs 276 bytes — well within the available space.

### HSV→RGB Color Wheel Algorithm

The conversion divides the 0–255 hue range into 6 sectors of 43 values each.
Within each sector, one RGB channel is at 255, one is at 0, and one ramps
linearly:

```
H =   0– 42: R=255,      G=frac*6,    B=0          (red → yellow)
H =  43– 85: R=255-frac, G=255,       B=0          (yellow → green)
H =  86–128: R=0,         G=255,       B=frac*6    (green → cyan)
H = 129–171: R=0,         G=255-frac,  B=255       (cyan → blue)
H = 172–214: R=frac*6,    G=0,         B=255       (blue → magenta)
H = 215–255: R=255,       G=0,         B=255-frac  (magenta → red)
```

Where `frac = (H - sector_start)` and the `*6` maps the 43-unit sector width
to 0–255.

### RISC-V Assembly Implementation

The patch script (`patch_firmware.py`) includes complete RV32I instruction
encoders and builds the code cave programmatically:

```python
# Instruction encoders for RV32I
def addi(rd, rs1, imm):    # rd = rs1 + imm
def slli(rd, rs1, shamt):  # rd = rs1 << shamt
def add(rd, rs1, rs2):     # rd = rs1 + rs2
def sub(rd, rs1, rs2):     # rd = rs1 - rs2
def sb(rs2, base, offset): # mem[base+offset] = rs2 (byte)
def lb(rd, base, offset):  # rd = mem[base+offset] (byte)
def bltu(rs1, rs2, off):   # if rs1 < rs2 (unsigned) goto pc+off
def j_instr(offset):       # unconditional jump to pc+offset
```

Each sector handler computes the fractional value and stores R, G, B:

```python
def build_sector(sector, h_offset):
    sc  = addi(t0, a2, -h_offset)   # t0 = H - sector_start
    sc += slli(t1, t0, 1)            # t1 = t0 * 2
    sc += slli(t2, t0, 2)            # t2 = t0 * 4
    sc += add(t0, t1, t2)            # t0 = t0 * 6  (frac scaled to 0–255)
    # Then store R, G, B to a4[0], a4[1], a4[2] based on sector
```

The comparison chain at the top of the code cave:

```python
for thresh in [43, 86, 129, 172, 215]:
    code += addi(t1, x0, thresh)     # t1 = threshold
    code += bltu(a2, t1, sector_N)   # if H < threshold, goto sector N
# Fall through to sector 5 (H >= 215)
```

### The Displaced Instruction Problem

The original code at `0xDA4E` is:

```
83 86 34 00   lb a3, 3(s1)      ← 4 bytes (RV32I)
bd bd         c.j 0xD8D0        ← 2 bytes (RV32C compressed)
```

The compressed `c.j` can only reach ±2 KB, but the code cave at `0x10A4` is
~51 KB away. Solution: replace all 6 bytes with a full 4-byte `j` instruction
(±1 MB range) plus a 2-byte `c.nop`. The displaced `lb a3, 3(s1)` becomes
the first instruction of the code cave.

### CRC Update

After patching, the firmware CRC must be recalculated:

```python
crc = binascii.crc32(bytes(fw[:-4])) & 0xFFFFFFFF
fw[-4:] = struct.pack('<I', crc ^ 0xFFFFFFFF)

# Verify: CRC32(entire patched firmware) must equal 0xFFFFFFFF
assert binascii.crc32(bytes(fw)) & 0xFFFFFFFF == 0xFFFFFFFF
```

Original CRC: `0xA7DA1601` → Patched CRC: `0x735D569A`

### Patch Summary

| Location  | Size    | Change                                        |
|-----------|---------|-----------------------------------------------|
| `0x10A4`  | 276 B   | HSV→RGB code cave (was zero padding)          |
| `0xDA4E`  | 6 B     | `lb+c.j` → `j 0x10A4 + c.nop`                |
| Last 4 B  | 4 B     | CRC update                                    |
| **Total** | **286 B** | out of 121,332 bytes (0.24% of firmware)    |

---

## 8. OTA Protocol Reverse Engineering

### Decompiling the .NET Flasher

The flasher (`Crush80-RGB-Firmware.exe`) was decompiled with ILSpy. Key
findings from `usb_Form.cs`:

**Device connection**: opens the HID device matching VID/PID on usage page
`0xFFEF`. The report ID, VID, and PID are loaded from `param_128K.bin`:

```
param_128K.bin:
  Offset 0x30 (48): enc_key[16]    ← loaded but NEVER USED
  Offset 0x40 (64): VID string     ← "320F"
  Offset 0x44 (68): PID string     ← "5055"
  Offset 0x48 (72): Report ID      ← "05"
```

**The encryption red herring**: `enc_key[16]` is loaded from the parameter
file and stored in a field — but never referenced anywhere in the data
transfer path. The `System.Security.Cryptography` import in the utility class
is only used for MD5 hashing in a license function. **The OTA protocol is
entirely unencrypted.**

### Protocol Structure

The OTA is response-driven: send a packet, wait for ACK, send the next.

**Start packet** (triggers OTA mode):

```
Byte layout: [report_id] [02] [02] [00] [01] [FF] [FF...FF]
              ↑           ↑    ↑    ↑    ↑    ↑
              Report ID   cmd  len  pad  START marker
```

**Data packet** (3 chunks per packet):

```
[report_id] [02] [len] [00] [chunk₀] [chunk₁] [chunk₂] [FF...FF]
                  ↑
                  20, 40, or 60

Each chunk (20 bytes):
  [index_lo] [index_hi] [16 bytes firmware data] [crc16_lo] [crc16_hi]
```

CRC16 is computed over the 18-byte prefix (index + data) using polynomial
`0xA001` (standard Telink OTA CRC):

```python
def crc16(data):
    crc = 0xFFFF
    for b in data:
        for _ in range(8):
            if (crc ^ b) & 1:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
            b >>= 1
    return crc
```

**End packet** (final chunk count + two's complement):

```
[report_id] [02] [06] [00] [02] [FF] [cnt_lo] [cnt_hi] [neg_lo] [neg_hi]
                             ↑                  ↑
                             END marker          ~count + 1
```

**Success response**: `[05] [02] [03] [00] [06] [FF] [result]` where
`result = 0x00` means success.

### Termination Logic

The .NET flasher's `ota_write()` function has a subtle boundary condition:
the termination check runs *after* building a chunk but *before* updating
the packet length field. This means the very last chunk at the firmware
boundary is built into the buffer but never included in the packet. The
firmware data is still fully transferred because this phantom chunk only
contains padding bytes beyond the firmware's actual size.

---

## 9. Building a Linux OTA Flasher

The stock flasher is Windows-only (.NET 4.0). A Python replacement
(`flash_ota.py`) was written using only the standard library — no external
dependencies.

### Device Auto-Detection

The script scans `/sys/class/hidraw/hidraw*` and for each device:

1. Reads `/sys/class/hidraw/hidrawN/device/report_descriptor` (binary)
2. Searches for the byte pattern `06 EF FF` (Usage Page 0xFFEF item)
3. Walks up the sysfs tree to find `HID_ID` in `uevent` and matches VID/PID
4. Parses the HID descriptor to determine the output report size

### HID Report Descriptor Parsing

The OTA interface's HID descriptor (from the device) revealed a critical
detail:

```
Usage Page 0xFFEF:
  Report ID 5 → Input (63 bytes), Output (63 bytes), Feature (63 bytes)

Report ID 6 → Mouse input only (no Output!)
```

The .NET flasher's field default is `report_id = 6`, but this is overridden
to `5` by `param_128K.bin` at offset 72 (and the form designer default
`ReportID_textBox.Text = "05"`). Report ID 6 is actually a **mouse** report
sharing the same USB interface — writing to it silently succeeds but the
OTA controller never sees the data.

This was the cause of the initial "no response" failure when testing the
flasher on Linux. The fix: use Report ID 5.

### Packet Construction

```python
# Total write = 1 (report ID) + 63 (report data) = 64 bytes
packet_size = 1 + report_size  # report_size from HID descriptor

# Fill with 0xFF (matches .NET flasher behavior)
pkt = bytearray(packet_size)
pkt[0] = REPORT_ID_OTA  # 5
for i in range(1, packet_size):
    pkt[i] = 0xFF

# Build 3 data chunks at pkt[4], pkt[24], pkt[44]
for j in range(3):
    chunk = [index_lo, index_hi] + firmware_16_bytes
    crc = crc16(chunk)
    pkt[4 + 20*j : 4 + 20*j + 20] = chunk + [crc_lo, crc_hi]
```

### Firmware Format Detection

The flasher auto-detects the input format:

- **`code_2M.bin`** (>1 MB): strips 256-byte header, reads firmware size
  from offset 48 (big-endian)
- **`firmware.bin`** (<1 MB): reads firmware size from offset 24 (little-endian),
  sends data from offset 0

Both formats undergo CRC32 validation before flashing.

### Flash Results

```
$ python3 flash_ota.py firmware_patched.bin

Loading firmware_patched.bin...
  Format: firmware
  Firmware size: 121332 bytes (0x1D9F4)
  CRC: OK (0x735D569A)

  [100%] 2528/2528 packets | 18.0s | 6.6 KB/s
  OTA SUCCESS!
```

---

## 10. Verification & SignalRGB Integration

### Testing the Patch

Using the WebHID test app (`via-test.html`), the hue sweep
(`H = 0, 32, 64, ... 255` with `S = 255`) now produces visible color changes
on the keyboard when in Effect 6 (LIGHT_MODE / solid color).

Before the patch, only saturation changes were visible. After the patch,
the full HSV color space is accessible via USB.

### SignalRGB Plugin

The plugin (`WobkeyCrush80.js`) was updated to:

1. **Initialize**: switch to Effect 6 (solid color mode)
2. **Render** (called each frame):
   - Read the SignalRGB canvas color at position (0,0)
   - Convert RGB → HSV (H and S in 0–255 range)
   - Send `[0x07, 0x03, 0x04, H, S]` if color changed
   - Map V to firmware brightness (0–9) and send if changed
3. **Shutdown**: restore full brightness

The plugin only sends packets when values actually change to minimize USB
bus traffic.

### What's Achievable vs. Not

| Feature            | Status                                              |
|--------------------|-----------------------------------------------------|
| Solid single color | **Fixed** — H+S via VIA channel 3, ID 4             |
| Brightness sync    | Works — VIA channel 3, ID 1 (0–9 range)             |
| Effect selection   | Works — VIA channel 3, ID 2 (0–18)                  |
| Per-key RGB        | Not possible — firmware doesn't expose per-key control |

---

## 11. Lessons Learned

### Reverse Engineering Methodology

1. **Start from the known interface** — the VIA protocol gave us the exact
   command bytes and expected behavior, narrowing the search space.

2. **Compare working vs. broken paths** — the keypress path worked, the USB
   path didn't. Diffing the two in decompilation immediately revealed the
   missing HSV→RGB conversion.

3. **Trust the hardware, question the firmware** — the fact that physical
   keypresses changed the hue proved the hardware was capable. The bug had
   to be in the firmware's data flow.

### Binary Patching Techniques

4. **Code caves in padding** — firmware binaries almost always have unused
   regions (alignment padding, reserved space). These are safe places to
   inject new code without displacing existing functions.

5. **Instruction displacement** — when the patch site is too small for the
   new code, move the displaced instruction(s) to the start of the code
   cave. The execution flow becomes: original site → cave → displaced
   instruction → new code → original continuation.

6. **Compressed instruction limitations** — RISC-V's compressed `c.j` has
   ±2 KB range. When the code cave is farther away, use the full 4-byte
   `j` instruction (±1 MB range). This required consuming 2 extra bytes at
   the patch site (filled with `c.nop`).

### Protocol Analysis

7. **Dead code in reference implementations** — the `enc_key` field was
   loaded from a config file, creating the appearance of encryption. Only
   by tracing every reference to it through the decompiled code could we
   confirm it was never used in the data transfer path.

8. **Report IDs matter** — the .NET flasher's hardcoded default (`report_id = 6`)
   was wrong for this device. The actual ID (`5`) was loaded from a config
   file at runtime. On Linux with hidraw, writing to the wrong report ID
   silently succeeds — the kernel accepts the write, but the device's OTA
   controller never processes it.

9. **Response-driven flow control** — the OTA protocol doesn't use fixed
   timing or polling. Each packet must be acknowledged before the next is
   sent. This prevents buffer overruns on the device side and is essential
   to replicate correctly in any alternative flasher implementation.

---

## File Reference

| File                    | Description                                          |
|-------------------------|------------------------------------------------------|
| `firmware.bin`          | Original extracted firmware (121,332 bytes)           |
| `firmware_patched.bin`  | Patched firmware with hue fix                        |
| `patch_firmware.py`     | Reproducible patch script (RISC-V encoders + CRC)    |
| `flash_ota.py`          | Python OTA flasher for Linux (zero dependencies)      |
| `SignalRGB/WobkeyCrush80.js` | SignalRGB plugin with color sync               |
| `SignalRGB/via-test.html`    | WebHID test app for interactive probing         |
| `Ghidra*.java`          | Ghidra headless scripts for automated decompilation   |
| `code_2M.bin`           | Original OTA image from .NET flasher                  |
| `code_2M_patched.bin`   | Patched OTA image for .NET flasher                    |
| `param_128K.bin`        | OTA parameters (report ID, VID/PID, unused AES key)   |
| `decompiled/`           | ILSpy output of the .NET flasher                      |
| `99-wobkey-crush80.rules` | udev rule for hidraw access                         |
