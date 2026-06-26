#!/usr/bin/env python3
"""
Wobkey Crush 80 Firmware Hue Fix Patch — 20260618 / v6.17 build

Re-port of patch_firmware.py to the official RGB firmware update dated
2026-06-18 (firmware "6.17"). That release is a full recompile, so all the
addresses and several register assignments moved; the original patch's
anchors no longer exist in it.

Root cause (unchanged): the VIA SET handler stores the H byte to the state
struct but copies stale cached RGB from a global buffer into state[6..8]
instead of converting H -> RGB.

Differences from the stock patch in this build:
  - Handler relocated 0xDA20 -> 0xDF84; the store block is now INLINE
    (stock jumped out to 0xD8D0; here it falls straight through at 0xDFB6).
  - Registers changed: H is now in a1 (was a2); struct base is a2 (was s1).
  - Patch site is a clean 4-byte instruction: 0xDFB2 `lb a3, 3(a2)`
    (stock was a 6-byte lb+c.j at 0xDA4E), so we replace it with a single
    4-byte `j CODE_CAVE` — no padding nop needed.
  - The 0x10A4 padding cave the stock patch used is occupied in this build;
    we inject into the zero-padding run at 0x1100 instead.

Flow after patching:
    0xDFB0  c.add a4, a3        ; a4 = stale-RGB buffer ptr (unchanged)
    0xDFB2  j CODE_CAVE         ; <-- patched (was: lb a3, 3(a2))
    CODE_CAVE:
        lb a3, 3(a2)            ; restore the overwritten instruction
        <HSV(a1)->RGB written to a4[0..2]>
        j 0xDFB6               ; back into the existing load+store sequence
    0xDFB6  lb a6,0(a4) / lb a0,1(a4) / lb a2,2(a4)   ; now read FRESH rgb
    0xDFC6  sb ... -> state[6,7,8] = computed R,G,B

Usage:
    cd scripts && python3 patch_firmware_20260618.py
    # Reads ../firmware/code_2M_20260618.bin
    # Produces ../firmware/firmware_20260618_patched.bin
    #      and ../firmware/code_2M_20260618_patched.bin
"""

import struct
import binascii
import os

# ---------------------------------------------------------------- RV32 encoders
def pack32(val): return struct.pack('<I', val & 0xFFFFFFFF)
def pack16(val): return struct.pack('<H', val & 0xFFFF)

def addi(rd, rs1, imm):
    return pack32(((imm & 0xFFF) << 20) | (rs1 << 15) | (0b000 << 12) | (rd << 7) | 0x13)

def slli(rd, rs1, shamt):
    return pack32((shamt << 20) | (rs1 << 15) | (0b001 << 12) | (rd << 7) | 0x13)

def add(rd, rs1, rs2):
    return pack32((rs2 << 20) | (rs1 << 15) | (0b000 << 12) | (rd << 7) | 0x33)

def sub(rd, rs1, rs2):
    return pack32((0x20 << 25) | (rs2 << 20) | (rs1 << 15) | (0b000 << 12) | (rd << 7) | 0x33)

def sb(rs2, base, offset):
    off = offset & 0xFFF
    return pack32(((off >> 5) << 25) | (rs2 << 20) | (base << 15) | (0b000 << 12) | ((off & 0x1F) << 7) | 0x23)

def lb(rd, base, offset):
    return pack32(((offset & 0xFFF) << 20) | (base << 15) | (0b000 << 12) | (rd << 7) | 0x03)

def bltu(rs1, rs2, offset):
    off = offset if offset >= 0 else offset + (1 << 13)
    off &= 0x1FFF
    return pack32(((off >> 12) << 31) | (((off >> 5) & 0x3F) << 25) | (rs2 << 20) |
                  (rs1 << 15) | (0b110 << 12) | (((off >> 1) & 0xF) << 8) |
                  (((off >> 11) & 1) << 7) | 0x63)

def j_instr(offset):
    off = offset if offset >= 0 else offset + (1 << 21)
    off &= 0x1FFFFF
    return pack32(((off >> 20) << 31) | (((off >> 1) & 0x3FF) << 21) |
                  (((off >> 11) & 1) << 20) | (((off >> 12) & 0xFF) << 12) | 0x6F)

# Registers
x0, t0, t1, t2 = 0, 5, 6, 7
a1, a2, a3, a4 = 11, 12, 13, 14   # a1 = H, a2 = struct base, a4 = rgb buffer ptr

# ---------------------------------------------------------------- patch layout
CODE_CAVE   = 0x1100    # zero-padding run (0x10A8..0x12AF) — well clear of markers
JUMP_TARGET = 0xDFB6    # back into the inline load+store sequence
PATCH_ADDR  = 0xDFB2    # `lb a3, 3(a2)` — replaced by a single 4-byte `j CODE_CAVE`
ORIG_BYTES  = bytes.fromhex("83063600")   # lb a3, 3(a2)

def build_sector(sector, h_offset):
    """One HSV sector: frac = (H - h_offset) * 6, then set R/G/B (S=V=255)."""
    sc = bytearray()
    sc += addi(t0, a1, (-h_offset) & 0xFFF)   # t0 = H - h_offset   (H in a1)
    sc += slli(t1, t0, 1)                       # t1 = t0 * 2
    sc += slli(t2, t0, 2)                       # t2 = t0 * 4
    sc += add(t0, t1, t2)                       # t0 = t0 * 6  (frac)

    if sector == 0:    sc += addi(t1,x0,255); sc += sb(t1,a4,0); sc += sb(t0,a4,1); sc += sb(x0,a4,2)
    elif sector == 1:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(t2,a4,0); sc += sb(t1,a4,1); sc += sb(x0,a4,2)
    elif sector == 2:  sc += addi(t1,x0,255); sc += sb(x0,a4,0); sc += sb(t1,a4,1); sc += sb(t0,a4,2)
    elif sector == 3:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(x0,a4,0); sc += sb(t2,a4,1); sc += sb(t1,a4,2)
    elif sector == 4:  sc += addi(t1,x0,255); sc += sb(t0,a4,0); sc += sb(x0,a4,1); sc += sb(t1,a4,2)
    elif sector == 5:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(t1,a4,0); sc += sb(x0,a4,1); sc += sb(t2,a4,2)
    return sc

def build_code_cave():
    sectors = {s: build_sector(s, s * 43) for s in range(6)}

    lb_size = 4
    cmp_size = 40  # 5 thresholds x 8 bytes (addi + bltu)
    s5_end = CODE_CAVE + lb_size + cmp_size + len(sectors[5]) + 4

    starts = {}
    pos = s5_end
    for s in range(5):
        starts[s] = pos
        pos += len(sectors[s]) + 4
    end_label = pos

    code = bytearray()
    pc = CODE_CAVE

    code += lb(a3, a2, 3); pc += 4          # restore overwritten `lb a3, 3(a2)`

    for i, thresh in enumerate([43, 86, 129, 172, 215]):
        code += addi(t1, x0, thresh); pc += 4
        code += bltu(a1, t1, starts[i] - pc); pc += 4   # if H < thresh -> sector i

    code += sectors[5]; pc += len(sectors[5])           # default sector 5
    code += j_instr(end_label - pc); pc += 4

    for s in range(5):
        code += sectors[s]; pc += len(sectors[s])
        code += j_instr(end_label - pc); pc += 4

    code += j_instr(JUMP_TARGET - pc); pc += 4           # end_label: back to 0xDFB6
    return code

# ---------------------------------------------------------------- image helpers
HERE = os.path.dirname(os.path.abspath(__file__))
FW_DIR = os.path.join(HERE, "..", "firmware")

def extract_fw(code2m):
    fw_size = (code2m[48] << 24) | (code2m[49] << 16) | (code2m[50] << 8) | code2m[51]
    return code2m[256:256 + fw_size], fw_size

def apply_patch():
    src = os.path.join(FW_DIR, "code_2M_20260618.bin")
    with open(src, "rb") as f:
        code2m = bytearray(f.read())

    fw_bytes, fw_size = extract_fw(code2m)
    fw = bytearray(fw_bytes)

    # --- sanity: the stock image must already satisfy the CRC scheme ---
    assert binascii.crc32(bytes(fw)) & 0xFFFFFFFF == 0xFFFFFFFF, \
        "Unpatched firmware fails CRC self-check — wrong image or layout?"

    # --- verify anchors ---
    assert fw[PATCH_ADDR:PATCH_ADDR + 4] == ORIG_BYTES, (
        f"Patch site mismatch at 0x{PATCH_ADDR:X}: "
        f"{fw[PATCH_ADDR:PATCH_ADDR+4].hex()} != {ORIG_BYTES.hex()} — wrong firmware version?")

    cave = build_code_cave()
    assert all(b == 0 for b in fw[CODE_CAVE:CODE_CAVE + len(cave)]), \
        f"Code cave 0x{CODE_CAVE:X} (+{len(cave)}) is not empty!"

    # --- insert cave ---
    fw[CODE_CAVE:CODE_CAVE + len(cave)] = cave

    # --- redirect the patch site: single 4-byte jump, no nop needed ---
    fw[PATCH_ADDR:PATCH_ADDR + 4] = j_instr(CODE_CAVE - PATCH_ADDR)

    # --- recompute trailing CRC (last 4 bytes = ~crc32(preceding)) ---
    crc = binascii.crc32(bytes(fw[:-4])) & 0xFFFFFFFF
    fw[-4:] = struct.pack('<I', crc ^ 0xFFFFFFFF)
    assert binascii.crc32(bytes(fw)) & 0xFFFFFFFF == 0xFFFFFFFF, "CRC check failed!"

    # --- write standalone firmware ---
    out_fw = os.path.join(FW_DIR, "firmware_20260618_patched.bin")
    with open(out_fw, "wb") as f:
        f.write(fw)
    print(f"Code cave:        0x{CODE_CAVE:X} ({len(cave)} bytes)")
    print(f"Patched 0x{PATCH_ADDR:X}: {ORIG_BYTES.hex()} -> {bytes(fw[PATCH_ADDR:PATCH_ADDR+4]).hex()}")
    print(f"New firmware CRC: 0x{struct.unpack_from('<I', fw, len(fw)-4)[0]:08X}")
    print(f"Written {out_fw} ({len(fw)} bytes)")

    # --- write full OTA image ---
    code2m[256:256 + len(fw)] = fw
    out_ota = os.path.join(FW_DIR, "code_2M_20260618_patched.bin")
    with open(out_ota, "wb") as f:
        f.write(code2m)
    print(f"Written {out_ota} ({len(code2m)} bytes)")
    print("\nPatch applied successfully.")

if __name__ == "__main__":
    apply_patch()
