# Client of the in-game test bridge (BigWalkModMenu/Core/Dev.cs, builds made with -p:Dev=1).
# The Windows twin of tools/dev.sh. Each argument is one command; the answers are printed when all
# of them have run.
#
#   powershell -ExecutionPolicy Bypass -File tools\dev.ps1 "open faces" "wait 30" "shot faces_tab"
#
# The bridge only works while the folder BepInEx\plugins\BigWalkModMenu\dev exists in the game folder.
# Game somewhere else:  $env:GAME_DIR = "D:\SteamLibrary\steamapps\common\Big Walk"

$commands = @($args | ForEach-Object { [string]$_ })

$game = $env:GAME_DIR
if (-not $game) { $game = [System.IO.Path]::Combine(${env:ProgramFiles(x86)}, "Steam\steamapps\common\Big Walk") }
$dir = [System.IO.Path]::Combine($game, "BepInEx\plugins\BigWalkModMenu\dev")
if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
    Write-Host "ERROR: $dir does not exist. Create it and start the game with a Dev build." -ForegroundColor Red
    exit 1
}

$in = Join-Path $dir "in.txt"
$tmp = Join-Path $dir "in.tmp"
$out = Join-Path $dir "out.txt"
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
# No byte order mark, and the file appears under its real name only when it is complete.
[System.IO.File]::WriteAllLines($tmp, [string[]]$commands, (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath $tmp -Destination $in -Force

for ($i = 0; $i -lt 160; $i++) {
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $out) {
        try { $done = @(Get-Content -LiteralPath $out -ErrorAction Stop | Where-Object { $_.StartsWith("> ") }).Count } catch { $done = 0 }
        if ($done -ge $commands.Count) { break }
    }
}
if (Test-Path -LiteralPath $out) { Get-Content -LiteralPath $out }
