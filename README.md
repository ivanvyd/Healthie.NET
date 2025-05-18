# Healthie.NET

Healthie.NET is a lightweight, extensible library for .NET applications designed to monitor background tasks, services, or any component whose health and operational status need to be periodically checked and reported. It provides a flexible way to define "pulse checkers" that execute at configurable intervals, persist their state, and expose their status through an optional API.

## Limitations

*   Currently, the library is in its early stages and doesn't support distributed environments. It may not cover all use cases or scenarios.

## Features

*   **Synchronous & Asynchronous Pulse Checkers**: Define health checks that run synchronously or asynchronously.
*   **Configurable Intervals**: Easily set and change the execution frequency for each checker using predefined intervals.
*   **Pluggable Scheduling**:
    *   **In-Memory Scheduler**: Default, simple scheduler.
    *   (IN DEVELOPMENT) **Hangfire Integration**: Use Hangfire for robust, distributed background job scheduling.
    *   **Quartz.NET Integration**: Leverage Quartz.NET for enterprise-level scheduling.
*   **Pluggable State Persistence**:
    *   **In-Memory Cache**: For simple scenarios or testing.
    *   (IN DEVELOPMENT) **SQL Server**: Persist checker states to a SQL Server database.
    *   **Azure Cosmos DB**: Store checker states in Azure Cosmos DB for scalable, global distribution.
*   **Configurable API Endpoints**: Optional API (`Healthie.Api`) to view and manage pulse checkers:
    *   List available pulse intervals.
    *   Get current states of synchronous and asynchronous checkers.
    *   Dynamically set the interval for specific checkers.
    *   Start and stop individual checkers.
    *   Secure the API with authorization policies.
*   **Automatic Discovery**: Pulse checkers can be automatically discovered from specified assemblies.
*   **Extensible**: Designed with interfaces to allow custom implementations for scheduling and state persistence.

## Core Abstractions (`Healthie.Abstractions`)

The `Healthie.Abstractions` project contains the fundamental interfaces, enums, and models that define the Healthie.NET framework.

*   **`IPulseChecker` / `IAsyncPulseChecker`**:
    *   `IPulseChecker`: Interface for synchronous health checks. Implement `PulseCheckerResult Check()` and `void Initialize()`.
    *   `IAsyncPulseChecker`: Interface for asynchronous health checks. Implement `Task<PulseCheckerResult> CheckAsync()` and `Task InitializeAsync()`.
    *   Both interfaces define properties like `Name`, `Interval`, `IsActive`, `LastExecutionDateTime`, `LastResult`, and methods like `SetInterval`, `Activate`, `Deactivate`.

*   **`PulseChecker` / `AsyncPulseChecker`**:
    *   Abstract base classes that provide a convenient starting point for implementing `IPulseChecker` and `IAsyncPulseChecker` respectively. They handle common state management logic.

*   **`PulseInterval` (Enum)**:
    *   Defines predefined execution intervals for pulse checkers (e.g., `EverySecond`, `Every5Minutes`). Each member has a `DescriptionAttribute` for user-friendly display.

*   **`PulseCheckerResult` (Record)**:
    *   Represents the outcome of a health check.
    *   `IsHealthy` (bool): Indicates if the check was successful.
    *   `Message` (string?): An optional message providing details about the check result.
    *   `Exception` (Exception?): An optional exception if the check failed.

*   **`PulseCheckerState` (Record)**:
    *   Represents the persisted state of a pulse checker.
    *   `LastExecutionDateTime` (DateTimeOffset?): The last time the checker executed.
    *   `LastResult` (PulseCheckerResult?): The result of the last

### TODO List

*   ** Add `CancellationToken` support
*   ** Add more state providers
*   ** Add more scheduling providers
*   ** Add persistent scheduler