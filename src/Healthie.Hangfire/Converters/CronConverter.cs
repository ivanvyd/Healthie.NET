namespace Healthie.Hangfire.Converters;

public class CronConverter : ICronConverter
{
    private const string CronMinutesFormat = "*/{0} * * * *";

    public string Convert(TimeSpan interval)
    {
        if (interval.TotalMinutes is > 1 and < 59)
        {
            ushort minutes = (ushort)interval.TotalMinutes;

            return string.Format(CronMinutesFormat, minutes);
        }
        else
        {
            // TODO: implement other intervals mapping to cron expression
            throw new NotImplementedException(
                $"Converting to Cron expression is not implemented for timespan: {interval}. " +
                $"Only intervals between 1 and 59 minutes are supported.");
        }
    }
}
