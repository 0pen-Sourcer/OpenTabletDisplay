# Troubleshooting

Start with **OpenTabletDisplay → Diagnostics**. It reports setup logs, verified dependencies, the background agent, and Android connection issues. Log exports are local and optional.

## Android is invisible to FSDL

Check, in order:

- the cable supports data (MTP alone proves data capability, not ADB authorization);
- Developer options → USB debugging is ON;
- Android is unlocked;
- approve the PC's USB-debugging RSA prompt;
- Windows Device Manager does not show a failed ADB interface.

`FSDL.exe devices` distinguishes `device`, `unauthorized`, and `offline` states.

## Multiple Android devices are connected

FSDL deliberately refuses to guess before enrollment. Disconnect extra authorized devices, or run:

```text
FSDL.exe devices
FSDL.exe enroll SERIAL
```

## FSDL says USB tethering is ON

Turn off Android's normal USB tethering. FSDL uses ADB + gnirehtet instead and does not compete with RNDIS.

## Android shows the gnirehtet VPN request

Approve it. This is the local VPN interface gnirehtet uses to route the selected host address into the USB relay. It is expected on first setup and may reappear if Android clears/revokes the VPN permission.

## Moonlight says the PC is offline after it worked before

Use **Diagnostics** and confirm the FSDL agent is Ready/Running. The agent automatically rebuilds the bridge after cable reconnects and after a host IPv4 change. Pairing normally does not need to be repeated.

## Moonlight opens apps on the wrong monitor

Onboarding defaults to **Keep PC monitor primary** (`dd_configuration_option = ensure_active`), so the tablet stays an extended desktop. **Make tablet primary during streaming** is optional in Preferences. Sunshine may temporarily change the primary monitor if you select that option.

In Windows, open **System → Display**, click **Identify**, choose **Extend these displays**, arrange the monitors, and mark your physical screen as the primary display. **Win + Shift + Left/Right Arrow** moves the active window between monitors. Windows and applications can remember their last window location.

Also ensure **Use touchscreen as a trackpad** is OFF in Moonlight when you expect absolute/direct touch.

## PC speakers disappear or Steam Streaming Speakers becomes default

Enable Moonlight's **Play audio on PC** option if you want simultaneous host and Android audio. If Windows remains stuck on Steam Streaming Speakers after the stream ends, restart Sunshine and include Sunshine's audio log in a bug report.

## Installer cannot discover the virtual display

FSDL refuses to guess by `DISPLAYn`. Sunshine must enumerate exactly one virtual-display candidate. Re-running after Sunshine has restarted usually provides a fresh enumeration log.

The native installer selects the monitor using its verified hardware identity and the Sunshine display GUID, never an arbitrary Windows `DISPLAYn` index. If enumeration fails, use the app's Diagnostics and report the full error; no unsigned PowerShell repair script should be run.


## Uninstall and Sunshine notifications after removal

**OpenTabletDisplay → Preferences & help → Remove OpenTabletDisplay only** removes the app's own integration, background tasks, and files. It deliberately **keeps Sunshine and Virtual Display Driver**, even if installed during setup. This is to avoid removing a service or display adapter used by another application. **Sunshine may still be running and displaying notifications after the integration is removed.**

To remove Sunshine as well, open **Windows Settings → Apps → Installed apps → Sunshine → Uninstall** and follow the prompts. Do not manually delete `C:\Program Files\Sunshine` before uninstalling its Windows service.

If you also want to remove a virtual display driver, first verify the exact installed device and whether other apps use it; do not remove unrelated adapters or disable driver signature checks.

A separate opt-in full-removal wizard with ownership checks is tracked in [issue #16](https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/16). This is not yet part of the current release candidate.
