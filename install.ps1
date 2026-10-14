<#
.SYNOPSIS
Installs, updates or removes Seneschal for the current user. No administrator rights are needed.

.DESCRIPTION
Downloads the newest release from GitHub, checks its checksum, puts it in %LOCALAPPDATA%\Programs\Seneschal, adds that folder to the
user's PATH and adds a Start menu shortcut. Running it again updates an existing install. The settings and data in %AppData%\Seneschal
are never touched, except by -Uninstall -RemoveData.

It also installs the .NET SDK (unless one of version 10 or newer is already there), which is what lets you and the assistant make extensions
without Visual Studio. The SDK goes in %LOCALAPPDATA%\Seneschal\dotnet, for this user only. Use -NoDev to skip it: Seneschal then runs as
it is, and the developer tools stay hidden until you run seneschal dev install.

.EXAMPLE
irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1 | iex

.EXAMPLE
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1))) -Version 1.0.5

.EXAMPLE
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1))) -NoDev

.EXAMPLE
.\install.ps1 -Zip .\Seneschal-1.0.5-win-x64.zip

.PARAMETER Version
A release to install, such as 1.0.5. Without it the newest release is installed. Also read from SENESCHAL_VERSION, which is the way to
choose one when the script is piped into iex.

.PARAMETER InstallDir
Where to install. Also read from SENESCHAL_INSTALL_DIR.

.PARAMETER Zip
Install from a zip on disk instead of downloading (the checksum is not checked).

.PARAMETER NoPath
Do not add the folder to PATH.

.PARAMETER NoShortcut
Do not add the Start menu shortcut.

.PARAMETER Desktop
Also add a shortcut on the desktop.

.PARAMETER NoDev
Do not install the .NET SDK. Also read from SENESCHAL_NO_DEV=1, which is the way to choose it when the script is piped into iex.

.PARAMETER Uninstall
Remove Seneschal, its PATH entry and its shortcut, and the .NET SDK it installed.

.PARAMETER RemoveData
With -Uninstall: also delete %AppData%\Seneschal (projects, startups, chat history, imported extensions).
#>
param(
    [string]$Version = $env:SENESCHAL_VERSION,
    [string]$InstallDir = $(if ($env:SENESCHAL_INSTALL_DIR) { $env:SENESCHAL_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\Seneschal' }),
    [string]$Zip,
    [switch]$NoPath,
    [switch]$NoShortcut,
    [switch]$NoDev,
    [switch]$Desktop,
    [switch]$Uninstall,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell 5.1 does not use TLS 1.2 by default, which GitHub needs
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Invoke-Seneschal-Install {
    $repo = 'araafario/seneschal'
    $exe = Join-Path $InstallDir 'Seneschal.exe'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Seneschal.lnk'
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Seneschal.lnk'
    # The shortcuts start the launcher, which has no console window. An older package without it falls back to the app itself
    $launcher = Join-Path $InstallDir 'SeneschalApp.exe'

    function Stop-Seneschal {
        # A running copy keeps its files locked
        if (Test-Path $exe) { & $exe stop *> $null }
    }

    # The SDK Seneschal installs lives in a folder of its own, so a system install is never touched and removing Seneschal can remove it
    $sdkDir = if ($env:SENESCHAL_DOTNET_DIR) { $env:SENESCHAL_DOTNET_DIR } else { Join-Path $env:LOCALAPPDATA 'Seneschal\dotnet' }
    $lite = $NoDev -or ($env:SENESCHAL_NO_DEV -eq '1')

    function Remove-FromPath([string]$folder = $InstallDir) {
        $current = [Environment]::GetEnvironmentVariable('Path', 'User')
        if (-not $current) { return }
        $kept = $current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $folder.TrimEnd('\') }
        [Environment]::SetEnvironmentVariable('Path', ($kept -join ';'), 'User')
    }

    # The dotnet that has an SDK of version 10 or newer, or nothing
    function Find-Sdk {
        $exe = Join-Path $sdkDir 'dotnet.exe'
        foreach ($candidate in 'dotnet', $exe, (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'), (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')) {
            try {
                $list = & $candidate --list-sdks 2>$null
                if ($LASTEXITCODE -eq 0 -and ($list | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 })) { return $candidate }
            }
            catch { }
        }
    }

    function Install-Sdk([string]$work) {
        if (Find-Sdk) { Write-Host 'The .NET SDK is already installed'; return }
        Write-Host 'Installing the .NET 10 SDK, so you can make extensions (a large download, use -NoDev to skip it)'
        try {
            $script = Join-Path $work 'dotnet-install.ps1'
            Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing
            & $script -Channel 10.0 -InstallDir $sdkDir -NoPath | Out-Null
            if (-not (Test-Path (Join-Path $sdkDir 'dotnet.exe'))) { throw 'the SDK was not found after the install' }
            if (-not $NoPath) {
                $current = [Environment]::GetEnvironmentVariable('Path', 'User')
                $has = $current -and ($current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ieq $sdkDir.TrimEnd('\') })
                if (-not $has) { [Environment]::SetEnvironmentVariable('Path', ((@($sdkDir, $current) | Where-Object { $_ }) -join ';'), 'User') }
                $env:Path = "$sdkDir;$env:Path"
            }
        }
        catch {
            # The app itself is installed: this is only the extra, and seneschal dev install tries it again
            Write-Host "Could not install the .NET SDK ($($_.Exception.Message)). Run seneschal dev install to try again" -ForegroundColor Yellow
        }
    }

    if ($Uninstall) {
        Stop-Seneschal
        if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
        Remove-FromPath
        if (Test-Path $sdkDir) { Remove-FromPath $sdkDir; Remove-Item $sdkDir -Recurse -Force }
        foreach ($link in $shortcut, $desktopShortcut) { if (Test-Path $link) { Remove-Item $link -Force } }
        if ($RemoveData) {
            $data = if ($env:SENESCHAL_DATA_DIR) { $env:SENESCHAL_DATA_DIR } else { Join-Path $env:APPDATA 'Seneschal' }
            if (Test-Path $data) { Remove-Item $data -Recurse -Force }
        }
        Write-Host 'Seneschal removed'
        if (-not $RemoveData) { Write-Host 'Your data is kept. Add -RemoveData to delete it too' }
        return
    }

    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $rid = switch ($arch) {
        'AMD64' { 'win-x64' }
        'ARM64' { 'win-arm64' }
        default { throw "Windows on $arch is not supported" }
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ("seneschal-install-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        if ($Zip) {
            if (-not (Test-Path $Zip)) { throw "Cannot find $Zip" }
            $archive = (Resolve-Path $Zip).Path
            $label = 'local build'
        }
        else {
            $api = "https://api.github.com/repos/$repo/releases"
            $url = if ($Version) { "$api/tags/v$($Version.TrimStart('v'))" } else { "$api/latest" }
            Write-Host 'Looking for the release'
            try {
                $release = Invoke-RestMethod -Uri $url -Headers @{ 'User-Agent' = 'seneschal-installer' }
            }
            catch {
                throw $(if ($Version) { "There is no release $Version" } else { 'There is no release yet' })
            }
            $label = $release.tag_name.TrimStart('v')

            $asset = $release.assets | Where-Object { $_.name -like "Seneschal-*-$rid.zip" } | Select-Object -First 1
            $sums = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
            if (-not $asset) { throw "Release $label has no build for $rid" }

            Write-Host "Downloading Seneschal $label"
            $archive = Join-Path $work $asset.name
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive -UseBasicParsing

            if ($sums) {
                $text = (Invoke-WebRequest -Uri $sums.browser_download_url -UseBasicParsing).Content
                if ($text -is [byte[]]) { $text = [Text.Encoding]::UTF8.GetString($text) }
                $line = $text -split "`n" | Where-Object { $_ -match [regex]::Escape($asset.name) } | Select-Object -First 1
                $expected = if ($line) { ($line.Trim() -split '\s+')[0].ToLowerInvariant() } else { $null }
                $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
                if (-not $expected -or $expected -ne $actual) { throw 'The download does not match its checksum' }
            }
        }

        Write-Host 'Installing'
        $staging = Join-Path $work 'files'
        Expand-Archive -Path $archive -DestinationPath $staging -Force
        if (-not (Test-Path (Join-Path $staging 'Seneschal.exe'))) { throw 'The package has no Seneschal.exe' }

        Stop-Seneschal
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        & robocopy $staging $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw 'Could not copy the files. Is Seneschal still running?' }

        if (-not $NoPath) {
            $current = [Environment]::GetEnvironmentVariable('Path', 'User')
            $has = $current -and ($current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') })
            if (-not $has) {
                [Environment]::SetEnvironmentVariable('Path', ((@($current, $InstallDir) | Where-Object { $_ }) -join ';'), 'User')
            }
            # This window can use the command at once; others need to be opened again
            if (-not (($env:Path -split ';') | Where-Object { $_ -and $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') })) { $env:Path = "$env:Path;$InstallDir" }
        }

        if (-not $NoShortcut) {
            $shell = New-Object -ComObject WScript.Shell
            foreach ($path in @($shortcut) + $(if ($Desktop) { $desktopShortcut })) {
                $link = $shell.CreateShortcut($path)
                if (Test-Path $launcher) { $link.TargetPath = $launcher; $link.Arguments = '' }
                else { $link.TargetPath = $exe; $link.Arguments = 'open'; $link.WindowStyle = 7 }
                $link.WorkingDirectory = $InstallDir
                $link.IconLocation = "$exe,0"
                $link.Description = 'Seneschal'
                $link.Save()
            }
        }

        if (-not $lite) { Install-Sdk $work }

        Write-Host ''
        Write-Host "Seneschal $label installed to $InstallDir"
        Write-Host 'Run seneschal in a terminal, or open Seneschal from the Start menu'
        if ($lite) { Write-Host 'The developer tools are off because the .NET SDK was skipped. Run seneschal dev install to add it' }
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    Invoke-Seneschal-Install
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    # Only a script that was run as a file may exit: under iex it would close the user's terminal
    if ($PSCommandPath) { exit 1 }
}
