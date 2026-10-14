# Runs Seneschal from source and starts it again whenever it exits with code 42
# (the "Restart app" button on the Developer page and in the Extension Manager), so a change needs no manual restart.
#
# The extensions of this repo are built with the app (the app project references them). In Development the app also loads
# the builds in artifacts/extensions (the Developer page puts new builds there), in place of the copy that ships next to the app.
$env:SENESCHAL_RESTARTABLE = "1"
$env:ASPNETCORE_ENVIRONMENT = "Development"

do {
    dotnet run --project "$PSScriptRoot\src\Seneschal.Host" --launch-profile https
    $code = $LASTEXITCODE
} while ($code -eq 42)

exit $code
