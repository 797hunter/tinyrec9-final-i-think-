# TinyRec 9

A macro recorder for Windows, made for games (Roblox and the like). Record your mouse and keyboard, play it back, chain recordings into sequences that wait for pictures on screen, and auto click while it all runs.

C# / .NET 8, WPF. Windows 10 or 11, 64-bit.

## Download and run

1. Go to **Releases** (right side of this page) and download `TinyRec9.exe`.
2. Put it in a folder of its own and double-click it.

That's all: the exe is the whole app. No install, no .NET needed, no build step.

- Windows may show a blue "Windows protected your PC" box the first time, because the exe isn't signed. Click **More info**, then **Run anyway**.
- Your recordings, images and settings are saved in a `data` folder next to the exe. Keep the two together.

## Hotkeys

| Key | Does |
| --- | --- |
| F8 | start / stop recording |
| F9 | play / stop |
| F11 | pause / continue |
| F12 | stop everything |
| F6 | auto clicker on / off |
| F7 | draw mode |
| F10 | full window / mini bar |

All of them can be changed in Settings.

## What it does

- **Record & Play**: record, play once / N times / forever, change speed while it plays. Record or play back keyboard only, mouse only, or your own mix.
- **Sequences**: steps that run top to bottom. Wait for a picture, play a recording, click spots, press keys, pause, repeat, jump, end early, bring a window to the front, alert. Pictures can be watched for the whole run ("always-watch") to click pop-ups away or restart.
- **Auto Clicker**: at the pointer, on spots, or wherever a picture is; bursts, random variation, hold, press a key instead.
- **Editor**: timeline and table view of a recording; nudge, scale, insert clicks and image steps.
- **Images**: snip part of the screen for image steps.
- **Draw**: boxes, arrows and notes on a see-through layer.

`README.txt` has the full list, including everything new in each version.

## Build it yourself

Only needed if you want to change the code.

1. Download the source (green **Code** button > Download ZIP) and unzip it.
2. Double-click `build.bat`. The first time, it installs the .NET 8 SDK into your user folder (no admin needed) and downloads OpenCV, so it needs internet and a few minutes.
3. The app appears at `dist\TinyRec9.exe`.

`test_engine.bat` runs the engine's 196 checks.

| Folder | What is in it |
| --- | --- |
| `App` | the Windows app (WPF) |
| `Core` | recording format, player, sequences, click engine |
| `Core.Tests` | the engine checks |
