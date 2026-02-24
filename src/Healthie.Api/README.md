![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Api

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Api.svg)](https://www.nuget.org/packages/Healthie.NET.Api)

ASP.NET Core API controller for managing Healthie.NET pulse checkers via REST endpoints. All endpoints are served under the `/healthie` route prefix.

## Installation

```shell
dotnet add package Healthie.NET.Api
```

## Usage

```csharp
using Healthie.Api;

builder.Services.AddHealthieController();

var app = builder.Build();
app.MapControllers();
app.Run();
```

With authorization:

```csharp
builder.Services.AddHealthieController(
    requireAuthorization: true,
    authorizationPolicy: "AdminPolicy");
```

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/healthie` | Get all pulse checker states |
| `GET` | `/healthie/intervals` | Get available polling intervals |
| `PUT` | `/healthie/{name}/interval?interval={value}` | Set polling interval |
| `PUT` | `/healthie/{name}/threshold?threshold={value}` | Set unhealthy threshold |
| `POST` | `/healthie/{name}/start` | Start a checker |
| `POST` | `/healthie/{name}/stop` | Stop a checker |
| `POST` | `/healthie/{name}/trigger` | Trigger an immediate check |
| `PATCH` | `/healthie/{name}/reset` | Reset state to healthy |

The `{name}` parameter is the fully-qualified type name (e.g. `MyApp.DatabasePulseChecker`).

## Key Types

| Type | Description |
|---|---|
| `StartupExtensions.AddHealthieController()` | Registers the controller with optional authorization. |
| `HealthCheckersController` | The API controller with all pulse checker management endpoints. |

## See Also

[Back to main README](../../README.md)
