# Contributing

Thanks for wanting to help. This is a small project, so the process is light.

## Getting set up

1. Visual Studio 2022 with the **Universal Windows Platform development** workload
2. Windows 10 SDK **10.0.19041.0** or later
3. Settings → System → For developers → **Developer Mode** = On
4. Open `MirrorReceiver.sln`, platform **x64**, press `F5`

You need a phone that supports Miracast to test the interesting path. Google Pixel
devices do not, so testing on one is not possible.

## Before opening a pull request

- Build and run it. A change to the receiver path that has not been tested against a
  real phone will be asked for evidence.
- Keep the code style of the surrounding file. Braces on their own lines, `_camelCase`
  private fields, XML doc comments on non-obvious members.
- Do not commit `bin/`, `obj/`, `AppPackages/`, signing certificates, or `.pfx` files.
  `.gitignore` covers these — check `git status` before committing.

## What's genuinely useful

- **Hardware compatibility reports.** If you test on a Wi-Fi chip or phone not listed
  in the README, say so in an issue. This project's biggest limitation is hardware
  support, and reports are the only way to map it.
- **Troubleshooting entries.** A symptom and its fix, added to the README table.
- **Audio/video sync or latency improvements** — anything that reduces the glass-to-glass
  delay.
- Bug fixes with a clear reproduction.

## What's out of scope

- **iOS / AirPlay support.** Different protocol entirely, different codebase.
- **Custom phone-side apps.** Supporting phones that dropped Miracast means shipping an
  Android sender and a bespoke protocol. That is a separate project, not a feature of
  this one.
- **Bundling or redistributing Microsoft's "Connect" app.** The script enables the
  optional feature locally; it does not ship the binary.

## Reporting bugs

Include:

- Windows build (`winver`)
- Wi-Fi adapter model (Device Manager → Network adapters)
- Phone make and model, and Android version
- What you did, what you expected, what happened
- The event log text from the app window — it is there for exactly this

## Licence

By contributing you agree your work is licensed under the [MIT Licence](LICENSE).
