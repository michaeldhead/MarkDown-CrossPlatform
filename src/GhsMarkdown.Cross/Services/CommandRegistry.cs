namespace GhsMarkdown.Cross.Services;

public record CommandDescriptor(
    string Id,
    string Title,
    string Category,
    Action Execute,
    string? KeyboardShortcut = null,
    string? Hint = null)
{
    /// <summary>Right-side text rendered in the Command Palette row.
    /// Prefers the explicit Hint; falls back to KeyboardShortcut when no hint is supplied.</summary>
    public string? DisplayHint => string.IsNullOrEmpty(Hint) ? KeyboardShortcut : Hint;
}

public class CommandRegistry
{
    private readonly List<CommandDescriptor> _commands = new();
    private readonly List<string> _recent = new();
    private const int MaxRecent = 10;

    public void Register(CommandDescriptor cmd)
    {
        _commands.RemoveAll(c => c.Id == cmd.Id);
        _commands.Add(cmd);
    }

    public IEnumerable<CommandDescriptor> GetAll() => _commands.AsReadOnly();

    public IEnumerable<CommandDescriptor> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _commands.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);

        var results = new List<(CommandDescriptor Cmd, int Score)>();
        var q = query.Trim();

        foreach (var cmd in _commands)
        {
            var haystack = cmd.Title + " " + cmd.Category;
            var score = FuzzyScore(q, haystack);
            if (score >= 0)
                results.Add((cmd, score));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Cmd.Title, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Cmd);
    }

    public void Execute(string commandId)
    {
        var cmd = _commands.FirstOrDefault(c => c.Id == commandId)
            ?? throw new KeyNotFoundException($"Command '{commandId}' not found.");

        _recent.Remove(commandId);
        _recent.Insert(0, commandId);
        if (_recent.Count > MaxRecent)
            _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);

        cmd.Execute();
    }

    public IEnumerable<CommandDescriptor> GetRecent(int count)
    {
        return _recent
            .Take(count)
            .Select(id => _commands.FirstOrDefault(c => c.Id == id))
            .OfType<CommandDescriptor>();
    }

    // ─── Fuzzy scoring ───────────────────────────────────────────────────────
    // Returns -1 if no match. 2 = prefix match, 1 = contiguous, 0 = subsequence.

    private static int FuzzyScore(string query, string haystack)
    {
        var qLower = query.ToLowerInvariant();
        var hLower = haystack.ToLowerInvariant();

        // Check contiguous substring first (includes prefix)
        var idx = hLower.IndexOf(qLower, StringComparison.Ordinal);
        if (idx >= 0)
        {
            // Prefix bonus: matches at start of a word
            if (idx == 0 || haystack[idx - 1] == ' ' || haystack[idx - 1] == ':')
                return 2;
            return 1;
        }

        // Subsequence match
        int qi = 0;
        for (int hi = 0; hi < hLower.Length && qi < qLower.Length; hi++)
        {
            if (hLower[hi] == qLower[qi])
                qi++;
        }

        return qi == qLower.Length ? 0 : -1;
    }
}
