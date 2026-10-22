# Security policy

## Dependency trust

OpenTabletDisplay (formerly FSDL) pins exact upstream release URLs and SHA-256 digests. A matching hash proves the downloaded bytes are the bytes the project expected; it does **not** by itself prove the publisher is trustworthy. For provenance, users should also consider the upstream project, source, release signing, and maintainer history.

The installer additionally checks the Windows Authenticode signature on the pinned `adb.exe`. The Virtual Display Driver is signed; the installer temporarily adds only the exact catalog signer certificate to Trusted Publishers when needed for unattended root-device installation, and removes that certificate immediately if it added it. It does not leave an `Always trust this publisher`-style trust change behind.

## Network exposure

OpenTabletDisplay's transport is designed for a local USB-connected Android device. It does not configure router port-forwarding, UPnP, public tunnels, or cloud relays. Sunshine may have capabilities beyond this project's wired use case; review Sunshine's own settings if you use it separately.

## Credentials

When the installer installs Sunshine fresh, it generates a random local Web UI password. The credentials file is stored under the installing user's LocalAppData and ACL-restricted to that user. Existing Sunshine credentials are preserved during upgrades.

## Legacy 3.x names

The public project name is OpenTabletDisplay. Some installed 3.x paths, binaries, state files, and scheduled-task names still contain `FSDL` for compatibility. Treat those as the same project when auditing an installation.

## Reporting

Please open a GitHub security advisory for vulnerabilities that could allow code execution, privilege escalation, credential exposure, unsafe driver removal, or unintended network exposure. For ordinary crashes/compatibility bugs, use a normal issue and attach `status.cmd` output with secrets removed.
