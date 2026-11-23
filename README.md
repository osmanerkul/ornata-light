# OrnataLight

A lightweight (~50 KB), zero-dependency native Windows tray controller for **Razer Ornata V3 X** keyboards (`VID_1532` & `PID_02A2` Rev2 / `PID_0294` Rev1) — built so you never need Razer Synapse, OpenRGB, kernel drivers, or administrator privileges just to keep your keyboard **Static White**.

Project page: [senkron.tech/ornata-light](https://senkron.tech/ornata-light/)

---

## Why OrnataLight?

The Razer Ornata V3 X has **no onboard profile flash** and does not expose a native Windows 11 Dynamic Lighting (`HID LampArray`) interface. Whenever power cycles — reboot, sleep/Modern Standby, or USB replug — the firmware reverts to factory spectrum cycling.

OrnataLight solves this in a single standalone `.exe` compiled against Windows' built-in .NET Framework 4.x:

- **Static White by default (`#FFFFFF`, 100%)** out of the box.
- **Zero bloat & zero elevation:** Talks directly to the keyboard's HID feature interface using standard user privileges (`desiredAccess = 0`).
- **Automatic recovery:** Re-applies your saved lighting automatically on startup (with retries), Windows login, Modern Standby / resume (`WM_POWERBROADCAST` & `PowerModeChanged`), display wake (`GUID_CONSOLE_DISPLAY_STATE`), screen unlock (`SessionUnlock`), and USB plug/unplug (`WM_DEVICECHANGE`).
- **Tray controls (left or right click):**
  - 9 quick presets (`White (Static)`, `Warm White`, `Ice Blue`, `Soft Cyan`, `Amber`, `Red`, `Green`, `Blue`, `Purple`) + **Custom Color...** picker
  - Hardware modes: `Static`, `Breathing`, `Spectrum Cycle`, `Off`
  - Brightness levels: `100%`, `75%`, `50%`, `25%`, `10%`
  - `Re-apply Now` and `Start with Windows` (`HKCU\...\Run`)
- **Multi-monitor & vertical-taskbar safe:** Enables Per-Monitor V2 DPI awareness and clamps the popup menu inside the clicked monitor's `WorkingArea` (including setups with vertical taskbars next to secondary displays).
- **Custom backlit-keycap icon:** Multi-resolution (`16/32/48/256 px`) icon with a dynamic color-tinted keycap tray indicator.

---

## Download

Grab the prebuilt single-file binary from [GitHub Releases](https://github.com/osmanerkul/ornata-light/releases/latest) (`OrnataLight.exe`).

> Note: On Windows 11, newly launched tray icons appear inside the **`^` overflow arrow** next to the clock until dragged onto the taskbar.

---

## CLI & Configuration

OrnataLight persists settings and diagnostics under `%APPDATA%\OrnataLight\`:
- `settings.ini` — saved `mode`, `r`, `g`, `b`, and `brightness` (`0–255`)
- `status.log` — rolling log (last 50 entries) showing trigger, USB HID path, and firmware response status bytes

Command-line flags:
```powershell
.\OrnataLight.exe --test         # Apply saved/default lighting once, log to status.log, exit 0 on firmware OK (0x02)
.\OrnataLight.exe --apply-once   # Same headless one-shot apply without starting the tray UI
```

---

## Build from Source

Requires only Windows 10/11 (uses the built-in .NET Framework 4.x C# compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — no Visual Studio or SDK download needed):

```powershell
git clone https://github.com/osmanerkul/ornata-light.git
cd ornata-light
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` generates `OrnataLight.ico` via `make_icon.ps1` and compiles `Program.cs` into `OrnataLight.exe`.

---

## Razer HID Protocol Notes

- **Target device:** `VID_1532`, `PID_02A2` (Ornata V3 X Rev2) or `PID_0294` (Rev1), selecting the control HID collection (`MI_02`) with `FeatureReportByteLength == 91`.
- **Non-admin open:** Opens the device handle with `CreateFile(..., desiredAccess = 0, FILE_SHARE_READ | FILE_SHARE_WRITE, ..., OPEN_EXISTING, ...)`, avoiding `ERROR_ACCESS_DENIED` on the mouse-usage collection while permitting `HidD_SetFeature` and `HidD_GetFeature`.
- **91-byte Razer Report:** `report_id=0x00`, `transaction_id=0x1F`, `command_class=0x0F`, `led_id=0x05` (`BACKLIGHT_LED`), XOR CRC across bytes `3..88` stored at byte `89`. Commands try `VARSTORE (0x01)` first and fall back to `NOSTORE (0x00)` if needed.

---

## Acknowledgments

HID command layout and PID mappings are informed by the open-source [OpenRazer](https://github.com/openrazer/openrazer) and [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) projects.

## License

[MIT](LICENSE)
