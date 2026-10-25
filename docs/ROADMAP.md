# Roadmap

OpenTabletDisplay is deliberately keeping the media stack boring: Sunshine encodes, Moonlight decodes, and this project owns the Windows-display + wired-Android integration.

## Near term

- Expand the real-device compatibility matrix beyond the Lenovo Tab M10 HD.
- Keep install/update/uninstall transactional and preserve user-owned Sunshine/VDD configuration.
- Improve first-run diagnostics and make failure reports safe to share.
- Add Authenticode signing when a maintainable signing identity/certificate is available. Until then, releases publish detached SHA-256 checksums and reproducible CI artifacts.

## Next architecture milestone

Remove the current dependency on the Windows host's active physical IPv4 address as Sunshine/Moonlight identity. The goal is an OpenTabletDisplay-owned host-only identity so the wired display session does not depend on LAN/Wi-Fi state. This should not ship until it is hardware-validated for both TCP and UDP.

## Later

- Broaden Android architecture/device validation if real-world reports justify it.
- Evaluate a native USB transport only if ADB + gnirehtet becomes the measured bottleneck; do not replace a proven component for novelty.
- Consider a carefully migrated 4.x runtime rename that removes legacy `FSDL` internal identifiers without breaking 3.x upgrades or uninstall.
