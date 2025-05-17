using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using Healthie.Scheduling.Quartz;
using Healthie.StateProviding.MemoryCache;
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
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddHealthieMemoryCache();
                services.AddHealthieQuartz();
                services.AddHealthie([Assembly.GetExecutingAssembly()]);
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
            WriteLine("2. /pulses-async");
            WriteLine("3. Exit");

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
                    await PrintPulseStatesAsync();
                    WriteLine("Press any key to return to the menu...");
                    ReadKey();
                    break;
                case "3":
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

    private static void WritePulsesStates(Dictionary<string, State> pulsesStates)
    {
        foreach (var pulse in pulsesStates)
        {
            var name = pulse.Key;
            var lastExecutionDateTime = pulse.Value.LastExecutionDateTime;
            var message = pulse.Value.LastPulse?.ToString();
            var isHealthy = pulse.Value.LastPulse?.IsSuccess is true && pulse.Value.LastPulse?.Result?.IsHealthy is true;

            WriteLine($"Name: {name}");
            WriteLine($"Last Execution DateTime: {lastExecutionDateTime}");
            WriteLine($"Message: {message}");
            WriteLine($"Is Healthy: {isHealthy}");
            WriteLine();
        }
    }
}
