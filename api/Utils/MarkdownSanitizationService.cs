using Markdig;
using Microsoft.Extensions.Logging;

namespace api.Utils;

/// <summary>
/// Service for safely processing markdown content to prevent XSS attacks.
/// Stores raw markdown but provides validation and HTML rendering capabilities.
/// Uses Markdig with DisableHtml() to prevent script injection.
/// </summary>
public interface IMarkdownSanitizationService
{
    /// <summary>
    /// Validates markdown input and returns a validation result.
    /// Does NOT modify the markdown - just checks for safety.
    /// </summary>
    ValidationResult ValidateMarkdown(string? markdown);

    /// <summary>
    /// Converts validated markdown to safe HTML.
    /// Supports code blocks, tables, and lists while preventing XSS.
    /// </summary>
    string ConvertToSafeHtml(string? markdown);
}

public class MarkdownSanitizationService(ILogger<MarkdownSanitizationService> logger) : IMarkdownSanitizationService
{
    private const int MaxMarkdownLength = 50000;
    private readonly ILogger<MarkdownSanitizationService> _logger = logger;

    // Configure Markdig pipeline with code block support
    // DisableHtml is the critical security feature - it prevents raw HTML/scripts
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()  // ⭐ CRITICAL: Prevents <script>, <iframe>, <embed>, etc.
        .Build();

    public ValidationResult ValidateMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new ValidationResult { IsValid = true };

        // Check size limit
        if (markdown.Length > MaxMarkdownLength)
        {
            _logger.LogWarning("Markdown exceeds size limit: {Length} > {MaxLength}",
                markdown.Length, MaxMarkdownLength);
            return new ValidationResult
            {
                IsValid = false,
                Error = $"Tournament rules must not exceed {MaxMarkdownLength:N0} characters"
            };
        }

        // Check for obvious HTML injection attempts
        // These should be caught by Markdig's DisableHtml, but we'll be defensive
        if (ContainsSuspiciousPatterns(markdown))
        {
            _logger.LogWarning("Markdown contains suspicious HTML patterns");
            return new ValidationResult
            {
                IsValid = false,
                Error = "HTML tags are not allowed in tournament rules. Use markdown formatting instead."
            };
        }

        return new ValidationResult { IsValid = true };
    }

    public string ConvertToSafeHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        try
        {
            // Markdig with DisableHtml prevents script injection
            // Standard markdown features supported:
            // - Headings, paragraphs, bold, italic, lists
            // - Code blocks (indented and fenced ```)
            // - Links and inline code
            var html = Markdown.ToHtml(markdown, _markdownPipeline);

            _logger.LogDebug("Successfully converted markdown to safe HTML");
            return html;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting markdown to HTML");
            // Return empty string on error rather than throwing
            return string.Empty;
        }
    }

    private static bool ContainsSuspiciousPatterns(string markdown)
    {
        // Check for raw HTML tags (case-insensitive)
        var suspiciousPatterns = new[]
        {
            "<script",
            "<iframe",
            "<embed",
            "<object",
            "<link",
            "javascript:",
            "onerror=",
            "onclick=",
            "onload=",
            "onmouseover=",
            "<svg",
            "<form",
            "<input"
        };

        var lowerMarkdown = markdown.ToLowerInvariant();
        return suspiciousPatterns.Any(pattern => lowerMarkdown.Contains(pattern));
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}
