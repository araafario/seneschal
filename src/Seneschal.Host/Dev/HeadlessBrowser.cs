using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Seneschal.Cli;

namespace Seneschal.Dev;

/// <summary>What the page looked like and what went wrong on it, in one theme.</summary>
public sealed record PagePreview(
    string Theme,
    byte[] Png,
    string Title,
    string Text,
    bool Loaded,
    bool ScrollsSideways,
    IReadOnlyList<string> WiderThanWindow,
    IReadOnlyList<string> ConsoleErrors,
    IReadOnlyList<string> FailedRequests);

/// <summary>
/// Opens a page of the running app in a headless Edge or Chrome (or one with its window shown, when the person asks to watch) and looks at it the way a person would: waits until it has drawn, takes a
/// picture, and reports what it found (errors in the console, requests that failed, content wider than the window, the text on the page). It
/// talks to the browser through the Chrome DevTools Protocol over a web socket, so nothing but the browser that is already on the machine
/// is needed. The page is opened with <c>?theme=dark</c> or <c>?theme=light</c>, which the app honours without changing the saved theme.
/// </summary>
internal static class HeadlessBrowser
{
    private static readonly SemaphoreSlim One = new(1, 1);

    public static bool Available => BrowserLauncher.FindAppBrowser() is not null;

    public static async Task<IReadOnlyList<PagePreview>> CaptureAsync(string url, IReadOnlyList<string> themes, int width, int height, bool showWindow, CancellationToken cancel)
    {
        var browserPath = BrowserLauncher.FindAppBrowser() ?? throw new InvalidOperationException("Neither Edge nor Chrome was found, so there is no browser to look at the page with");
        await One.WaitAsync(cancel);
        var profile = Path.Combine(Path.GetTempPath(), "seneschal-headless-" + Guid.NewGuid().ToString("N")[..8]);
        Process? process = null;
        try
        {
            Directory.CreateDirectory(profile);
            var start = new ProcessStartInfo(browserPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            // Headless unless the person chose to watch: then it is an ordinary window, which the script drives the same way
            var arguments = new List<string>
            {
                "--remote-debugging-port=0", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check",
                "--disable-extensions", "--disable-background-networking", $"--window-size={width},{height}",
            };
            if (!showWindow) arguments.AddRange(["--headless=new", "--disable-gpu", "--hide-scrollbars"]);
            arguments.Add("about:blank");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            process = Process.Start(start) ?? throw new InvalidOperationException("The browser did not start");
            var port = await ReadDebugPortAsync(process, cancel);
            var pageSocket = await FindPageSocketAsync(port, cancel);

            using var session = new CdpSession();
            await session.ConnectAsync(pageSocket, cancel);
            foreach (var domain in new[] { "Page.enable", "Runtime.enable", "Log.enable", "Network.enable" })
                await session.CallAsync(domain, null, cancel);
            await session.CallAsync("Emulation.setDeviceMetricsOverride",
                new JsonObject { ["width"] = width, ["height"] = height, ["deviceScaleFactor"] = 1, ["mobile"] = false }, cancel);

            var previews = new List<PagePreview>();
            foreach (var theme in themes)
                previews.Add(await LookAsync(session, url, theme, cancel));
            return previews;
        }
        finally
        {
            try { process?.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process?.Dispose();
            // The browser may hold its files for a moment after it ends
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(profile, recursive: true); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(300, CancellationToken.None); }
            }
            One.Release();
        }
    }

    private static async Task<PagePreview> LookAsync(CdpSession session, string url, string theme, CancellationToken cancel)
    {
        session.ResetEvents();
        var address = url + (url.Contains('?') ? "&" : "?") + "theme=" + theme;
        await session.CallAsync("Page.navigate", new JsonObject { ["url"] = address }, cancel);

        // The page is drawn in two steps (the frame, then the tool), so it is waited for until the frame is there and the placeholders are gone
        var loaded = false;
        var until = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(400, cancel);
            var state = await session.EvaluateAsync(ReadyScript, cancel);
            if (state?["ready"]?.GetValue<bool>() == true)
            {
                loaded = true;
                break;
            }
        }
        // Let the layout settle: fonts, the first paint of a list that was just filled
        await Task.Delay(700, cancel);

        var facts = await session.EvaluateAsync(FactsScript, cancel) ?? new JsonObject();
        var shot = await session.CallAsync("Page.captureScreenshot", new JsonObject { ["format"] = "png" }, cancel);
        var png = Convert.FromBase64String(shot?["data"]?.GetValue<string>() ?? "");

        return new PagePreview(
            theme,
            png,
            facts["title"]?.GetValue<string>() ?? "",
            facts["text"]?.GetValue<string>() ?? "",
            loaded,
            facts["scrollsSideways"]?.GetValue<bool>() ?? false,
            (facts["wide"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToList() ?? [],
            session.ConsoleErrors,
            session.FailedRequests);
    }

    private const string ReadyScript = """
        (() => {
            const frame = document.querySelector('.page-shell, .home-banner, .opt-layout');
            const waiting = document.querySelector('.page-loading, .mud-skeleton');
            const reconnect = document.querySelector('#components-reconnect-modal');
            const reconnecting = reconnect && getComputedStyle(reconnect).display !== 'none' && reconnect.offsetHeight > 0;
            return { ready: document.readyState === 'complete' && !!frame && !waiting && !reconnecting };
        })()
        """;

    private const string FactsScript = """
        (() => {
            const body = document.querySelector('.page-body') || document.body;
            const wide = [];
            for (const el of document.querySelectorAll('.page-body *')) {
                const r = el.getBoundingClientRect();
                if (r.width > 0 && r.right > window.innerWidth + 1 && wide.length < 6)
                    wide.push(el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/)[0] : ''));
            }
            return {
                title: document.title,
                text: (body.innerText || '').replace(/\n{3,}/g, '\n\n').slice(0, 3500),
                scrollsSideways: document.documentElement.scrollWidth > window.innerWidth + 1,
                wide
            };
        })()
        """;

    private static async Task<int> ReadDebugPortAsync(Process process, CancellationToken cancel)
    {
        var line = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && Regex.Match(e.Data, @"ws://127\.0\.0\.1:(\d+)/") is { Success: true } match)
                line.TrySetResult(int.Parse(match.Groups[1].Value));
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await line.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new InvalidOperationException("The browser did not say where to connect in time");
        }
    }

    private static async Task<string> FindPageSocketAsync(int port, CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var pages = await http.GetFromJsonAsync<JsonArray>($"http://127.0.0.1:{port}/json/list", cancel);
                if (pages?.FirstOrDefault(p => p?["type"]?.GetValue<string>() == "page")?["webSocketDebuggerUrl"]?.GetValue<string>() is { } socket)
                    return socket;
            }
            catch (HttpRequestException) { }
            await Task.Delay(250, cancel);
        }
        throw new InvalidOperationException("The browser has no page to look at");
    }

    /// <summary>One conversation with the page over the DevTools Protocol, with what the page said while it was open.</summary>
    private sealed class CdpSession : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _waiting = [];
        private readonly object _gate = new();
        private readonly List<string> _console = [];
        private readonly List<string> _failed = [];
        private int _next;

        public IReadOnlyList<string> ConsoleErrors { get { lock (_gate) return [.. _console]; } }

        public IReadOnlyList<string> FailedRequests { get { lock (_gate) return [.. _failed]; } }

        public void ResetEvents()
        {
            lock (_gate)
            {
                _console.Clear();
                _failed.Clear();
            }
        }

        public async Task ConnectAsync(string url, CancellationToken cancel)
        {
            await _socket.ConnectAsync(new Uri(url), cancel);
            _ = Task.Run(() => ReceiveAsync(cancel), cancel);
        }

        public async Task<JsonNode?> CallAsync(string method, JsonObject? parameters, CancellationToken cancel)
        {
            var id = Interlocked.Increment(ref _next);
            var done = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _waiting[id] = done;
            var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
            await _socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, cancel);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel);
        }

        /// <summary>Runs a script in the page and returns its value as JSON.</summary>
        public async Task<JsonNode?> EvaluateAsync(string script, CancellationToken cancel)
        {
            var result = await CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = script, ["returnByValue"] = true }, cancel);
            return result?["result"]?["value"];
        }

        private async Task ReceiveAsync(CancellationToken cancel)
        {
            var buffer = new byte[1 << 16];
            try
            {
                while (_socket.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await _socket.ReceiveAsync(buffer, cancel);
                        message.Write(buffer, 0, received.Count);
                    } while (!received.EndOfMessage);

                    if (received.MessageType == WebSocketMessageType.Close) break;
                    Handle(JsonNode.Parse(message.ToArray()));
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The page or the browser ended
            }
            lock (_gate)
                foreach (var waiting in _waiting.Values) waiting.TrySetCanceled();
        }

        private void Handle(JsonNode? message)
        {
            if (message?["id"]?.GetValue<int>() is { } id)
            {
                TaskCompletionSource<JsonNode?>? waiting;
                lock (_gate) _waiting.Remove(id, out waiting);
                waiting?.TrySetResult(message["result"]);
                return;
            }

            var parameters = message?["params"];
            lock (_gate)
            {
                switch (message?["method"]?.GetValue<string>())
                {
                    case "Runtime.consoleAPICalled" when (parameters?["type"]?.GetValue<string>() == "error"):
                        _console.Add(Text(parameters?["args"] as JsonArray));
                        break;
                    case "Runtime.exceptionThrown":
                        _console.Add(parameters?["exceptionDetails"]?["exception"]?["description"]?.GetValue<string>() ?? parameters?["exceptionDetails"]?["text"]?.GetValue<string>() ?? "An exception");
                        break;
                    case "Log.entryAdded" when (parameters?["entry"]?["level"]?.GetValue<string>() == "error"):
                        _console.Add(parameters?["entry"]?["text"]?.GetValue<string>() ?? "");
                        break;
                    case "Network.responseReceived" when ((parameters?["response"]?["status"]?.GetValue<int>() ?? 0) >= 400):
                        _failed.Add($"{parameters?["response"]?["status"]} {parameters?["response"]?["url"]}");
                        break;
                    case "Network.loadingFailed" when (parameters?["canceled"]?.GetValue<bool>() != true):
                        _failed.Add($"failed {parameters?["errorText"]}");
                        break;
                }
            }
        }

        private static string Text(JsonArray? args) =>
            string.Join(" ", (args ?? []).Select(a => a?["value"]?.ToString() ?? a?["description"]?.GetValue<string>() ?? ""));

        public void Dispose() => _socket.Dispose();
    }
}
