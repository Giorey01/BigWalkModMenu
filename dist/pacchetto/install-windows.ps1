# Big Walk Mod Menu - install on Windows 10 / 11.
# Usually a double-click on "install-windows.bat" is enough.
#
#   install-windows.ps1                         looks for Big Walk in the Steam libraries
#   install-windows.ps1 -Game "D:\Games\Big Walk"
#   options:  -Yes          answers yes to every question (and installs every mod)
#             -DryRun       shows what it would do, without touching anything
#             -AllMods      installs every mod without asking
#             -Mod a,b,c    installs only these (names: faces solo character camera
#                           performance world reshade voice extras replay)
#
# Run it again any time: it updates what is installed and adds what you pick.

param(
    [string]$Game = "",
    [switch]$Yes,
    [switch]$DryRun,
    [switch]$AllMods,
    [string[]]$Mod = @()
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip"
$BepInExSha256 = "2a7cbf74d26abe4765c3e662db1721b923bac39849ebfef2ca5dc7de7e2d9b7f"
$GameName = "Big Walk"
$GameExe = "Big Walk.exe"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Stop-Install([string]$Message) {
    Write-Host ""
    Write-Host ("ERROR: " + $Message) -ForegroundColor Red
    exit 1
}

function Ask([string]$Question) {
    if ($Yes) { return $true }
    $answer = Read-Host ($Question + " [y/N]")
    return ($answer -match '^(y|yes)$')
}

function Join([string]$A, [string]$B) { return [System.IO.Path]::Combine($A, $B) }

if (-not (Test-Path -LiteralPath (Join $Here "BepInEx\plugins\BigWalkModMenu"))) {
    Stop-Install "The package's BepInEx folder is missing next to this script. Extract the whole zip and try again."
}

# ---- find the game ----

function Get-SteamRoots {
    $roots = @()
    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $props = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($props.SteamPath) { $roots += [string]$props.SteamPath }
            if ($props.InstallPath) { $roots += [string]$props.InstallPath }
        } catch { }
    }
    $roots += "C:\Program Files (x86)\Steam"
    $roots += "C:\Program Files\Steam"
    return $roots | Where-Object { $_ } | ForEach-Object { $_.Replace("/", "\").TrimEnd("\") } | Select-Object -Unique
}

function Find-Game {
    foreach ($root in Get-SteamRoots) {
        $apps = Join $root "steamapps"
        if (-not (Test-Path -LiteralPath $apps)) { continue }
        $candidate = Join $apps ("common\" + $GameName)
        if (Test-Path -LiteralPath (Join $candidate $GameExe)) { return $candidate }

        # Other libraries (other drives) are listed here.
        $vdf = Join $apps "libraryfolders.vdf"
        if (-not (Test-Path -LiteralPath $vdf)) { continue }
        foreach ($line in Get-Content -LiteralPath $vdf) {
            if ($line -match '^\s*"path"\s*"(.*)"\s*$') {
                $lib = $Matches[1].Replace("\\", "\")
                $candidate = Join $lib ("steamapps\common\" + $GameName)
                if (Test-Path -LiteralPath (Join $candidate $GameExe)) { return $candidate }
            }
        }
    }
    return $null
}

if (-not $Game) {
    $Game = Find-Game
    if (-not $Game) {
        Stop-Install ("Cannot find " + $GameName + " in the Steam libraries. Drag the game folder onto install-windows.bat, or: install-windows.ps1 -Game ""D:\path\" + $GameName + """")
    }
}
$Game = $Game.TrimEnd("\", "/")
if (-not (Test-Path -LiteralPath (Join $Game $GameExe))) {
    Stop-Install ("There is no """ + $GameExe + """ in """ + $Game + """: that is not the game folder.")
}
Write-Host ("Game found: " + $Game)

if (Get-Process -Name "Big Walk" -ErrorAction SilentlyContinue) {
    Stop-Install "Big Walk is running. Close it and run the installer again."
}

# ---- BepInEx (the mod loader) ----

$BepInExMarker = Join $Game "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
if (Test-Path -LiteralPath $BepInExMarker) {
    Write-Host "BepInEx is already installed."
} else {
    Write-Host ""
    Write-Host "The game does not have BepInEx, the program that loads mods."
    Write-Host "Downloading it from the official site (about 35 MB):"
    Write-Host ("  " + $BepInExUrl)
    if ($DryRun) {
        Write-Host ("[dry run] would download BepInEx and extract it into: " + $Game)
    } else {
        if (-not (Ask "Go ahead?")) { Stop-Install "Without BepInEx the mods cannot start. Installation cancelled." }
        $tmp = Join ([System.IO.Path]::GetTempPath()) ("bigwalk-bepinex-" + [System.Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $tmp | Out-Null
        try {
            $zip = Join $tmp "bepinex.zip"
            # A file already downloaded next to the script is fine too, if it is the right one.
            $local = Get-ChildItem -LiteralPath $Here -Filter "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($local) {
                Copy-Item -LiteralPath $local.FullName -Destination $zip
            } else {
                try {
                    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
                    Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
                } catch {
                    Stop-Install ("Download failed: " + $_.Exception.Message)
                }
            }
            $sum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($sum -ne $BepInExSha256) { Stop-Install "The downloaded file is not the expected one (different checksum). Not installing it." }
            Expand-Archive -LiteralPath $zip -DestinationPath $Game -Force
        } finally {
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not (Test-Path -LiteralPath $BepInExMarker)) { Stop-Install "BepInEx does not appear to be installed after extraction." }
        Write-Host "BepInEx installed."
    }
}

# ---- the mods ----
#
# Each mod is a tab of the menu and a file of its own. The base menu ("General") is always installed.

$Mods = @(
    @{ Id = "faces";       Name = "Faces";       Text = "a photo of a face on the characters' heads"; Files = @("FaceMaskMod\BigWalk.FaceMaskMod.dll", "FaceMaskMod\StbImageSharp.dll") },
    @{ Id = "solo";        Name = "Solo";        Text = "up to four characters controlled by one player"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Solo.dll") },
    @{ Id = "character";   Name = "Character";   Text = "hats, accessories, colours, proportions"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Character.dll") },
    @{ Id = "camera";      Name = "Camera";      Text = "selfie, third person, free camera, photos"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Camera.dll") },
    @{ Id = "performance"; Name = "Performance"; Text = "FPS counter and advanced graphics options"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Performance.dll") },
    @{ Id = "world";       Name = "World";       Text = "time of day, sky, fog, colour filters"; Files = @("BigWalkModMenu\BigWalk.ModMenu.World.dll") },
    @{ Id = "reshade";     Name = "Reshade";     Text = "bloom, film tones, sharpening"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Reshade.dll") },
    @{ Id = "voice";       Name = "Voice";       Text = "volume of each player's voice"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Voice.dll") },
    @{ Id = "extras";      Name = "Extras";      Text = "music, sounds, emotes, speech bubbles"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Extras.dll", "BigWalkModMenu\NVorbis.dll", "BigWalkModMenu\NLayer.dll") },
    @{ Id = "replay";      Name = "Replay";      Text = "record and replay scenes"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Replay.dll") }
)
$CoreFiles = @("BigWalkModMenu\BigWalk.ModMenu.dll", "BigWalkModMenu\StbImageSharp.dll", "BigWalkModMenu\StbImageWriteSharp.dll")

$Src = Join $Here "BepInEx\plugins"
$Dst = Join $Game "BepInEx\plugins"

function In-Package($m) { return (Test-Path -LiteralPath (Join $Src $m.Files[0])) }
function Is-Installed($m) { return (Test-Path -LiteralPath (Join $Dst $m.Files[0])) }

$Available = @($Mods | Where-Object { In-Package $_ })
$Selected = @()

if ($Mod.Count -gt 0) {
    $names = @($Mod | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    foreach ($name in $names) {
        $found = @($Available | Where-Object { $_.Id -eq $name })
        if ($found.Count -eq 0) {
            Stop-Install ("Unknown mod """ + $name + """ (or it is not in this package). Valid names: " + (($Available | ForEach-Object { $_.Id }) -join " "))
        }
        $Selected += $found[0].Id
    }
} elseif ($AllMods -or $Yes -or $DryRun) {
    $Selected = @($Available | ForEach-Object { $_.Id })
} else {
    Write-Host ""
    Write-Host "Which mods do you want? Each one is a tab of the menu. The base menu is always installed."
    Write-Host ""
    for ($i = 0; $i -lt $Available.Count; $i++) {
        $m = $Available[$i]
        $state = ""
        if (Is-Installed $m) { $state = "  [installed]" }
        Write-Host ("  {0,2}) {1,-12} {2}{3}" -f ($i + 1), $m.Name, $m.Text, $state)
    }
    Write-Host ""
    Write-Host "  Enter      all of them"
    Write-Host "  numbers    only those, separated by spaces (example: 1 3 8)"
    Write-Host "  m          only the ones you are missing"
    Write-Host ""
    Write-Host "The ones already installed are updated anyway, and none is removed."
    $choice = (Read-Host "Choice").Trim()
    if ($choice -eq "") {
        $Selected = @($Available | ForEach-Object { $_.Id })
    } elseif ($choice -match '^[mM]$') {
        $Selected = @($Available | Where-Object { -not (Is-Installed $_) } | ForEach-Object { $_.Id })
    } else {
        foreach ($token in ($choice -split '[\s,;]+' | Where-Object { $_ })) {
            $number = 0
            if (-not [int]::TryParse($token, [ref]$number)) { Stop-Install ("""" + $token + """ is not a number from the list.") }
            if ($number -lt 1 -or $number -gt $Available.Count) { Stop-Install ("There is no number " + $number + " in the list.") }
            $Selected += $Available[$number - 1].Id
        }
    }
}

# Mods already present are always updated: tabs of a different version do not work with the menu.
foreach ($m in $Available) {
    if ((Is-Installed $m) -and ($Selected -notcontains $m.Id)) { $Selected += $m.Id }
}

function Copy-ModFile([string]$Relative) {
    $from = Join $Src $Relative
    if (-not (Test-Path -LiteralPath $from)) { Stop-Install ("The package is missing the file " + $Relative + ". Extract the whole zip again.") }
    if ($DryRun) { Write-Host ("[dry run] would copy: " + $Relative); return }
    $to = Join $Dst $Relative
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    } catch {
        Stop-Install ("Copy failed: " + $_.Exception.Message + " (if the game is in Program Files, try again with ""Run as administrator"")")
    }
}

Write-Host ""
foreach ($f in $CoreFiles) { Copy-ModFile $f }
Write-Host "Base menu: ok"
$done = @()
foreach ($m in $Available) {
    if ($Selected -contains $m.Id) {
        foreach ($f in $m.Files) { Copy-ModFile $f }
        $done += $m.Name
    }
}
if ($done.Count -gt 0) { Write-Host ("Mods installed or updated: " + ($done -join " ")) }
if (-not $DryRun) {
    if (-not (Test-Path -LiteralPath (Join $Dst "BigWalkModMenu\BigWalk.ModMenu.dll"))) { Stop-Install "The mod does not appear to have been copied." }
    if (Test-Path -LiteralPath (Join $Dst "FaceMaskMod\BigWalk.FaceMaskMod.dll")) {
        New-Item -ItemType Directory -Path (Join $Dst "FaceMaskMod\faces") -Force -ErrorAction SilentlyContinue | Out-Null
    }
}
$missing = @($Available | Where-Object { ($Selected -notcontains $_.Id) -and -not (Is-Installed $_) } | ForEach-Object { $_.Name })
if ($missing.Count -gt 0) { Write-Host ("Not installed (run the installer again when you want them): " + ($missing -join " ")) }

if ((Test-Path -LiteralPath (Join $Dst "BigWalkSoloMod\BigWalkSoloMod.dll")) -and (Test-Path -LiteralPath (Join $Dst "BigWalkModMenu\BigWalk.ModMenu.Solo.dll"))) {
    Write-Host ""
    Write-Host "WARNING: the stand-alone ""Big Walk Solo Player"" mod is installed too. It does the same job as the" -ForegroundColor Yellow
    Write-Host "Solo tab: remove the folder BepInEx\plugins\BigWalkSoloMod, or the tab will stay off." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "DONE." -ForegroundColor Green
Write-Host ""
Write-Host "The first start after installing BepInEx takes a few minutes (it prepares the"
Write-Host "game files) and looks frozen: let it finish."
Write-Host ""
Write-Host "In game the menu opens with F8 or Insert."
if (Test-Path -LiteralPath (Join $Dst "FaceMaskMod\BigWalk.FaceMaskMod.dll")) {
    Write-Host ("Photos for the faces go in: " + (Join $Game "BepInEx\plugins\FaceMaskMod\faces"))
    Write-Host "(or pick another folder from the Faces tab)."
}
exit 0
