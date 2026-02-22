using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Api.Models;
using Healthie.Api.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;


namespace Healthie.Api.Controllers;

/// <summary>
/// API controller for managing pulse checker states and operations.
/// Provides endpoints for retrieving checker states, configuring intervals and thresholds,
/// and controlling checker lifecycle (start, stop, trigger, reset).
/// </summary>
[ApiController]
[Route(RoutesConstants.HealthieApiRoute)]
public class HealthCheckersController(
    IPulsesScheduler pulsesScheduler,
    ILogger<HealthCheckersController>? logger) : ControllerBase
{
    private static readonly HashSet<PulseIntervalDescription> CachedIntervals = [.. Enum
        .GetValues<PulseInterval>()
        .Select(e =>
        {
            var field = typeof(PulseInterval).GetField(e.ToString());
            var description = field?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return new PulseIntervalDescription((int)e, e.ToString())
            {
                Description = description,
            };
        })];

    /// <summary>
    /// Retrieves all available pulse interval options with their descriptions.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A set of <see cref="PulseIntervalDescription"/> representing the available intervals.</returns>
    [HttpGet("intervals")]
    [ProducesResponseType(typeof(HashSet<PulseIntervalDescription>), StatusCodes.Status200OK)]
    public ActionResult<HashSet<PulseIntervalDescription>> GetAvailableIntervals(CancellationToken cancellationToken)
    {
        return Ok(CachedIntervals);
    }

    /// <summary>
    /// Retrieves the current states of all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary of checker names mapped to their current states.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, PulseCheckerState>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, PulseCheckerState>>> GetCheckersStates(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error retrieving pulse states.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving pulse states.");
        }
    }

    /// <summary>
    /// Sets the polling interval for a specific pulse checker.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker.</param>
    /// <param name="interval">The new interval to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPut("{checkerName}/interval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetCheckerInterval(string checkerName, [FromQuery] PulseInterval interval, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            await pulsesScheduler.SetIntervalAsync(checkerName, interval, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Checker '{CheckerName}' not found for setting interval.", checkerName);
            return NotFound($"Checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error setting interval for checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures before the checker is considered unhealthy.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPut("{checkerName}/threshold")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetCheckerThreshold(string checkerName, [FromQuery] uint threshold, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            await pulsesScheduler.SetUnhealthyThresholdAsync(checkerName, threshold, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Checker '{CheckerName}' not found for setting threshold.", checkerName);
            return NotFound($"Checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error setting threshold for checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Starts (activates) a specific pulse checker.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker to start.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPost("{checkerName}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartChecker(string checkerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            await pulsesScheduler.ActivateAsync(checkerName, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Checker '{CheckerName}' not found for starting.", checkerName);
            return NotFound($"Checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error starting checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Stops (deactivates) a specific pulse checker.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker to stop.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPost("{checkerName}/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StopChecker(string checkerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            await pulsesScheduler.DeactivateAsync(checkerName, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Checker '{CheckerName}' not found for stopping.", checkerName);
            return NotFound($"Checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error stopping checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Triggers an immediate execution of a specific pulse checker.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker to trigger.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPost("{checkerName}/trigger")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerChecker(string checkerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken).ConfigureAwait(false);
            if (!checkers.TryGetValue(checkerName, out var checker))
            {
                logger?.LogWarning("Checker '{CheckerName}' not found for triggering.", checkerName);
                return NotFound($"Checker '{checkerName}' not found.");
            }

            await checker.TriggerAsync(cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error triggering checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Resets the state of a specific pulse checker to healthy.
    /// </summary>
    /// <param name="checkerName">The name of the pulse checker to reset.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>204 No Content on success, 404 if the checker is not found, or 400 if the name is empty.</returns>
    [HttpPatch("{checkerName}/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetChecker(string checkerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            await pulsesScheduler.ResetAsync(checkerName, cancellationToken).ConfigureAwait(false);

            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Checker '{CheckerName}' not found for resetting.", checkerName);
            return NotFound($"Checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error resetting checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
