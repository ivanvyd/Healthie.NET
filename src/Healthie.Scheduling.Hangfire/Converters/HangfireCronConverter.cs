using Hangfire;

namespace Healthie.Scheduling.Hangfire.Converters;

public class HangfireCronConverter : ICronConverter
{
    private const string CronMinutesIntervalFormat = "*/{0} * * * *";

    public string Convert(TimeSpan interval) => interval.TotalMinutes switch
    {
        0 => throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero."),
        1 => Cron.Minutely(),
        > 1 and <= 59 => string.Format(CronMinutesIntervalFormat, (ushort)interval.TotalMinutes),

        // TODO: implement other intervals mapping to cron expression
        _ => throw new NotImplementedException(
            $"Converting to Cron expression is not implemented for timespan: {interval}. " +
            $"Only intervals between 1 and 59 minutes are supported."),
    };
}
