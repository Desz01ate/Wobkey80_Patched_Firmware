#!/usr/bin/env python3
"""Focused disassembly of VIA dispatch and RGB handler areas."""

import struct
from capstone import Cs, CS_ARCH_RISCV, CS_MODE_RISCV32, CS_MODE_RISCVC

with open("firmware.bin", "rb") as f:
    fw = f.read()

md = Cs(CS_ARCH_RISCV, CS_MODE_RISCV32 | CS_MODE_RISCVC)
md.detail = False

def disasm_range(start, length, label=""):
    print(f"\n{'='*78}")
    print(f"  {label}")
    print(f"{'='*78}")
    code = fw[start:start+length]
    count = 0
    for insn in md.disasm(code, start):
        print(f"  0x{insn.address:05X}: {insn.bytes.hex(' '):20s}  {insn.mnemonic:10s} {insn.op_str}")
        count += 1
    if count == 0:
        print(f"  (no valid instructions decoded, raw: {code[:32].hex(' ')})")

# VIA dispatch candidate at 0x1528E
disasm_range(0x15200, 512, "VIA dispatch region 0x15200-0x15400")

# Now let's look at the 0x0DC area more carefully — this seems like the RGB handler
# with the effect bounds check. Let me trace back to find the entry point.
disasm_range(0x0D5E0, 2048, "RGB/VIA custom channel handler 0x0D5E0-0x0DDE0")
