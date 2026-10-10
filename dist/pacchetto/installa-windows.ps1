# Big Walk Mod Menu - installazione su Windows 10 / 11.
# Di solito basta un doppio clic su "installa-windows.bat".
#
#   installa-windows.ps1                         cerca Big Walk nelle librerie di Steam
#   installa-windows.ps1 -Gioco "D:\Giochi\Big Walk"
#   opzioni:  -Si           risponde si' a tutte le domande (e installa tutte le mod)
#             -Prova        mostra cosa farebbe, senza toccare niente
#             -Tutte        installa tutte le mod senza chiedere
#             -Mod a,b,c    installa solo queste (nomi: facce solo personaggio telecamera
#                           prestazioni mondo reshade voce extra replay)
#
# Si puo' rilanciare quando si vuole: aggiorna quello che c'e' gia' e aggiunge quello che scegli.

param(
    [string]$Gioco = "",
    [switch]$Si,
    [switch]$Prova,
    [switch]$Tutte,
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
    Write-Host ("ERRORE: " + $Message) -ForegroundColor Red
    exit 1
}

function Ask([string]$Question) {
    if ($Si) { return $true }
    $answer = Read-Host ($Question + " [s/N]")
    return ($answer -match '^(s|si|y)$')
}

function Join([string]$A, [string]$B) { return [System.IO.Path]::Combine($A, $B) }

if (-not (Test-Path -LiteralPath (Join $Here "BepInEx\plugins\BigWalkModMenu"))) {
    Stop-Install "Accanto a questo script manca la cartella BepInEx del pacchetto. Estrai tutto lo zip e riprova."
}

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

        # Altre librerie (altri dischi) sono elencate qui.
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
        Stop-Install ("Non trovo " + $GameName + " nelle librerie di Steam. Trascina la cartella del gioco sul file installa-windows.bat, oppure: installa-windows.ps1 -Gioco ""D:\percorso\" + $GameName + """")
    }
}
$Gioco = $Gioco.TrimEnd("\", "/")
if (-not (Test-Path -LiteralPath (Join $Gioco $GameExe))) {
    Stop-Install ("In """ + $Gioco + """ non c'e' """ + $GameExe + """: non e' la cartella del gioco.")
}
Write-Host ("Gioco trovato: " + $Gioco)

if (Get-Process -Name "Big Walk" -ErrorAction SilentlyContinue) {
    Stop-Install "Big Walk e' aperto. Chiudilo e rilancia l'installazione."
}

# ---- BepInEx (il caricatore di mod) ----

$BepInExMarker = Join $Gioco "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
if (Test-Path -LiteralPath $BepInExMarker) {
    Write-Host "BepInEx e' gia' installato."
} else {
    Write-Host ""
    Write-Host "Nel gioco manca BepInEx, il programma che carica le mod."
    Write-Host "Lo scarico dal sito ufficiale (circa 35 MB):"
    Write-Host ("  " + $BepInExUrl)
    if ($Prova) {
        Write-Host ("[prova] scaricherei BepInEx e lo estrarrei in: " + $Gioco)
    } else {
        if (-not (Ask "Procedo?")) { Stop-Install "Senza BepInEx le mod non partono. Installazione annullata." }
        $tmp = Join ([System.IO.Path]::GetTempPath()) ("bigwalk-bepinex-" + [System.Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $tmp | Out-Null
        try {
            $zip = Join $tmp "bepinex.zip"
            # Un file gia' scaricato accanto allo script va bene lo stesso, se e' quello giusto.
            $local = Get-ChildItem -LiteralPath $Here -Filter "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($local) {
                Copy-Item -LiteralPath $local.FullName -Destination $zip
            } else {
                try {
                    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
                    Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
                } catch {
                    Stop-Install ("Download non riuscito: " + $_.Exception.Message)
                }
            }
            $sum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($sum -ne $BepInExSha256) { Stop-Install "Il file scaricato non e' quello atteso (impronta diversa). Non lo installo." }
            Expand-Archive -LiteralPath $zip -DestinationPath $Gioco -Force
        } finally {
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not (Test-Path -LiteralPath $BepInExMarker)) { Stop-Install "BepInEx non risulta installato dopo l'estrazione." }
        Write-Host "BepInEx installato."
    }
}

# ---- le mod ----
#
# Ogni mod e' una scheda del menu e un file a se'. Il menu base ("Generale") c'e' sempre.

$Mods = @(
    @{ Id = "facce";       Name = "Facce";       Text = "la foto di una faccia sulla testa dei personaggi"; Files = @("FaceMaskMod\BigWalk.FaceMaskMod.dll", "FaceMaskMod\StbImageSharp.dll") },
    @{ Id = "solo";        Name = "Solo";        Text = "fino a quattro personaggi comandati da un solo giocatore"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Solo.dll") },
    @{ Id = "personaggio"; Name = "Personaggio"; Text = "cappelli, accessori, colori, proporzioni"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Character.dll") },
    @{ Id = "telecamera";  Name = "Telecamera";  Text = "selfie, terza persona, camera libera, foto"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Camera.dll") },
    @{ Id = "prestazioni"; Name = "Prestazioni"; Text = "contatore FPS e opzioni grafiche avanzate"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Performance.dll") },
    @{ Id = "mondo";       Name = "Mondo";       Text = "ora del giorno, cielo, nebbia, filtri di colore"; Files = @("BigWalkModMenu\BigWalk.ModMenu.World.dll") },
    @{ Id = "reshade";     Name = "Reshade";     Text = "bagliore, toni da film, nitidezza"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Reshade.dll") },
    @{ Id = "voce";        Name = "Voce";        Text = "volume della voce di ogni giocatore"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Voice.dll") },
    @{ Id = "extra";       Name = "Extra";       Text = "musica, suoni, emote, fumetti"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Extras.dll", "BigWalkModMenu\NVorbis.dll", "BigWalkModMenu\NLayer.dll") },
    @{ Id = "replay";      Name = "Replay";      Text = "registra e rivedi le scene"; Files = @("BigWalkModMenu\BigWalk.ModMenu.Replay.dll") }
)
$CoreFiles = @("BigWalkModMenu\BigWalk.ModMenu.dll", "BigWalkModMenu\StbImageSharp.dll", "BigWalkModMenu\StbImageWriteSharp.dll")

$Src = Join $Here "BepInEx\plugins"
$Dst = Join $Gioco "BepInEx\plugins"

function In-Package($m) { return (Test-Path -LiteralPath (Join $Src $m.Files[0])) }
function Is-Installed($m) { return (Test-Path -LiteralPath (Join $Dst $m.Files[0])) }

$Available = @($Mods | Where-Object { In-Package $_ })
$Selected = @()

if ($Mod.Count -gt 0) {
    $names = @($Mod | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    foreach ($name in $names) {
        $found = @($Available | Where-Object { $_.Id -eq $name })
        if ($found.Count -eq 0) {
            Stop-Install ("Non conosco la mod """ + $name + """ (o non e' in questo pacchetto). Nomi validi: " + (($Available | ForEach-Object { $_.Id }) -join " "))
        }
        $Selected += $found[0].Id
    }
} elseif ($Tutte -or $Si -or $Prova) {
    $Selected = @($Available | ForEach-Object { $_.Id })
} else {
    Write-Host ""
    Write-Host "Quali mod vuoi? Ognuna e' una scheda del menu. Il menu base c'e' sempre."
    Write-Host ""
    for ($i = 0; $i -lt $Available.Count; $i++) {
        $m = $Available[$i]
        $state = ""
        if (Is-Installed $m) { $state = "  [gia' installata]" }
        Write-Host ("  {0,2}) {1,-12} {2}{3}" -f ($i + 1), $m.Name, $m.Text, $state)
    }
    Write-Host ""
    Write-Host "  Invio      tutte"
    Write-Host "  numeri     solo quelle, separate da spazi (esempio: 1 3 8)"
    Write-Host "  m          solo quelle che ti mancano"
    Write-Host ""
    Write-Host "Quelle gia' installate vengono comunque aggiornate, e nessuna viene tolta."
    $choice = (Read-Host "Scelta").Trim()
    if ($choice -eq "") {
        $Selected = @($Available | ForEach-Object { $_.Id })
    } elseif ($choice -match '^[mM]$') {
        $Selected = @($Available | Where-Object { -not (Is-Installed $_) } | ForEach-Object { $_.Id })
    } else {
        foreach ($token in ($choice -split '[\s,;]+' | Where-Object { $_ })) {
            $number = 0
            if (-not [int]::TryParse($token, [ref]$number)) { Stop-Install ("""" + $token + """ non e' un numero dell'elenco.") }
            if ($number -lt 1 -or $number -gt $Available.Count) { Stop-Install ("Nell'elenco non c'e' il numero " + $number + ".") }
            $Selected += $Available[$number - 1].Id
        }
    }
}

# Le mod gia' presenti si aggiornano sempre: schede di versioni diverse non vanno d'accordo col menu.
foreach ($m in $Available) {
    if ((Is-Installed $m) -and ($Selected -notcontains $m.Id)) { $Selected += $m.Id }
}

function Copy-ModFile([string]$Relative) {
    $from = Join $Src $Relative
    if (-not (Test-Path -LiteralPath $from)) { Stop-Install ("Nel pacchetto manca il file " + $Relative + ". Estrai di nuovo tutto lo zip.") }
    if ($Prova) { Write-Host ("[prova] copierei: " + $Relative); return }
    $to = Join $Dst $Relative
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    } catch {
        Stop-Install ("Copia non riuscita: " + $_.Exception.Message + " (se il gioco e' in Programmi, riprova con ""Esegui come amministratore"")")
    }
}

Write-Host ""
foreach ($f in $CoreFiles) { Copy-ModFile $f }
Write-Host "Menu base: ok"
$done = @()
foreach ($m in $Available) {
    if ($Selected -contains $m.Id) {
        foreach ($f in $m.Files) { Copy-ModFile $f }
        $done += $m.Name
    }
}
if ($done.Count -gt 0) { Write-Host ("Mod installate o aggiornate: " + ($done -join " ")) }
if (-not $Prova) {
    if (-not (Test-Path -LiteralPath (Join $Dst "BigWalkModMenu\BigWalk.ModMenu.dll"))) { Stop-Install "La mod non risulta copiata." }
    if (Test-Path -LiteralPath (Join $Dst "FaceMaskMod\BigWalk.FaceMaskMod.dll")) {
        New-Item -ItemType Directory -Path (Join $Dst "FaceMaskMod\faces") -Force -ErrorAction SilentlyContinue | Out-Null
    }
}
$missing = @($Available | Where-Object { ($Selected -notcontains $_.Id) -and -not (Is-Installed $_) } | ForEach-Object { $_.Name })
if ($missing.Count -gt 0) { Write-Host ("Non installate (rilancia l'installazione quando le vuoi): " + ($missing -join " ")) }

if ((Test-Path -LiteralPath (Join $Dst "BigWalkSoloMod\BigWalkSoloMod.dll")) -and (Test-Path -LiteralPath (Join $Dst "BigWalkModMenu\BigWalk.ModMenu.Solo.dll"))) {
    Write-Host ""
    Write-Host "ATTENZIONE: hai anche la mod ""Big Walk Solo Player"" originale. Fa le stesse cose della" -ForegroundColor Yellow
    Write-Host "scheda Solo: togli la cartella BepInEx\plugins\BigWalkSoloMod, altrimenti la scheda resta ferma." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "FATTO." -ForegroundColor Green
Write-Host ""
Write-Host "Il primo avvio dopo aver installato BepInEx dura qualche minuto (prepara i file"
Write-Host "del gioco) e sembra bloccato: lascialo finire."
Write-Host ""
Write-Host "In gioco il menu si apre con F8 oppure Insert."
if (Test-Path -LiteralPath (Join $Dst "FaceMaskMod\BigWalk.FaceMaskMod.dll")) {
    Write-Host ("Le foto per le facce vanno in: " + (Join $Gioco "BepInEx\plugins\FaceMaskMod\faces"))
    Write-Host "(oppure scegli un'altra cartella dalla scheda Facce)."
}
exit 0
