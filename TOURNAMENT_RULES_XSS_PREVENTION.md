# Tournament Rules XSS Prevention Strategy

## Overview
Tournament rules are stored as raw markdown in the database. This document outlines the security measures implemented to prevent XSS attacks while preserving markdown functionality including code blocks, tables, and lists.

## Architecture

```
User Input (Markdown)
    ↓
[Validate: size limits, pattern detection]
    ↓
Store raw markdown in database (safe due to validation)
    ↓
When serving to frontend:
  1. Retrieve raw markdown
  2. Parse with Markdig (DisableHtml enabled)
  3. Return HTML to frontend
    ↓
Frontend renders with sanitization (DOMPurify or similar)
```

## Backend Security Implementation

### 1. IMarkdownSanitizationService (`Services/MarkdownSanitizationService.cs`)

This service provides two key functions:

#### ValidateMarkdown(string? markdown)
- **Size validation**: Limits markdown to 50,000 characters
- **Pattern detection**: Blocks suspicious HTML patterns before they reach the parser:
  - `<script`, `<iframe`, `<embed`, `<object`, `<link`
  - Event handlers: `onclick=`, `onerror=`, `onload=`, `onmouseover=`
  - Other XSS vectors: `javascript:`, `<svg`, `<form`, `<input`
- **Returns**: `ValidationResult` with `IsValid` flag and error message if invalid

#### ConvertToSafeHtml(string? markdown)
- Parses markdown using Markdig with `DisableHtml()` enabled
- Supports all standard markdown features:
  - Headings, paragraphs, emphasis (bold, italic)
  - Lists (ordered and unordered)
  - **Code blocks** (both indented and fenced with triple backticks)
  - Inline code
  - Links
- Safe for direct use in frontend or server-side rendering

### 2. Controller Integration (`Controllers/AdminTournamentController.cs`)

Both `CreateTournament` and `UpdateTournament` endpoints validate markdown:

```csharp
// Validate markdown for XSS risks
var validationResult = _markdownSanitizer.ValidateMarkdown(request.Rules);
if (!validationResult.IsValid)
    return BadRequest(new { message = validationResult.Error });

// Store the raw markdown (safe to store due to validation)
sanitizedRules = request.Rules;
```

## Supported Markdown Features

✅ **Fully Supported:**
- Headings: `# H1`, `## H2`, etc.
- **Bold**: `**text**` or `__text__`
- *Italic*: `*text*` or `_text_`
- ~~Strikethrough~~: `~~text~~`
- Lists:
  - Unordered: `- item`, `* item`, `+ item`
  - Ordered: `1. item`, `2. item`
- **Code blocks** (both formats):
  ```
  Indented by 4 spaces
  ```
  Or fenced:
  ```markdown
  ```language
  code here
  ```
  ```
- Inline code: `` `code` ``
- Links: `[text](url)`
- Line breaks and paragraphs

❌ **Blocked:**
- Raw HTML tags: `<div>`, `<script>`, `<iframe>`, etc.
- Event handlers: `onclick=`, `onerror=`, etc.
- Embedded SVG or forms
- Any markdown that violates the size limit

## Defense Layers

### Layer 1: Input Validation (Backend)
- Size limits prevent DoS
- Pattern detection blocks obvious XSS attempts
- Happens **before** markdown is parsed

### Layer 2: Safe Parsing (Backend - Markdig)
- `DisableHtml()` prevents parsing of HTML tags
- The markdown parser will treat `<script>` as literal text
- Code blocks are safely encoded

### Layer 3: Frontend Rendering
When rendering the HTML returned by the API:
- Consider using a markdown renderer (recommended)
- Or use DOMPurify to sanitize HTML before insertion
- Content Security Policy headers on the server

## Configuration

Service is registered in `Program.cs`:
```csharp
builder.Services.AddScoped<IMarkdownSanitizationService, MarkdownSanitizationService>();
```

Injected into controllers:
```csharp
public AdminTournamentController(
    PlayerTrackerDbContext context,
    ILogger<AdminTournamentController> logger,
    IMarkdownSanitizationService markdownSanitizer)
```

## Size Limits

- **Maximum markdown length**: 50,000 characters
- This allows for comprehensive tournament rules while preventing storage attacks
- Adjust `MaxMarkdownLength` constant if needed

## Testing the Security

### Safe Examples
These should be accepted:
```markdown
# Tournament Rules

## Server Settings
- Game: Battlefield 1942
- Max players: 32

## Code Examples
```csharp
// Example code block
var tournament = new Tournament();
```

Inline code: `player.Score`

[Link to forum](https://example.com)
```

### Unsafe Examples
These should be rejected:
```markdown
# Rules

<script>alert('xss')</script>

<img src=x onerror=alert('xss')>

<!-- HTML comment with malicious intent -->
```

## Future Enhancements

1. **Advanced sanitizer**: If additional HTML features are needed, add HtmlSanitizer NuGet package for post-parsing sanitization
2. **Markdown preview endpoint**: Create an API endpoint to preview rendered HTML before submission
3. **Audit logging**: Log all markdown validation failures for security monitoring
4. **Rate limiting**: Add rate limiting to validation endpoint if exposed separately

## References

- **Markdig**: https://github.com/xoofx/markdig (markdown parser)
- **OWASP XSS Prevention**: https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html
- **Markdown CommonMark Spec**: https://spec.commonmark.org/

## Questions?

The implementation prioritizes:
1. **Security**: Multiple defense layers against XSS
2. **Usability**: All standard markdown features (including code blocks)
3. **Simplicity**: Minimal dependencies, focused approach
