using System.Text;
using System.Text.RegularExpressions;

namespace RSBotWorks;

public static class NameSanitizer
{
    private const int MaxNameLength = 100;
    private static readonly Regex ValidNamePattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex InvalidCharactersPattern = new(@"[^a-zA-Z0-9_-]+", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes a participant name to be safe for use in AI systems.
    /// Removes spaces, special characters, and limits length.
    /// </summary>
    /// <param name="participantName">The name to sanitize</param>
    /// <returns>A sanitized name containing only alphanumeric characters, underscores, and hyphens</returns>
    /// <exception cref="ArgumentNullException">Thrown when participantName is null</exception>
    public static string SanitizeName(string participantName)
    {
        ArgumentNullException.ThrowIfNull(participantName, nameof(participantName));

        string withoutSpaces = participantName.Replace(" ", "_");
        string normalized = withoutSpaces.Normalize(NormalizationForm.FormD);
        string safeName = InvalidCharactersPattern.Replace(normalized, "");
        
        if (safeName.Length > MaxNameLength)
            safeName = safeName.Substring(0, MaxNameLength);

        safeName = safeName.Trim('_');
        return safeName;
    }

    /// <summary>
    /// Validates that a name contains only allowed characters.
    /// </summary>
    /// <param name="name">The name to validate</param>
    /// <returns>True if the name is valid, false otherwise</returns>
    public static bool IsValidName(string name)
    {
        return !string.IsNullOrEmpty(name) && ValidNamePattern.IsMatch(name);
    }
}