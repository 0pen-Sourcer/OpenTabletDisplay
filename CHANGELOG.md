# Changelog

## Unreleased — OpenTabletDisplay public rebrand

- Public project name changed from **FSDL** to **OpenTabletDisplay** so the repository immediately communicates its purpose: use an Android tablet or phone as a real second Windows monitor over USB.
- README rewritten around the end-user problem, installation flow, USB-vs-video-input explanation, architecture, limitations, and hardware-validated status.
- Documentation now treats `FSDL` as the legacy/internal 3.x compatibility name rather than the product's public identity.
- Runtime executable, install path, scheduled-task name, state files, and Go module imports are intentionally **not** renamed in this docs-first pass; that migration requires an explicit compatibility/rollback plan.

## 3.1.0 — GitHub-ready wired Android display release

- Generalized FSDL from a single Lenovo test target to Android-over-ADB.
- Persistent hidden bridge agent; no `RUN_ME.cmd` console required for daily use.
- First healthy Android client is durably enrolled so other USB-debugging phones are not hijacked.
- USB bridge dependencies are downloaded/verified during install instead of on first daily cable connection.
- Reuses existing Sunshine/VDD installs or installs pinned dependencies on fresh systems.
- Selects the VDD using Sunshine's stable MTT display GUID rather than `DISPLAYn` mapping.
- Uses client-driven resolution and refresh rate instead of a hard-coded 1280×800 profile.
- Retires machine-specific bring-up knobs (NVENC adapter, 40 Mbps cap, forced 60 FPS floor, disabled controller) only when a strong legacy-FSDL profile match is detected.
- Makes the virtual display primary only during streaming and restores the desktop on disconnect.
- Real Android TCP + UDP self-tests before bridge READY.
- Improved ADB states: missing, unauthorized, offline, multiple devices.
- Opens official Moonlight/Play Store path instead of bundling/forking Moonlight.
- Transactional Sunshine config backup/restore on every install/upgrade and ownership-aware dependency removal.
- Uninstall restores only FSDL-owned Sunshine values and preserves settings the user changed afterward.
- Temporary VDD publisher trust only during install; no persistent publisher trust change.
- CI, release packaging, security policy, third-party notices, architecture and troubleshooting docs.

## 3.0.x — Hardware bring-up series

Internal bring-up builds established the VDD → Sunshine → ADB/gnirehtet → Moonlight architecture and fixed RNDIS routing, Sunshine listener startup, gnirehtet APK path, and Moonlight address-semantics failures.
