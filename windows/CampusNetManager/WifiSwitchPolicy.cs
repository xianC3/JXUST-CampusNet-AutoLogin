namespace CampusNetManager;

public static class WifiSwitchPolicy
{
    public static bool ShouldSwitch(
        bool enabled,
        bool campusVisible,
        bool currentInternetAvailable
    )
    {
        return enabled &&
               campusVisible &&
               !currentInternetAvailable;
    }
}
