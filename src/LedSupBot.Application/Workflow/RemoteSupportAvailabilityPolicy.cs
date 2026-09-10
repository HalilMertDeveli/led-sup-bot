namespace LedSupBot.Application.Workflow;

public interface IRemoteSupportAvailabilityPolicy { bool IsAvailable(DateTimeOffset utcNow); }

public sealed class TurkeyRemoteSupportAvailabilityPolicy : IRemoteSupportAvailabilityPolicy
{
    private static readonly TimeZoneInfo Turkey = ResolveTurkeyTimeZone();
    public bool IsAvailable(DateTimeOffset utcNow) => TimeZoneInfo.ConvertTime(utcNow, Turkey).TimeOfDay < new TimeSpan(18, 30, 0);

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        throw new InvalidOperationException("Turkey time zone is unavailable.");
    }
}
