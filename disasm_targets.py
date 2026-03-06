#!/usr/bin/env python3
"""Targeted disassembly around effect/VIA handler areas."""

import struct
from capstone import Cs, CS_ARCH_RISCV, CS_MODE_RISCV32, CS_MODE_RISCVC

with open("firmware.bin", "rb") as f:
    fw = f.read()

md = Cs(CS_ARCH_RISCV, CS_MODE_RISCV32 | CS_MODE_RISCVC)
md.detail = False

def disasm_range(start, length, label=""):
    print(f"\n{'='*72}")
    print(f"  {label} (0x{start:05X} - 0x{start+length:05X})")
    print(f"{'='*72}")
    code = fw[start:start+length]
    for insn in md.disasm(code, start):
        # Annotate interesting constants
        ann = ""
        op = insn.op_str
        if "0x07" in op or "7" == op.split(",")[-1].strip():
            ann = "  # VIA SET_CUSTOM?"
        if "0x08" in op or "8" == op.split(",")[-1].strip():
            ann = "  # VIA GET_CUSTOM?"
        if "18" in op.split(",")[-1].strip() or "0x12" in op:
            ann = "  # max effect=18?"
        if "0x09" in op or "9" == op.split(",")[-1].strip():
            ann = "  # max brightness=9?"
        print(f"  0x{insn.address:05X}: {insn.bytes.hex(' '):20s}  {insn.mnemonic:10s} {insn.op_str}{ann}")

# Key targets from the pattern search:
# addi x15, x0, 18 at 0x04488 — likely effect count bounds
# addi x15, x0, 19 at 0x03B0C — another bounds check
# addi x10, x0, 18 at 0x15A38
# addi x12, x0, 18 at 0x0DCA0, 0x0DCC4

targets = [
    (0x04440, 256, "Near addi x15,x0,18 @ 0x04488 — effect handler?"),
    (0x03AD0, 256, "Near addi x15,x0,19 @ 0x03B0C — bounds check?"),
    (0x0DC70, 256, "Near addi x12,x0,18 @ 0x0DCA0 — effect table?"),
    (0x15A00, 256, "Near addi x10,x0,18 @ 0x15A38 — effect ref?"),
]

for start, length, label in targets:
    disasm_range(start, length, label)

# Now look more broadly: the VIA handler will have a switch on the first
# byte of the received report. We know:
#   0x01 = GET_PROTOCOL_VERSION
#   0x02 = GET_KEYBOARD_VALUE
#   0x03 = SET_KEYBOARD_VALUE
#   0x07 = CUSTOM_SET_VALUE
#   0x08 = CUSTOM_GET_VALUE
#
# The handler likely loads byte[0] from a buffer pointer, then does
# a series of comparisons. Let's search for functions that compare
# a register against multiple small VIA command constants.

print(f"\n{'='*72}")
print(f"  Searching for VIA dispatch (compare with 7, then branch)")
print(f"{'='*72}")

# Look for: li reg, 7 followed within ~20 bytes by beq/bne
# In RISC-V: addi rd, x0, 7 = 0x00700x13 where x varies
# Or c.li rd, 7

import re

# Search for sequences that compare with multiple VIA commands
# Strategy: find "c.li rd, 7" followed closely by branch instructions
for rd in range(1, 32):
    # c.li rd, 7: [15:13]=010 [12]=0 [11:7]=rd [6:2]=00111 [1:0]=01
    imm = 7
    insn_hw = (0b010 << 13) | (0 << 12) | (rd << 7) | ((imm & 0x1F) << 2) | 0b01
    insn_bytes = struct.pack("<H", insn_hw)
    pos = 0
    while True:
        pos = fw.find(insn_bytes, pos)
        if pos == -1:
            break
        # Check if there's also a compare with 8 nearby (within 40 bytes)
        region = fw[max(0,pos-40):pos+40]
        # c.li same_rd, 8
        insn_8 = (0b010 << 13) | (0 << 12) | (rd << 7) | ((8 & 0x1F) << 2) | 0b01
        insn_8_bytes = struct.pack("<H", insn_8)
        if insn_8_bytes in region:
            # Also check for compare with 1, 2, 3 (other VIA commands)
            insn_1 = struct.pack("<H", (0b010 << 13) | (0 << 12) | (rd << 7) | ((1 & 0x1F) << 2) | 0b01)
            insn_2 = struct.pack("<H", (0b010 << 13) | (0 << 12) | (rd << 7) | ((2 & 0x1F) << 2) | 0b01)
            insn_3 = struct.pack("<H", (0b010 << 13) | (0 << 12) | (rd << 7) | ((3 & 0x1F) << 2) | 0b01)
            big_region = fw[max(0,pos-100):pos+100]
            matches = sum(1 for x in [insn_1, insn_2, insn_3, insn_bytes, insn_8_bytes] if x in big_region)
            if matches >= 3:
                print(f"\n  ** Likely VIA dispatch at ~0x{pos:05X} (x{rd} compared with {matches}/5 VIA cmd IDs)")
                disasm_range(max(0, pos-64), 256, f"VIA dispatch candidate near 0x{pos:05X}")
        pos += 1
