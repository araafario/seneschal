# Builds Seneschal from source and runs it the way an installed copy runs: through the launcher (SeneschalApp.exe), hidden, controlled
# by the seneschal command. The Restart App button works by itself, and `seneschal logs` shows the output.
#
#   .\dev.ps1                  build, stop the copy running from this build folder, start it again and open it
#   .\dev.ps1 -Attached        run it in this window instead (dotnet run), and start it again whenever it exits with code 42
#   .\dev.ps1 -Attached -Copy  run it in this window from a copy of the build, so a rebuild never hits a locked file
#   .\dev.ps1 -Configuration Release
#
# The extensions of this repo are built with the app (the app project references them). From a checkout the app runs as Development,
# which gives it the Developer page and lets it load the builds in artifacts/extensions in place of the copy that ships next to it.
param(
    [switch]$Attached,
    # With -Attached: run from a copy of the build (artifacts\run) instead of bin, so nothing is locked and a rebuild never fails while the app
    # runs. This is what the "Seneschal (copy)" profile in Visual Studio uses
    [switch]$Copy,
    [string]$Configuration = "Debug"
)

if ($Attached) {
    $env:SENESCHAL_RESTARTABLE = "1"
    $env:ASPNETCORE_ENVIRONMENT = "Development"

    if ($Copy) {
        # Build, copy the output next to the repo's artifacts, run the copy. Every start (and every restart, exit code 42) builds and copies again
        $run = Join-Path $PSScriptRoot "artifacts\run"
        $env:ASPNETCORE_URLS = "https://localhost:7228;http://localhost:5293"
        do {
            dotnet build "$PSScriptRoot\src\Seneschal.Host" -c $Configuration -nologo -v:q
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $built = Join-Path $PSScriptRoot "src\Seneschal.Host\bin\$Configuration\net10.0"
            robocopy $built $run /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Could not copy the build to $run" }
            & (Join-Path $run "Seneschal.exe")
            $code = $LASTEXITCODE
        } while ($code -eq 42)
        exit $code
    }

    do {
        dotnet run --project "$PSScriptRoot\src\Seneschal.Host" --launch-profile https
        $code = $LASTEXITCODE
    } while ($code -eq 42)

    exit $code
}

dotnet build "$PSScriptRoot\src\Seneschal.Host" -c $Configuration -nologo -v:q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $PSScriptRoot "src\Seneschal.Host\bin\$Configuration\net10.0"
& (Join-Path $out "Seneschal.exe") stop --from $out | Out-Null
# A program without a console is not waited for by the shell, so it is waited for here: the script ends when Seneschal is up
$launcher = Start-Process -FilePath (Join-Path $out "SeneschalApp.exe") -Wait -PassThru
exit $launcher.ExitCode
