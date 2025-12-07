namespace Automaton.Studio.Domain
{
    public static class CustomStepLanguage
    {
        public const string Python = "Python";
        public const string Batch = "Batch";

        /// <summary>
        /// Gets all supported languages for custom steps.
        /// This is the single source of truth for supported languages.
        /// </summary>
        public static IReadOnlyList<string> GetSupportedLanguages() => new[] { Python, Batch };

        /// <summary>
        /// Gets the default language for new custom steps.
        /// </summary>
        public static string GetDefaultLanguage() => Python;

        /// <summary>
        /// Checks if a language is supported.
        /// </summary>
        public static bool IsSupported(string language) =>
            GetSupportedLanguages().Contains(language, StringComparer.OrdinalIgnoreCase);
    }
}
