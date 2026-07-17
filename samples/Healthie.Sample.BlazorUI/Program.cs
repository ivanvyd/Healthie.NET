using Healthie.DependencyInjection;
using Healthie.Sample.BlazorUI.Components;
using Healthie.Scheduling.Quartz;
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

        // Healthie:AllowMutations=false serves the read-only board -- every state still visible,
        // no control to change one. Left at its default of true otherwise, which is this sample's
        // point: showing what the dashboard can do.
        options.AllowMutations = builder.Configuration.GetValue("Healthie:AllowMutations", true);
    });

// Swap the scheduler with Healthie:Scheduler=Quartz. AddHealthie has already registered the
// built-in timer, and the last registration wins, so this overrides it.
if (string.Equals(builder.Configuration["Healthie:Scheduler"], "Quartz", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHealthieQuartz();
}

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
