# Architecture

OpenTabletDisplay (formerly FSDL) is an orchestration layer. It deliberately does not implement its own video codec, Windows capture stack, or Android streaming client.

```text
Windows applications
        │
        ▼
VirtualDrivers Virtual Display Driver
(real Windows desktop surface)
        │
        ▼
Sunshine
(capture, encode, input/audio host)
        │
        ▼
OpenTabletDisplay transport
(ADB + gnirehtet, TCP + UDP over USB)
        │
        ▼
Moonlight Android
(decode, touch/input, audio)
```

## Components OpenTabletDisplay owns

OpenTabletDisplay owns only the integration and lifecycle layer:

- dependency pinning, integrity checks, and install/update logic;
- Android-device enrollment and safe multi-device selection;
- starting/stopping the private gnirehtet relay and ADB reverse mapping;
- live Android-to-Sunshine TCP and UDP health tests;
- Sunshine configuration changes required for a real second-display session;
- reconnect behavior at Windows logon and after USB reconnects;
- state/ownership tracking for conservative uninstall and rollback.

OpenTabletDisplay does **not** modify Moonlight or Sunshine source code and does not bundle a fork of either project.

## Display selection

Windows `DISPLAY1`, `DISPLAY2`, and DXGI enumeration order are not stable identifiers. OpenTabletDisplay therefore configures Sunshine using Sunshine's display-device GUID for the Virtual Display Driver. A public install fails rather than guessing when the VDD cannot be identified uniquely.

Sunshine is configured to:

- capture the selected VDD;
- ensure that display is primary while the stream is active;
- allow client-requested resolution and refresh rate;
- restore the prior Windows display layout after disconnect.

## USB transport

Android's ordinary USB tethering is intentionally not used. Some OEM tether implementations are asymmetric, and tethering also changes the host's network topology.

OpenTabletDisplay instead uses:

1. ADB over the USB data connection.
2. gnirehtet's Android VPN client and host relay.
3. A `/32` route for the selected Sunshine host IPv4 only.

Before reporting READY, OpenTabletDisplay tests from the actual Android client:

- Sunshine `/serverinfo` over TCP;
- Sunshine pairing/control TCP 47984;
- RTSP TCP 48010;
- an arbitrary UDP token to a temporary Windows listener.

This catches a transport that merely "started" but cannot actually carry a Moonlight session.

## Host address identity

The currently validated transport uses the host's active physical IPv4 as Sunshine/Moonlight's address identity, then routes that address through gnirehtet over USB. Android Wi-Fi may remain off and stream packets still travel over USB, but the Windows host currently needs an active physical IPv4/default route so OpenTabletDisplay has a stable address Sunshine can advertise.

## Android enrollment

The first single authorized Android device that completes OpenTabletDisplay's live TCP+UDP checks is persisted in `enrollment.json`. Thereafter the runtime selects only that serial and will not silently switch to another authorized phone/tablet.

`reset-device.cmd` clears this enrollment. The current 3.x compatibility CLI remains `FSDL.exe enroll SERIAL`.

## Install ownership

The runtime records whether Sunshine and VDD already existed. Default uninstall removes the OpenTabletDisplay/FSDL integration only. Optional dependency removal is allowed only for dependencies that install-state records as project-owned.

Sunshine configuration restoration is compare-and-restore: a key is restored only if its current value still equals the value the installer applied. User edits made after installation win.

## Legacy runtime names

OpenTabletDisplay is the public project name. The 3.x runtime intentionally retains some `FSDL` names for binary paths, state, scheduled tasks, and Go module imports so a documentation rebrand cannot break installed systems.
