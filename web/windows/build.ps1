# Builds the portable Windows client: extension (webview2), web player (web/app), tests, single-file
# publish, then puts BitChord.exe + extension\ + web\ in web/windows/dist/BitChord/, ready to run.
# -Zip also packs that folder to web/windows/dist/BitChord-windows-x64.zip (CI).
# Needs Node 22 with corepack and the .NET 8 SDK. PowerShell 5.1 compatible.
param([switch]$Zip)
$ErrorActionPreference = 'Stop'

$windows = $PSScriptRoot
$web = Split-Path $windows -Parent
$work = Join-Path $windows 'obj'
$publish = Join-Path $work 'publish'
$dist = Join-Path $windows 'dist'
$stage = Join-Path $dist 'BitChord'
$zipPath = Join-Path $dist 'BitChord-windows-x64.zip'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $dotnet = $dotnet.Source } else { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw "dotnet not found on PATH or at $dotnet" }

function Invoke-Native([string]$what, [scriptblock]$cmd) {
    Write-Host "==> $what"
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# vite.config.ts refuses a production build without the party server. Vite loads these files itself;
# only check that the key is there, never read or print the value.
$app = Join-Path $web 'app'
$partyFiles = '.env', '.env.local', '.env.production', '.env.production.local' |
    ForEach-Object { Join-Path $app $_ } | Where-Object { Test-Path $_ }
$partyPlaceholder = $false
if (-not $env:VITE_PARTY_SERVER_URL -and
    -not ($partyFiles | Where-Object { Select-String -Quiet -Path $_ -Pattern '^\s*VITE_PARTY_SERVER_URL\s*=\s*\S' })) {
    # shortcut: Listen Together is out of scope for the Windows client for now, so a placeholder lets the
    # build run and the feature simply fails to connect. Set the real URL (and allow the app origin on the
    # party server, JAM_ALLOWED_ORIGINS) when Listen Together should work.
    Write-Warning 'VITE_PARTY_SERVER_URL is not set: using a placeholder, Listen Together will not work.'
    $env:VITE_PARTY_SERVER_URL = 'https://party.example.com'
    $partyPlaceholder = $true
}

# corepack must run from web/ (or below): the repo root has no matching packageManager.
Push-Location $web
try {
    if (-not (Test-Path (Join-Path $web 'node_modules'))) {
        Invoke-Native 'pnpm install' { corepack pnpm install --frozen-lockfile }
    }
    # Release zip: trust only the bundled player's origin (the shell's virtual host, see NavigationPolicy.cs).
    # Not http://localhost:5173 (dev only), not bitchord.in (the WordPress landing site), and not
    # bitchord-8bw.pages.dev: the app never loads the player from a website.
    # The manifest key stays, so the extension id stays pinned. A plain build:webview2 keeps localhost for dev.
    $env:BITCHORD_SITE_ORIGINS = 'https://app.bitchord.example'
    try {
        Invoke-Native 'extension build:webview2' { corepack pnpm --dir extension run build:webview2 }
    } finally {
        Remove-Item Env:BITCHORD_SITE_ORIGINS -ErrorAction SilentlyContinue
    }
    Invoke-Native 'app build' { corepack pnpm --dir app build }
} finally {
    if ($partyPlaceholder) { Remove-Item Env:VITE_PARTY_SERVER_URL -ErrorAction SilentlyContinue }
    Pop-Location
}

# A stray origin in the extension would let that site drive it, so check what the build really trusts.
$manifest = Get-Content -Raw (Join-Path $web 'extension\.output\webview2-mv3\manifest.json') | ConvertFrom-Json
$connectable = @($manifest.externally_connectable.matches)
if ($connectable.Count -ne 1 -or $connectable[0] -ne 'https://app.bitchord.example/*') {
    throw "extension externally_connectable.matches is '$($connectable -join ', ')', expected only https://app.bitchord.example/*"
}

Invoke-Native 'dotnet test' { & $dotnet test (Join-Path $windows 'BitChord.Windows.Tests') }

if (Get-Process BitChord -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$stage\*" }) {
    throw "BitChord is running from $stage. Quit it from the tray, then run build.ps1 again."
}
foreach ($dir in $publish, $stage) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
Invoke-Native 'dotnet publish' {
    # Self-contained: the .NET runtime ships inside the exe, so the user installs nothing (WebView2 aside).
    & $dotnet publish (Join-Path $windows 'BitChord.Windows') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $publish
}

# The csproj copies the extension into the publish dir; without it the app shows an error popup.
if (-not (Test-Path (Join-Path $publish 'extension\manifest.json'))) {
    throw "extension\manifest.json missing from $publish (did build:webview2 produce web/extension/.output/webview2-mv3?)"
}
if (-not (Test-Path (Join-Path $publish 'web\index.html'))) {
    throw "web\index.html missing from $publish (did 'pnpm --dir app build' produce web/app/dist?)"
}
if (-not (Test-Path (Join-Path $publish 'BitChord.exe'))) { throw "BitChord.exe missing from $publish" }

# Ship only the exe, the extension and the web player: leave out BitChord.pdb and the WebView2 *.xml docs.
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $publish 'BitChord.exe') $stage
Copy-Item -Recurse (Join-Path $publish 'extension') $stage
Copy-Item -Recurse (Join-Path $publish 'web') $stage

# GPLv3 section 6: every copy handed out says where its exact source is. The URL comes from the origin
# remote with any credentials stripped (an https://user:token@ remote must never end up in the zip).
$repoRoot = Split-Path $web -Parent
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stage 'LICENSE.txt')
$sourceUrl = $null
$commit = $null
$git = Get-Command git -ErrorAction SilentlyContinue
# try: under PowerShell 5.1 with Stop, any git stderr line would end the build.
if ($git) { try {
    $remote = & git -C $repoRoot remote get-url origin 2>$null
    if ($LASTEXITCODE -eq 0 -and $remote) {
        $remote = $remote.Trim() -replace '^git@github\.com:', 'https://github.com/' -replace '\.git$', ''
        $u = $null
        if ([Uri]::TryCreate($remote, [UriKind]::Absolute, [ref]$u) -and $u.Scheme -eq 'https') {
            $sourceUrl = 'https://' + $u.Host + $u.AbsolutePath.TrimEnd('/')
        }
    }
    $commit = & git -C $repoRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { $commit = $null }
    elseif (& git -C $repoRoot status --porcelain --untracked-files=no 2>$null) { $commit = "$commit (plus uncommitted changes)" }
} catch { $commit = $null } }
if (-not $sourceUrl) { Write-Warning 'No https origin remote: README.txt will not name the source repository.' }
$readme = @"
BitChord for Windows (personal build)
=====================================

How to start
1. Extract this whole zip into a folder that stays, for example C:\Apps\BitChord\
   (not inside the zip, and not a folder you clean up later).
2. Double-click BitChord.exe.
3. Windows may show "Windows protected your PC" because the app is not signed.
   Click "More info", then "Run anyway". This is needed only the first time.
4. If it says the WebView2 Runtime is missing (some Windows 10 PCs), install
   "Evergreen Bootstrapper" from https://developer.microsoft.com/microsoft-edge/webview2/
   and start BitChord again. Windows 11 already has it.
5. Sign in with your own account. Closing the window keeps the music playing in
   the tray (near the clock). Right-click the tray icon for Sign out and Quit.

Nothing else needs installing: .NET is inside BitChord.exe.

Your data stays on this PC, in %LOCALAPPDATA%\BitChord (sign-in, settings, cache).
Never copy that folder to anyone: it holds your login. To remove the app, use
"Sign out" in the tray, quit, then delete this folder and %LOCALAPPDATA%\BitChord.

BitChord is not affiliated with YouTube, Google, Spotify or Discord.

Licence
BitChord is free software under the GNU General Public License v3.0 (LICENSE.txt).
You may use, share and change it under that licence.
Source code: $(if ($sourceUrl) { $sourceUrl } else { 'ask the person who gave you this copy' })$(if ($commit) { "`r`nVersion: commit $commit" })
"@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding $true))
Write-Host "==> Ready to run: $(Join-Path $stage 'BitChord.exe')"

if (-not $Zip) { return }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Name entries by hand: under Windows PowerShell 5.1, CreateFromDirectory and Compress-Archive write '\' separators.
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -Recurse -File $stage | ForEach-Object {
        $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
}

$size = (Get-Item $zipPath).Length
Write-Host ("==> {0} ({1:N0} bytes, {2:N1} MB)" -f $zipPath, $size, ($size / 1MB))
