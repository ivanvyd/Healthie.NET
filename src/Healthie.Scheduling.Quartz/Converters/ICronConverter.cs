namespace Healthie.Scheduling.Quartz.Converters;

public interface ICronConverter
{
    string Convert(TimeSpan interval);
}
