namespace Healthie.Scheduling.Hangfire.Converters;

public interface ICronConverter
{
    string Convert(TimeSpan interval);
}
