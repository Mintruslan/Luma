# Third-party components

NAudio 2.2.1 is used for WASAPI loopback and microphone capture. Copyright Mark Heath and contributors, MIT license. Source: https://github.com/naudio/NAudio. Its license is included as `NAUDIO-LICENSE.txt`.

Luma invokes FFmpeg as a separate command-line executable to record the screen.
FFmpeg is copyright the FFmpeg developers and its incorporated libraries' authors.

Windows binaries are obtained from Gyan's release essentials package, a Windows build provider linked by FFmpeg's official download page:

- Official project: https://ffmpeg.org/
- Official documentation for desktop capture: https://ffmpeg.org/ffmpeg-devices.html#gdigrab
- Windows binary package, build configuration and source links: https://www.gyan.dev/ffmpeg/builds/
- Project source: https://github.com/FFmpeg/FFmpeg

The essentials build enables GPL components including libx264. Its licensing terms are in the accompanying `FFMPEG-LICENSE.txt`; the build provider's notes, version and corresponding-source references are in `FFMPEG-README.txt`. The exact downloaded archive and binary hashes are recorded in `build-info.json`. The application's MIT license does not replace these component licenses.

The self-contained application also includes the Microsoft .NET runtime, licensed under MIT with its associated third-party components. Runtime source and notices: https://github.com/dotnet/runtime and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
