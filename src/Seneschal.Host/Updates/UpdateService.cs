using Seneschal.Cli;

namespace Seneschal.Updates;

/// <summary>What the pages show about updates.</summary>
public sealed class UpdateState
{
    /// <summary>A newer release with a new major number: never installed without being asked.</summary>
    public ReleaseInfo? Major { get; set; }

    /// <summary>A newer release that is not being installed by itself (automatic updates are off).</summary>
    public ReleaseInfo? Available { get; set; }

    /// <summary>A release that was downloaded and is installed the next time Seneschal restarts.</summary>
    public string? StagedVersion { get; set; }

    public bool Any => Major is not null || Available is not null || StagedVersion is not null;

    public event Action? Changed;

    public void Notify() => Changed?.Invoke();
}

/// <summary>
/// Looks for a new release every few days while the installed app runs. A release with the same major number is downloaded, checked and left
/// ready, and goes in the next time Seneschal restarts. A new major number only produces a notice: <c>seneschal update</c> installs it.
/// </summary>
public sealed class UpdateService(UpdateState state, ILogger<UpdateService> log) : BackgroundService
{
    /// <summary>Seconds to wait after the app started before the first look, so starting is not slowed down. SENESCHAL_UPDATE_DELAY changes it.</summary>
    private static double Delay => double.TryParse(Environment.GetEnvironmentVariable("SENESCHAL_UPDATE_DELAY"), System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!EngineMode.Active || !UpdateInstaller.CanUpdate) return;

        try
        {
            UpdateInstaller.CleanOld();
            Show(UpdateSettings.Load());

            await Task.Delay(TimeSpan.FromSeconds(Delay), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var settings = UpdateSettings.Load();
                    if (settings.Due) await CheckAsync(settings, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Looking for an update failed");
                }
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping
        }
        catch (Exception ex)
        {
            // Never take the app down: a background service that throws stops the whole host
            log.LogWarning(ex, "The updater stopped");
        }
    }

    private async Task CheckAsync(UpdateSettings settings, CancellationToken cancel)
    {
        var latest = await UpdateChecker.LatestAsync(cancel);
        if (latest is null) return; // No answer: try again at the next round instead of waiting days

        settings.CheckedAt = DateTimeOffset.Now;
        settings.Latest = latest.Text;
        settings.Save();

        if (latest.Version > UpdateInstaller.Current && !Versions.IsMajor(UpdateInstaller.Current, latest.Version) && settings.Auto)
        {
            var (ok, message) = await UpdateInstaller.StageAsync(latest, cancel);
            if (!ok) log.LogWarning("Update {Version} was not downloaded: {Reason}", latest.Text, message);
            else log.LogInformation("Update {Version} is ready and installs when Seneschal restarts", latest.Text);
        }

        Show(settings, latest);
    }

    private void Show(UpdateSettings settings, ReleaseInfo? latest = null)
    {
        var current = UpdateInstaller.Current;
        latest ??= Versions.Parse(settings.Latest) is { } version ? new ReleaseInfo(version, $"v{version.ToString(3)}", $"https://github.com/{UpdateChecker.Repo}/releases", null, null) : null;

        state.StagedVersion = UpdateInstaller.Staged()?.Version.ToString(3);
        state.Major = latest is not null && latest.Version > current && Versions.IsMajor(current, latest.Version) ? latest : null;
        state.Available = latest is not null && latest.Version > current && state.Major is null && state.StagedVersion is null ? latest : null;
        state.Notify();
    }
}
