# Moonlight Android setup

OpenTabletDisplay (formerly FSDL) intentionally uses the official Moonlight Android client instead of maintaining a fork.

## One-time setup

1. Install **Moonlight Game Streaming** on Android.
2. Enable Android Developer options and **USB debugging**.
3. Connect the Android device using a data-capable USB cable and approve the PC's RSA fingerprint.
4. Leave Android's normal **USB tethering OFF**.
5. Let the OpenTabletDisplay background agent establish the bridge. Approve gnirehtet's Android VPN/Connection request when Android asks.
6. Add the host address printed by `status.cmd` if Moonlight has not already stored it.
7. Moonlight shows a short numeric pairing PIN. Enter that PIN on Sunshine's PIN page.

The Sunshine Web UI username/password are **not** the Moonlight PIN.

## Recommended client preferences

For second-monitor behavior rather than touchpad behavior:

- Resolution: **Native** or the panel's preferred resolution
- Frame rate: **60 FPS** to start
- Use touchscreen as a trackpad: **OFF**
- Optimize game settings: **ON**
- Play audio on PC: **ON** if you want host speakers and Android audio at the same time

With touchscreen-as-trackpad disabled, a tap maps to the corresponding point on the streamed display instead of clicking wherever the host cursor was already sitting.

## Normal daily use

After pairing and enrollment:

```text
plug USB → unlock Android if needed → open Moonlight → Desktop
```

The current 3.x OpenTabletDisplay agent still uses the legacy FSDL scheduled-task/runtime names internally for compatibility; no command window should remain open.

## Switching Android devices

Run `reset-device.cmd`, then connect only the new authorized Android device. It becomes enrolled only after its real USB TCP+UDP checks succeed.
