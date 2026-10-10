# Big Walk Mod Menu - online installer for Windows 10 / 11.
#
#   irm https://github.com/Giorey01/BigWalkModMenu/releases/latest/download/install.ps1 | iex
#
# Downloads the package of this release, checks it, and runs the installer that is inside it.
# It asks for administrator rights only if the game folder cannot be written without them.
#
# @VERSION@ and @SHA256@ are filled in by build.sh: the copy to run is the one attached to a release.

& {
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"

    $version = "@VERSION@"
    $sha256 = "@SHA256@"
    $url = "https://github.com/Giorey01/BigWalkModMenu/releases/download/v$version/BigWalkModMenu-$version.zip"

    # No "exit" in here: this runs inside the user's own PowerShell window.
    if ($version -like "*@*") {
        Write-Host "ERROR: this is the template. Run the install.ps1 attached to a release instead." -ForegroundColor Red
        return
    }

    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("BigWalkModMenu-" + $version)
    $zip = Join-Path $dir "package.zip"
    $package = Join-Path $dir "package"
    $installer = Join-Path $package "install-windows.ps1"

    try {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $package -Force | Out-Null

        Write-Host ("Downloading Big Walk Mod Menu " + $version + "...")
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

        $sum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sum -ne $sha256) {
            Write-Host "ERROR: the downloaded file is not the expected one (different checksum). Not installing it." -ForegroundColor Red
            return
        }
        Expand-Archive -LiteralPath $zip -DestinationPath $package -Force
    } catch {
        Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
        return
    }

    # A new process, because script files are blocked by default in the current one.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
    $code = $LASTEXITCODE

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($code -ne 0 -and -not $isAdmin) {
        Write-Host ""
        $answer = Read-Host "The installer did not finish. Try again as administrator? [y/N]"
        if ($answer -match '^(y|yes)$') {
            $command = "& '" + $installer.Replace("'", "''") + "'; Write-Host ''; Read-Host 'Press Enter to close'"
            try {
                Start-Process -FilePath "powershell.exe" -Verb RunAs -Wait -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ('"' + $command + '"'))
            } catch {
                Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
            }
        }
    }

    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}
