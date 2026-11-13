# Expo one-button iOS size comparison

This fixture renders one React Native `Button` whose title contains a counter.
Pressing it increments the counter. It uses Expo, React and React Native only;
there is no router, development client, custom font, icon or image.

The source is based on Expo's blank template. Expo 57.0.28, React 19.2.3 and
React Native 0.86.3 are pinned. The machine's seven-day npm release-age policy
excluded the newest SDK 58 template when this comparison was prepared.

The measured result below came from an unsigned Xcode Release archive for a
generic physical iPhone (`arm64`), generated with Expo prebuild and CocoaPods. It
sums every file inside the `.app`, excludes external debug symbols, and compresses
the full payload with ZIP DEFLATE level 9 and fixed ZIP timestamps.

These are unsigned local device-bundle and ZIP measurements, not App Store
download/installed sizes. The Expo and Kumiko buttons have equivalent counter
behavior in their source, but use their respective frameworks' normal controls.
Expo's default native runtime/modules are retained. This measures this minimal
configuration, not all possible React Native or .NET applications.

## Measured result

On 2026-10-21 with Xcode 26.4.1, for unsigned `arm64` iPhone Release bundles
(MiB = 1,048,576 bytes):

| One-button app | App bundle | Local ZIP payload |
| --- | ---: | ---: |
| Expo 57.0.28 / React Native 0.86.3 | 25.23 MiB | 6.57 MiB |
| Kumiko / Mono | 13.46 MiB | 4.62 MiB |
| Kumiko / NativeAOT | 3.66 MiB | 1.60 MiB |

The Expo JavaScript bundle is Hermes bytecode, version 98, and occupies 1.36 MiB.
The largest files are React.framework (11.46 MiB), hermesvm.framework (4.83 MiB),
and ExpoModulesCore.framework (3.65 MiB). The artifact includes Expo's default
native dependencies, including file-system/font support, even though the app
does not use those features. No extra native-size optimization was applied.

The Expo archive completed with upstream compiler/build-phase warnings retained
in `build.log`. The Kumiko comparison's warning-free builds and simulator behavior
verification are recorded separately in its report.
