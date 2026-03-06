# Wobkey Crush 80 — Patched Firmware & SignalRGB Plugin

The Wobkey Crush 80 (VID `0x320F`, PID `0x5055`) ships with a firmware bug: setting the hue (H byte) via the VIA USB protocol has no effect on the displayed color. This repository contains a binary patch that fixes the bug and a custom SignalRGB plugin that syncs the keyboard's backlight color to SignalRGB effects.

## The Problem

The VIA SET handler at `0xDA20` stores the H byte to the internal state struct but then overwrites the RGB fields with stale cached values from global RAM instead of converting H to RGB. The result is that any color set through VIA (including SignalRGB) is ignored — the keyboard stays on whatever color was last set locally.

## Repository Structure

```
firmware/
  firmware.bin              Stock firmware
  firmware_patched.bin      Patched firmware (ready to flash)
  code_2M.bin               Stock OTA image
  code_2M_patched.bin       Full OTA image with patch applied
  Crush80-RGB-Firmware.exe  Stock OTA flasher (.NET, Windows)
  Crush80-RGB-USB.JSON      VIA keymap definition
  99-wobkey-crush80.rules   Linux udev rule for hidraw access

scripts/
  patch_firmware.py         Generates the patched firmware
  flash_ota.py              Cross-platform OTA flasher (Linux)
  extract_firmware.py       Extracts firmware from the .NET flasher
  extract_fw_code.py        Extracts code section from firmware
  analyze_firmware.py       Firmware analysis utilities
  disasm_targets.py         Disassembly helper
  disasm_via.py             VIA handler disassembler
  ghidra_analyze.py         Ghidra analysis script

SignalRGB/
  WobkeyCrush80.js          SignalRGB plugin
  via-test.html             Browser-based WebHID test tool
```

---

## Flashing the Patched Firmware

The patched firmware fixes the VIA color handler by inserting an HSV-to-RGB conversion routine into unused padding space in the firmware image. It is **required** for the SignalRGB plugin (or any VIA-based color control) to work.

### 1. Generate the patched firmware

Pre-built binaries are included (`firmware/firmware_patched.bin` and `firmware/code_2M_patched.bin`), but you can regenerate them:

```
cd scripts
python3 patch_firmware.py
```

This reads `firmware/firmware.bin`, applies the patch, verifies the CRC, and produces `firmware_patched.bin` and `code_2M_patched.bin` in the `firmware/` directory.

### 2. Flash via `flash_ota.py` (recommended)

`flash_ota.py` is a standalone Python 3 script (no dependencies) that flashes firmware directly over USB using the Telink OTA protocol. It auto-detects the keyboard's OTA HID interface.

**Prerequisites:**
- Linux (uses `/dev/hidraw*`)
- The keyboard connected via USB (not Bluetooth/2.4G)
- Read/write access to the hidraw device (see [udev rule](#linux-udev-rule) below)

**Flash the patched firmware:**

```
cd scripts
python3 flash_ota.py ../firmware/firmware_patched.bin
```

Or using the full OTA image:

```
python3 flash_ota.py ../firmware/code_2M_patched.bin
```

The script will show the firmware details, ask for confirmation, then flash with a progress bar. The keyboard reboots automatically after a successful flash.

**Other options:**

```
# Dry run — show what would be sent without flashing
python3 flash_ota.py --dry-run ../firmware/firmware_patched.bin

# Specify device manually
python3 flash_ota.py --device /dev/hidraw4 ../firmware/firmware_patched.bin

# Probe the OTA interface without flashing
python3 flash_ota.py --probe
```

### Alternative: Flash via the Windows OTA flasher

The stock flasher (`Crush80-RGB-Firmware.exe`) is a .NET WinForms app that embeds the firmware as a resource. To flash the patched version on Windows:

1. Open `Crush80-RGB-Firmware.exe` in [dnSpy](https://github.com/dnSpyEx/dnSpy)
2. Navigate to **Resources** > `WindowsFormsApplication1.Properties.Resources`
3. Right-click the `code_2M` resource > **Edit Resource** > select `code_2M_patched.bin`
4. Save the modified executable
5. Run it and follow the on-screen instructions

> **Warning:** Flashing custom firmware carries risk. Keep a copy of the original `firmware.bin` and `code_2M.bin` so you can restore stock firmware if needed. The OTA bootloader should remain intact even after a failed flash, allowing recovery.

---

## Installing the SignalRGB Plugin

The plugin file is `SignalRGB/WobkeyCrush80.js`. It must be placed in SignalRGB's custom plugins folder so it overrides the built-in device handling.

### Steps

1. Open **SignalRGB**
2. Go to the **Devices** page and find the Wobkey Crush 80
3. Open the device's **Device Information** page
4. Click the **Plugins** button — this opens the custom plugins folder in your file explorer
5. Copy `WobkeyCrush80.js` into that folder
6. **Restart SignalRGB** completely (close and relaunch)

The keyboard should now appear as a controllable device. SignalRGB effects will be synced to the keyboard's backlight.

### How the Plugin Works

- On **Initialize**, the plugin switches the keyboard to solid-color mode (Effect 6 / `LIGHT_MODE`)
- On each **Render** frame, it averages all LED positions on the SignalRGB canvas into a single RGB color, converts it to HSV, and sends the hue + saturation via VIA channel 3 (command `0x07`). Brightness is derived from the V component and mapped to the keyboard's 0–9 range
- On **Shutdown**, brightness is restored to maximum (9)
- Values are only sent when they change, to avoid flooding the USB bus

### Removing the Plugin

Delete `WobkeyCrush80.js` from the custom plugins folder and restart SignalRGB. The keyboard will revert to default behavior.

> **Note:** While a custom plugin is installed, SignalRGB will not apply its own updates or fixes for that device. Remove the plugin file to receive upstream improvements again.

---

## VIA Test Tool

`SignalRGB/via-test.html` is a standalone browser-based tool for testing VIA communication with the keyboard over WebHID. Open it in Chrome or Edge, click **Connect**, and use the controls to send color, brightness, and effect commands. This is useful for verifying the firmware patch is working before setting up SignalRGB.

### USB Interface Map

| Interface | Usage Page | Purpose |
|---|---|---|
| 0 | `0x0001` | Standard keyboard HID |
| 1 | `0xFF60` | VIA protocol (use this one) |
| 2 | `0xFFEF` | OTA firmware flasher |
| 3 | `0xFF1C` | Wireless mode switch — **never write to this** |

---

## Linux udev Rule

For hidraw access without root (required by `flash_ota.py` and useful for the VIA test tool):

```
sudo cp firmware/99-wobkey-crush80.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules
sudo udevadm trigger
```
