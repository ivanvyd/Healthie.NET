using Healthie.DependencyInjection;
using Healthie.Sample.BlazorUI.Components;
using Healthie.StateProviding.CosmosDb;
using Healthie.Dashboard;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddCircuitOptions(options => options.DetailedErrors = true);

builder.Services
    .AddHealthie(options => options.MaxHistoryLength = 20, typeof(Program).Assembly)
    .AddHealthieUI(options =>
    {
        options.DashboardTitle = "Healthie.NET Test Dashboard";
    });

// Persist state to CosmosDB when a connection string is configured (set
// ConnectionStrings:CosmosDb, for example via user secrets or the emulator). Without one the
// built-in in-memory provider is used, so this sample runs with no external dependency.
var cosmosConnectionString = builder.Configuration.GetConnectionString("CosmosDb");
if (!string.IsNullOrWhiteSpace(cosmosConnectionString))
{
    var cosmosClient = new CosmosClient(cosmosConnectionString);
    var database = await cosmosClient.CreateDatabaseIfNotExistsAsync("Healthie");
    var container = await database.Database.CreateContainerIfNotExistsAsync("HealthieState", "/id");

    builder.Services.AddHealthieCosmosDb(container.Container);
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
