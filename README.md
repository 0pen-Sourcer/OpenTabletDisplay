# FSDL

**Turn an Android tablet or phone into a real second Windows display over a USB cable.**

FSDL is the integration layer around proven components rather than another remote-desktop stack. It creates/uses a real Windows virtual monitor, configures Sunshine to capture that monitor, and keeps a low-latency ADB USB transport alive for the official Moonlight Android client.

```text
Windows Display 2 (Virtual Display Driver)
        ↓
Sunshine capture + encoder (hardware when available)
        ↓
FSDL USB bridge (ADB + gnirehtet)
        ↓
USB data cable
        ↓
Moonlight Android + hardware decoder
        ↓
Android panel
```

## Why FSDL exists

Sunshine already does excellent capture/encoding and Moonlight already does excellent Android decoding/input. FSDL does **not** fork or replace either one. It handles the ugly integration work that those projects intentionally do not own:

- installs/reuses a signed Windows virtual display;
- installs/reuses Sunshine and selects the virtual monitor by Sunshine's stable device GUID, not `DISPLAYn` guesses;
- provides a wired Android transport that carries both TCP and UDP over ADB;
- validates the real tablet-to-Sunshine TCP path and UDP path before declaring the bridge healthy;
- keeps the bridge alive in the background across cable reconnects and Windows logon;
- makes the FSDL display primary **only during a Moonlight session**, then restores the normal desktop layout;
- lets Moonlight request the stream resolution/refresh rate instead of hard-coding one tablet model;
- preserves existing Sunshine credentials/pairings and backs up only the settings FSDL changes;
- uninstalls by ownership instead of wildcard-deleting drivers/processes.

## Current support

- **Host:** Windows 10/11 x86-64
- **Client:** Android with USB debugging + the official Moonlight Game Streaming app (`com.limelight`)
- **Transport:** USB data cable via ADB/gnirehtet; Android Wi-Fi may remain off
- **Current host-address requirement:** Windows needs an active physical IPv4/default route for address identity; stream traffic itself is routed through USB
- **Device ownership:** the first Android device that completes a healthy bridge session is enrolled; other authorized phones/tablets are ignored until enrollment is cleared
- **Display:** VirtualDrivers Virtual Display Driver (signed)
- **Streaming:** Sunshine

FSDL is device-model agnostic. It does not contain Lenovo-specific IDs or resolution assumptions. The wired path has been validated end-to-end on a Lenovo Tab M10 HD; other Android devices are supported by the same ADB/Moonlight interfaces, but hardware/ROM quirks can still exist. See [Compatibility](docs/COMPATIBILITY.md) and [Moonlight client setup](docs/MOONLIGHT.md).

## Install

1. Download the Windows release ZIP and extract it.
2. Run **`install.cmd`**.
3. On Android, install official Moonlight, enable **Developer options → USB debugging**, connect a USB data cable, and approve the RSA prompt.
4. Keep Android's normal **USB tethering OFF**. FSDL uses its own ADB transport.
5. Approve the one-time Android VPN/Connection request from gnirehtet.
6. Pair Moonlight with the host address FSDL exposes. Pairing is one-time. FSDL enrolls that Android device after the transport passes its live TCP+UDP checks.

After first setup, normal use is intended to be:

> **plug USB → open Moonlight → Desktop**

No visible bridge console needs to remain open.

### Recommended Moonlight settings

These are client preferences owned by Moonlight, so FSDL does not modify them behind the app's back:

- **Optimize game settings:** ON (lets Sunshine match the virtual display to the client request)
- **Resolution:** Native / preferred tablet resolution
- **Frame rate:** 60 FPS to start
- **Use touchscreen as a trackpad:** OFF if you want direct-touch second-monitor behavior
- **Play audio on PC:** ON if you want audio on both the PC speakers and Android device

## Existing Sunshine/VDD users

`install.cmd` is an in-place installer. If a working Sunshine or MTT VDD installation already exists, FSDL reuses it instead of installing a duplicate. Existing Sunshine pairing state and credentials are kept.

## Status and uninstall

- `status.cmd` — Sunshine/VDD/agent/bridge health plus recent agent log
- `reset-device.cmd` — forget the enrolled Android display before switching to another tablet/phone
- `uninstall.cmd` — removes FSDL and restores the Sunshine settings it changed
- `installer\uninstall.ps1 -RemoveDependencies` — additionally removes Sunshine/VDD **only when the FSDL install-state says FSDL originally installed them**

## Safety model

FSDL downloads third-party dependencies during installation from pinned upstream release URLs and verifies exact SHA-256 values. The background agent then reuses those verified private copies, so plugging the tablet in later does not trigger dependency downloads. Hashes provide integrity, not provenance; FSDL additionally relies on upstream signing where available and keeps third-party source/license links in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The VDD publisher certificate is added to Trusted Publishers only for the install transaction and removed immediately if FSDL added it.

See [SECURITY.md](SECURITY.md) and [Architecture](docs/ARCHITECTURE.md).

## Build

```powershell
./scripts/build-release.ps1
```

or on Linux/macOS with Go installed:

```bash
./scripts/build-release.sh
```

The release build compiles the Windows x64 bridge plus the hardware-validated native Android ARM64 transport probe, runs tests/vet/race/self-tests, and packages a Windows release ZIP with checksums. GitHub Actions also parses every PowerShell installer script on a Windows runner.

## License

FSDL's own source is MIT licensed. It downloads and orchestrates separate third-party projects under their own licenses; FSDL does not relicense them. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
