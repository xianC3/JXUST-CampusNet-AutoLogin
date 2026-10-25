namespace CampusNetManager;

public static class CheckThrottlePolicy
{
    public static bool ShouldRun(
        DateTime lastStartedUtc,
        DateTime nowUtc,
        bool force,
        TimeSpan interval
    )
    {
        return force ||
               lastStartedUtc == DateTime.MinValue ||
               nowUtc - lastStartedUtc >= interval;
    }
}
