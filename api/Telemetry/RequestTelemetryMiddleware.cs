using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace api.Telemetry;

/// <summary>
/// Middleware that enriches OpenTelemetry traces with request metadata including IP address,
/// user agent, browser detection, bot detection, and route categorization.
/// This ensures all traces have consistent metadata for querying in Tempo.
/// </summary>
public class RequestTelemetryMiddleware
{
    private readonly RequestDelegate _next;

    public RequestTelemetryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            // Add user agent if not already present
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            if (!string.IsNullOrEmpty(userAgent))
            {
                activity.SetTag("user_agent.original", userAgent);

                // Detect browser
                var browser = DetectBrowser(userAgent);
                if (!string.IsNullOrEmpty(browser))
                {
                    activity.SetTag("browser", browser);
                }

                // Detect if it's a bot
                var isBot = IsBot(userAgent);
                activity.SetTag("is_bot", isBot.ToString().ToLowerInvariant());
            }

            // Add IP address
            var ipAddress = GetClientIpAddress(context);
            if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "unknown")
            {
                activity.SetTag("client_ip", ipAddress);
            }

            // Add request path
            var requestPath = context.Request.Path.Value;
            if (!string.IsNullOrEmpty(requestPath))
            {
                activity.SetTag("request_path", requestPath);

                // Categorize route type
                var routeType = CategorizeRoute(requestPath);
                if (!string.IsNullOrEmpty(routeType))
                {
                    activity.SetTag("route_type", routeType);
                }
            }

            // Add HTTP method
            var httpMethod = context.Request.Method;
            if (!string.IsNullOrEmpty(httpMethod))
            {
                activity.SetTag("http_method", httpMethod);
            }
        }

        await _next(context);
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
