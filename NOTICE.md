# Third-party notices

## MiracastReceiverSample

The API usage in `src/MirrorReceiver/MainPage.xaml.cs` was verified against, and
follows the structure of, the minimal reference sample:

- **Project:** <https://github.com/KevinKennedy/MiracastReceiverSample>
- **Author:** Kevin Kennedy
- **License:** MIT

That sample is the only publicly available known-good demonstration of the
`Windows.Media.Miracast` API. Because the Microsoft Learn reference pages for these
types are stubs without member lists, it served as ground truth for:

- the `GetDefaultSettings()` → `DisconnectAllAndApplySettingsAsync()` flow
- `CreateSessionAsync(null)` argument shape
- the `MediaSourceCreated` → `MediaPlayer.Source` handoff
- the required `Package.appxmanifest` capability set

No source code was copied verbatim; the implementation here is independently written,
but the API call sequence follows that reference.

```
MIT License

Copyright (c) Kevin Kennedy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Microsoft APIs and assets

- `Windows.Media.Miracast`, `Windows.Media.Playback` and the UWP XAML framework are
  Microsoft platform APIs, used under the Windows SDK licence.
- The Windows "Wireless Display" optional feature referenced in
  `scripts/Enable-BuiltInReceiver.ps1` is a Microsoft component and is **not**
  redistributed here — the script only enables it on the local machine.

## Trademarks

Miracast, Windows, and Android are trademarks of their respective owners. This project
is not affiliated with or endorsed by the Wi-Fi Alliance, Microsoft, or Google.
