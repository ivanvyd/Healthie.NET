using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using Healthie.Scheduling.Quartz;
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using static System.Console;

namespace Healthie.Sample.Console;

public static class Program
{
    private static IHost? _host;
    private static IHost Host => _host ?? throw new InvalidOperationException("Host is not initialized.");

    public static async Task Main(string[] args)
    {
        // CONSIDER: This is a sample code. In Prod, handle it via DI.
        CosmosClient client = new("");
        Database db = await client.CreateDatabaseIfNotExistsAsync("Healthie");
        Container container = await db.CreateContainerIfNotExistsAsync("HealthieState", "/id");

        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddHealthieQuartz();
                services.AddHealthie([Assembly.GetExecutingAssembly()]);
                // In Memory
                // services.AddHealthieMemoryCache();
                // SQL Server
                // services.AddHealthieSqlServer(connectionString: "");
                // Cosmos DB
                services.AddHealthieCosmosDb(container);
            })
            .Build();

        Task.Run(DisplayMenuAsync);

        await Host.RunAsync();
    }

    private static async Task DisplayMenuAsync()
    {
        while (true)
        {
            Clear();
            WriteLine("Select an option:");
            WriteLine("1. /pulses");
            WriteLine("2. /pulses/interval");
            WriteLine("3. /pulses-async");
            WriteLine("4. /pulses-async/interval");
            WriteLine();
            WriteLine("5. /pulses/stop");
            WriteLine("6. /pulses/start");
            WriteLine("7. /pulses-async/stop");
            WriteLine("8  /pulses-async/start");
            WriteLine();
            WriteLine("10. Exit");

            var choice = ReadLine();

            switch (choice)
            {
                case "1":
                    Clear();
                    PrintPulseStates();
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
                case "2":
                    Clear();
                    DisplayIntervalOptions();
                    var intervalChoice = ReadLine();
                    if (TryParsePulseInterval(intervalChoice, out var pulseInterval))
                    {
                        WriteLine("Enter pulse name:");
                        var name = ReadLine();
                        SetInterval(name, pulseInterval);
                    }
                    else
                    {
                        WriteLine("Invalid interval choice.");
                    }
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
                case "3":
                    Clear();
                    await PrintPulseStatesAsync();
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
                case "4":
                    Clear();
                    DisplayIntervalOptions();
                    var asyncIntervalChoice = ReadLine();
                    if (TryParsePulseInterval(asyncIntervalChoice, out var asyncPulseInterval))
                    {
                        WriteLine("Enter pulse name:");
                        var name = ReadLine();
                        await SetIntervalAsync(name, asyncPulseInterval);
                    }
                    else
                    {
                        WriteLine("Invalid interval choice.");
                    }
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
                case "5":
                    Clear();
                    WriteLine("Enter pulse name to stop:");
                    var nameToStop = ReadLine();
                    Host.Services.GetRequiredService<IPulsesScheduler>().Deactivate(nameToStop);
                    break;
                case "6":
                    Clear();
                    WriteLine("Enter pulse name to start:");
                    var nameToStart = ReadLine();
                    Host.Services.GetRequiredService<IPulsesScheduler>().Activate(nameToStart);
                    break;
                case "7":
                    Clear();
                    WriteLine("Enter pulse name to stop:");
                    var nameToStopAsync = ReadLine();
                    await Host.Services.GetRequiredService<IAsyncPulsesScheduler>().DeactivateAsync(nameToStopAsync);
                    break;
                case "8":
                    Clear();
                    WriteLine("Enter pulse name to start:");
                    var nameToStartAsync = ReadLine();
                    await Host.Services.GetRequiredService<IAsyncPulsesScheduler>().ActivateAsync(nameToStartAsync);
                    break;
                case "10":
                    await Host.StopAsync();
                    return;
                default:
                    WriteLine("Invalid choice. Please try again.");
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
            }
        }
    }

    private static void DisplayIntervalOptions()
    {
        WriteLine("Select an interval:");
        var intervalValues = Enum.GetValues<PulseInterval>();
        for (int i = 0; i < intervalValues.Length; i++)
        {
            WriteLine($"{i + 1}. {intervalValues[i]}");
        }
    }

    private static bool TryParsePulseInterval(string? input, out PulseInterval pulseInterval)
    {
        pulseInterval = default;
        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out int choiceIndex))
        {
            return false;
        }

        var intervalValues = Enum.GetValues<PulseInterval>();
        if (choiceIndex < 1 || choiceIndex > intervalValues.Length)
        {
            return false;
        }

        pulseInterval = intervalValues[choiceIndex - 1];
        return true;
    }

    private static void SetInterval(string name, PulseInterval interval)
    {
        var pulsesScheduler = Host.Services.GetRequiredService<IPulsesScheduler>();

        try
        {
            pulsesScheduler.SetInterval(name, interval);
            WriteLine($"Interval set to {interval}.");
        }
        catch (ArgumentException)
        {
            WriteLine($"Error: checker {name} is not found.");
        }
        catch (Exception ex)
        {
            WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

    private static async Task SetIntervalAsync(string name, PulseInterval interval)
    {
        var pulsesScheduler = Host.Services.GetRequiredService<IAsyncPulsesScheduler>();
        
        try
        {
            await pulsesScheduler.SetIntervalAsync(name, interval);
            WriteLine($"Interval set to {interval}.");
        }
        catch (ArgumentException)
        {
            WriteLine($"Error: checker {name} is not found.");
        }
        catch (Exception ex)
        {
            WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

    private static void PrintPulseStates()
    {
        var pulsesScheduler = Host.Services.GetRequiredService<IPulsesScheduler>();

        var pulsesStates = pulsesScheduler.GetPulsesStates();

        WritePulsesStates(pulsesStates);
    }

    private static async Task PrintPulseStatesAsync()
    {
        var pulsesScheduler = Host.Services.GetRequiredService<IAsyncPulsesScheduler>();

        var pulsesStates = await pulsesScheduler.GetPulsesStatesAsync();

        WritePulsesStates(pulsesStates);
    }

    private static void WritePulsesStates(Dictionary<string, PulseCheckerState> pulsesStates)
    {
        foreach (var pulse in pulsesStates)
        {
            WriteLine($"Name: {pulse.Key}");
            WriteLine($"Last Execution DateTime: {pulse.Value.LastExecutionDateTime}");
            WriteLine($"Health Status: {pulse.Value.LastResult?.Health ?? PulseCheckerHealth.Unhealthy}");
            WriteLine($"Is Healthy: {pulse.Value.LastResult?.IsHealthy ?? false}");
            WriteLine($"Message: {pulse.Value.LastResult?.Message}");
            WriteLine($"Interval: {pulse.Value.Interval}");
            WriteLine($"Is Active: {pulse.Value.IsActive}");
            WriteLine();
        }
    }
}
