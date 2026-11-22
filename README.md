# OpenTabletDisplay

> **Installer advisory (November 2026):** The published v3.1.1 ZIP has a confirmed fresh-install logging defect and can incorrectly reject a Virtual Display Driver registered under a ROOT\DISPLAY instance. Read [known issue #11](https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/11) before attempting a new install. A redesigned 3.2 release candidate is under [review in PR #10](https://github.com/0pen-Sourcer/OpenTabletDisplay/pull/10), but has **not yet passed end-to-end physical USB/tablet validation**. Do not treat its CI success as hardware compatibility confirmation.


[![CI](https://github.com/0pen-Sourcer/OpenTabletDisplay/actions/workflows/ci.yml/badge.svg)](https://github.com/0pen-Sourcer/OpenTabletDisplay/actions/workflows/ci.yml) [![Release](https://img.shields.io/github/v/release/0pen-Sourcer/OpenTabletDisplay)](https://github.com/0pen-Sourcer/OpenTabletDisplay/releases/latest) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)](docs/COMPATIBILITY.md)

![OpenTabletDisplay — Android as a real wired Windows second monitor](docs/assets/open-tablet-display-hero.svg)

**Turn an Android tablet or phone into a real extended Windows monitor over the USB cable you already have.**

OpenTabletDisplay gives Windows another display surface, captures that display with Sunshine, and carries the Sunshine/Moonlight session through a USB data path to Android. It is a **second monitor**, not a browser mirror.

> **Everyday use after setup:** plug in USB → open Moonlight → launch **Desktop**.

[**Download the latest Windows release**](https://github.com/0pen-Sourcer/OpenTabletDisplay/releases/latest) · [Compatibility](docs/COMPATIBILITY.md) · [Troubleshooting](docs/TROUBLESHOOTING.md) · [Report a device test](docs/DEVICE_TESTING.md)

## Why use it?

Most Android tablets cannot accept raw DisplayPort/HDMI video through USB-C. OpenTabletDisplay treats USB as a **data connection** instead:

- Windows sees a genuine virtual monitor that you can extend the desktop onto.
- Sunshine handles mature low-latency capture and hardware encoding.
- Moonlight handles mature Android hardware decode and input.
- ADB + gnirehtet carry the required TCP and UDP traffic through USB.
- OpenTabletDisplay owns the integration: install/reuse, device selection, health checks, reconnect, rollback and uninstall.

No custom Android client and no home-grown video codec are required.

## Architecture

```text
Windows applications
        │
        ▼
Virtual Display Driver
(real extended desktop surface)
        │
        ▼
Sunshine
(capture + hardware encode)
        │
        ▼
OpenTabletDisplay USB bridge
(ADB + gnirehtet, TCP + UDP)
        │
        ▼
ordinary USB data cable
        │
        ▼
Moonlight for Android
(hardware decode + input)
        │
        ▼
Android tablet / phone
```

The virtual display is selected using Sunshine's stable display-device GUID rather than guessing from unstable Windows `DISPLAYn` numbering.

## Highlights

- **Real extended desktop** — move ordinary Windows apps onto the Android display.
- **USB-first transport** — Android Wi-Fi may remain off for display traffic.
- **Hardware accelerated** — Sunshine can use supported GPU encoders; Moonlight uses Android hardware decode.
- **Background reconnect** — no bridge console needs to stay open during normal use.
- **Device enrollment** — a second debugging-enabled phone cannot silently hijack the session.
- **Client-driven display mode** — no hard-coded Lenovo resolution or device ID.
- **Live transport verification** — real Android-side Sunshine TCP checks plus UDP relay verification before READY.
- **Existing installs respected** — compatible Sunshine/VDD installations are reused instead of duplicated.
- **Conservative rollback/uninstall** — changes are tracked by ownership rather than wildcard-deleting drivers, processes or user configuration.
- **Open source** — the OpenTabletDisplay integration layer is MIT licensed.

## Support

| | Current support |
| --- | --- |
| **Windows host** | Windows 10/11 x86-64 |
| **Android client** | Android with USB debugging + official Moonlight Game Streaming app (`com.limelight`) |
| **Transport** | USB data cable via ADB + gnirehtet |
| **Display** | VirtualDrivers Virtual Display Driver |
| **Capture / encoding** | Sunshine |
| **Decode / input** | Moonlight Android |
| **Tablet Wi-Fi** | May remain off for display traffic |

The full wired path has been validated end-to-end on a **Lenovo Tab M10 HD**. The runtime itself contains no Lenovo-specific device ID or fixed tablet-resolution assumption, but Android ROM/USB/VPN behavior varies, so reports from other hardware are useful.

### Current stable limitation

The stream packets themselves travel through USB, but stable **v3.1.1** still uses the Windows host's active physical IPv4 address as the Sunshine/Moonlight address identity. The PC therefore needs an active physical IPv4/default route while the session is established.

## Install

> **Development preview:** The 3.3 native Windows overhaul is still in draft PR #10, not the public stable release. The current public 3.1.1 has known installer defects. Treat all unsigned previews as test-only and keep antivirus protection enabled.

1. Download the complete packaged Windows ZIP, not GitHub's automatic Source code ZIP, and extract it.
2. Open **OpenTabletDisplay.exe**. The desktop UI is a compiled, self-contained .NET 10 WPF application—not a PowerShell-hosted window.
3. Choose **Install / repair**. Windows UAC appears only for installation and removal of system components. Do not bypass antivirus alerts or add exclusions.
4. Follow the one-time Android USB debugging authorization. Keep Android USB tethering OFF, and approve gnirehtet's explicit VPN prompt if shown.
5. Open **My devices**, scan for authorized Android devices, and explicitly enroll the intended device. Multiple devices are not selected automatically.
6. Install official Moonlight on Android, pair with Sunshine using the host address shown in the app, and launch Desktop.

The desktop app shows installer progress, dependency errors and local diagnostics. It blocks connection controls until Windows setup completes and preserves existing Sunshine settings where possible.

### Recommended Moonlight settings

- **Optimize game settings:** ON
- **Resolution:** Native / preferred tablet resolution
- **Frame rate:** 60 FPS to start
- **Use touchscreen as a trackpad:** OFF for direct-touch-style interaction
- **Play audio on PC:** ON if you want audio on both the PC and Android device

OpenTabletDisplay deliberately does not rewrite Moonlight preferences behind your back.

## Status, switching devices and uninstall

The current 3.x package retains some internal **FSDL** names for upgrade compatibility with the project's original name.

- `status.cmd` — Sunshine/VDD/agent/bridge health + recent agent log
- `reset-device.cmd` — forget the enrolled Android device before switching
- `uninstall.cmd` — remove OpenTabletDisplay/FSDL integration and restore Sunshine settings it changed
- `installer\uninstall.ps1 -RemoveDependencies` — also remove Sunshine/VDD **only when install-state proves this project originally installed them**

Existing Sunshine credentials and Moonlight pairing state are preserved when compatible installations are reused.

## Help test hardware

CI can validate code, packaging and rollback logic. It cannot attach your USB controller, Android ROM or GPU.

If you have a Windows PC and an old Android tablet/phone, a successful **or failed** hardware report is useful. Please include:

- Windows version and GPU;
- Android model/version;
- native panel resolution/refresh rate;
- whether initial setup, reconnect, sleep/wake and uninstall worked;
- `status.cmd` output with credentials/private data removed.

See [Device testing](docs/DEVICE_TESTING.md) for the reporting format.

## Safety / provenance

Third-party dependencies are downloaded from pinned upstream release URLs and checked against exact SHA-256 digests. Hashes are **integrity checks, not publisher trust proofs**; upstream signing and source provenance matter too.

The VDD publisher certificate is added to Trusted Publishers only for the install transaction and removed immediately if OpenTabletDisplay added it. Existing user-owned Sunshine/VDD state wins over the installer.

Read [Security](SECURITY.md), [Architecture](docs/ARCHITECTURE.md) and [Third-party notices](THIRD_PARTY_NOTICES.md) for details.

## Documentation

- [Desktop app and first-run setup](docs/DESKTOP_APP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Compatibility](docs/COMPATIBILITY.md)
- [Moonlight client setup](docs/MOONLIGHT.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Device testing](docs/DEVICE_TESTING.md)
- [Roadmap](docs/ROADMAP.md)
- [Security](SECURITY.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## Build and test

Windows:

```powershell
./scripts/build-release.ps1
```

Linux/macOS with Go installed (cross-build tests only; not a Windows app release):

```bash
./scripts/build-release.sh
```

The Windows build generates the app icon, compiles the Go transport and Android probe, publishes the self-contained .NET 10 WPF desktop EXE, and verifies package SHA-256 checksums. The Linux/macOS script only tests and cross-compiles the transport; it cannot publish a Windows desktop ZIP. Windows CI launches the actual native WPF smoke test. Public release automation refuses unsigned executables or missing physical hardware QA signoff.

The project follows a strict hardware boundary: automation proves everything it realistically can, while claims about the actual USB/display path are made only after real-device validation.

## Name / compatibility note

**OpenTabletDisplay** is the public product name. **FSDL** was the original project name and remains visible in parts of the 3.x runtime (`FSDL.exe`, install-state paths, scheduled-task names and Go module path) so existing installations and upgrades do not break for a cosmetic rename.

## License

The OpenTabletDisplay integration layer is MIT licensed. It downloads and orchestrates separate third-party projects under their own licenses; it does not relicense them. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
