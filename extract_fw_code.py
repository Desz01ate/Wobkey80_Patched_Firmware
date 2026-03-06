#!/usr/bin/env python3
"""Extract the actual firmware code from code_2M.bin and analyze its header."""

import struct

with open("code_2M.bin", "rb") as f:
    data = f.read()

# Header analysis (from usb_Form.cs decompile)
fw_version = struct.unpack_from("<I", data, 2)[0]
# source_bin_size = big-endian uint32 at offset 48
fw_size = (data[48] << 24) | (data[49] << 16) | (data[50] << 8) | data[51]
# CRC at end of firmware
fw_data = data[256 : 256 + fw_size]
fw_crc = struct.unpack_from("<I", fw_data, fw_size - 4)[0]

print(f"Firmware version: 0x{fw_version:08X}")
print(f"Firmware size:    {fw_size} bytes (0x{fw_size:X})")
print(f"Firmware CRC:     0x{fw_crc:08X}")
print(f"First 32 bytes:   {fw_data[:32].hex(' ')}")
print(f"Telink marker:    {fw_data[0x20:0x24]}")

with open("firmware.bin", "wb") as f:
    f.write(fw_data)
print(f"\nExtracted firmware.bin ({len(fw_data)} bytes)")
