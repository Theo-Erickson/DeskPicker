# DeskPicker

Run several copies of GitHub Desktop side by side, each signed in to a different GitHub account, such as work and personal.

GitHub Desktop only keeps one GitHub.com account at a time. DeskPicker gives each profile its own data folder (Electron's `--user-data-dir`), so each copy has its own sign-in and repository list. You always sign in through Desktop's own browser flow. DeskPicker never sees a password or token.

## What it does

- **Profiles.** Each profile is a Desktop data folder. "Main" is Desktop's normal folder. Profiles are kept in `profiles.json` next to the app. Removing a profile never deletes its folder.
- **Sign-in hand-off.** The browser always sends Desktop's `x-github-desktop-auth://` sign-in link to the main copy. While a profile is signing in, DeskPicker spots the short-lived `GitHubDesktop.exe --protocol-launcher <link>` process and passes the same link on to that profile. It tells which profile is waiting from that profile's own Desktop log. The link is never saved. (Windows only for now.)
- **Commit identity per profile.** Each profile can have its own commit name and email. DeskPicker writes `~/.deskpicker/<folder>.gitconfig`, which includes your real `~/.gitconfig` and then sets the profile's identity. It starts that copy with `GIT_CONFIG_GLOBAL` pointing at the file. Your global git config and command-line git are left alone, and Desktop's "Configure Git" step writes to the profile's file instead.
- **Signed-in badge.** On Windows, DeskPicker checks Credential Manager for a `GitHub - https://api.github.com/<login>` entry. It reads the entry names only.
- **Shortcuts.** A shortcut runs `DeskPicker --open "<name>"`, so the profile's commit identity comes along.

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
