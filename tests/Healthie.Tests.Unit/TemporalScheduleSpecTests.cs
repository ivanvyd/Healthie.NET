using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Temporal;

namespace Healthie.Tests.Unit;

/// <summary>
/// Temporal needs a running cluster, so almost none of that integration can be exercised here. The
/// mapping from a schedule to a Temporal specification can be, and it is the piece where a mistake
/// would silently change how often a check runs rather than failing loudly.
/// <para>
/// The rest -- creating a schedule, deleting one, the workflow reaching the activity -- needs a
/// server, and is covered by the environment-gated integration path described in the package README
/// rather than pretended at with a mock of Temporal's client.
/// </para>
/// </summary>
public class TemporalScheduleSpecTests
{
    [Fact]
    public void AFixedPeriod_BecomesAnInterval()
    {
        var spec = TemporalScheduleSpec.From(PulseSchedule.Every(TimeSpan.FromSeconds(30)));

        var interval = Assert.Single(spec.Intervals!);
        Assert.Equal(TimeSpan.FromSeconds(30), interval.Every);
        Assert.Empty(spec.CronExpressions ?? []);
    }

    /// <summary>
    /// The whole reason PulseSchedule exists: six hours is not something the interval enum can say.
    /// </summary>
    [Fact]
    public void APeriodBeyondTheIntervalEnum_IsExpressedExactly()
    {
        var spec = TemporalScheduleSpec.From(PulseSchedule.Every(TimeSpan.FromHours(6)));

        Assert.Equal(TimeSpan.FromHours(6), Assert.Single(spec.Intervals!).Every);
    }

    /// <summary>
    /// Temporal parses standard Unix cron, the same syntax a schedule carries, so unlike Quartz
    /// there is no dialect to translate -- and translating anyway would be the way to break it.
    /// </summary>
    [Fact]
    public void ACronExpression_PassesThroughUntranslated()
    {
        var spec = TemporalScheduleSpec.From(PulseSchedule.Cron("0 3 * * 1-5"));

        Assert.Equal("0 3 * * 1-5", Assert.Single(spec.CronExpressions!));
        Assert.Empty(spec.Intervals ?? []);
    }

    /// <summary>
    /// Healthie's six-field contract puts seconds first. Temporal interprets six fields as the
    /// five ordinary fields plus a year, and uses seven fields when seconds are present.
    /// </summary>
    [Fact]
    public void ASecondsCron_GainsTheWildcardYearTemporalRequires()
    {
        var spec = TemporalScheduleSpec.From(PulseSchedule.Cron("*/10 * * * * *"));

        Assert.Equal("*/10 * * * * * *", Assert.Single(spec.CronExpressions!));
    }

    [Theory]
    [InlineData("0 3 * * 1-5", true)]
    [InlineData("*/10 * * * * *", true)]
    [InlineData("H * * * *", false)]
    [InlineData("0 0 L * *", false)]
    [InlineData("0 0 15W * *", false)]
    [InlineData("0 0 * * 6#3", false)]
    [InlineData("0 23-1 * * *", false)]
    [InlineData("0 0 * DEC-FEB *", false)]
    [InlineData("0 0 * * FRI-MON", false)]
    [InlineData("not a cron", false)]
    [InlineData("99 99 * * *", false)]
    public void CronValidationMatchesHealthiesPublicSyntax(string expression, bool expected)
    {
        Assert.Equal(
            expected,
            TemporalScheduleSpec.TryValidate(PulseSchedule.Cron(expression), out var error));
        Assert.Equal(expected, string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidationRejectsAnIntervalTemporalCannotRun()
    {
        Assert.False(
            TemporalScheduleSpec.TryValidate(
                PulseSchedule.Every(TimeSpan.FromMilliseconds(999)), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidationRejectsAnIntervalBeyondProtobufDurationsRange()
    {
        var impossible = PulseSchedule.Every(TimeSpan.MaxValue);

        Assert.False(TemporalScheduleSpec.TryValidate(impossible, out var error));
        Assert.Throws<ArgumentException>(() => TemporalScheduleSpec.From(impossible));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ACronScheduleIsNotAlsoAnInterval_AndViceVersa()
    {
        var cron = TemporalScheduleSpec.From(PulseSchedule.Cron("0 0 * * *"));
        var period = TemporalScheduleSpec.From(PulseSchedule.Every(TimeSpan.FromMinutes(5)));

        // Setting both would make Temporal fire on the union of them, which is neither schedule.
        Assert.Empty(cron.Intervals ?? []);
        Assert.Empty(period.CronExpressions ?? []);
    }

    [Fact]
    public void AnIntervalSchedule_IsNotOffset()
    {
        var spec = TemporalScheduleSpec.From(PulseSchedule.Every(TimeSpan.FromMinutes(1)));

        // Temporal counts interval occurrences from an epoch rather than from creation, so leaving
        // the offset unset is what makes two replicas creating this schedule agree on when it fires.
        Assert.Null(Assert.Single(spec.Intervals!).Offset);
    }
}
