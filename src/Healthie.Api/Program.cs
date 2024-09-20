using Healthie.Extensions;
using Healthie.Scheduling;
using Healthie.Scheduling.Hangfire;
using Healthie.Storage.MemoryCache;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthie([Assembly.GetExecutingAssembly()]);
builder.Services.AddHealthieHangfire();
builder.Services.AddHealthieMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/pulses", ([FromServices] IPulsesScheduler pulsesScheduler) =>
{
    var pulsesStates = pulsesScheduler.GetPulsesStates();

    return pulsesStates.Select(pulse => new
    {
        Name = pulse.Key,
        pulse.Value.LastExecutionDateTime,
        Message = pulse.Value.LastPulse?.ToString(),
        IsHealthy = pulse.Value.LastPulse?.IsSuccess is true && pulse.Value.LastPulse?.Result?.IsHealthy is true,
    });
})
.WithName("Get Pulses")
.WithOpenApi();

await app.RunAsync();
