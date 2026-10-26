/// <summary>
/// Reads the repository's local <c>.env</c> file, the same file Docker Compose interpolates, so one file
/// configures both launch paths. Keys use environment-variable form (<c>Bedrock__ApiKey</c>) and become
/// configuration keys (<c>Bedrock:ApiKey</c>). Blank values are skipped so placeholders never override
/// other sources. There is no variable expansion or escape processing.
/// </summary>
internal static class DotEnvFile
{
    /// <summary>Parses the file, or returns nothing when it does not exist.</summary>
    /// <exception cref="InvalidOperationException">A line is not <c>KEY=VALUE</c>; only its number is reported, never its content.</exception>
    public static Dictionary<string, string?> Read(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;

        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export", StringComparison.Ordinal) && line.Length > 6 && char.IsWhiteSpace(line[6]))
                line = line[6..].TrimStart();

            var separator = line.IndexOf('=');
            var key = separator > 0 ? line[..separator].Trim() : "";
            if (key.Length == 0 || key.Any(char.IsWhiteSpace))
                throw new InvalidOperationException($"Invalid line {number} in {Path.GetFileName(path)}. Use KEY=VALUE.");

            var value = Unquote(line[(separator + 1)..].Trim());
            if (value.Length > 0) values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }
        return values;
    }

    private static string Unquote(string value)
    {
        // A quoted value ends at its closing quote; anything after it (such as a comment) is ignored.
        if (value.Length >= 2 && value[0] is '"' or '\'' && value.IndexOf(value[0], 1) is var close and > 0)
            return value[1..close];
        // As in Compose, an unquoted value ends at a comment preceded by whitespace.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }
}
