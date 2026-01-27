using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace api.Telemetry;

/// <summary>
/// Enriches logs with comprehensive request information including IP address, user agent, browser detection,
/// bot detection, and route categorization. This allows querying logs by these fields in Grafana Loki.
/// </summary>
public class RequestEnricher : ILogEventEnricher
{
    private static IHttpContextAccessor? _httpContextAccessor;

    public RequestEnricher()
    {
    }

    // Static method to set the HttpContextAccessor (called from Program.cs)
    public static void SetHttpContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return;
        }

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        // Extract user agent from activity or HTTP context
        var userAgent = activity.TagObjects
            .FirstOrDefault(tag => tag.Key == "user_agent.original")
            .Value as string;

        if (string.IsNullOrEmpty(userAgent))
        {
            userAgent = httpContext.Request.Headers["User-Agent"].ToString();
        }

        if (!string.IsNullOrEmpty(userAgent))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("user_agent_original", userAgent));

            // Detect browser from user agent
            var browser = DetectBrowser(userAgent);
            if (!string.IsNullOrEmpty(browser))
            {
                logEvent.AddPropertyIfAbsent(
                    propertyFactory.CreateProperty("browser", browser));
            }

            // Detect if it's a bot/crawler
            var isBot = IsBot(userAgent);
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("is_bot", isBot));
        }

        // Extract IP address
        var ipAddress = GetClientIpAddress(httpContext);
        if (!string.IsNullOrEmpty(ipAddress))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("client_ip", ipAddress));
        }

        // Extract request path
        var requestPath = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(requestPath))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("request_path", requestPath));

            // Categorize route type
            var routeType = CategorizeRoute(requestPath);
            if (!string.IsNullOrEmpty(routeType))
            {
                logEvent.AddPropertyIfAbsent(
                    propertyFactory.CreateProperty("route_type", routeType));
            }
        }

        // Extract HTTP method
        var httpMethod = httpContext.Request.Method;
        if (!string.IsNullOrEmpty(httpMethod))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("http_method", httpMethod));
        }
    }

    private string GetClientIpAddress(HttpContext httpContext)
    {
        // Check X-Forwarded-For header first (for reverse proxies)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, take the first one
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check X-Real-IP header
        var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string DetectBrowser(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "unknown";

        var ua = userAgent.ToLowerInvariant();

        if (ua.Contains("chrome") && !ua.Contains("edg"))
            return "Chrome";
        if (ua.Contains("firefox"))
            return "Firefox";
        if (ua.Contains("safari") && !ua.Contains("chrome"))
            return "Safari";
        if (ua.Contains("edg"))
            return "Edge";
        if (ua.Contains("opera") || ua.Contains("opr"))
            return "Opera";
        if (ua.Contains("msie") || ua.Contains("trident"))
            return "IE";

        return "Other";
    }

    private bool IsBot(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        var ua = userAgent.ToLowerInvariant();

        // Common bot/crawler patterns
        var botPatterns = new[]
        {
            "googlebot", "bingbot", "slurp", "duckduckbot", "baiduspider",
            "yandexbot", "sogou", "exabot", "facebot", "ia_archiver",
            "applebot", "twitterbot", "rogerbot", "linkedinbot", "embedly",
            "quora link preview", "showyoubot", "outbrain", "pinterest",
            "slackbot", "vkShare", "W3C_Validator", "whatsapp", "flipboard",
            "tumblr", "bitlybot", "skypeuripreview", "nuzzel", "discordbot",
            "qwantify", "pinterestbot", "bitrix link preview", "xing-contenttabreceiver",
            "chrome-lighthouse", "semrushbot", "ahrefsbot", "dotbot", "mj12bot",
            "petalbot", "crawler", "spider", "bot", "scraper"
        };

        return botPatterns.Any(pattern => ua.Contains(pattern));
    }

    private string CategorizeRoute(string requestPath)
    {
        if (string.IsNullOrEmpty(requestPath))
            return "unknown";

        var path = requestPath.ToLowerInvariant();

        // User-facing routes (main website routes)
        if (path.StartsWith("/stats/players/") && !path.Contains("/sessions/") && !path.Contains("/server/") && !path.Contains("/compare"))
            return "player_page";
        if (path.StartsWith("/stats/servers/"))
            return "server_page";
        if (path == "/" || path == "/stats" || path.StartsWith("/stats/") && path.Split('/').Length == 2)
            return "landing_page";

        // API routes (internal app calls)
        if (path.StartsWith("/api/") || path.StartsWith("/stats/players/search") || 
            path.StartsWith("/stats/players/compare") || path.StartsWith("/stats/players/") && 
            (path.Contains("/sessions/") || path.Contains("/server/")))
            return "api_call";

        // Other routes
        return "other";
    }
}
