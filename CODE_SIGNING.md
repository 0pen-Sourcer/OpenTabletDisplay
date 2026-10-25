# Code signing policy

## Current status

OpenTabletDisplay release binaries are **not Authenticode-signed yet**. Official releases are built in GitHub Actions from the public repository and publish:

- `OpenTabletDisplay-<version>-windows-x64.zip`;
- a detached `OpenTabletDisplay-<version>-windows-x64.zip.sha256` file; and
- an in-package `SHA256SUMS.txt` covering the shipped files.

Users should verify release checksums until a trusted signing pipeline is in place. A self-signed certificate is not used for public releases because it does not establish normal Windows trust.

## Target signing path

The preferred open-source signing path is **SignPath Foundation / SignPath.io**, subject to their eligibility review and approval. If the project is accepted, this policy and the release page will be updated with the exact certificate/publisher information required by SignPath Foundation.

A paid fallback is Microsoft Azure Artifact Signing (formerly Trusted Signing) or another publicly trusted Authenticode provider suitable for automated CI.

## Build and approval policy

- Source, build scripts, installer logic, and release workflows live in this repository.
- Release artifacts must be produced from a tagged commit by the repository's CI/release pipeline.
- Code-signing private keys must never be committed to the repository or placed in ordinary repository variables.
- Any future signing service must keep private key material in a managed HSM/service or equivalent protected signing environment.
- Signing approval should be separate from ordinary authoring where the provider supports approval roles.
- Third-party upstream binaries are not to be re-signed as if they were OpenTabletDisplay binaries. Their original publisher signatures/licenses remain their own.

## Team roles

The repository owner and maintainers currently act as committers/reviewers. Signing approver roles will be recorded here before the first signed public release and will follow the signing provider's required separation of duties.

## Privacy and network behavior

OpenTabletDisplay does not operate a telemetry or analytics backend. See [PRIVACY.md](PRIVACY.md) for the installer, dependency-download, local-network, and third-party-client behavior relevant to signing and user trust.

## System changes and uninstall

The installer documents and records the components/settings it owns, reuses compatible existing Sunshine/VDD installations instead of claiming ownership of them, and provides `uninstall.cmd`. Dependency removal is ownership-aware and optional. See [SECURITY.md](SECURITY.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
