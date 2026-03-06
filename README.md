# Wobkey Crush 80 — Patched Firmware & SignalRGB Plugin

The Wobkey Crush 80 (VID `0x320F`, PID `0x5055`) ships with a firmware bug: setting the hue (H byte) via the VIA USB protocol has no effect on the displayed color. This repository contains a binary patch that fixes the bug and a custom SignalRGB plugin that syncs the keyboard's backlight color to SignalRGB effects.

## The Problem

The VIA SET handler at `0xDA20` stores the H byte to the internal state struct but then overwrites the RGB fields with stale cached values from global RAM instead of converting H to RGB. The result is that any color set through VIA (including SignalRGB) is ignored — the keyboard stays on whatever color was last set locally.

## Repository Contents

| File / Directory | Description |
|---|---|
| `patch_firmware.py` | Applies the hue-fix patch to the stock firmware |
| `firmware.bin` | Stock firmware extracted from `code_2M.bin` |
| `firmware_patched.bin` | Patched firmware (ready to flash) |
| `code_2M_patched.bin` | Full OTA image with patched firmware embedded |
| `Crush80-RGB-Firmware.exe` | Stock OTA flasher (.NET, Windows) |
| `Crush80-RGB-USB.JSON` | VIA keymap definition |
| `SignalRGB/WobkeyCrush80.js` | SignalRGB plugin |
| `SignalRGB/via-test.html` | Browser-based WebHID test tool |
| `extract_firmware.py` | Extracts `code_2M.bin` and `param_128K.bin` from the decompiled flasher |
| `decompiled/` | Decompiled source of the stock OTA flasher |

---

## Flashing the Patched Firmware

The patched firmware fixes the VIA color handler by inserting an HSV-to-RGB conversion routine into unused padding space in the firmware image. It is **required** for the SignalRGB plugin (or any VIA-based color control) to work.

### Prerequisites

- Windows (the OTA flasher is a .NET WinForms app)
- The keyboard connected via USB (not Bluetooth/2.4G)

### Steps

1. **Generate the patched firmware** (or use the pre-built `code_2M_patched.bin`):

   ```
   python3 patch_firmware.py
   ```

   This reads `firmware.bin`, applies the patch, verifies the CRC, and produces:
   - `firmware_patched.bin` — the patched firmware blob
   - `code_2M_patched.bin` — full OTA image ready for the flasher

2. **Prepare the flasher:**

   The stock flasher (`Crush80-RGB-Firmware.exe`) embeds the firmware as a .NET resource named `code_2M`. To flash the patched version, replace that resource with `code_2M_patched.bin` using a tool like [dnSpy](https://github.com/dnSpyEx/dnSpy) or [Resource Hacker](https://www.angusj.com/resourcehacker/):

   - Open `Crush80-RGB-Firmware.exe` in dnSpy
   - Navigate to **Resources** > `WindowsFormsApplication1.Properties.Resources`
   - Right-click the `code_2M` resource > **Edit Resource** > select `code_2M_patched.bin`
   - Save the modified executable

3. **Flash:**

   - Run the modified flasher
   - Follow the on-screen instructions (it uses the `0xFFEF` HID interface for OTA)
   - The keyboard will reboot after flashing

> **Warning:** Flashing custom firmware carries risk. Keep a copy of the original `code_2M.bin` so you can restore stock firmware if needed.

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

`SignalRGB/via-test.html` is a standalone browser-based tool for testing VIA communication with the keyboard over WebHID. Open it in Chrome or Edge, click **Connect**, and use the controls to:

- Switch between all 19 built-in effects
- Send color (H, S) and brightness values
- Sweep hue across the full range
- Send raw HID packets

This is useful for verifying the firmware patch is working before setting up SignalRGB.

### USB Interface Map

| Interface | Usage Page | Purpose |
|---|---|---|
| 0 | `0x0001` | Standard keyboard HID |
| 1 | `0xFF60` | VIA protocol (use this one) |
| 2 | `0xFFEF` | OTA firmware flasher |
| 3 | `0xFF1C` | Wireless mode switch — **never write to this** |

---

## Linux udev Rule

If testing on Linux (e.g. with the VIA test tool), copy the udev rule so the HID device is accessible without root:

```
sudo cp 99-wobkey-crush80.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules
sudo udevadm trigger
```
