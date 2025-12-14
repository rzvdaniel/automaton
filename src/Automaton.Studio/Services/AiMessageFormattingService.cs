using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for formatting AI messages with Markdown support
/// </summary>
public interface IAiMessageFormattingService
{
    /// <summary>
    /// Converts Markdown content to HTML with proper formatting for AI messages
    /// </summary>
    string FormatMarkdown(string content);

    /// <summary>
    /// Formats AI reasoning text by converting code blocks (```) into HTML pre elements
    /// Legacy method - kept for backward compatibility, now uses full Markdown parsing
    /// </summary>
    string FormatReasoningTextWithCode(string content);
}

/// <summary>
/// Implementation of AI message formatting service using Markdig
/// </summary>
public class AiMessageFormattingService : IAiMessageFormattingService
{
    private readonly MarkdownPipeline _pipeline;

    public AiMessageFormattingService()
    {
        // Configure Markdig pipeline with common extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()     // Includes tables, task lists, etc.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub) // GitHub-style header IDs
            .UseEmojiAndSmiley()         // Emoji support :emoji:
            .UseSoftlineBreakAsHardlineBreak() // Treat single newlines as <br>
            .Build();
    }

    /// <summary>
    /// Converts Markdown content to HTML with proper formatting for AI messages
    /// </summary>
    public string FormatMarkdown(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        try
        {
            // Convert Markdown to HTML
            var html = Markdown.ToHtml(content, _pipeline);
            
            // Add CSS class to code blocks for consistent styling
            html = html.Replace("<pre><code", "<pre class='ai-code-block'><code");
            
            return html;
        }
        catch
        {
            // Fallback to plain text if parsing fails
            return $"<p>{System.Net.WebUtility.HtmlEncode(content)}</p>";
        }
    }

    /// <summary>
    /// Formats AI reasoning text - now uses full Markdown parsing
    /// </summary>
    public string FormatReasoningTextWithCode(string content)
    {
        // Use the new Markdown formatter
        return FormatMarkdown(content);
    }
}
