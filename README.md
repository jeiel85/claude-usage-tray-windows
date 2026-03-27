# Claude Usage Tray (Windows)

A Windows system tray application that monitors your Claude AI usage in real-time.

> **Inspired by [claude-usage-mini](https://github.com/jeremy-prt/claude-usage-mini) by [@jeremy-prt](https://github.com/jeremy-prt)**
> The original project is a beautifully crafted macOS menu bar app built in Swift/SwiftUI.
> This project is a Windows port, reimagined as a WPF application.
> Full credit to Jeremy Prat and the original [claude-usage-bar](https://github.com/Krystian-key/claude-usage-bar) by Krystian for the concept and inspiration.

---

## Features

- **System Tray Icon** — Shows real-time usage level with color-coded indicator (purple → amber → red)
- **5-Hour & 7-Day API Quota** — Live progress bars from Anthropic's OAuth usage API
- **Today's Token Stats** — Input, output, cache read, and cache write tokens aggregated from local session files
- **Rate Limit Detection** — Warns you when a rate limit has been hit, with reset time
- **Auto-Refresh** — Polls every 30 seconds
- **Dark UI** — Modern dark theme popup with rounded corners and smooth animations
- **No login required** — Reuses the OAuth token already stored by Claude Code

## Screenshots

> *Coming soon*

## Requirements

- Windows 10 or later
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Claude Code](https://claude.ai/code) installed and logged in
  (The app reads credentials from `~/.claude/.credentials.json`)

## Getting Started

### Run from source

```bash
git clone https://github.com/YOUR_USERNAME/claude-usage-tray-windows
cd claude-usage-tray-windows/ClaudeUsageTray
dotnet run
```

### Build release

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## How It Works

### Authentication

Rather than implementing a new OAuth flow, this app reuses the access token that Claude Code already stores at:

```
%USERPROFILE%\.claude\.credentials.json
```

This is the same token used by the original macOS app.

### API Usage

Calls `https://api.anthropic.com/api/oauth/usage` with a Bearer token to retrieve:
- 5-hour rolling window usage and quota
- 7-day rolling window usage and quota

### Local Session Data

Scans `%USERPROFILE%\.claude\projects\**\*.jsonl` to aggregate today's token usage directly from Claude Code session files — no extra API calls needed.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | WPF (.NET 9) |
| System Tray | [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| HTTP | System.Net.Http |
| JSON | System.Text.Json |

## Project Structure

```
ClaudeUsageTray/
├── Models/
│   ├── Credentials.cs      # OAuth credentials model
│   └── UsageData.cs        # API response + session stats models
├── Services/
│   ├── CredentialService.cs  # Reads ~/.claude/.credentials.json
│   ├── UsageApiService.cs    # Calls Anthropic usage API
│   └── SessionMonitor.cs     # Parses local .jsonl session files
├── ViewModels/
│   └── MainViewModel.cs      # Data bindings + refresh logic
├── Views/
│   └── UsagePopup.xaml       # Dark-themed popup UI
└── App.xaml.cs               # Tray icon setup + app lifecycle
```

## Differences from the macOS Original

| Feature | macOS (claude-usage-mini) | Windows (this project) |
|---------|--------------------------|----------------------|
| Language | Swift 6.2 + SwiftUI | C# 13 + WPF |
| Platform | macOS 26+ | Windows 10+ |
| UI location | Menu bar | System tray |
| Auth | Full OAuth PKCE flow | Reuses Claude Code token |
| Icon style | Animated bars in menu bar | Color-coded tray icon |

## Contributing

Pull requests are welcome! Some ideas for improvement:

- [ ] Token refresh when access token expires
- [ ] Configurable refresh interval (settings panel)
- [ ] Startup with Windows option
- [ ] Toast notifications when approaching rate limit
- [ ] Per-model usage breakdown in the popup

## License

MIT License

---

*If you find this useful, please also give a ⭐ to the original [claude-usage-mini](https://github.com/jeremy-prt/claude-usage-mini) project that inspired this work.*
