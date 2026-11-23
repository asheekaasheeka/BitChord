# BitChord (personal fork, Windows client)

> [!WARNING]
> This repository is for **personal use only**. It is not an official BitChord release, it is not supported by the upstream maintainers, and it is not meant for anyone else to use.
> Original project: [kushagrasinghx/BitChord](https://github.com/kushagrasinghx/BitChord). Report bugs in this fork here, not upstream.

This fork adds a **Windows client** (a WebView2 shell in `web/windows/`) and several features to the web player (`web/app/`). The Android, TV and backend code is the same as upstream `main`.

BitChord is not affiliated with, endorsed by, or connected to YouTube, Google, Spotify or Discord.

## Why this is not sent upstream

- The maintainers removed the desktop app (`e4dd63d8 chore: remove the desktop app now that the web player replaces it`). A native Windows client goes against that direction.
- The changes are large and mixed (about 7 features across 75 files) and have no issue. `CONTRIBUTING.md` asks for an issue before big features, and one change per PR.
- It adds a new stack (C#/.NET 8) and new Microsoft dependencies (see [Dependencies and licensing](#dependencies-and-licensing)).

To upstream any of it later, split it into small PRs first, starting with the web features (EQ, loudness, sleep timer). Open an issue before proposing the Windows client.

## Added features

### Windows client (`web/windows/`)
- One `BitChord.exe` (WinForms + WebView2). No Developer mode or "Load unpacked".
- The web player is bundled and served locally at `https://app.bitchord.example/`. No BitChord website is loaded from the internet.
- The BitChord extension (`webview2` build) installs automatically and trusts only that app origin.
- Tray: closing the window hides it to the tray, single instance, Show / Sign out / Quit.
- "Start with Windows" option (per-user entry in `HKCU\...\Run`, starts hidden in the tray).
- Fullscreen with F11 and with the player's fullscreen button.
- Dark title bar; window size and position are remembered.

### Web player features (active in the Windows client)
- **Equalizer**: 7 bands, presets and preamp, in Settings > Equalizer.
- **Normalize Volume**: on by default, only lowers the volume, based on YouTube's `loudness_db`.
- **Spatial Audio**.
- **Sleep Timer**: moon icon in the player bar, 30-second fade, "End of Track" option.
- **Local music** (Windows client only): pick folders, tags read through the Windows property system, album art from ID3/FLAC/MP4 tags or `cover.jpg`/`folder.jpg`, format info (bitrate, sample rate, bit depth).
- **Karaoke / vocal remover** (Windows client only): the UVR MDX-Net model (`UVR_MDXNET_KARA_2.onnx`) runs on ONNX Runtime DirectML (GPU, CPU fallback). The model is downloaded once after you agree, and checked against its SHA-256.

### Known limits
- Not fully tested: Google sign-in inside WebView2 (Google may block it with "This browser or app may not be secure"), parts of the Discord flow, and some tray menu actions.
- Karaoke: a song resumed from the middle must be replayed from the start; songs longer than 10 minutes are refused; stems are not cached; it is off while AutoMix is on.
- An app update that changes the extension files likely clears `chrome.storage` (reconnect Discord afterwards).
- No media keys/SMTC, installer or auto-update.
- Listen Together is removed from the web player and the Windows client (no party server, no "listening now" presence ping). The Android app and `backend/` still have it, unchanged from upstream.
- Stats for nerds values (Web Audio format, output sample rate) are app telemetry, not proof of hardware or DAC behaviour.

## Security risks

Read this before signing in.

1. **Your Google session is stored on disk.** The login cookie (full access to the Google account, reusable without 2FA) lives in the WebView2 profile at `%LOCALAPPDATA%\BitChord\WebView2`. Windows encrypts it per user (DPAPI), so any program running as your Windows user can read it. Use a **throwaway Google account**, not your main one.
2. **Other tokens.** The Discord token and Spotify data are kept in the extension's storage in the same profile. Treat that folder like a password: do not back it up to the cloud, share it, or zip it for a bug report.
3. **Sign out from the tray** deletes cookies, browsing data and the extension's storage. When you stop using the app, also remove the device at myaccount.google.com > Security > Your devices.
4. **The exe is unsigned.** SmartScreen will warn about it. Only run builds made from your own source. Do not run a zip from someone else.
5. **The karaoke model download** comes from `github.com/TRvlvr/model_repo` (a third party). The file is used only when its SHA-256 matches, but the model's licence is unclear.
6. **Navigation is restricted.** Only these hosts open inside the app: the local app, `accounts.google.com`, `accounts.youtube.com`, `music.youtube.com`, `www.youtube.com`, `accounts.spotify.com`, `open.spotify.com`, `discord.com`. Other links open in your normal browser. Other schemes (`file:`, `javascript:`, `data:`) are blocked.
7. **The native bridge** (karaoke, local music) accepts messages only from the app origin. Google/YouTube pages in the same window cannot call it. Local files are served by ID, not by path, and path traversal is covered by tests.
8. **Logs** record only the host and the exception type name, never full URLs, cookies or tokens. Still check a log before sharing it: search for `cookie`, `SAPISID`, `token`, `Authorization`.
9. **Never commit** `local.properties`, `keystore.properties`, `*.jks`, `*.keystore`, or any `.env` file with server URLs. Run `git status` before `git add`.

## Stored data

Everything is under `%LOCALAPPDATA%\BitChord\`:

| Path | Contents |
|------|----------|
| `WebView2\` | Browser profile: login cookies, extension storage, cache |
| `window.json` | Window size and position |
| `local-library.json` | Local music folders and scanned tags |
| `local-art\` | Album art cache (JPEG) |
| `models\` | Karaoke model (about 53 MB) |

To uninstall: turn off "Start with Windows" first (or delete the `BitChord` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), then delete the app folder and the folder above.

## Build

Needs Node 22 (corepack), the .NET 8 SDK, and Windows 10/11 with the WebView2 Runtime.

```powershell
cd web
corepack pnpm install
corepack pnpm run build:windows
```

The output is in `web/windows/dist/BitChord/` (`BitChord.exe` + `extension\` + `web\`). Use `windows/build.ps1 -Zip` to also make a zip.

The exe is self-contained (the .NET runtime is inside it), so the target PC needs only the WebView2 Runtime. Each build also writes `README.txt` (setup steps for the person you give it to, plus the source URL and commit) and `LICENSE.txt` next to the exe, as the GPLv3 requires when you share a copy.

Tests:

```powershell
cd web
corepack pnpm test
dotnet test windows/BitChord.Windows.Tests
```

The Android app builds as upstream: `.\gradlew.bat assembleDevDebug`. Use the `dev` flavour so it does not touch the `prod` app's data.

## Syncing with upstream

```powershell
git fetch upstream
git merge upstream/main
```

This README differs from upstream on purpose, so `README.md` will often conflict on merge. Keep this version.

## Dependencies and licensing

The code in this repository is licensed under the **GPLv3**, as upstream. See [LICENSE](LICENSE). Copyright of the original code belongs to the BitChord contributors.

New dependencies in the Windows client:

| Package | Licence | Notes |
|---------|---------|-------|
| `Microsoft.Web.WebView2` | Microsoft licence (redistributable) | `WebView2Loader.dll` is bundled into the exe |
| `Microsoft.ML.OnnxRuntime.DirectML` | MIT (ORT); DirectML: Microsoft redistribution licence | Used by karaoke |
| UVR MDX-Net model | Unclear | Not bundled; downloaded with the user's consent |

Personal use carries no obligations. If you **share** the exe or zip with anyone, the GPLv3 requires you to provide the source under the same licence. Whether the Microsoft components above are compatible with the GPLv3 has not been checked legally. That is another reason not to share this build.

## Disclaimer

BitChord does not host or store copyrighted music. You alone are responsible for making sure your use complies with your local copyright law and the Terms of Service of YouTube, Spotify and Discord.
