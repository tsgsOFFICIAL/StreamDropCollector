# StreamDropCollector

**A fully automated, open-source drops miner for Twitch.tv and Kick.com**

> Mine streams in the background, earn campaign rewards, and claim them automatically, all without lifting a finger.

[![Issues](https://img.shields.io/github/issues/tsgsOFFICIAL/StreamDropCollector)](https://github.com/tsgsOFFICIAL/StreamDropCollector/issues)
[![Last Commit](https://img.shields.io/github/last-commit/tsgsOFFICIAL/StreamDropCollector)](https://github.com/tsgsOFFICIAL/StreamDropCollector/commits/master)
[![Nightly Build](https://github.com/tsgsOFFICIAL/StreamDropCollector/actions/workflows/nightly.yml/badge.svg)](https://github.com/tsgsOFFICIAL/StreamDropCollector/actions/workflows/nightly.yml)
[![Release Build](https://github.com/tsgsOFFICIAL/StreamDropCollector/actions/workflows/release.yml/badge.svg)](https://github.com/tsgsOFFICIAL/StreamDropCollector/actions/workflows/release.yml)
[![GitHub license](https://img.shields.io/github/license/tsgsOFFICIAL/StreamDropCollector)](https://github.com/tsgsOFFICIAL/StreamDropCollector/blob/master/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/download)
[![WPF](https://img.shields.io/badge/WPF-Modern_UI-teal)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview)
[![GitHub stars](https://img.shields.io/github/stars/tsgsOFFICIAL/StreamDropCollector?style=social)](https://github.com/tsgsOFFICIAL/StreamDropCollector)
[![Discord](https://img.shields.io/discord/227048721710317569?logo=discord&logoColor=FFF&label=Discord&labelColor=5865F2)](https://discord.gg/Cddu5aJ)
[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

## Table of Contents

- [Features](#features)
- [Privacy & Safety](#privacy--safety)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [Building from Source](#building-from-source)
- [Important Notes](#important-notes)
- [Support the Project](#support-the-project--the-creator)
- [How to Contribute](#how-to-contribute)
- [Star History](#star-history)
- [License](#license)

## Features

- Dual-platform support: mines drops on **Twitch** and **Kick** simultaneously
- Smart stream selection: automatically picks the best active campaign and streamer, including general category drops
- Auto-claiming: detects and claims ready rewards instantly
- Lowest quality mode: sets streams to minimum quality so it barely touches your bandwidth or CPU
- Mature content bypass: handles age gates automatically
- Live progress tracking: real-time percentage and mined channel display
- Clean modern UI: built with WPF, dark mode, responsive cards
- Background operation: runs quietly while you do whatever you want

## Privacy & Safety

- No third-party APIs or services. All network traffic goes only to **Twitch.tv** and **Kick.com** (Helix, GraphQL, EventSub, and Kick's own REST APIs)
- Drops logins happen inside secure embedded WebView2 browsers, the same engine Edge and Chrome use
- Twitch stream metadata uses a separate one-time Helix device-code login, still sent only to Twitch and nowhere else
- Your credentials never leave your machine outside the WebView2 engine and local token storage. You can always find them at `%appdata%\Stream Drop Collector\Stream Drop Collector.exe.WebView2` and `%appdata%\Stream Drop Collector\helix-auth.dat`. Both are encrypted and never leave your device
- No analytics, telemetry, or data collection by this app, only logging happens at `%appdata%\Stream Drop Collector\logs`

## Screenshots

![Dashboard](https://github.com/tsgsOFFICIAL/StreamDropCollector/blob/master/Github-Assets/Dashboard.png?raw=true)
_Main dashboard showing live progress for both platforms_

---

![Inventory](https://github.com/tsgsOFFICIAL/StreamDropCollector/blob/master/Github-Assets/Inventory.png?raw=true)
_Active campaigns and rewards overview_

---

## Requirements

- Windows 10/11 (64-bit)
- A Twitch and/or Kick account

## Quick Start

1. **Download** the latest release from [Releases](https://github.com/tsgsOFFICIAL/StreamDropCollector/releases/latest) (recommended)
2. Extract the ZIP to `%appdata%\Stream Drop Collector\`
3. Run `Stream Drop Collector.exe`
4. Log in to Twitch and/or Kick when the embedded browsers appear
5. On first run, Twitch will also prompt for a one-time **Helix** authorization (device code). This is a separate Twitch login used only for live stream metadata, it's not connected to your drops login and the data still goes only to Twitch
6. Enjoy the free drops!

> **Alternative download:** if you'd rather grab the raw published folder instead of a packaged release, you can use this third-party convenience link to zip it directly from GitHub: [download-directory.github.io](https://download-directory.github.io/?url=https://github.com/tsgsOFFICIAL/StreamDropCollector/tree/master/UI/bin/Release/net10.0-windows10.0.17763.0/publish/win-x64). This isn't part of the app itself, it just zips a public GitHub folder for you.

> **Nightly Builds:** Want the latest code before it gets a tagged release? Every push to `master` automatically rebuilds and republishes the [nightly release](https://github.com/tsgsOFFICIAL/StreamDropCollector/releases/tag/nightly), both self-contained and framework-dependent. The version number includes the short commit hash it was built from, so you always know exactly what you're running. It's untested beyond CI passing, so expect the occasional rough edge, grab a regular [Release](https://github.com/tsgsOFFICIAL/StreamDropCollector/releases/latest) instead if you want something stable.

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed.

```bash
git clone https://github.com/tsgsOFFICIAL/StreamDropCollector.git
cd StreamDropCollector
dotnet restore UI/UI.csproj
dotnet publish UI/UI.csproj -c Release -r win-x64 -p:SelfContained=true
```

Executable will be in `UI\bin\Release\net10.0-windows10.0.17763.0\publish\win-x64\Stream Drop Collector.exe`

## Important Notes

- This tool is for personal use only
- Respect Twitch and Kick's Terms of Service
- Use at your own risk, automated viewing may violate platform rules in some contexts
- This tool is not affiliated with, endorsed by, or sponsored by Twitch or Kick

## Support the Project & the creator

This tool is (and always will be) **100% free**.
If you're farming drops 24/7 and want to fuel more rage-fueled coding sessions, then hit that button and become a legend.

[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

## How to Contribute

Bugs, ideas, docs, code, all of it helps. A few ground rules so we don't end up untangling a mess later:

**Don't:**

- Rebrand or republish this as your own, keep attribution to me (tsgsOFFICIAL) and this repo intact
- Strip license headers, credits, or "made by" notices when sharing builds or forks
- Drop a giant PR that rewrites half the codebase with no issue or discussion first
- Bundle unrelated changes (formatting sweeps, dependency bumps, drive-by refactors) into a feature fix
- Build anything that encourages abuse or clearly violates Twitch/Kick's ToS, it won't get merged

**Do:**

- Open an issue first for anything bigger than a small fix, so we agree on the approach before you sink hours into a PR
- Throw a "what if we..." into [GitHub Issues](https://github.com/tsgsOFFICIAL/StreamDropCollector/issues) or [Discord](https://discord.gg/Cddu5aJ) even if you're not sure it's worth a full PR
- Keep PRs focused, one logical change per PR with a clear "what and why"
- Test on Windows before submitting (WPF + WebView2, no Linux/macOS builds)
- Match the existing naming, structure, and code style

### (Suggested) Workflow

1. Fork [tsgsOFFICIAL/StreamDropCollector](https://github.com/tsgsOFFICIAL/StreamDropCollector)
2. Branch off (`fix/kick-claim-timeout`, `feature/inventory-filter`, or whatever fits)
3. Make your changes, confirm it still builds and nothing is broken `dotnet publish UI/UI.csproj -c Release -r win-x64`
4. Open a PR against `master`. Short summary, link the issue if there is one

Not sure where to start? Check [open issues](https://github.com/tsgsOFFICIAL/StreamDropCollector/issues) or ask in [Discord](https://discord.gg/Cddu5aJ).

## Star History

<a href="https://www.star-history.com/#tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left" />
 </picture>
</a>

## License

[MIT License](LICENSE), see the file for details.

---

**Made with rage, caffeine, and zero sleep by tsgsOFFICIAL**  
Happy farming! 🚀
