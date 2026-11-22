# OpenTabletDisplay desktop control center — 3.2.0 release candidate

## One-click Windows setup

Download the packaged release ZIP from GitHub Releases (not GitHub's auto-generated Source Code ZIP). Extract it fully and double-click **OpenTabletDisplay.exe**.

The Windows control center provides:
- Overview: status of OpenTabletDisplay, Sunshine, the virtual display driver, and Android enrollment.
- Set up & connect: administrator-approved installation, progress, device USB authorization check, explicit authorized-device enrollment, host-address copy, Moonlight and Sunshine setup links.
- Diagnostics: local installer/agent logs, live Doctor, optional redacted text report.
- Preferences & help: start/stop the USB bridge, forget the enrolled device, restore app-owned settings through safe uninstall, and troubleshooting links.

On the first run, select **Install / repair**. This downloads pinned and SHA-256-verified Sunshine, the Virtual Display Driver, NefCon, ADB, and gnirehtet. The GUI itself uses Windows' built-in WPF runtime and does not need an extra UI-framework download.

After setup, connect an Android device with a data-capable USB cable, unlock it, enable Developer options and USB debugging, and approve the computer's RSA trust prompt. Keep ordinary Android USB tethering **OFF**. Approve the local gnirehtet VPN/connection permission when prompted.

Click **Check connected device**. If several authorized devices exist, select which one to enroll explicitly. Install the official Moonlight Game Streaming Android app, pair it using the Moonlight host address shown by the control center and the PIN from Sunshine's web UI, then select **Desktop** in Moonlight.

Everyday use after the first authorized setup should be: plug USB, open Moonlight, launch Desktop. The background USB agent reconnects on device changes; the control center does not have to stay open.

**Plug-and-play does not mean bypassing the operating system's protections.** Driver UAC, Android USB debugging/RSA authorization, local VPN permission and Moonlight pairing require explicit user action at first setup.

## Privacy and safety

- No cloud account, telemetry, ad tracking, or third-party desktop browser engine is added by the GUI.
- Windows setup elevates only after explicit user approval; status and diagnostic checks remain unprivileged.
- Installation tracks app-owned changes; existing compatible Sunshine settings and VDD installations are reused. Default uninstall preserves upstream Sunshine and drivers, and restores only config entries that still equal app-applied values.
- Device enrollment does not silently switch to another USB-debugging phone. Forgetting an enrolled device requires confirmation.
- Original Sunshine credentials are stored privately in the local installation directory. Never share sunshine-webui.txt.
- Diagnostic export redacts common user-profile paths, device serial labels and IP addresses, but you must still review exported files before sharing.

## Troubleshooting

Open Diagnostics, run the live check, and inspect the latest installation and agent logs. If VDD is found but unhealthy, inspect Device Manager and the driver error rather than blindly uninstalling unrelated drivers. If Android is invisible, inspect USB data cable, unlocked screen, debugging authorization, and Windows ADB interface. If the Moonlight PC appears offline, verify Sunshine and the USB agent, and re-check the host address.

If an installer run fails, don't run several overlapping installers. Fix the reported blocker, then use Install / repair again. For device switching, use the explicit selection or forget-device function.

## Supported hardware and limitations

Windows 10/11 x64, Sunshine-compatible GPU/encoder, USB data cable, Android ADB and official Moonlight, and a host with an active physical IPv4/default route. Normal Android tethering must be OFF. iOS, macOS/Linux hosts, Windows on ARM, and Android ROMs that prohibit ADB/VPN permission are not supported in this release.

## Release gate

CI now checks Go tests, Go vet/race, offline self-tests, Windows XAML loading, desktop-controller smoke execution, exact VDD hardware-ID matching, fresh installer-log creation, and ZIP contents. It **cannot** establish that real Windows driver installation, Sunshine video capture, Android USB transport, sleep/wake, and uninstall are safe on actual hardware. A stable release should follow physical test coverage and official code signing. This release candidate is not claimed to have passed those final gates.
