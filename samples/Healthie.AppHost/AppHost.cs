// Runs the samples against a real CosmosDB with one command:
//
//   aspire run          (or: dotnet run --project samples/Healthie.AppHost)
//
// Everything starts together and appears on the Aspire dashboard: the emulator, the Web API, and
// the Healthie dashboard. Requires a container runtime for the emulator; without one, the samples
// still run on their own and fall back to the in-memory state provider.
//
// Aspire is a development-time orchestrator. Nothing in src/ depends on it.

var builder = DistributedApplication.CreateBuilder(args);

// The Linux emulator, rather than the Windows one: it starts faster and runs the same on CI and on
// arm64. The data explorer is opt-in.
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator => emulator.WithDataExplorer());

var database = cosmos.AddCosmosDatabase("Healthie");

builder.AddProject<Projects.Healthie_Sample_WebApi>("webapi")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.Healthie_Sample_BlazorUI>("dashboard")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
