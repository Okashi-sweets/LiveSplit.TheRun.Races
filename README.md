# LiveSplit.TheRun.Races

An unofficial LiveSplit race provider for public races on
[therun.gg](https://therun.gg/races). It follows LiveSplit's existing race
provider menu style and opens race rooms in a LiveSplit-owned WebView2 window.

This project is not an official therun.gg or LiveSplit component.

[日本語版 README](README.ja.md)

## Features

- Adds a **therun.gg Races** submenu to LiveSplit's context menu.
- Lists visible races that are accepting participants (`pending`).
- Lists `starting` and `progress` races separately as races in progress.
- Opens races in progress without changing the LiveSplit timer offset or
  starting a race countdown watcher.
- Rechecks race status before opening a room.
- Opens the race room inside LiveSplit and keeps its login session in a
  dedicated WebView2 profile.
- Attempts to unjoin a pending race when either race-room window is closed.
  Closing after the countdown starts does not forfeit the race.
- Provides separate official-page and lightweight-HTML component DLLs. Choose
  the DLL for the room you want; there is no in-component display switch.
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

## Official and lightweight components

`LiveSplit.TheRun.Races.dll` opens the official therun.gg race page. This is
the recommended component for normal use because it provides the complete
race experience and remains compatible with features added by therun.gg.

`LiveSplit.TheRun.Races.Lite.dll` always opens a small embedded HTML room. Use
this separate component only when the official race page does not load or work
correctly in LiveSplit. A saved therun.gg Upload Key is required before the
Lite component can open a room.

The lightweight room:

- displays the race status, countdown, participants, current split, progress,
  and times;
- supports Join, Ready, Unready, and Unjoin before the race starts;
- reuses the therun.gg login session stored in the component's WebView2
  profile without exposing the session token to its HTML; and

It does not provide Finish or Forfeit actions, and it does not replace team
management, chat, moderation, detailed graphs, stream views, or other advanced
features. It displays no action buttons when an in-progress room is opened.
Use the official page for those features.

### Lite component maintenance status

Use the Lite component only when the standard official-page component does not
work in your environment. The Lite component is provided as-is and is now
feature-frozen. No further development, compatibility updates, or user support
will be provided for it.

## Requirements

- LiveSplit 1.8.37 or a compatible version

## Downloads

Choose one of the following releases. Do not install both component variants
at the same time.

- **Standard version (recommended):** [Download from the v0.4.0 release](https://github.com/Okashi-sweets/LiveSplit.TheRun.Races/releases/tag/v0.4.0)
  Download `LiveSplit.TheRun.Races.dll`. This version opens the official
  therun.gg race page.
- **Lite version (unsupported fallback):** [Download from the Lite v0.4.0 release](https://github.com/Okashi-sweets/LiveSplit.TheRun.Races/releases/tag/lite_v0.4.0)
  Download `LiveSplit.TheRun.Races.Lite.dll`. Use it only when the standard
  version does not work in your environment.

## Installation

1. Download the DLL for the selected version from the corresponding release
   linked above.
2. Copy the selected DLL to LiveSplit's `Components` directory. Install only
   the variant you intend to use.
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

dotnet build src/LiveSplit.TheRun.Races.Lite/LiveSplit.TheRun.Races.Lite.csproj `
  -p:LsSrcPath=C:/path/to/LiveSplit/src
```

Or build against the DLLs from a LiveSplit release:

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit

dotnet build src/LiveSplit.TheRun.Races.Lite/LiveSplit.TheRun.Races.Lite.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit
```

## License

This project is released under the [MIT License](LICENSE). Portions of the
timer-upload implementation are adapted from the official MIT-licensed
[`LiveSplit.TheRun`](https://github.com/therungg/LiveSplit.TheRun) component.
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution and
third-party license information.
