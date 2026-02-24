using Healthie.DependencyInjection;
using Healthie.Sample.BlazorUI.Components;
using Healthie.StateProviding.CosmosDb;
using Healthie.Dashboard;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddCircuitOptions(options => options.DetailedErrors = true);

// CosmosDB setup
var cosmosClient = new CosmosClient(builder.Configuration.GetConnectionString("CosmosDb"));
var database = await cosmosClient.CreateDatabaseIfNotExistsAsync("Healthie");
var container = await database.Database.CreateContainerIfNotExistsAsync("HealthieState", "/id");

builder.Services
    .AddHealthie(options => options.MaxHistoryLength = 10, typeof(Program).Assembly)
    .AddHealthieCosmosDb(container.Container)
    .AddHealthieUI(options =>
    {
        options.DashboardTitle = "Healthie.NET Test Dashboard";
    });

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
