# Big Walk Mod Menu - disinstallazione su Windows 10 / 11.
# Di solito basta un doppio clic su "disinstalla-windows.bat".
#
#   disinstalla-windows.ps1                        cerca Big Walk nelle librerie di Steam
#   disinstalla-windows.ps1 -Gioco "D:\Giochi\Big Walk"
#   opzioni:  -Si        risponde si' alle domande (toglie le mod, TIENE i tuoi file)
#             -Tutto     toglie anche i tuoi file: foto, facce, registrazioni, impostazioni
#             -BepInEx   toglie anche BepInEx (e quindi ogni altra mod installata)
#             -Prova     mostra cosa farebbe, senza toccare niente

param(
    [string]$Gioco = "",
    [switch]$Si,
    [switch]$Tutto,
    [switch]$BepInEx,
    [switch]$Prova
)

$ErrorActionPreference = "Stop"

$GameName = "Big Walk"
$GameExe = "Big Walk.exe"

function Stop-Uninstall([string]$Message) {
    Write-Host ""
    Write-Host ("ERRORE: " + $Message) -ForegroundColor Red
    exit 1
}

# $WithSi: la risposta usata quando c'e' -Si
function Ask([string]$Question, [bool]$WithSi) {
    if ($Si) { return $WithSi }
    $answer = Read-Host ($Question + " [s/N]")
    return ($answer -match '^(s|si|y)$')
}

function Join([string]$A, [string]$B) { return [System.IO.Path]::Combine($A, $B) }

# ---- trova il gioco ----

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

if (-not $Gioco) {
    $Gioco = Find-Game
    if (-not $Gioco) {
        Stop-Uninstall ("Non trovo " + $GameName + " nelle librerie di Steam. Trascina la cartella del gioco sul file disinstalla-windows.bat.")
    }
}
$Gioco = $Gioco.TrimEnd("\", "/")
# Si cancella solo dentro una cartella che contiene davvero l'eseguibile del gioco.
if (-not (Test-Path -LiteralPath (Join $Gioco $GameExe))) {
    Stop-Uninstall ("In """ + $Gioco + """ non c'e' """ + $GameExe + """: non e' la cartella del gioco.")
}
Write-Host ("Gioco trovato: " + $Gioco)

if (Get-Process -Name "Big Walk" -ErrorAction SilentlyContinue) {
    Stop-Uninstall "Big Walk e' aperto. Chiudilo e rilancia la disinstallazione."
}

$Plugins = Join $Gioco "BepInEx\plugins"
$Menu = Join $Plugins "BigWalkModMenu"
$Faces = Join $Plugins "FaceMaskMod"

function Remove-Inside([string]$Path) {
    if (-not $Path.StartsWith($Gioco + [System.IO.Path]::DirectorySeparatorChar)) { Stop-Uninstall ("Rifiuto di cancellare fuori dal gioco: " + $Path) }
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if ($Prova) {
        Write-Host ("[prova] cancellerei: " + $Path.Substring($Gioco.Length + 1))
    } else {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

$hasMenu = Test-Path -LiteralPath $Menu
$hasFaces = Test-Path -LiteralPath $Faces
$removeAll = [bool]$Tutto

if (-not $hasMenu -and -not $hasFaces) {
    Write-Host "Big Walk Mod Menu non risulta installato."
} else {
    Write-Host ""
    Write-Host "Tolgo Big Walk Mod Menu e la mod delle facce."
    if (-not (Ask "Procedo?" $true)) { Stop-Uninstall "Disinstallazione annullata." }

    if (-not $removeAll -and -not $Si) {
        Write-Host ""
        Write-Host "I tuoi file sono: foto scattate, registrazioni, musica e suoni, profili, le facce"
        Write-Host "(le foto nella cartella faces) e tutte le regolazioni."
        if (Ask "Cancello anche quelli?" $false) { $removeAll = $true }
    }

    if ($removeAll) {
        Remove-Inside $Menu
        Remove-Inside $Faces
        Write-Host "Mod e file personali tolti."
    } else {
        # Solo i programmi: BepInEx ignora tutto il resto, che resta per una futura reinstallazione.
        foreach ($dir in @($Menu, $Faces)) {
            if (Test-Path -LiteralPath $dir) {
                foreach ($dll in @(Get-ChildItem -LiteralPath $dir -Filter "*.dll" -File)) { Remove-Inside $dll.FullName }
            }
        }
        Write-Host "Mod tolte. I tuoi file restano in:"
        if (Test-Path -LiteralPath $Menu) { Write-Host ("  " + $Menu) }
        if (Test-Path -LiteralPath $Faces) { Write-Host ("  " + $Faces) }
    }
}

# ---- BepInEx, solo su richiesta ----

$loaderDir = Join $Gioco "BepInEx"
if (Test-Path -LiteralPath $loaderDir) {
    $others = @()
    if (Test-Path -LiteralPath $Plugins) {
        $sep = [System.IO.Path]::DirectorySeparatorChar
        $others = @(Get-ChildItem -LiteralPath $Plugins -Recurse -Filter "*.dll" -File |
            Where-Object { -not $_.FullName.StartsWith($Menu + $sep) -and -not $_.FullName.StartsWith($Faces + $sep) } |
            ForEach-Object { $_.FullName.Substring($Plugins.Length + 1) } | Sort-Object)
    }
    $removeLoader = [bool]$BepInEx
    if (-not $removeLoader -and -not $Si) {
        Write-Host ""
        if ($others.Count -gt 0) {
            Write-Host "BepInEx (il caricatore di mod) resta installato. Lo usano anche queste altre mod:"
            foreach ($o in $others) { Write-Host ("  " + $o) }
            $removeLoader = Ask "Tolgo anche BepInEx? Spariranno TUTTE le mod elencate." $false
        } else {
            $removeLoader = Ask "Tolgo anche BepInEx (il caricatore di mod)? Il gioco torna come appena installato." $false
        }
    }

    if ($removeLoader) {
        if (-not $removeAll -and ((Test-Path -LiteralPath $Menu) -or (Test-Path -LiteralPath $Faces))) {
            # I file personali stanno dentro BepInEx: vanno messi in salvo prima.
            $keep = Join $Gioco "BigWalkModMenu-i-miei-file"
            if ($Prova) {
                Write-Host ("[prova] sposterei i tuoi file in: " + $keep.Substring($Gioco.Length + 1))
            } else {
                New-Item -ItemType Directory -Path $keep -Force | Out-Null
                foreach ($pair in @(@($Menu, "BigWalkModMenu"), @($Faces, "FaceMaskMod"))) {
                    if (Test-Path -LiteralPath $pair[0]) {
                        $dest = Join $keep $pair[1]
                        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
                        Move-Item -LiteralPath $pair[0] -Destination $dest
                    }
                }
                Write-Host ("I tuoi file sono stati spostati in: " + $keep)
            }
        }
        # Esattamente i file che BepInEx aggiunge alla cartella del gioco.
        foreach ($item in @("BepInEx", "dotnet", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt")) {
            Remove-Inside (Join $Gioco $item)
        }
        Write-Host "BepInEx tolto."
    }
}

Write-Host ""
Write-Host "FATTO." -ForegroundColor Green
exit 0
