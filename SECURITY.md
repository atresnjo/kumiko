# Security policy

## Supported versions

The latest minor release receives security fixes; the previous minor receives
them for three months after its successor ships.

## Reporting a vulnerability

Report vulnerabilities privately through this repository's
**Security → Report a vulnerability** (GitHub private advisory). Do not open a
public issue. Include the affected version, operating system, a minimal
reproduction and the impact you observed. Reports are acknowledged within five
working days, and fixes are coordinated with the reporter before disclosure.

## Security-relevant surfaces

These components handle data that may come from outside the application:

- **Web bridge** (`Kumiko.Web`): only the configured page origin's main frame may
  call native services; requests are typed and unknown methods are rejected.
- **Image and font decoders**: the managed PNG and TrueType readers and the
  platform SVG backends parse untrusted bytes. Malformed input must throw
  `InvalidDataException`, never read out of bounds or hang.
- **Updater** (`Kumiko.Desktop.Updater`, macOS): downloads are verified by SHA-256,
  code signature, signing team and bundle version before an explicit install.
- **Clipboard, dialogs, shell operations and process watches**: they run only
  on explicit application calls.

Applications remain responsible for validating their own data, storing
credentials in the operating system's credential store, and signing their
release binaries.
