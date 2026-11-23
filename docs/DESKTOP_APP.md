# OpenTabletDisplay 3.4 native setup preview

OpenTabletDisplay's everyday desktop interface is now a **compiled, self-contained .NET 10 WPF executable**. It is not a PowerShell-hosted window. Users need no separate .NET installation. The app starts as a normal Windows user; the installer and uninstaller request Windows administrator approval. Sunshine service controls try existing Windows permissions first and request UAC only when Windows requires it.

## First launch

1. Download the full, checksum-verified Windows release archive, not GitHub's auto-generated source ZIP. Extract it completely.
2. Open **OpenTabletDisplay.exe**. The overview identifies Sunshine and the virtual-display driver using native Windows APIs.
3. Choose **Set up & connect → Install / repair**. Accept Windows UAC if you authorize installation of pinned verified dependencies. Antivirus verdicts stay in force. Do not disable endpoint protection to make setup succeed.
4. While installing, the UI displays the actual installation log and alerts when no progress is logged. Driver and USB controls remain unavailable until the one-time setup commits.
5. Connect your Android device using a real USB data cable, unlock it, enable USB debugging, and approve the Windows computer's RSA prompt.
6. Select **My devices → Scan connected Android devices**. Choose an authorized device to enroll; no automatic enrollment of unknown or additional devices.
7. Install the official Moonlight app on Android, pair with Sunshine through its local web UI, then open Desktop. A one-time gnirehtet VPN connection prompt may appear; ordinary Android USB tethering must remain OFF.

After setup, normal use should be cable → Android authorization (if necessary) → Moonlight Desktop. It is not necessary to keep OpenTabletDisplay's control window open. A dedicated no-console Go agent runs as a limited-user scheduled task at login; there is no background PowerShell desktop or watchdog.

## Native Windows controls

- **Overview:** installation completeness, **Start/Stop Sunshine** controls, Sunshine service state, exact ROOT\MttVDD driver hardware ID, Android enrollment, and host address.
- **Set up & connect:** elevated setup, non-fake progress, Android permissions and Moonlight onboarding.
- **My devices:** asynchronous, bounded USB device scans, explicit serial validation and enrollment; one USB action at a time.
- **Diagnostics:** bounded local log reads and connection doctor, never automatically uploaded, opt-in sanitized export.
- **Preferences & help:** separate Start/Stop Sunshine controls; choose **Start automatically** (with Windows) or **Start manually** (only when requested); trusted support links, diagnostics and guarded uninstall.

Stopping Sunshine disconnects any active Moonlight stream but does not uninstall the display driver, clear Moonlight pairing, or erase Android enrollment. Selecting Manual startup does not stop an already-running stream. The app is single-instance and does not start the USB transport merely because its executable is double-clicked. The separate FSDL runtime is not intended for direct use by consumers. A bare FSDL.exe invocation displays help only.

## Security commitments

- No AVG/Defender disabling, antivirus exclusions, quarantine restores, security bypass, silent Android RSA trust, or silent VPN permissions.
- The user-facing GUI **and administrator-approved setup/uninstall path** are implemented in compiled .NET 10 code. The packaged Windows ZIP contains no PowerShell or batch installer scripts. Native setup verifies pinned hashes, requires Windows-trusted upstream signatures, and refuses account-identity mismatches. Windows PowerShell execution policy is neither inspected nor altered during setup.
- Exact pinned download SHA-256 checks and valid upstream signatures where supplied; local package manifest verifies extraction integrity.
- Main app runs unelevated; UAC is user-triggered. Sunshine Start/Stop and Manual/Automatic actions are restricted to the exact `SunshineService` service and refuse different-account elevation. Startup settings do not stop or start Sunshine implicitly. Commands use fixed native APIs and .NET argument lists, not arbitrary shell strings.
- No remote telemetry, personal data uploads or cloud login. Logs can contain local device information; exported reports have basic redaction but must still be reviewed.
- **Unsigned apps may legitimately be quarantined for review.** Passing CI or hash checks does NOT override a security product's verdict and does NOT establish trusted publisher identity.
- Code signing and independent security verification are required before publicly promoting a stable release.

## Current limitations

- This app targets Windows 10/11 x64 and Android Moonlight/ADB. It does not support iOS, Linux/macOS host, or every Android ROM.
- First setup requires network access for pinned Sunshine, VDD/NefCon (if VDD absent), Android Platform Tools, and gnirehtet. The native installer checks the USB transport helper before changing Sunshine or the display driver.
- Real Moonlight video, touch, protected-content behavior, GPU encoders, multi-monitor layout, USB reconnect, hibernation and driver rollback **must be validated on physical hardware**, not inferred from CI.
- Standard Windows accounts requiring different administrator credentials can have per-user setup ownership issues. That multi-user elevation path is NOT certified until tested.
- The application's own publisher certificate is not yet configured. Do not tell users to weaken Windows security.

## Support workflow

Open **Diagnostics**, inspect the latest installation and USB preparation error, and check AVG/Defender protection history for the exact affected executable. Determine whether the event was a scan, a quarantine, a suspicious-behavior detection, or a final malware verdict before attempting any retry. A blocked executable should not be force-run.

## Stable release gate

1. Windows CI builds a self-contained WPF EXE, runs its offline window/serial/redaction smoke test and Sunshine-action account-identity guards, checks exact package files and embedded icon, and verifies SHA-256.
2. Real Windows 10/11 install and uninstall (including existing Sunshine) without settings loss, standard-user account tests, accessibility and scaling checks.
3. Real Android USB authorization, gnirehtet/VPN permission, Moonlight stream, touch, resolution, sleep/reconnect and multiple-device tests.
4. Trusted code signing for OpenTabletDisplay.exe, FSDL.exe AND FSDL-Agent.exe, plus documented source/build provenance and security review.

Until these requirements pass, this remains an **unsigned development release candidate**, not production-safe proof for general consumers.
