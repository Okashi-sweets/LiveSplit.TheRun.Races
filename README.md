# LiveSplit.TheRun.Races

An unofficial LiveSplit race provider for public races on
[therun.gg](https://therun.gg/races). It follows LiveSplit's existing race
provider menu style and opens race rooms in a LiveSplit-owned WebView2 window.

This project is not an official therun.gg or LiveSplit component.

[日本語版 README](README.ja.md)

## Features

- Adds a **therun.gg Races** submenu to LiveSplit's context menu.
- Lists only visible races that are still accepting participants (`pending`).
- Rechecks race status before opening a room.
- Opens the race room inside LiveSplit and keeps its login session in a
  dedicated WebView2 profile.
- Attempts to unjoin a pending race when either race-room window is closed.
  Closing after the countdown starts does not forfeit the race.
- Provides a lightweight local HTML room with participant progress and race
  actions. The official page is the default; enable the lightweight room in
  the race-provider settings when desired.
- Applies the configured countdown as a negative LiveSplit offset when a room
  is opened and restores the previous offset after leaving.
- Starts LiveSplit so its zero aligns with the race start time, without later
  timer corrections.
- Supports immediate, manually triggered, and scheduled race countdowns as
  long as therun.gg exposes the normal `starting` state and `startTime`.
- Optionally sends live timer state and uploads the LSS on completion/reset.
- Shares the upload key used by the official `LiveSplit.TheRun` component.
- Disables its own timer uploader while the official component is active in
  the current layout, preventing duplicate updates.

## Official and lightweight race rooms

The official therun.gg race page is used by default and is recommended for
normal use. It provides the complete race experience and remains compatible
with features added by therun.gg.

The optional lightweight room is a small HTML page embedded in this component.
Enable **Use lightweight HTML race room** in the **therun.gg Races** provider
settings only when the official race page does not load or work correctly in
LiveSplit. A saved therun.gg Upload Key is required to enable this option. The
choice is saved and applies the next time a room is opened.

The lightweight room:

- displays the race status, countdown, participants, current split, progress,
  and times;
- supports Join, Ready, Unready, and Unjoin before the race starts;
- reuses the therun.gg login session stored in the component's WebView2
  profile without exposing the session token to its HTML; and
- includes an **Official page** button for opening the complete room.

It does not provide Finish or Forfeit actions, and it does not replace team
management, chat, moderation, detailed graphs, stream views, or other advanced
features. Use the official page for those features.
Because the lightweight room relies on therun.gg API behavior, a future API
change may require a component update.

## Requirements

- LiveSplit 1.8.37 or a compatible version

## Installation

1. Download the release archive containing `LiveSplit.TheRun.Races.dll`,
   `LICENSE`, and `THIRD-PARTY-NOTICES.md` from the latest GitHub Release.
2. Copy it to LiveSplit's `Components` directory.
3. Restart LiveSplit.
4. Enable/configure **therun.gg Races** in LiveSplit's race-provider settings.

## Upload key and data handling

### Getting an upload key

1. Sign in to [therun.gg](https://therun.gg/).
2. Open [therun.gg/livesplit](https://therun.gg/livesplit), or open the
   **LiveSplit** page from your user menu.
3. Click the upload-key field to copy the key.
4. In LiveSplit, open the race-provider settings for **therun.gg Races**.
5. Paste the key into **therun.gg upload key**, then select **Save and test**.

Keep the upload key secret. Anyone with this key may be able to upload runs on
your behalf.

The upload key is stored in the same local file used by the official
therun.gg component:

```text
%LOCALAPPDATA%\Livesplit.TheRun\uploadkey.txt
```

The upload key must never be committed to this repository.

When enabled, timer state and split data are sent to therun.gg after timer
actions. The LSS is uploaded on completion and reset. Game and segment icons
are removed from uploaded LSS data; the layout path is omitted by default.

The embedded browser stores therun.gg/Twitch session data under:

```text
%LOCALAPPDATA%\LiveSplit\TheRunWebView2
```

## Debug log

Diagnostic events are written to:

```text
%LOCALAPPDATA%\LiveSplit\TheRunRaces\debug.log
```

The log is rotated to `debug.log.old` at approximately 2 MB. Upload keys and
cookies are not written to the log.

## Building

Build against a LiveSplit source checkout:

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsSrcPath=C:/path/to/LiveSplit/src
```

Or build against the DLLs from a LiveSplit release:

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit
```

## License

This project is released under the [MIT License](LICENSE). Portions of the
timer-upload implementation are adapted from the official MIT-licensed
[`LiveSplit.TheRun`](https://github.com/therungg/LiveSplit.TheRun) component.
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution and
third-party license information.

Release archives should contain this project's DLL, `LICENSE`, and
`THIRD-PARTY-NOTICES.md`. Do not repackage the WebView2 assemblies from the
NuGet build output; supported LiveSplit installations already provide them.
