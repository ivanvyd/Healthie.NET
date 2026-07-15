using Healthie.Api;
using Healthie.DependencyInjection;
using Healthie.Mcp;
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHealthie(typeof(Program).Assembly);

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

builder.Services.AddHealthieController(requireAuthorization: false);

// Expose the checkers to AI agents over the Model Context Protocol. Mutating tools are turned on
// here so the sample can demonstrate them; require authorization on the endpoint outside of a local
// development setup.
builder.Services.AddHealthieMcp(options => options.AllowMutations = true);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Probe endpoints for an orchestrator: /healthie/live reports that the process is up, and
// /healthie/ready reports 503 while any active checker is unhealthy.
app.MapHealthieLiveness();
app.MapHealthieReadiness();

// MCP endpoint at /healthie/mcp.
app.MapHealthieMcp();

app.Run();
