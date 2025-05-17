using Healthie.Scheduling.Quartz.Converters;

namespace Healthie.Scheduling.Quartz.Converters;

public class QuartzCronConverter : ICronConverter
{
    // Quartz cron format: Seconds Minutes Hours DayOfMonth Month DayOfWeek Year(optional)
    // For seconds and sub-minute level scheduling
    private const string CronSecondsIntervalFormat = "*/{0} * * * * ?";
    private const string CronMinutesIntervalFormat = "0 */{0} * * * ?";
    private const string CronHourlyIntervalFormat = "0 0 */{0} * * ?";
    private const string CronDailyIntervalFormat = "0 0 0 */{0} * ?";

    public string Convert(TimeSpan interval)
    {
        if (interval.TotalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        
        // Handle sub-minute intervals (1-59 seconds)
        if (interval.TotalMinutes < 1)
        {
            int seconds = (int)interval.TotalSeconds;
            return string.Format(CronSecondsIntervalFormat, seconds);
        }
        
        // Handle minute-level intervals (1-59 minutes)
        if (interval.TotalHours < 1)
        {
            int minutes = (int)interval.TotalMinutes;
            return string.Format(CronMinutesIntervalFormat, minutes);
        }
        
        // Handle hourly intervals (1-23 hours)
        if (interval.TotalDays < 1)
        {
            int hours = (int)interval.TotalHours;
            return string.Format(CronHourlyIntervalFormat, hours);
        }
        
        // Handle daily intervals
        if (interval.TotalDays >= 1 && interval.TotalDays <= 31)
        {
            int days = (int)interval.TotalDays;
            return string.Format(CronDailyIntervalFormat, days);
        }
        
        throw new NotSupportedException(
            $"Converting to Cron expression is not implemented for timespan: {interval}. " +
            $"Only intervals between 1 second and 31 days are supported.");
    }
}
