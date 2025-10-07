using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for comparing text similarity to detect duplicate or near-duplicate content.
/// Uses a combination of length ratio, substring matching, and word overlap analysis.
/// </summary>
public interface ITextSimilarityService
{
    /// <summary>
    /// Determines if two text contents are similar enough to be considered duplicates.
    /// </summary>
    /// <param name="content1">First text content to compare</param>
    /// <param name="content2">Second text content to compare</param>
    /// <param name="similarityThreshold">Threshold for word overlap (0.0 to 1.0, default 0.8)</param>
    /// <param name="lengthRatioThreshold">Minimum length ratio to consider similar (0.0 to 1.0, default 0.7)</param>
    /// <returns>True if the contents are similar, false otherwise</returns>
    bool AreSimilar(string content1, string content2, double similarityThreshold = 0.8, double lengthRatioThreshold = 0.7);
}

/// <summary>
/// Default implementation of text similarity comparison service.
/// </summary>
public class TextSimilarityService : ITextSimilarityService
{
    /// <inheritdoc />
    public bool AreSimilar(
        string content1, 
        string content2, 
        double similarityThreshold = 0.8, 
        double lengthRatioThreshold = 0.7)
    {
        if (string.IsNullOrWhiteSpace(content1) || string.IsNullOrWhiteSpace(content2))
            return false;

        // Normalize whitespace for comparison
        var normalized1 = NormalizeWhitespace(content1);
        var normalized2 = NormalizeWhitespace(content2);

        // Check if one contains the other - this should be checked FIRST
        // because substring matches are similar regardless of length difference
        if (IsSubstringMatch(normalized1, normalized2))
            return true;

        // Check length ratio - if one is much shorter than the other, they're not similar
        // (only check this if substring match failed)
        var lengthRatio = CalculateLengthRatio(content1, content2);
        if (lengthRatio < lengthRatioThreshold)
            return false;

        // Calculate word overlap similarity as final check
        var wordSimilarity = CalculateWordSimilarity(normalized1, normalized2);
        return wordSimilarity >= similarityThreshold;
    }

    /// <summary>
    /// Calculates the length ratio between two strings (smaller/larger).
    /// </summary>
    private static double CalculateLengthRatio(string content1, string content2)
    {
        var minLength = Math.Min(content1.Length, content2.Length);
        var maxLength = Math.Max(content1.Length, content2.Length);
        
        if (maxLength == 0)
            return 0.0;
            
        return (double)minLength / maxLength;
    }

    /// <summary>
    /// Normalizes whitespace in text for consistent comparison.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    /// <summary>
    /// Checks if one string is a substantial substring of another.
    /// </summary>
    private static bool IsSubstringMatch(string normalized1, string normalized2)
    {
        return normalized1.Contains(normalized2, StringComparison.OrdinalIgnoreCase) ||
               normalized2.Contains(normalized1, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Calculates word-based similarity using Jaccard similarity coefficient.
    /// </summary>
    private static double CalculateWordSimilarity(string normalized1, string normalized2)
    {
        var words1 = ExtractWords(normalized1);
        var words2 = ExtractWords(normalized2);

        if (words1.Count == 0 && words2.Count == 0)
            return 1.0; // Both empty = similar
            
        if (words1.Count == 0 || words2.Count == 0)
            return 0.0; // One empty = not similar

        var commonWords = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var totalWords = Math.Max(words1.Count, words2.Count);

        return (double)commonWords / totalWords;
    }

    /// <summary>
    /// Extracts words from normalized text.
    /// </summary>
    private static HashSet<string> ExtractWords(string normalizedText)
    {
        return normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
