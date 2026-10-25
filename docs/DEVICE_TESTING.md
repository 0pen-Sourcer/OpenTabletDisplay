# Device testing

Real Android hardware reports are the highest-value contribution right now. OpenTabletDisplay intentionally does not claim universal Android compatibility from CI alone.

## What to test

1. Install the latest release on Windows 10 or 11 x86-64.
2. Use an Android tablet/phone with USB debugging enabled and normal Android USB tethering disabled.
3. Complete first-run enrollment and Moonlight pairing.
4. Verify a real extended desktop, direct-touch behavior if desired, audio routing, cable reconnect, Windows logon reconnect, and a 10+ minute 60 FPS session.
5. Run `status.cmd` after the test.

## Report

Use the **Device compatibility report** issue template and include the Android model/version, Windows/GPU, cable/port if known, health checks, video/input/audio/reconnect results, Moonlight settings/performance observations, and sanitized `status.cmd` output. Never post Sunshine passwords, pairing PINs, private keys, or unrelated personal data.
