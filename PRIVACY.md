# Privacy

OpenTabletDisplay does not operate an account service, analytics backend, advertising system, or project-owned telemetry endpoint.

## What OpenTabletDisplay itself sends

During installation, OpenTabletDisplay may make outbound HTTPS requests to the pinned upstream download locations documented in the source and third-party notices in order to obtain required dependencies when compatible copies are not already installed. Those requests necessarily expose ordinary network metadata such as the host IP address to the upstream download provider.

During normal display use, the OpenTabletDisplay integration creates a local/wired networking path between the user's Windows PC and the enrolled Android device so Sunshine and Moonlight can communicate. OpenTabletDisplay does not relay that stream through an OpenTabletDisplay-operated server.

The current 3.x architecture still uses the Windows host's active physical IPv4 address as Sunshine/Moonlight address identity while the stream traffic itself is routed through the USB bridge. This limitation is documented in the README and roadmap.

## Data stored locally

OpenTabletDisplay stores local state needed to operate and safely uninstall/update the integration, such as the enrolled Android device identity, ownership/install records, backups of settings it changes, dependency/runtime files, and diagnostic logs. These are stored on the user's Windows machine and are not uploaded to an OpenTabletDisplay service.

Diagnostic/status output can contain device or network details. Users should sanitize logs before posting them publicly and must not share Sunshine passwords, pairing PINs, private keys, or unrelated personal information.

## Third-party software

OpenTabletDisplay orchestrates independent third-party projects including Sunshine, Moonlight, Android Platform Tools/ADB, gnirehtet, Virtual Display Driver, and NefCon. Those projects are distributed under their own licenses and may have their own privacy/network behavior. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the upstream projects for their policies.

## No hidden background reporting

The background OpenTabletDisplay/FSDL compatibility agent exists to maintain the local USB display transport and reconnect behavior. It is not intended to send usage analytics or diagnostic reports to the project maintainers.

If future versions add any project-operated network service or telemetry, the behavior must be documented before release and should be opt-in where collection is not strictly required for a user-requested feature.
