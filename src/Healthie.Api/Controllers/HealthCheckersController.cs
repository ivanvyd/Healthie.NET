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

[ApiController]
[Route(RoutesConstants.HealthieApiRoute)]
public class HealthCheckersController : ControllerBase
{
    private readonly IPulsesScheduler _pulsesScheduler;
    private readonly IAsyncPulsesScheduler _asyncPulsesScheduler;
    private readonly ILogger<HealthCheckersController>? _logger;

    public HealthCheckersController(
        IPulsesScheduler pulsesScheduler,
        IAsyncPulsesScheduler asyncPulsesScheduler,
        ILogger<HealthCheckersController>? logger)
    {
        _pulsesScheduler = pulsesScheduler ?? throw new ArgumentNullException(nameof(pulsesScheduler));
        _asyncPulsesScheduler = asyncPulsesScheduler ?? throw new ArgumentNullException(nameof(asyncPulsesScheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("intervals")]
    [ProducesResponseType(typeof(HashSet<PulseIntervalDescription>), StatusCodes.Status200OK)]
    public ActionResult<HashSet<PulseIntervalDescription>> GetAvailableIntervals()
    {
        TypeInfo typeInfo = typeof(PulseInterval).GetTypeInfo();

        HashSet<PulseIntervalDescription> intervals = [.. Enum
            .GetValues<PulseInterval>()
            .Select(e =>
            {
                string key = e.ToString();

                DescriptionAttribute? descriptionAttribute = typeInfo
                    .GetMember(key)
                    .FirstOrDefault(member => member.MemberType == MemberTypes.Field)?
                    .GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault() as DescriptionAttribute;

                return new PulseIntervalDescription((int)e, key)
                {
                    Description = descriptionAttribute?.Description,
                };
            })];

        return Ok(intervals);
    }

    [HttpGet("sync")]
    [ProducesResponseType(typeof(Dictionary<string, PulseCheckerState>), StatusCodes.Status200OK)]
    public ActionResult<Dictionary<string, PulseCheckerState>> GetSyncCheckersStates()
    {
        try
        {
            return Ok(_pulsesScheduler.GetPulsesStates());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving synchronous pulse states.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving synchronous pulse states.");
        }
    }

    [HttpPut("sync/{checkerName}/interval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SetSyncCheckerInterval(string checkerName, [FromQuery] PulseInterval interval)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            _pulsesScheduler.SetInterval(checkerName, interval);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Synchronous checker '{CheckerName}' not found for setting interval.", checkerName);
            return NotFound($"Synchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting interval for synchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    [HttpPost("sync/{checkerName}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult StartSyncChecker(string checkerName)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }

        try
        {
            _pulsesScheduler.Activate(checkerName);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Synchronous checker '{CheckerName}' not found for starting.", checkerName);
            return NotFound($"Synchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting synchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    [HttpPost("sync/{checkerName}/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult StopSyncChecker(string checkerName)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }
        try
        {
            _pulsesScheduler.Deactivate(checkerName);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Synchronous checker '{CheckerName}' not found for stopping.", checkerName);
            return NotFound($"Synchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping synchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    [HttpGet("async")]
    [ProducesResponseType(typeof(Dictionary<string, PulseCheckerState>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, PulseCheckerState>>> GetAsyncCheckersStates()
    {
        try
        {
            return Ok(await _asyncPulsesScheduler.GetPulsesStatesAsync());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving asynchronous pulse states.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving asynchronous pulse states.");
        }
    }

    [HttpPut("async/{checkerName}/interval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetAsyncCheckerInterval(string checkerName, [FromQuery] PulseInterval interval)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }
        try
        {
            await _asyncPulsesScheduler.SetIntervalAsync(checkerName, interval);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Asynchronous checker '{CheckerName}' not found for setting interval.", checkerName);
            return NotFound($"Asynchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting interval for asynchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    [HttpPost("async/{checkerName}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartAsyncChecker(string checkerName)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }
        try
        {
            await _asyncPulsesScheduler.ActivateAsync(checkerName);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Asynchronous checker '{CheckerName}' not found for starting.", checkerName);
            return NotFound($"Asynchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting asynchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    [HttpPost("async/{checkerName}/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StopAsyncChecker(string checkerName)
    {
        if (string.IsNullOrWhiteSpace(checkerName))
        {
            return BadRequest("Checker name cannot be empty.");
        }
        try
        {
            await _asyncPulsesScheduler.DeactivateAsync(checkerName);
            return NoContent();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Asynchronous checker '{CheckerName}' not found for stopping.", checkerName);
            return NotFound($"Asynchronous checker '{checkerName}' not found.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping asynchronous checker '{CheckerName}'.", checkerName);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
