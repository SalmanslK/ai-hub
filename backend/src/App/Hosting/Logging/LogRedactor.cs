using System.Collections;
using System.Text.RegularExpressions;

namespace App.Api.Hosting.Logging;

// Masks sensitive data in a log entry: a structured property whose key names a secret or
// personal data, and anything in a value or message that looks like an email address, a
// bearer credential or a `password=…` assignment. Keys catch what a caller forgot to tag.
public static partial class LogRedactor
{
    public const string Marker = "[REDACTED]";
    private const string OriginalFormat = "{OriginalFormat}";

    public static bool IsSensitiveKey(string key) => key != OriginalFormat && SensitiveKey().IsMatch(key);

    public static string RedactText(string text)
    {
        text = Credential().Replace(text, $"$1 {Marker}");
        text = SecretAssignment().Replace(text, $"$1={Marker}");
        return Email().Replace(text, Marker);
    }

    // A log entry's state and formatted message, redacted: what a sink may receive instead.
    public static RedactedState Redact(object? state, string message)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            return new RedactedState([], RedactText(message));

        var redacted = new List<KeyValuePair<string, object?>>();
        var hidden = new List<string>();
        foreach (var (key, value) in pairs)
        {
            if (IsSensitiveKey(key))
            {
                if (value?.ToString() is { Length: > 0 } secret)
                    hidden.Add(secret);
                redacted.Add(new(key, Marker));
                continue;
            }
            var text = value?.ToString();
            var clean = text is null ? null : RedactText(text);
            redacted.Add(new(key, clean == text ? value : clean));
        }

        if (hidden.Count > 0)
            message = Render(redacted) ?? hidden.OrderByDescending(s => s.Length)
                .Aggregate(message, (current, secret) => current.Replace(secret, Marker, StringComparison.Ordinal));
        return new RedactedState(redacted, RedactText(message));
    }

    // A scope's state, redacted the same way; a scope without properties becomes its text.
    public static object? RedactScope(object? scope) => scope switch
    {
        null => null,
        IEnumerable<KeyValuePair<string, object?>> => Redact(scope, scope.ToString() ?? ""),
        _ => RedactText(scope.ToString() ?? "") is var text && text == scope.ToString() ? scope : text,
    };

    public static Exception? Redact(Exception? exception)
    {
        if (exception is null)
            return null;
        var text = exception.ToString();
        var clean = RedactText(text);
        return clean == text ? exception : new RedactedException(clean);
    }

    // Re-renders a message template with the redacted values, holes in order; null without a template.
    private static string? Render(List<KeyValuePair<string, object?>> pairs)
    {
        var template = pairs.FirstOrDefault(p => p.Key == OriginalFormat).Value as string;
        if (template is null)
            return null;
        var values = pairs.Where(p => p.Key != OriginalFormat).Select(p => p.Value).ToList();
        var index = 0;
        return Hole().Replace(template, match => match.Value switch
        {
            "{{" => "{",
            "}}" => "}",
            _ => index < values.Count ? values[index++]?.ToString() ?? "(null)" : match.Value,
        });
    }

    [GeneratedRegex(@"password|passwd|pwd|secret|token|authorization|api[_-]?key|connectionstring|credential|cookie|e-?mail",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKey();

    [GeneratedRegex(@"\b(Bearer|Basic)\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credential();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|client_secret|token|access_token|refresh_token|api[_-]?key)\s*[=:]\s*[^&;,\s""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\{\{|\}\}|\{[^{}]+\}")]
    private static partial Regex Hole();
}

// The structured properties and message of a redacted entry or scope.
public sealed class RedactedState(IReadOnlyList<KeyValuePair<string, object?>> pairs, string text)
    : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => pairs.Count;

    public KeyValuePair<string, object?> this[int index] => pairs[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => pairs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => text;
}

// Stands in for an exception whose text held sensitive data; carries the redacted text only.
public sealed class RedactedException(string text) : Exception(text)
{
    public override string? StackTrace => null;

    public override string ToString() => Message;
}
