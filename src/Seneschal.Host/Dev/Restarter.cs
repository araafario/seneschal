namespace Seneschal.Dev;

/// <summary>
/// Restarts the app. Started from source with dev.ps1 (SENESCHAL_RESTARTABLE) it exits with code 42 and the script starts it again, which
/// also picks up newly built source extensions. Running hidden (seneschal start) it starts a new copy of itself and ends. Anywhere
/// else there is nothing to start it again, so there is no button.
/// </summary>
public sealed class Restarter
{
    public bool Available => Cli.EngineMode.Active || Environment.GetEnvironmentVariable("SENESCHAL_RESTARTABLE") == "1";

    public void Restart()
    {
        if (!Available)
            throw new InvalidOperationException("Start the app with dev.ps1 or seneschal start to enable one-click restart.");

        // Small delay so the UI can acknowledge the click before the connection drops
        _ = Task.Run(async () =>
        {
            await Task.Delay(400);
            if (Cli.EngineMode.Active)
            {
                // A downloaded update goes in first, so the new copy that is started is the new version
                try { Updates.UpdateInstaller.ApplyStaged(); } catch (Exception) { /* the same version simply starts again */ }
                Cli.EngineMode.StartReplacement();
                Environment.Exit(0);
            }
            Environment.Exit(DevMode.RestartExitCode);
        });
    }
}
