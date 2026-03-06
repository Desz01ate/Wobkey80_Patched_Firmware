#!/usr/bin/env python3
"""
Analyze the Telink TLSR firmware binary.

Telink TLSR825x (RISC-V) firmware layout:
  0x00-0x03: Boot vector (jump instruction)
  0x08:      FW size or entry info
  0x18-0x1B: Firmware size (LE)
  0x20-0x23: "KNLT" marker (Telink reversed)
  Code follows after header, typically mapped at 0x00000000 in flash.

The TLSR825x/TLSR8278 uses RV32 (RISC-V 32-bit) with compressed instructions (RV32IMC).
"""

import struct
from capstone import Cs, CS_ARCH_RISCV, CS_MODE_RISCV32, CS_MODE_RISCVC

with open("firmware.bin", "rb") as f:
    fw = f.read()

print(f"Firmware size: {len(fw)} bytes")
print(f"Boot vector:   {fw[0:4].hex(' ')}")
print(f"FW size field: {struct.unpack_from('<I', fw, 0x18)[0]:#x}")
print(f"Marker:        {fw[0x20:0x24]}")
print()

# --- String search: look for VIA / RGB / hue / color related strings ---
print("=== String search ===")
import re

# Find printable ASCII strings >= 4 chars
strings = []
for m in re.finditer(rb'[\x20-\x7e]{4,}', fw):
    strings.append((m.start(), m.group().decode('ascii', errors='replace')))

# Filter for interesting keywords
keywords = ['rgb', 'hue', 'sat', 'color', 'colour', 'via', 'hid', 'led', 'light',
            'bright', 'effect', 'mode', 'breath', 'channel', 'matrix', 'key',
            'custom', 'set', 'get', 'value', 'usb', 'report', 'cmd', 'wave',
            'scan', 'rain', 'laser', 'logo', 'side', 'power', 'ota']

print("\nAll strings (interesting):")
for offset, s in strings:
    sl = s.lower()
    if any(k in sl for k in keywords):
        print(f"  0x{offset:05X}: {s}")

print(f"\nAll strings ({len(strings)} total), first 80:")
for offset, s in strings[:80]:
    print(f"  0x{offset:05X}: {s}")

# --- Look for VIA protocol constants ---
print("\n=== VIA protocol byte pattern search ===")
# VIA custom channel handler: the firmware should compare incoming cmd byte to 0x07/0x08
# and channel byte to 0x03
# Look for the byte sequence that handles custom_set_value (0x07)

# Search for potential handler dispatch: bytes 0x07 and 0x03 near each other
for pattern_name, pattern in [
    ("VIA SET custom (07 03)", bytes([0x07, 0x03])),
    ("VIA GET custom (08 03)", bytes([0x08, 0x03])),
]:
    positions = []
    start = 0
    while True:
        pos = fw.find(pattern, start)
        if pos == -1:
            break
        positions.append(pos)
        start = pos + 1
    if positions:
        print(f"  {pattern_name}: {len(positions)} hits (first 5: {[hex(p) for p in positions[:5]]})")

# --- Disassemble around boot vector and interesting areas ---
md = Cs(CS_ARCH_RISCV, CS_MODE_RISCV32 | CS_MODE_RISCVC)
md.detail = False

def disasm_range(start, length, label=""):
    print(f"\n=== Disassembly: {label} (0x{start:05X} - 0x{start+length:05X}) ===")
    code = fw[start:start+length]
    for insn in md.disasm(code, start):
        print(f"  0x{insn.address:05X}: {insn.bytes.hex(' '):20s}  {insn.mnemonic:10s} {insn.op_str}")

# Boot vector
disasm_range(0, 64, "Boot vector")

# Look for the interrupt vector table (typical Telink layout)
# After the header, there's usually a vector table at specific offsets
disasm_range(0x100, 128, "Code at 0x100")

# --- Search for RGB matrix effect table ---
print("\n=== Effect count / table search ===")
# We know there are 19 effects (0-18). Look for the value 18 or 19 used as a bound check.
# In RISC-V, comparing with immediate 18 would be: li reg, 18 then bge/blt
# li a5, 18 = addi a5, zero, 18 = 0x01200793
for val in [18, 19]:
    # addi rd, x0, val  — opcode 0x13, rs1=x0, various rd
    for rd in range(32):
        imm = val
        insn_word = (imm << 20) | (0 << 15) | (0 << 12) | (rd << 7) | 0x13
        insn_bytes = struct.pack("<I", insn_word)
        pos = 0
        while True:
            pos = fw.find(insn_bytes, pos)
            if pos == -1:
                break
            print(f"  addi x{rd}, x0, {val} at 0x{pos:05X}")
            pos += 1

# Also check compressed: c.li rd, imm
for val in [18, 19, 9, 4, 255]:
    for rd in range(8, 16):  # c.li uses rd = x8-x15 mostly
        # Actually c.li can use any rd!=x0
        # c.li rd, imm: [15:13]=010 [12]=imm[5] [11:7]=rd [6:2]=imm[4:0] [1:0]=01
        for rd in range(1, 32):
            imm5 = val & 0x1F
            imm_sign = (val >> 5) & 1  # for 6-bit signed
            if val < 32:
                insn_hw = (0b010 << 13) | (imm_sign << 12) | (rd << 7) | (imm5 << 2) | 0b01
                insn_bytes = struct.pack("<H", insn_hw)
                pos = 0
                hits = 0
                while True:
                    pos = fw.find(insn_bytes, pos)
                    if pos == -1:
                        break
                    hits += 1
                    pos += 1
                if hits > 0 and hits < 20:
                    print(f"  c.li x{rd}, {val}: {hits} hits")
        break  # only do inner loop once per val
