#\!/usr/bin/env python3
"""
Wobkey Crush 80 Firmware Hue Fix Patch

Fixes the VIA USB color handler so that setting hue (H byte) via
VIA protocol actually changes the displayed color.

Root cause: The VIA SET handler at 0xDA20 stores the H byte to the
state struct but overwrites the RGB fields (state[6-8]) with stale
cached values from global RAM instead of converting H to RGB.

This patch:
1. Inserts an HSV->RGB color wheel conversion at 0x10A4 (unused padding)
2. Redirects the VIA color handler to call the conversion before storing
3. Updates the firmware CRC

Usage:
    python3 patch_firmware.py
    # Produces: firmware_patched.bin and code_2M_patched.bin
"""

import struct
import binascii

# RV32I instruction encoders
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
s1, a2, a3, a4, a5 = 9, 12, 13, 14, 15

CODE_CAVE = 0x10A4
JUMP_TARGET = 0xD8D0
PATCH_ADDR = 0xDA4E

def build_sector(sector, h_offset):
    """Build one HSV sector handler (computes frac, sets R/G/B)."""
    sc = bytearray()
    sc += addi(t0, a2, (-h_offset) & 0xFFF)
    sc += slli(t1, t0, 1)
    sc += slli(t2, t0, 2)
    sc += add(t0, t1, t2)

    if sector == 0:    sc += addi(t1,x0,255); sc += sb(t1,a4,0); sc += sb(t0,a4,1); sc += sb(x0,a4,2)
    elif sector == 1:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(t2,a4,0); sc += sb(t1,a4,1); sc += sb(x0,a4,2)
    elif sector == 2:  sc += addi(t1,x0,255); sc += sb(x0,a4,0); sc += sb(t1,a4,1); sc += sb(t0,a4,2)
    elif sector == 3:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(x0,a4,0); sc += sb(t2,a4,1); sc += sb(t1,a4,2)
    elif sector == 4:  sc += addi(t1,x0,255); sc += sb(t0,a4,0); sc += sb(x0,a4,1); sc += sb(t1,a4,2)
    elif sector == 5:  sc += addi(t1,x0,255); sc += sub(t2,t1,t0); sc += sb(t1,a4,0); sc += sb(x0,a4,1); sc += sb(t2,a4,2)
    return sc

def build_code_cave():
    """Build the HSV->RGB code cave."""
    sectors = {s: build_sector(s, s * 43) for s in range(6)}

    # Calculate layout
    lb_size = 4
    cmp_size = 40  # 5 thresholds × 8 bytes
    s5_end = CODE_CAVE + lb_size + cmp_size + len(sectors[5]) + 4

    starts = {}
    pos = s5_end
    for s in range(5):
        starts[s] = pos
        pos += len(sectors[s]) + 4
    end_label = pos

    # Build
    code = bytearray()
    pc = CODE_CAVE

    code += lb(a3, s1, 3); pc += 4

    for i, thresh in enumerate([43, 86, 129, 172, 215]):
        code += addi(t1, x0, thresh); pc += 4
        code += bltu(a2, t1, starts[i] - pc); pc += 4

    code += sectors[5]; pc += len(sectors[5])
    code += j_instr(end_label - pc); pc += 4

    for s in range(5):
        code += sectors[s]; pc += len(sectors[s])
        code += j_instr(end_label - pc); pc += 4

    code += j_instr(JUMP_TARGET - pc); pc += 4
    return code

def apply_patch():
    with open("firmware.bin", "rb") as f:
        fw = bytearray(f.read())

    # Verify original bytes
    assert fw[PATCH_ADDR:PATCH_ADDR+6] == bytes.fromhex("83863400bdbd"), \
        "Patch site doesn't match expected bytes - wrong firmware version?"
    assert all(b == 0 for b in fw[CODE_CAVE:CODE_CAVE+276]), \
        "Code cave area is not empty!"

    # Build and insert code cave
    cave = build_code_cave()
    fw[CODE_CAVE:CODE_CAVE+len(cave)] = cave

    # Patch jump at 0xDA4E: replace lb+c.j with j+nop
    fw[PATCH_ADDR:PATCH_ADDR+4] = j_instr(CODE_CAVE - PATCH_ADDR)
    fw[PATCH_ADDR+4:PATCH_ADDR+6] = pack16(0x0001)  # c.nop

    # Update CRC (last 4 bytes = ~CRC32 of preceding data)
    crc = binascii.crc32(bytes(fw[:-4])) & 0xFFFFFFFF
    fw[-4:] = struct.pack('<I', crc ^ 0xFFFFFFFF)

    # Verify
    assert binascii.crc32(bytes(fw)) & 0xFFFFFFFF == 0xFFFFFFFF, "CRC check failed!"

    with open("firmware_patched.bin", "wb") as f:
        f.write(fw)
    print(f"Written firmware_patched.bin ({len(fw)} bytes)")

    # Also create patched code_2M.bin for OTA flasher
    try:
        with open("code_2M.bin", "rb") as f:
            code2m = bytearray(f.read())
        code2m[256:256+len(fw)] = fw
        with open("code_2M_patched.bin", "wb") as f:
            f.write(code2m)
        print(f"Written code_2M_patched.bin ({len(code2m)} bytes)")
    except FileNotFoundError:
        print("code_2M.bin not found, skipping OTA package")

    print("\nPatch applied successfully!")
    print("To flash: replace code_2M resource in OTA flasher with code_2M_patched.bin")

if __name__ == "__main__":
    apply_patch()
