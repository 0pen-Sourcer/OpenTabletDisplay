# Third-party components

FSDL intentionally does not vendor Sunshine, Moonlight, Virtual Display Driver, Android Platform Tools, NefCon, or gnirehtet into the source repository. Install/runtime code downloads pinned upstream artifacts when required.

| Component | Purpose | Pinned release | License / source | Artifact integrity |
|---|---|---:|---|---|
| Sunshine | Windows capture, hardware encoding, GameStream-compatible host | 2026.914.233613 | GPL-3.0-only, `LizardByte/Sunshine` | Windows AMD64 MSI SHA-256 `1d7fed8beecd5889dc7ff14cf9f42d6d38f37c3066c13c6c2a5f4e91847e0ccf` |
| Moonlight Android | Android decoding, input and client UI | user-installed official app | GPL-3.0, `moonlight-stream/moonlight-android`, package `com.limelight` | installed from the user's app store/upstream |
| Virtual Display Driver | Real Windows indirect virtual monitor | 25.7.23 | MIT, `VirtualDrivers/Virtual-Display-Driver` | x86 Driver Only ZIP SHA-256 `e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a` |
| NefCon | Root-enumerated driver install helper | 1.14.0 | MIT, `nefarius/nefcon` | ZIP SHA-256 `a15557da24a9efca203158de3b43b0eaf982db231f0194031f1ed428bc13e669` |
| Android Platform Tools | ADB client | 37.0.1 | Google Android SDK Platform Tools terms/source | Windows ZIP SHA-256 `45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99`; `adb.exe` Authenticode checked at runtime |
| gnirehtet | TCP+UDP IPv4 transport over ADB without root | 2.5.1 | Apache-2.0, `Genymobile/gnirehtet` | Windows Rust ZIP SHA-256 `7f5b1063e7895182aa60def1437e50363c3758144088dcd079037bb7c3c46a1c` |

Third-party trademarks belong to their respective owners. FSDL is not affiliated with or endorsed by those projects.
