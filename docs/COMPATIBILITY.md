# Compatibility

## Supported host

- Windows 10 or Windows 11, x86-64
- A working GPU/encoder supported by Sunshine
- A USB data port/cable
- An active physical IPv4 on the Windows host (current address-identity requirement)

FSDL's installer can install its pinned Sunshine and Virtual Display Driver versions, or reuse an existing compatible installation.

## Supported Android client

Required:

- Android device with USB debugging/ADB support
- a data-capable USB connection
- the official Moonlight Game Streaming Android app (`com.limelight`)
- ability to approve an Android VPN/Connection request for gnirehtet

The release ships a native transport probe for `arm64-v8a`, which is the hardware-validated path. Other Android ABIs use `toybox nc` when the ROM provides it; FSDL fails clearly rather than shipping an unvalidated native probe for those ABIs.

## Hardware validation

The complete wired path has been validated end-to-end on a Lenovo Tab M10 HD class Android tablet with a Windows 10 host. That validation demonstrated real Sunshine capture, TCP+UDP transport, Moonlight pairing, video/input, and low-latency streaming.

This does **not** mean every Android OEM ROM is guaranteed to behave identically. Relevant OEM differences include USB-debugging policy, background restrictions, VPN prompts, and bundled `toybox` commands.

## Known constraints

- Android's normal **USB tethering must be OFF** while FSDL is active.
- USB debugging must remain enabled and the PC must be authorized.
- Moonlight's client preferences are intentionally not rewritten by FSDL. See [MOONLIGHT.md](MOONLIGHT.md).
- The current validated transport needs the Windows host to have an active physical IPv4/default route for address identity. Android itself may keep Wi-Fi off.
- Multiple authorized Android devices are never resolved by guesswork. Disconnect extras or explicitly enroll one.

## Not currently supported

- Windows on ARM as a host
- iOS/iPadOS clients
- macOS/Linux hosts through this installer
- Android devices with ADB disabled by policy
- ordinary USB tethering/RNDIS as FSDL's streaming transport
