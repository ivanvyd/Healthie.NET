using Healthie.Api;
using Healthie.DependencyInjection;
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// CONSIDER: This is a sample code. In Prod, handle it via DI.
CosmosClient client = new("");
Database db = await client.CreateDatabaseIfNotExistsAsync("Healthie");
Container container = await db.CreateContainerIfNotExistsAsync("HealthieState", "/id");

// Add services to the container.
builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieCosmosDb(container);

builder.Services.AddHealthieController(requireAuthorization: false);

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

app.Run();
