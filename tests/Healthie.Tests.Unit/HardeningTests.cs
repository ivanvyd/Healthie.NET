using Healthie.Api.Controllers;
using Healthie.StateProviding.Relational;
using System.Text.RegularExpressions;

namespace Healthie.Tests.Unit;

/// <summary>
/// Guards that did not guard quite what they said they did.
/// </summary>
public class HardeningTests
{
    /// <summary>
    /// The dashboard page is the one place in the library that builds HTML as a string instead of
    /// letting Razor build it, and the title went in unencoded.
    /// </summary>
    [Fact]
    public void ADashboardTitle_CannotCloseTheTitleElement()
    {
        var page = Healthie.Dashboard.StartupExtensions.BuildPage("</title><script>alert(1)</script>");

        // One closing title tag: the one the page itself writes.
        Assert.Equal(1, Regex.Matches(page, "</title>", RegexOptions.IgnoreCase).Count);
        Assert.DoesNotContain("<script>alert", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADashboardTitle_IsStillShownWhenItIsOrdinary()
    {
        Assert.Contains(
            "<title>Payments Health</title>",
            Healthie.Dashboard.StartupExtensions.BuildPage("Payments Health"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoDashboardTitle_FallsBackToTheDefault()
    {
        Assert.Contains(
            "<title>System Health</title>",
            Healthie.Dashboard.StartupExtensions.BuildPage(null),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A checker name comes from the route, so a caller picks it, and the not-found branch logs it
    /// precisely when it matches nothing. Percent-encoded CR and LF arrive here decoded, and a log
    /// sink writing plain text writes them as line breaks -- which is a caller forging log entries.
    /// </summary>
    [Fact]
    public void ACheckerName_CannotCarryLineBreaksIntoALog()
    {
        var forged = HealthCheckersController.ForLog("api\r\nWARN  Everything is fine");

        Assert.DoesNotContain('\r', forged);
        Assert.DoesNotContain('\n', forged);

        // Replaced rather than dropped, so what was attempted is still legible in the log.
        Assert.Contains("WARN", forged, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryCheckerName_IsLoggedUnchanged()
    {
        const string name = "Acme.Checkers.DatabasePulseChecker";

        Assert.Same(name, HealthCheckersController.ForLog(name));
    }

    [Theory]
    [InlineData("healthie_pulse_state")]
    [InlineData("dbo.healthie_pulse_state")]
    [InlineData("_leading_underscore")]
    [InlineData("With9Digits")]
    public void APlainIdentifier_IsAccepted(string tableName)
    {
        RelationalDialect.ValidateTableName(tableName);
    }

    /// <summary>
    /// The trailing-newline cases are the ones that used to get through: in .NET <c>$</c> matches
    /// immediately before a single trailing newline as well as at the end of the string, so a guard
    /// whose error message promises "letters, digits and underscores" accepted one with a line break
    /// on the end.
    /// </summary>
    [Theory]
    [InlineData("healthie_pulse_state\n")]
    [InlineData("healthie_pulse_state\r\n")]
    [InlineData("healthie_pulse_state\r")]
    [InlineData("healthie_pulse_state; DROP TABLE users")]
    [InlineData("healthie pulse state")]
    [InlineData("1_starts_with_a_digit")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public void AnythingElse_IsRefused(string tableName)
    {
        Assert.Throws<ArgumentException>(() => RelationalDialect.ValidateTableName(tableName));
    }
}
