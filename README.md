# StreamDropCollector

**A fully automated, open-source drops miner for Twitch.tv and Kick.com**  
Watch streams in the background, earn campaign rewards, and claim them automatically - all without lifting a finger.

[![GitHub stars](https://img.shields.io/github/stars/tsgsOFFICIAL/StreamDropCollector?style=social)](https://github.com/tsgsOFFICIAL/StreamDropCollector)
[![GitHub license](https://img.shields.io/github/license/tsgsOFFICIAL/StreamDropCollector)](https://github.com/tsgsOFFICIAL/StreamDropCollector/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/download)
[![WPF](https://img.shields.io/badge/WPF-Modern_UI-teal)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview)
[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

## Features

- Dual-platform support: Mines drops on **Twitch** and **Kick** simultaneously
- Smart stream selection: Automatically picks the best active campaign and streamer (including general category drops)
- Auto-claiming: Detects and claims ready rewards instantly
- Lowest quality mode: Sets streams to minimum quality for minimal resource usage
- Mature content bypass: Handles age gates automatically
- Live progress tracking: Real-time percentage and watched channel display
- Clean modern UI: Built with WPF, dark mode, responsive cards
- Background operation: Runs quietly while you do whatever you want

## Screenshots

*(Add your own screenshots here - dashboard, progress cards, help tab, etc.)*

## Requirements

- Windows 10/11 (64-bit)
- A Twitch and/or Kick account

## Quick Start

1. **Download** the latest release from [Releases](https://github.com/tsgsOFFICIAL/StreamDropCollector/releases/latest) _(recommended)_
   - or direct folder download: [https://download-directory.github.io/?url=https://github.com/tsgsOFFICIAL/StreamDropCollector/tree/main/bin/Release/net10.0-windows10.0.17763.0/publish/win-x64](https://download-directory.github.io/?url=https://github.com/tsgsOFFICIAL/StreamDropCollector/tree/main/bin/Release/net10.0-windows10.0.17763.0/publish/win-x64)
2. Extract the ZIP
3. Run `StreamDropCollector.exe`
4. Log in to Twitch and Kick when the embedded browsers appear
5. Enjoy the free drops!

## Building from Source

```bash
git clone https://github.com/tsgsOFFICIAL/StreamDropCollector.git
cd StreamDropCollector
dotnet restore
dotnet publish -c Release -r win-x64 -p:SelfContained=true
```

Executable will be in `bin\Release\net10.0-windows10.0.17763.0\publish\win-x64\`

## Privacy & Safety

- No external APIs or third-party services used for authentication
- All logins happen inside secure embedded WebView2 browsers (same as Edge/Chrome)
- Your credentials **never** leaves your machine
- No data is sent anywhere except directly to Twitch.tv and Kick.com

## Important Notes

- This tool is for personal use only
- Respect Twitch and Kick's Terms of Service
- Use at your own risk - automated viewing may violate platform rules in some contexts
- Not affiliated with Twitch or Kick

## ❤️ Support the Project

This tool is (and always will be) **100% free**.  
If you're farming drops 24/7 and want to fuel more rage-fueled coding sessions, then hit that button and become a legend.

[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

## Contributing

Contributions are welcome! Feel free to:
- Open issues for bugs or suggestions
- Submit pull requests for improvements

## Star History

<a href="https://www.star-history.com/#tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=tsgsOFFICIAL/StreamDropCollector&type=date&legend=top-left" />
 </picture>
</a>

## License

[MIT License](LICENSE) - see the file for details.

---

**Made with rage, caffeine, and zero sleep by tsgsOFFICIAL**  
Happy farming! 🚀
