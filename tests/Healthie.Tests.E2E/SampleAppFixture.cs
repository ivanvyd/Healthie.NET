using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Healthie.Tests.E2E;

/// <summary>
/// The combination of providers a sample app is started with. The dashboard behaves the same
/// whichever is in use, which is exactly the claim these tests exist to check.
/// </summary>
/// <param name="Scheduler">The <c>IPulseScheduler</c> to run: <c>Timer</c> or <c>Quartz</c>.</param>
/// <param name="UseCosmos">
/// Whether to persist through CosmosDB. Requires a reachable CosmosDB, so it is skipped unless
/// <c>HEALTHIE_TEST_COSMOS</c> holds a connection string.
/// </param>
public sealed record ProviderSetup(string Scheduler, bool UseCosmos)
{
    public string StateProvider => UseCosmos ? "CosmosDb" : "InMemory";

    public override string ToString() => $"{Scheduler} + {StateProvider}";
}

/// <summary>
/// Runs the Blazor sample as a real process on a free port, so the tests drive the dashboard the
/// way a browser does rather than through a test host that fakes the parts under test.
/// </summary>
public sealed class SampleApp : IAsyncDisposable
{
    private readonly Process _process;
    private readonly List<string> _output = [];

    private SampleApp(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;

        // Kept so a startup failure reports what the app said, rather than only that it never
        // answered -- the difference between reading the reason and bisecting for it.
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void Capture(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Add(line);
        }
    }

    private string OutputSoFar()
    {
        lock (_output)
        {
            return _output.Count == 0 ? "(the app printed nothing)" : string.Join(Environment.NewLine, _output);
        }
    }

    public string BaseUrl { get; }

    public string DashboardUrl => $"{BaseUrl}/healthie/dashboard";

    /// <summary>A CosmosDB connection string for the tests, or null when none is configured.</summary>
    public static string? CosmosConnectionString =>
        Environment.GetEnvironmentVariable("HEALTHIE_TEST_COSMOS") is { Length: > 0 } value ? value : null;

    public static async Task<SampleApp> StartAsync(ProviderSetup setup, CancellationToken cancellationToken = default)
    {
        var port = FreePort();
        var url = $"http://127.0.0.1:{port}";

        var info = new ProcessStartInfo("dotnet")
        {
            // --no-build: the project reference already built the sample, and rebuilding here would
            // race the other setups in this collection over the same output files.
            // --no-launch-profile: launchSettings.json pins an applicationUrl, and it takes
            // precedence over ASPNETCORE_URLS -- so without this the app ignores the free port
            // picked above, every setup fights over one port, and the wait below times out.
            Arguments = $"run --project \"{SampleProjectPath()}\" --configuration {Configuration} --no-build --no-launch-profile",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["ASPNETCORE_URLS"] = url;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["Healthie__Scheduler"] = setup.Scheduler;
        // Always set, so a developer's own user-secrets connection string cannot silently decide
        // which provider a test ran against.
        info.Environment["ConnectionStrings__CosmosDb"] = setup.UseCosmos ? CosmosConnectionString ?? "" : "";

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the sample app.");
        var app = new SampleApp(process, url);

        try
        {
            await app.WaitUntilReadyAsync(cancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The sample app exited with code {_process.ExitCode} before serving a request:"
                    + Environment.NewLine + OutputSoFar());
            }

            try
            {
                var response = await client.GetAsync(DashboardUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"The sample app did not serve {DashboardUrl} within 90s. It printed:"
            + Environment.NewLine + OutputSoFar());
    }

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static string SampleProjectPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Healthie.NET.sln")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory)
            : Path.Combine(dir.FullName, "samples", "Healthie.Sample.BlazorUI", "Healthie.Sample.BlazorUI.csproj");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }
}
