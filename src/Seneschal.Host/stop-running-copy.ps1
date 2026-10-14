# Run by the build (StopRunningCopy in Seneschal.Host.csproj) before it replaces files: ends a Seneschal that runs from the folder the build writes to.
# It does not use that folder's Seneschal.exe to do it, because a build that failed half way leaves one that cannot start.
param([Parameter(Mandatory)][string]$Folder)

$folder = [IO.Path]::GetFullPath($Folder).TrimEnd('\')

$running = Get-Process Seneschal -ErrorAction SilentlyContinue | Where-Object {
    try { [string]::Equals([IO.Path]::GetDirectoryName($_.Path), $folder, 'OrdinalIgnoreCase') } catch { $false }
}

foreach ($process in $running) {
    Write-Host "Stopping Seneschal (process $($process.Id)) that runs from $folder"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    $null = $process.WaitForExit(10000)

    # The file that tells the commands where it was is left behind by a process that was ended like this
    $data = if ($env:SENESCHAL_DATA_DIR) { $env:SENESCHAL_DATA_DIR } else { Join-Path $env:APPDATA 'Seneschal' }
    $state = Join-Path $data 'engine.json'
    if ((Test-Path $state) -and ((Get-Content $state -Raw) -match "`"Pid`":\s*$($process.Id)\b")) { Remove-Item $state -Force -ErrorAction SilentlyContinue }
}
