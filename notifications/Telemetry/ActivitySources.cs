using System.Diagnostics;

namespace junie_des_1942stats.Notifications.Telemetry;

public static class ActivitySources
{
    public static readonly ActivitySource Redis = new("junie-des-1942stats.Notifications.Redis");
    public static readonly ActivitySource Http = new("junie-des-1942stats.Notifications.Http");
    public static readonly ActivitySource SignalR = new("junie-des-1942stats.Notifications.SignalR");
    public static readonly ActivitySource Events = new("junie-des-1942stats.Notifications.Events");
}