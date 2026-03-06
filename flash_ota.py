#!/usr/bin/env python3
"""
Wobkey Crush 80 OTA Firmware Flasher

Flashes firmware via the Telink USB OTA interface (usage page 0xFFEF).
No dependencies beyond Python 3 standard library.

Protocol (reverse-engineered from .NET OTA flasher):
  - Start packet: triggers OTA mode
  - Data packets: 3x [2B index][16B data][2B CRC16] per packet
  - End packet: final chunk count + complement
  - Flow control: device ACKs each packet before next is sent

Usage:
    python3 flash_ota.py firmware_patched.bin
    python3 flash_ota.py code_2M_patched.bin
    python3 flash_ota.py --dry-run firmware_patched.bin
    python3 flash_ota.py --device /dev/hidraw4 firmware_patched.bin
"""

import argparse
import binascii
import glob
import os
import select
import struct
import sys
import time

VID = 0x320F
PID = 0x5055
OTA_USAGE_PAGE = 0xFFEF
REPORT_ID_OTA = 5  # Report ID for OTA (input, output, and feature)
CHUNK_DATA_SIZE = 16
CHUNKS_PER_PACKET = 3


def crc16(data: bytes) -> int:
    """Telink OTA CRC16 (polynomial 0xA001)."""
    crc = 0xFFFF
    for b in data:
        for _ in range(8):
            if (crc ^ b) & 1:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
            b >>= 1
    return crc


def parse_output_report_size(desc: bytes, target_report_id: int) -> int | None:
    """Parse HID report descriptor to find output report byte count for a report ID."""
    i = 0
    report_id = 0
    report_size_bits = 0
    report_count = 0

    while i < len(desc):
        prefix = desc[i]

        if prefix == 0xFE:  # long item
            if i + 1 < len(desc):
                i += 3 + desc[i + 1]
            else:
                break
            continue

        bSize = prefix & 0x03
        if bSize == 3:
            bSize = 4
        bType = (prefix >> 2) & 0x03
        bTag = (prefix >> 4) & 0x0F

        if i + 1 + bSize > len(desc):
            break

        data = desc[i + 1:i + 1 + bSize]
        value = int.from_bytes(data, 'little') if data else 0

        if bType == 1:  # Global
            if bTag == 7:
                report_size_bits = value
            elif bTag == 8:
                report_id = value
            elif bTag == 9:
                report_count = value
        elif bType == 0 and bTag == 9:  # Output (Main)
            if report_id == target_report_id:
                return (report_size_bits * report_count + 7) // 8

        i += 1 + bSize

    return None


def find_ota_device() -> tuple[str | None, int]:
    """Find the OTA hidraw device by VID/PID and usage page 0xFFEF.
    Returns (device_path, output_report_size) or (None, 0)."""
    for hidraw in sorted(glob.glob('/sys/class/hidraw/hidraw*')):
        name = os.path.basename(hidraw)
        dev_path = f'/dev/{name}'

        desc_path = os.path.join(hidraw, 'device', 'report_descriptor')
        if not os.path.exists(desc_path):
            continue

        with open(desc_path, 'rb') as f:
            desc = f.read()

        # Usage Page 0xFFEF = item bytes 06 EF FF
        if b'\x06\xef\xff' not in desc:
            continue

        # Verify VID/PID by walking sysfs to find HID_ID
        search = os.path.join(hidraw, 'device')
        matched = False
        for _ in range(5):
            ue = os.path.join(search, 'uevent')
            if os.path.exists(ue):
                with open(ue) as f:
                    for line in f:
                        if line.startswith('HID_ID='):
                            parts = line.strip().split('=')[1].split(':')
                            if len(parts) == 3:
                                vid = int(parts[1], 16)
                                pid = int(parts[2], 16)
                                if vid == VID and pid == PID:
                                    matched = True
                            break
                if matched:
                    break
            search = os.path.dirname(search)

        if not matched:
            continue

        report_size = parse_output_report_size(desc, REPORT_ID_OTA) or 64
        return dev_path, report_size

    return None, 0


def load_firmware(path: str) -> tuple[bytearray, int]:
    """Load firmware file. Supports both code_2M.bin (OTA image) and raw firmware.bin.
    Returns (padded_data, firmware_size)."""
    with open(path, 'rb') as f:
        raw = f.read()

    if len(raw) > 1_000_000:
        # code_2M format: 256-byte OTA wrapper header + firmware
        fw_size = (raw[48] << 24) | (raw[49] << 16) | (raw[50] << 8) | raw[51]
        if fw_size == 0 or fw_size > len(raw) - 256:
            raise ValueError(f"Invalid firmware size in OTA header: {fw_size} "
                             f"(file is {len(raw)} bytes)")
        fw_data = raw[256:256 + fw_size]
        fmt = "code_2M"
    else:
        # Raw firmware format: size at offset 24 (LE uint32)
        fw_size = struct.unpack_from('<I', raw, 24)[0]
        if fw_size == 0 or fw_size > len(raw):
            fw_size = len(raw)
        fw_data = raw[:fw_size]
        fmt = "firmware"

    print(f"  Format: {fmt}")
    print(f"  Firmware size: {fw_size} bytes (0x{fw_size:X})")

    # Validate CRC: stored CRC is ~CRC32, so CRC32(entire) should be 0xFFFFFFFF
    if fw_size >= 4:
        crc_check = binascii.crc32(fw_data[:fw_size]) & 0xFFFFFFFF
        crc_stored = struct.unpack_from('<I', fw_data, fw_size - 4)[0]
        if crc_check == 0xFFFFFFFF:
            print(f"  CRC: OK (0x{crc_stored:08X})")
        else:
            print(f"  WARNING: CRC mismatch (stored=0x{crc_stored:08X}, "
                  f"computed=0x{crc_check:08X})")

    # Build padded send buffer matching .NET flasher behavior:
    # source_binchar[0..fw_size-1] = firmware
    # source_binchar[fw_size..fw_size+15] = 0xFF
    # source_binchar[fw_size+16..] = 0x00
    # Termination: last chunk index = (fw_size + 15) // 16 - 1
    # (chunk at boundary is discarded per .NET ota_write logic)
    num_chunks = (fw_size + 15) // 16
    buf = bytearray(num_chunks * CHUNK_DATA_SIZE)
    buf[:fw_size] = fw_data[:fw_size]
    # Pad remainder of last chunk with 0xFF
    tail = fw_size % CHUNK_DATA_SIZE
    if tail != 0:
        for i in range(fw_size, fw_size + CHUNK_DATA_SIZE - tail):
            if i < len(buf):
                buf[i] = 0xFF

    return buf, fw_size


def read_response(fd: int, timeout: float = 5.0) -> bytes | None:
    """Read HID response with timeout."""
    ready, _, _ = select.select([fd], [], [], timeout)
    if ready:
        try:
            return os.read(fd, 512)
        except OSError:
            return None
    return None


def drain_pending(fd: int):
    """Read and discard any pending data on the device."""
    while True:
        ready, _, _ = select.select([fd], [], [], 0.05)
        if not ready:
            break
        try:
            os.read(fd, 512)
        except OSError:
            break


def probe_device(dev_path: str, report_size: int):
    """Probe the OTA device: try reading and sending a start command."""
    packet_size = 1 + report_size
    print(f"\nProbing {dev_path} (report size {report_size})...")

    try:
        fd = os.open(dev_path, os.O_RDWR | os.O_NONBLOCK)
    except (PermissionError, FileNotFoundError) as e:
        print(f"  Cannot open: {e}")
        return

    try:
        # Check for any pending data
        print("  Checking for pending data...")
        resp = read_response(fd, timeout=1.0)
        if resp:
            print(f"  Pending data: {resp.hex(' ')}")
        else:
            print("  No pending data")

        # Send OTA start command
        print(f"  Sending start command ({packet_size} bytes)...")
        start = bytearray(packet_size)
        start[0] = REPORT_ID_OTA
        for i in range(1, packet_size):
            start[i] = 0xFF
        start[1] = 0x02
        start[2] = 0x02
        start[3] = 0x00
        start[4] = 0x01
        start[5] = 0xFF
        print(f"  TX: {bytes(start[:12]).hex(' ')} ...")
        try:
            written = os.write(fd, bytes(start))
            print(f"  Written: {written} bytes")
        except OSError as e:
            print(f"  Write error: {e}")
            return

        # Wait for response with extended timeout
        print("  Waiting for response (10s timeout)...")
        for attempt in range(10):
            resp = read_response(fd, timeout=1.0)
            if resp:
                print(f"  RX ({len(resp)} bytes): {resp.hex(' ')}")
                return
            print(f"  ... {attempt + 1}s")
        print("  No response received")

        # Try without report ID prefix (some hidraw setups)
        print("\n  Retrying without report ID prefix...")
        start_no_id = bytes(start[1:])
        try:
            written = os.write(fd, start_no_id)
            print(f"  Written: {written} bytes (no report ID)")
        except OSError as e:
            print(f"  Write error: {e}")
            return

        print("  Waiting for response (5s)...")
        for attempt in range(5):
            resp = read_response(fd, timeout=1.0)
            if resp:
                print(f"  RX ({len(resp)} bytes): {resp.hex(' ')}")
                return
            print(f"  ... {attempt + 1}s")
        print("  No response received")

    finally:
        os.close(fd)


def flash(dev_path: str, fw_buf: bytearray, fw_size: int,
          report_size: int, dry_run: bool = False) -> bool:
    """Flash firmware via Telink OTA protocol."""
    num_chunks = len(fw_buf) // CHUNK_DATA_SIZE
    total_packets = (num_chunks + CHUNKS_PER_PACKET - 1) // CHUNKS_PER_PACKET
    # +1 byte for report ID in hidraw writes
    packet_size = 1 + report_size

    print(f"\n  Device:       {dev_path}")
    print(f"  Chunks:       {num_chunks} ({CHUNK_DATA_SIZE} bytes each)")
    print(f"  Packets:      ~{total_packets}")
    print(f"  Report size:  {report_size} + 1 (report ID) = {packet_size} bytes")

    if dry_run:
        print("\n[DRY RUN] Packet examples:")
        pkt = bytearray(packet_size)
        pkt[0] = REPORT_ID_OTA
        for i in range(1, packet_size):
            pkt[i] = 0xFF
        pkt[1] = 0x02; pkt[2] = 0x02; pkt[3] = 0x00; pkt[4] = 0x01; pkt[5] = 0xFF
        print(f"  Start: {bytes(pkt[:12]).hex(' ')} ...")

        # First data packet
        pkt2 = bytearray(packet_size)
        pkt2[0] = REPORT_ID_OTA
        for i in range(1, packet_size):
            pkt2[i] = 0xFF
        pkt2[1] = 0x02; pkt2[2] = 0x00; pkt2[3] = 0x00
        idx = 0
        for j in range(min(3, num_chunks)):
            chunk = bytearray(18)
            chunk[0:2] = struct.pack('<H', idx)
            chunk[2:18] = fw_buf[idx * 16:idx * 16 + 16]
            c = crc16(bytes(chunk))
            base = 4 + 20 * j
            pkt2[base:base + 18] = chunk
            pkt2[base + 18:base + 20] = struct.pack('<H', c)
            idx += 1
            pkt2[2] = 20 * (j + 1)
        print(f"  Data:  {bytes(pkt2[:32]).hex(' ')} ...")
        return True

    # Open device
    try:
        fd = os.open(dev_path, os.O_RDWR | os.O_NONBLOCK)
    except PermissionError:
        print(f"\nERROR: Permission denied on {dev_path}")
        print("  Check udev rules or run with sudo")
        return False
    except FileNotFoundError:
        print(f"\nERROR: Device {dev_path} not found")
        return False

    try:
        # --- START ---
        print("\nSending OTA start...")
        start = bytearray(packet_size)
        start[0] = REPORT_ID_OTA
        for i in range(1, packet_size):
            start[i] = 0xFF
        start[1] = 0x02  # sen[0]
        start[2] = 0x02  # sen[1] = length
        start[3] = 0x00  # sen[2]
        start[4] = 0x01  # sen[3] = command: start
        start[5] = 0xFF  # sen[4]
        os.write(fd, bytes(start))

        resp = read_response(fd, timeout=5.0)
        if resp is None:
            print("ERROR: No response to start command (is keyboard in OTA mode?)")
            return False
        print(f"  ACK received ({len(resp)} bytes)")

        # --- DATA ---
        ota_index = 0
        pkt_count = 0
        start_time = time.monotonic()

        while True:
            # Build data packet (mirrors .NET ota_write logic)
            pkt = bytearray(packet_size)
            pkt[0] = REPORT_ID_OTA
            for i in range(1, packet_size):
                pkt[i] = 0xFF
            pkt[1] = 0x02  # sen[0] = always 2
            pkt[2] = 0x00  # sen[1] = data length (updated below)
            pkt[3] = 0x00  # sen[2]

            chunks_in_pkt = 0
            saved_index = ota_index

            for j in range(CHUNKS_PER_PACKET):
                # Build chunk: [index_lo, index_hi, 16B data] for CRC
                chunk = bytearray(18)
                chunk[0] = ota_index & 0xFF
                chunk[1] = (ota_index >> 8) & 0xFF
                offset = ota_index * CHUNK_DATA_SIZE
                for k in range(CHUNK_DATA_SIZE):
                    if offset + k < len(fw_buf):
                        chunk[2 + k] = fw_buf[offset + k]
                    else:
                        chunk[2 + k] = 0xFF

                c = crc16(bytes(chunk))

                # Place in packet: sen[3 + 20*j .. 3 + 20*j + 19]
                base = 4 + 20 * j  # +1 for report_id offset
                pkt[base:base + 18] = chunk
                pkt[base + 18] = c & 0xFF
                pkt[base + 19] = (c >> 8) & 0xFF

                ota_index += 1

                # Termination check (matches .NET: ota_index*16 >= fw_size+16)
                if ota_index * CHUNK_DATA_SIZE >= fw_size + CHUNK_DATA_SIZE:
                    ota_index -= 1
                    break

                # Only count chunk if we didn't break
                chunks_in_pkt = j + 1
                pkt[2] = 20 * (j + 1)  # sen[1] = data length

            if chunks_in_pkt == 0:
                # No chunks to send — time for end packet
                ota_index = saved_index
                break

            os.write(fd, bytes(pkt))
            pkt_count += 1

            # Wait for ACK
            resp = read_response(fd, timeout=10.0)
            if resp is None:
                print(f"\nERROR: No ACK for packet {pkt_count} "
                      f"(chunks {saved_index}-{ota_index - 1})")
                return False

            # Progress
            progress = min(100, ota_index * CHUNK_DATA_SIZE * 100 // fw_size)
            elapsed = time.monotonic() - start_time
            rate = (ota_index * CHUNK_DATA_SIZE / 1024) / elapsed if elapsed > 0 else 0
            sys.stdout.write(
                f"\r  [{progress:3d}%] {pkt_count}/{total_packets} packets | "
                f"{elapsed:.1f}s | {rate:.1f} KB/s")
            sys.stdout.flush()

        elapsed = time.monotonic() - start_time
        print(f"\n  Data transfer complete: {pkt_count} packets in {elapsed:.1f}s")

        # --- END ---
        print("Sending OTA end...")
        end = bytearray(packet_size)
        end[0] = REPORT_ID_OTA
        for i in range(1, packet_size):
            end[i] = 0xFF
        end[1] = 0x02  # sen[0]
        end[2] = 0x06  # sen[1] = length
        end[3] = 0x00  # sen[2]
        end[4] = 0x02  # sen[3] = command: end
        end[5] = 0xFF  # sen[4]
        count = (ota_index - 1) & 0xFFFF
        neg_count = (0x10000 - count) & 0xFFFF
        end[6] = count & 0xFF
        end[7] = (count >> 8) & 0xFF
        end[8] = neg_count & 0xFF
        end[9] = (neg_count >> 8) & 0xFF
        os.write(fd, bytes(end))

        # Wait for result (device may reboot, so timeout is OK)
        resp = read_response(fd, timeout=10.0)
        if resp is None:
            print("  No final response (device likely rebooted with new firmware)")
            return True

        # Check: [05][02][03][00][06][FF][result]
        if (len(resp) >= 7 and resp[0] == REPORT_ID_OTA
                and resp[1] == 0x02 and resp[4] == 0x06):
            if resp[6] == 0x00:
                print("  OTA SUCCESS!")
                return True
            else:
                print(f"  OTA FAILED (error code: {resp[6]})")
                return False
        else:
            print(f"  Response: {resp[:12].hex(' ')}")
            print("  (unrecognized format, device may have rebooted successfully)")
            return True

    finally:
        os.close(fd)


def main():
    parser = argparse.ArgumentParser(
        description='Wobkey Crush 80 OTA Firmware Flasher',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog='Examples:\n'
               '  %(prog)s firmware_patched.bin\n'
               '  %(prog)s code_2M_patched.bin\n'
               '  %(prog)s --dry-run firmware_patched.bin\n'
               '  %(prog)s --device /dev/hidraw4 firmware_patched.bin')
    parser.add_argument('firmware', nargs='?',
                        help='Firmware file (firmware_patched.bin or code_2M_patched.bin)')
    parser.add_argument('--dry-run', action='store_true',
                        help='Show what would be sent without actually flashing')
    parser.add_argument('--device',
                        help='Override hidraw device path (auto-detected if omitted)')
    parser.add_argument('--probe', action='store_true',
                        help='Probe the OTA device without flashing')
    args = parser.parse_args()

    # Probe mode
    if args.probe:
        if args.device:
            dev_path = args.device
            name = os.path.basename(dev_path)
            desc_path = f'/sys/class/hidraw/{name}/device/report_descriptor'
            report_size = 64
            if os.path.exists(desc_path):
                with open(desc_path, 'rb') as f:
                    desc = f.read()
                report_size = parse_output_report_size(desc, REPORT_ID_OTA) or 64
        else:
            print("Searching for OTA device...")
            dev_path, report_size = find_ota_device()
            if dev_path is None:
                print("ERROR: OTA device not found")
                sys.exit(1)
            print(f"  Found: {dev_path}")
        probe_device(dev_path, report_size)
        sys.exit(0)

    if not args.firmware:
        parser.error("firmware file is required (unless using --probe)")
    if not os.path.exists(args.firmware):
        print(f"ERROR: File not found: {args.firmware}")
        sys.exit(1)

    # Load firmware
    print(f"Loading {args.firmware}...")
    try:
        fw_buf, fw_size = load_firmware(args.firmware)
    except (ValueError, struct.error) as e:
        print(f"ERROR: {e}")
        sys.exit(1)

    num_chunks = len(fw_buf) // CHUNK_DATA_SIZE
    print(f"  Send buffer: {len(fw_buf)} bytes ({num_chunks} chunks)")

    # Find device
    if args.device:
        dev_path = args.device
        name = os.path.basename(dev_path)
        desc_path = f'/sys/class/hidraw/{name}/device/report_descriptor'
        report_size = 64
        if os.path.exists(desc_path):
            with open(desc_path, 'rb') as f:
                desc = f.read()
            report_size = parse_output_report_size(desc, REPORT_ID_OTA) or 64
    else:
        print("\nSearching for OTA device (VID=0x{:04X} PID=0x{:04X})...".format(VID, PID))
        dev_path, report_size = find_ota_device()
        if dev_path is None:
            print("ERROR: Wobkey Crush 80 OTA interface not found")
            print("  - Is the keyboard connected via USB?")
            print("  - Check udev rules (99-wobkey-crush80.rules)")
            print("  - Try: --device /dev/hidrawN")
            sys.exit(1)
        print(f"  Found: {dev_path}")

    # Confirm before flashing
    if not args.dry_run:
        print(f"\n{'=' * 54}")
        print("  FIRMWARE FLASH — THIS WILL OVERWRITE THE FIRMWARE")
        print(f"  Device:   {dev_path}")
        print(f"  File:     {args.firmware}")
        print(f"  Size:     {fw_size} bytes")
        print(f"{'=' * 54}")
        print("\nIf the flash fails, the OTA bootloader should still")
        print("allow recovery by re-flashing the original firmware.")
        try:
            resp = input("\nProceed? [y/N] ")
        except (EOFError, KeyboardInterrupt):
            print("\nAborted.")
            sys.exit(0)
        if resp.strip().lower() != 'y':
            print("Aborted.")
            sys.exit(0)

    success = flash(dev_path, fw_buf, fw_size, report_size, args.dry_run)

    if success and not args.dry_run:
        print("\nFlash complete. The keyboard should reboot automatically.")
        print("If it doesn't respond, unplug and replug the USB cable.")

    sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
