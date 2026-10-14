# Troubleshooting

Start with `status.cmd`. It reports Sunshine, VDD, the background agent, Android enrollment, FSDL doctor output, and the recent agent log.

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

Run `status.cmd` and confirm the FSDL agent is Ready/Running. The agent automatically rebuilds the bridge after cable reconnects and after a host IPv4 change. Pairing normally does not need to be repeated.

## Moonlight opens apps on the wrong monitor

FSDL configures the VDD as session-primary while streaming and asks Sunshine to restore the original display layout on disconnect. If this fails, `status.cmd` plus Sunshine's current log are useful for a bug report.

Also ensure **Use touchscreen as a trackpad** is OFF in Moonlight when you expect absolute/direct touch.

## PC speakers disappear or Steam Streaming Speakers becomes default

Enable Moonlight's **Play audio on PC** option if you want simultaneous host and Android audio. If Windows remains stuck on Steam Streaming Speakers after the stream ends, restart Sunshine and include Sunshine's audio log in a bug report.

## Installer cannot discover the virtual display

FSDL refuses to guess by `DISPLAYn`. Sunshine must enumerate exactly one virtual-display candidate. Re-running after Sunshine has restarted usually provides a fresh enumeration log.

Advanced users can provide Sunshine's exact stable display GUID:

```powershell
installer\install.ps1 -OutputDeviceId '{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}'
```

Do not substitute `DISPLAY1`, `DISPLAY2`, or a DXGI index.

## Uninstall

`uninstall.cmd` removes FSDL and restores only Sunshine keys still equal to FSDL's applied values.

To also remove Sunshine/VDD **only if FSDL originally installed them**:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\uninstall.ps1 -RemoveDependencies
```
