<p align="center">
  <img src=".github/readme/banner.gif" alt="DeskPicker: Accounts, side by side" width="100%">
</p>

<p align="center">
  <img alt="made with: C#" src="https://img.shields.io/badge/made%20with-C%23-7ee2a8?style=flat-square&logo=dotnet&logoColor=white">
  <img alt="version: 0.1.0" src="https://img.shields.io/badge/version-0.1.0-4cc36a?style=flat-square">
  <img alt="platform: Windows" src="https://img.shields.io/badge/platform-Windows-2a3441?style=flat-square">
  <img alt=".NET: 10" src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square">
  <img alt="passwords seen: 0" src="https://img.shields.io/badge/passwords%20seen-0-3fb950?style=flat-square">
</p>

<p align="center">
  <a href="#what-it-does">What it does</a> ·
  <a href="#see-it-in-action">See it in action</a> ·
  <a href="#getting-started">Getting started</a> ·
  <a href="#build">Build</a> ·
  <a href="#roadmap">Roadmap</a>
</p>

<table>
<tr>
<td width="52%"><img src=".github/readme/hero.gif" alt="DeskPicker in action" width="100%"></td>
<td>

### Accounts, side by side

Run several copies of GitHub Desktop side by side, each signed in to a different GitHub account, such as work and personal.

GitHub Desktop only keeps one GitHub.com account at a time. DeskPicker gives each profile its own data folder (Electron's `--user-data-dir`), so each copy has its own sign-in and repository list. You always sign in through Desktop's own browser flow. DeskPicker never sees a password or token.

</td>
</tr>
</table>

> [!NOTE]
> Windows is where DeskPicker is tested. The macOS and Linux builds run but are untested; see [Platforms](#platforms).

## What it does

- **Profiles.** Each profile is a Desktop data folder. "Main" is Desktop's normal folder. Profiles are kept in `profiles.json` next to the app. Removing a profile never deletes its folder.
- **Sign-in hand-off.** The browser always sends Desktop's `x-github-desktop-auth://` sign-in link to the main copy. While a profile is signing in, DeskPicker spots the short-lived `GitHubDesktop.exe --protocol-launcher <link>` process and passes the same link on to that profile. It tells which profile is waiting from that profile's own Desktop log. The link is never saved. (Windows only for now.)
- **Commit identity per profile.** Each profile can have its own commit name and email. DeskPicker writes `~/.deskpicker/<folder>.gitconfig`, which includes your real `~/.gitconfig` and then sets the profile's identity. It starts that copy with `GIT_CONFIG_GLOBAL` pointing at the file. Your global git config and command-line git are left alone, and Desktop's "Configure Git" step writes to the profile's file instead.
- **Signed-in badge.** On Windows, DeskPicker checks Credential Manager for a `GitHub - https://api.github.com/<login>` entry. It reads the entry names only.
- **Shortcuts.** A shortcut runs `DeskPicker --open "<name>"`, so the profile's commit identity comes along.

## See it in action

Every clip is the real project, recorded with Readmotion. Nothing is mocked up.

<table>
<tr>
<td align="center"><img src=".github/readme/clip_profiles.gif" width="400" alt="Profiles: who each account signs in as, and commits as"><br><b>Profiles</b>: who each account signs in as, and commits as</td>
<td align="center"><img src=".github/readme/clip_new.gif" width="400" alt="New: a new profile gets its own data folder, named as you type"><br><b>New</b>: a new profile gets its own data folder, named as you type</td>
</tr>
<tr>
<td align="center"><img src=".github/readme/clip_identity.gif" width="400" alt="Identity: each profile commits with its own name and email"><br><b>Identity</b>: each profile commits with its own name and email</td>
<td align="center"><img src=".github/readme/clip_open.gif" width="400" alt="Open: each profile opens its own GitHub Desktop, next to the others"><br><b>Open</b>: each profile opens its own GitHub Desktop, next to the others</td>
</tr>
</table>

## Getting started

1. Build it (see [Build](#build)) and run `DeskPicker.exe`. The Main profile is GitHub Desktop's normal folder.
2. Click **New profile**, name it, and optionally give it a GitHub login and a commit name and email.
3. Click **Open**, then sign in from that copy's window as usual.

## Build

Needs the [.NET 10 SDK](https://dot.net).

- Windows: `build.cmd`, which builds `DeskPicker.exe` here.
- macOS / Linux: `./build.sh`, which builds into `dist/<target>/` (a `.app` on macOS).

## Platforms

| | Windows | macOS | Linux |
|---|---|---|---|
| Profiles, launch, running status | yes | untested | untested (shiftkey/desktop fork) |
| Commit identity | yes | untested (`open --env`) | untested |
| Sign-in hand-off | yes | not yet: sign in while other copies are closed | not yet |
| Signed-in badge | yes | no | no |

## Development

```
assets/   fonts: Atkinson Hyperlegible (SIL OFL)
src/      the app: Avalonia on .NET 10
tools/    the icon maker, and readmotion/: the config, demo profiles and driver that record this README's media
```

### Recording the README media

The GIFs above are made with Readmotion from `tools/readmotion/readmotion.json`. It runs a copy of the app from `tools/readmotion/.work/demo/` with the made-up profiles in `demo_profiles.json`, so no real account shows, and `driver.py` moves the mouse. Build that copy first, since building is slower than the recording waits:

```
python tools/readmotion/demo.py --build
```

Then open the config in the Readmotion app and click **Record all**, then **Create README**, or from a terminal. Keep the screen unlocked and the window uncovered while it records:

```
readmotion-cli capture tools/readmotion/readmotion.json
readmotion-cli build tools/readmotion/readmotion.json
```

## Roadmap

- [x] Profiles, launch and running status on Windows
- [x] Commit identity per profile
- [x] Sign-in hand-off on Windows
- [ ] Test on macOS and Linux
- [ ] Sign-in hand-off on macOS and Linux
- [ ] Signed-in badge on macOS and Linux

## License

Fonts: AtkinsonHyperlegibleMono, AtkinsonHyperlegibleNext (each under its own license, in the repo).
