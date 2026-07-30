using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Setting a schedule from outside the code that declared it.
/// </summary>
/// <remarks>
/// The trap these cover is that <see cref="PulseCheckerState.Interval"/> is ignored once
/// <see cref="PulseCheckerState.Schedule"/> is set. Anything that writes one without considering the
/// other stores a value nothing reads, and the checker carries on at its old cadence while the caller
/// believes otherwise.
/// </remarks>
public class ScheduleMutationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A real <see cref="PulseChecker"/> on a cron schedule, because the behaviour under test lives
    /// in the base class rather than in the interface.
    /// </summary>
    /// <remarks>
    /// Takes only a state provider, like the other checkers in this assembly: assembly scanning
    /// registers every concrete <see cref="PulseChecker"/> here, so one whose constructor the
    /// container cannot satisfy fails every registration test in the suite rather than only its own.
    /// </remarks>
    internal sealed class CronScheduledChecker(IStateProvider stateProvider)
        : PulseChecker(stateProvider, PulseSchedule.Cron(InitialCron))
    {
        public const string InitialCron = "0 3 * * *";

        public override string Name => "schedule-target";

        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));
    }

    private static async Task<(CronScheduledChecker Checker, IStateProvider Provider)> DailyCheckerAsync()
    {
        var provider = new InMemoryStateProvider();
        var checker = new CronScheduledChecker(provider);

        // Seeds the initial state, which is what makes the cron schedule the stored one.
        await checker.TriggerAsync(Ct);

        return (checker, provider);
    }

    private static async Task<PulseCheckerState> StateOf(IStateProvider provider) =>
        (await provider.GetStateAsync<PulseCheckerState>("schedule-target", Ct))!;

    /// <summary>
    /// The headline bug: choosing an interval on a cron checker used to write a field nothing reads.
    /// </summary>
    [Fact]
    public async Task SettingAnInterval_ClearsACronSchedule_SoTheChoiceTakesEffect()
    {
        var (checker, provider) = await DailyCheckerAsync();
        using var _ = checker;

        Assert.True((await StateOf(provider)).EffectiveSchedule.IsCron);

        await checker.SetIntervalAsync(PulseInterval.Every30Seconds, Ct);

        var state = await StateOf(provider);

        Assert.Null(state.Schedule);
        Assert.Equal(PulseInterval.Every30Seconds, state.Interval);
        Assert.Equal(TimeSpan.FromSeconds(30), state.EffectiveSchedule.Period);
    }

    [Fact]
    public async Task SettingACronSchedule_StoresIt()
    {
        var (checker, provider) = await DailyCheckerAsync();
        using var _ = checker;

        await checker.SetScheduleAsync(PulseSchedule.Cron("*/5 * * * *"), Ct);

        Assert.Equal("*/5 * * * *", (await StateOf(provider)).Schedule?.CronExpression);
    }

    /// <summary>
    /// A schedule the enum can name exactly is stored as that interval, so the common case keeps the
    /// shape every stored state and every older reader already understands.
    /// </summary>
    [Fact]
    public async Task SettingAScheduleAnIntervalCanExpress_StoresTheInterval_NotTheSchedule()
    {
        var (checker, provider) = await DailyCheckerAsync();
        using var _ = checker;

        await checker.SetScheduleAsync(PulseSchedule.Every(TimeSpan.FromMinutes(5)), Ct);

        var state = await StateOf(provider);

        Assert.Null(state.Schedule);
        Assert.Equal(PulseInterval.Every5Minutes, state.Interval);
    }

    [Fact]
    public async Task ClearingTheSchedule_GoesBackToTheStoredInterval()
    {
        var (checker, provider) = await DailyCheckerAsync();
        using var _ = checker;

        await checker.SetScheduleAsync(null, Ct);

        var state = await StateOf(provider);

        Assert.Null(state.Schedule);
        Assert.False(state.EffectiveSchedule.IsCron);
    }

    /// <summary>
    /// A period no <see cref="PulseInterval"/> names has to survive as a schedule rather than being
    /// rounded to the nearest one the enum happens to have.
    /// </summary>
    [Fact]
    public async Task SettingAnAwkwardPeriod_KeepsItExactly()
    {
        var (checker, provider) = await DailyCheckerAsync();
        using var _ = checker;

        await checker.SetScheduleAsync(PulseSchedule.Every(TimeSpan.FromSeconds(90)), Ct);

        Assert.Equal(TimeSpan.FromSeconds(90), (await StateOf(provider)).Schedule?.Period);
    }

    [Fact]
    public void TimerScheduler_AcceptsAValidCronExpression()
    {
        using var scheduler = new TimerPulseScheduler();

        Assert.True(scheduler.TryValidateSchedule(PulseSchedule.Cron("0 6 * * MON-FRI"), out var error));
        Assert.Null(error);
    }

    /// <summary>
    /// Refused with a reason, not just refused: the schedulers disagree about cron dialects, so which
    /// rule was broken is the part worth showing whoever typed it.
    /// </summary>
    [Theory]
    [InlineData("99 99 * * *")]
    [InlineData("not a cron expression")]
    [InlineData("* * *")]
    public void TimerScheduler_RefusesABadCronExpression_WithAReason(string expression)
    {
        using var scheduler = new TimerPulseScheduler();

        Assert.False(scheduler.TryValidateSchedule(PulseSchedule.Cron(expression), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TimerScheduler_AcceptsAPeriodWithoutOpinion()
    {
        using var scheduler = new TimerPulseScheduler();

        Assert.True(scheduler.TryValidateSchedule(PulseSchedule.Every(TimeSpan.FromSeconds(90)), out _));
    }

    /// <summary>
    /// Refused before it is stored. Persisting first and failing on the reschedule leaves a checker
    /// that no longer runs and a store that says it should.
    /// </summary>
    [Fact]
    public async Task PulsesScheduler_RefusingASchedule_LeavesTheStoredOneAlone()
    {
        var provider = new InMemoryStateProvider();
        using var checker = new CronScheduledChecker(provider);
        await checker.TriggerAsync(Ct);

        using var scheduler = new TimerPulseScheduler();
        var pulses = new PulsesScheduler([checker], scheduler, new HealthieOptions());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            pulses.SetScheduleAsync("schedule-target", PulseSchedule.Cron("99 99 * * *"), Ct));

        Assert.Equal(CronScheduledChecker.InitialCron, (await StateOf(provider)).Schedule?.CronExpression);
    }
}
