# OpenTabletDisplay

[![CI](https://github.com/0pen-Sourcer/OpenTabletDisplay/actions/workflows/ci.yml/badge.svg)](https://github.com/0pen-Sourcer/OpenTabletDisplay/actions/workflows/ci.yml) [![Release](https://img.shields.io/github/v/release/0pen-Sourcer/OpenTabletDisplay)](https://github.com/0pen-Sourcer/OpenTabletDisplay/releases/latest) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)](docs/COMPATIBILITY.md)

**Use an Android tablet or phone as a real second Windows monitor over USB.**

OpenTabletDisplay turns an ordinary Android device into an **extended Windows display** using the USB cable you already have. No HDMI input on the tablet is required, and the tablet's Wi-Fi can stay off while the display traffic travels over USB.

It is the public-facing name for the project previously called **FSDL**. The current 3.x runtime still keeps some `FSDL` executable, task, folder, and state names internally so existing installs remain compatible.

> **Normal use after setup:** plug in the USB cable → open Moonlight → launch **Desktop**.

## What it does

Your tablet does **not** need USB-C DisplayPort input or any special display hardware. OpenTabletDisplay creates a genuine virtual monitor inside Windows, captures only that monitor with Sunshine, and carries the Sunshine/Moonlight connection through a low-latency USB data path.

```text
Windows desktop
      │
      ▼
Virtual Display Driver
(real extended Windows monitor)
      │
      ▼
Sunshine
(capture + hardware encoder when available)
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

This is an **extended desktop**, not a browser mirror. Windows gets another display surface that you can arrange beside your physical monitor and move normal windows onto.

## Why this project exists

There are already excellent pieces for the difficult media work:

- **Sunshine** provides mature low-latency Windows capture and hardware encoding.
- **Moonlight** provides a mature Android decoder and input client.
- **Virtual Display Driver** provides the real Windows display surface.
- **ADB + gnirehtet** provide a wired Android data path that can carry both TCP and UDP.

OpenTabletDisplay deliberately does **not** reimplement those projects. It owns the integration and lifecycle work needed to make them behave like one USB second-monitor system:

- installs or reuses a signed Windows virtual display;
- installs or reuses Sunshine;
- identifies the virtual monitor using Sunshine's stable device GUID instead of guessing from unstable `DISPLAYn` numbers;
- creates a USB-only path for the Sunshine/Moonlight traffic;
- verifies the real Android-to-Windows TCP **and UDP** path before reporting the bridge healthy;
- remembers the enrolled Android display instead of silently switching to another authorized device;
- keeps the bridge alive across Windows logon and cable reconnects;
- makes the virtual display primary only when required for the Moonlight session, then restores the normal desktop layout;
- lets the client request resolution and refresh rate instead of baking in one tablet model;
- preserves existing Sunshine credentials and pairings;
- backs up only the settings it changes;
- uninstalls by recorded ownership instead of wildcard-deleting drivers or processes.

## Highlights

- **Real second monitor:** extend the Windows desktop instead of merely mirroring a physical screen.
- **USB-first:** stream traffic runs through the Android USB data connection; tablet Wi-Fi may remain off.
- **No video-input port required:** works as software over USB data rather than relying on USB-C DisplayPort/HDMI input support.
- **Hardware accelerated:** Sunshine can use NVENC/other supported hardware encoders and Moonlight uses the Android hardware decoder.
- **No custom Android client to maintain:** use the official Moonlight app.
- **Background reconnect:** no bridge console needs to remain open for everyday use.
- **Device-model agnostic:** no Lenovo-specific IDs or hard-coded tablet resolution assumptions.
- **Conservative install/uninstall:** existing Sunshine/VDD installs are reused and user-owned configuration wins.
- **Open source:** the OpenTabletDisplay integration layer is MIT licensed.

## Current support

| | Support |
| --- | --- |
| **Windows host** | Windows 10/11 x86-64 |
| **Android client** | Android device with USB debugging + official Moonlight Game Streaming app (`com.limelight`) |
| **Transport** | USB data cable via ADB + gnirehtet |
| **Display** | VirtualDrivers Virtual Display Driver (signed) |
| **Capture / encoding** | Sunshine |
| **Decode / input** | Moonlight Android |
| **Tablet Wi-Fi** | May remain off for the display traffic |

The wired path has been validated end-to-end on a **Lenovo Tab M10 HD**. The project itself uses standard ADB/Moonlight interfaces and does not contain Lenovo-specific device IDs, but Android ROM and hardware quirks can still exist. See [Compatibility](docs/COMPATIBILITY.md).

### Current networking limitation

The stream packets themselves are routed through USB, but the current implementation still uses the Windows host's active physical IPv4 address as Sunshine/Moonlight address identity. Windows therefore needs an active physical IPv4/default route while a session is established.

Removing that identity dependency with an OpenTabletDisplay-owned host-only address is a future improvement; it is not shipped until hardware-validated.

## Install

1. Download the latest **Windows release ZIP** from [Releases](https://github.com/0pen-Sourcer/OpenTabletDisplay/releases/latest) and extract it.
2. Run **`install.cmd`**.
3. Install the official **Moonlight Game Streaming** app on the Android device.
4. Enable **Developer options → USB debugging** on Android.
5. Connect the Android device with a data-capable USB cable and approve the RSA debugging prompt.
6. Keep Android's normal **USB tethering OFF**. OpenTabletDisplay uses its own ADB/gnirehtet path.
7. Approve the one-time Android VPN/Connection request from gnirehtet.
8. Pair Moonlight with the host address shown by OpenTabletDisplay, then launch **Desktop**.

Pairing is one-time. The first Android device that completes the live TCP + UDP health checks becomes the enrolled display until you run `reset-device.cmd`.

### Everyday use

```text
plug USB → open Moonlight → Desktop
```

The background bridge agent handles reconnects without keeping a visible command window open.

## Recommended Moonlight settings

These settings belong to Moonlight, so OpenTabletDisplay does not silently rewrite them:

- **Optimize game settings:** ON
- **Resolution:** Native / preferred tablet resolution
- **Frame rate:** 60 FPS to start
- **Use touchscreen as a trackpad:** OFF if you want direct-touch-style interaction
- **Play audio on PC:** ON if you want audio on both the PC and Android device

## Existing Sunshine or VDD installation?

That is fine. `install.cmd` is an in-place installer.

If compatible Sunshine or Virtual Display Driver installations already exist, OpenTabletDisplay reuses them rather than installing duplicates. Existing Sunshine credentials and pairing state are preserved.

## Status, switching devices, and uninstall

The current 3.x package retains legacy `FSDL` internal names for upgrade compatibility.

- `status.cmd` — Sunshine/VDD/agent/bridge health plus the recent agent log
- `reset-device.cmd` — forget the enrolled Android display before switching devices
- `uninstall.cmd` — remove the OpenTabletDisplay/FSDL integration and restore Sunshine settings it changed
- `installer\uninstall.ps1 -RemoveDependencies` — additionally remove Sunshine/VDD **only when install-state proves this project originally installed them**

## Safety model

Third-party dependencies are downloaded from pinned upstream release URLs and checked against exact SHA-256 digests. The background agent then reuses those verified private copies, so attaching the tablet later does not trigger fresh dependency downloads.

A hash is an integrity check, not proof that a publisher is trustworthy. OpenTabletDisplay also relies on upstream signing where available and records source/license information in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The VDD publisher certificate is added to Trusted Publishers only for the install transaction and removed immediately if the installer added it.

Read [SECURITY.md](SECURITY.md) and [Architecture](docs/ARCHITECTURE.md) before deploying on machines where driver or local-network changes require review.

## How it is different from a physical USB-C monitor

A portable monitor normally receives a raw DisplayPort/HDMI video signal. Most Android tablets cannot accept that signal through their USB-C port.

OpenTabletDisplay instead treats USB-C as a **data connection**:

1. Windows renders a virtual monitor.
2. Sunshine encodes that monitor as a low-latency video stream.
3. The stream crosses the USB data cable.
4. Moonlight decodes it on Android.

That is why it can work with tablets whose USB-C port has no video-input capability.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Compatibility](docs/COMPATIBILITY.md)
- [Moonlight client setup](docs/MOONLIGHT.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Device testing](docs/DEVICE_TESTING.md)
- [Roadmap](docs/ROADMAP.md)
- [Security](SECURITY.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## Build and test

On Windows:

```powershell
./scripts/build-release.ps1
```

On Linux/macOS with Go installed:

```bash
./scripts/build-release.sh
```

The release build compiles the Windows x64 bridge plus the native Android ARM64 transport probe, runs tests/vet/race/self-tests, and packages the Windows release ZIP with checksums. GitHub Actions also parses every PowerShell installer script on a Windows runner.

The original project was developed with a strict hardware-boundary rule: CI and reproducible builds prove everything they realistically can, while claims about the actual USB/display path are only made after real-device validation.

## Name / compatibility note

**OpenTabletDisplay** is the public product name going forward. **FSDL** was the original project name and remains visible in parts of the 3.x runtime (`FSDL.exe`, install-state paths, scheduled-task names, Go module path) so existing installations and upgrades do not break for a cosmetic rename.

A later major release can migrate those internals with an explicit compatibility/rollback plan.

## License

The OpenTabletDisplay integration layer (formerly FSDL) is MIT licensed. It downloads and orchestrates separate third-party projects under their own licenses; it does not relicense them. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
