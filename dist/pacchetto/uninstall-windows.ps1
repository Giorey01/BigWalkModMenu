# Big Walk Mod Menu - uninstall on Windows 10 / 11.
# Usually a double-click on "uninstall-windows.bat" is enough.
#
#   uninstall-windows.ps1                        looks for Big Walk in the Steam libraries
#   uninstall-windows.ps1 -Game "D:\Games\Big Walk"
#   options:  -Yes       answers yes (removes the mods, KEEPS your files)
#             -All       also removes your files: photos, faces, recordings, settings
#             -BepInEx   also removes BepInEx (and so every other installed mod)
#             -DryRun    shows what it would do, without touching anything

param(
    [string]$Game = "",
    [switch]$Yes,
    [switch]$All,
    [switch]$BepInEx,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$GameName = "Big Walk"
$GameExe = "Big Walk.exe"

function Stop-Uninstall([string]$Message) {
    Write-Host ""
    Write-Host ("ERROR: " + $Message) -ForegroundColor Red
    exit 1
}

# $WithYes: the answer used when -Yes is given
function Ask([string]$Question, [bool]$WithYes) {
    if ($Yes) { return $WithYes }
    $answer = Read-Host ($Question + " [y/N]")
    return ($answer -match '^(y|yes)$')
}

function Join([string]$A, [string]$B) { return [System.IO.Path]::Combine($A, $B) }

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
        Stop-Uninstall ("Cannot find " + $GameName + " in the Steam libraries. Drag the game folder onto uninstall-windows.bat.")
    }
}
# -Game "D:\Games\Big Walk\" : Windows reads the last \" as a quote, and the options that follow end up in the path.
if ($Game.Contains('"')) {
    Stop-Uninstall "The game folder was not understood. Write it without the final backslash: -Game ""D:\Games\Big Walk"""
}
$Game = $Game.TrimEnd("\", "/")
# Nothing is deleted outside a folder that really holds the game's executable.
if (-not (Test-Path -LiteralPath (Join $Game $GameExe))) {
    Stop-Uninstall ("There is no """ + $GameExe + """ in """ + $Game + """: that is not the game folder.")
}
Write-Host ("Game found: " + $Game)

if (Get-Process -Name "Big Walk" -ErrorAction SilentlyContinue) {
    Stop-Uninstall "Big Walk is running. Close it and run the uninstaller again."
}

$Plugins = Join $Game "BepInEx\plugins"
$Menu = Join $Plugins "BigWalkModMenu"
# Where the Faces mod lived up to 1.4.0: an install that was never started since may still have it.
$Faces = Join $Plugins "FaceMaskMod"

function Remove-Inside([string]$Path) {
    if (-not $Path.StartsWith($Game + [System.IO.Path]::DirectorySeparatorChar)) { Stop-Uninstall ("Refusing to delete outside the game folder: " + $Path) }
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if ($DryRun) {
        Write-Host ("[dry run] would delete: " + $Path.Substring($Game.Length + 1))
    } else {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

$hasMenu = Test-Path -LiteralPath $Menu
$hasFaces = Test-Path -LiteralPath $Faces
$removeAll = [bool]$All

if (-not $hasMenu -and -not $hasFaces) {
    Write-Host "Big Walk Mod Menu does not appear to be installed."
} else {
    Write-Host ""
    Write-Host "Removing Big Walk Mod Menu."
    if (-not (Ask "Go ahead?" $true)) { Stop-Uninstall "Uninstall cancelled." }

    if (-not $removeAll -and -not $Yes) {
        Write-Host ""
        Write-Host "Your files are: photos taken, recordings, music and sounds, profiles, the faces"
        Write-Host "(the photos in the faces folder) and every setting."
        if (Ask "Delete those too?" $false) { $removeAll = $true }
    }

    if ($removeAll) {
        Remove-Inside $Menu
        Remove-Inside $Faces
        Write-Host "Mods and personal files removed."
    } else {
        # Only the programs: BepInEx ignores everything else, which stays for a future reinstall.
        foreach ($dir in @($Menu, $Faces)) {
            if (Test-Path -LiteralPath $dir) {
                foreach ($dll in @(Get-ChildItem -LiteralPath $dir -Filter "*.dll" -File)) { Remove-Inside $dll.FullName }
            }
        }
        Write-Host "Mods removed. Your files stay in:"
        if (Test-Path -LiteralPath $Menu) { Write-Host ("  " + $Menu) }
        if (Test-Path -LiteralPath $Faces) { Write-Host ("  " + $Faces) }
    }
}

# ---- BepInEx, only on request ----

$loaderDir = Join $Game "BepInEx"
if (Test-Path -LiteralPath $loaderDir) {
    $others = @()
    if (Test-Path -LiteralPath $Plugins) {
        $sep = [System.IO.Path]::DirectorySeparatorChar
        $others = @(Get-ChildItem -LiteralPath $Plugins -Recurse -Filter "*.dll" -File |
            Where-Object { -not $_.FullName.StartsWith($Menu + $sep) -and -not $_.FullName.StartsWith($Faces + $sep) } |
            ForEach-Object { $_.FullName.Substring($Plugins.Length + 1) } | Sort-Object)
    }
    $removeLoader = [bool]$BepInEx
    if (-not $removeLoader -and -not $Yes) {
        Write-Host ""
        if ($others.Count -gt 0) {
            Write-Host "BepInEx (the mod loader) stays installed. These other mods use it too:"
            foreach ($o in $others) { Write-Host ("  " + $o) }
            $removeLoader = Ask "Remove BepInEx too? ALL the mods listed will stop working." $false
        } else {
            $removeLoader = Ask "Remove BepInEx (the mod loader) too? The game goes back to a fresh install." $false
        }
    }

    if ($removeLoader) {
        if (-not $removeAll -and ((Test-Path -LiteralPath $Menu) -or (Test-Path -LiteralPath $Faces))) {
            # Personal files live inside BepInEx: they are moved to safety first.
            $keep = Join $Game "BigWalkModMenu-my-files"
            if ($DryRun) {
                Write-Host ("[dry run] would move your files to: " + $keep.Substring($Game.Length + 1))
            } else {
                New-Item -ItemType Directory -Path $keep -Force | Out-Null
                foreach ($pair in @(@($Menu, "BigWalkModMenu"), @($Faces, "FaceMaskMod"))) {
                    if (Test-Path -LiteralPath $pair[0]) {
                        $dest = Join $keep $pair[1]
                        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
                        Move-Item -LiteralPath $pair[0] -Destination $dest
                    }
                }
                Write-Host ("Your files were moved to: " + $keep)
            }
        }
        # Exactly the files BepInEx adds to the game folder.
        foreach ($item in @("BepInEx", "dotnet", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt")) {
            Remove-Inside (Join $Game $item)
        }
        Write-Host "BepInEx removed."
    }
}

Write-Host ""
Write-Host "DONE." -ForegroundColor Green
exit 0
