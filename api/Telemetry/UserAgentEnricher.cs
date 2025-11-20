using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace api.Telemetry;

/// <summary>
/// Enriches logs with the user_agent.original span attribute from OpenTelemetry traces.
/// This allows querying logs by specific user agent values in Grafana Loki.
/// </summary>
public class UserAgentEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return;
        }

        // Look for user_agent.original in the current activity's tags
        var userAgent = activity.TagObjects
            .FirstOrDefault(tag => tag.Key == "user_agent.original")
            .Value as string;

        if (!string.IsNullOrEmpty(userAgent))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("user_agent_original", userAgent));
        }
    }
}
