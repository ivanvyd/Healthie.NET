namespace Healthie.Hangfire.Converters;

public interface ICronConverter
{
    string Convert(TimeSpan interval);
}
