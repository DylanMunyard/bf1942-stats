using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Filters;

/// <summary>
/// Action filter that logs controller action execution with request parameters and response status codes.
/// Includes correlation ID (TraceIdentifier) for request tracing across logs.
/// </summary>
public class LoggingActionFilter : IActionFilter
{
    private readonly ILogger<LoggingActionFilter> _logger;

    public LoggingActionFilter(ILogger<LoggingActionFilter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var controllerName = context.Controller.GetType().Name;
        var actionName = context.ActionDescriptor.DisplayName ?? "Unknown";
        var traceId = context.HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "Request started - TraceId: {TraceId}, Controller: {Controller}, Action: {Action}, Method: {Method}, Path: {Path}",
            traceId,
            controllerName,
            actionName,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        // Log action arguments if any
        if (context.ActionArguments.Count > 0)
        {
            var args = string.Join(", ", context.ActionArguments
                .Select(kvp => $"{kvp.Key}={FormatArgumentValue(kvp.Value)}"));
            _logger.LogDebug(
                "Action arguments - TraceId: {TraceId}, Arguments: {Arguments}",
                traceId,
                args);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var controllerName = context.Controller.GetType().Name;
        var actionName = context.ActionDescriptor.DisplayName ?? "Unknown";
        var statusCode = context.HttpContext.Response.StatusCode;
        var traceId = context.HttpContext.TraceIdentifier;

        if (context.Exception != null)
        {
            _logger.LogError(
                context.Exception,
                "Request completed with exception - TraceId: {TraceId}, Controller: {Controller}, Action: {Action}, StatusCode: {StatusCode}",
                traceId,
                controllerName,
                actionName,
                statusCode);
        }
        else
        {
            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            _logger.Log(
                logLevel,
                "Request completed - TraceId: {TraceId}, Controller: {Controller}, Action: {Action}, StatusCode: {StatusCode}",
                traceId,
                controllerName,
                actionName,
                statusCode);
        }
    }

    private static string FormatArgumentValue(object? value)
    {
        if (value == null)
            return "null";

        if (value is string strValue)
            return $"\"{strValue}\"";

        if (value is IEnumerable<object> enumValue && !(value is string))
            return $"[{string.Join(", ", enumValue)}]";

        return value.ToString() ?? "null";
    }
}
