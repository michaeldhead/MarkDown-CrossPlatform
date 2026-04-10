using System.Text.Json;
using GhsMarkdown.Cross.Models;

namespace GhsMarkdown.Cross.Services;

public class SnippetService
{
    private readonly string _filePath;
    private List<Snippet> _snippets = new();

    public IReadOnlyList<Snippet> Snippets => _snippets;

    public event EventHandler? SnippetsChanged;

    public SnippetService()
    {
        var dir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHSMarkdownEditor")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "GHSMarkdownEditor");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "snippets.json");
    }

    public async Task LoadAsync(string? path = null)
    {
        var loadPath = path is not null && path.Length > 0
            ? Path.Combine(path, "snippets.json")
            : _filePath;
        try
        {
            if (File.Exists(loadPath))
            {
                var json = await File.ReadAllTextAsync(loadPath);
                var loaded = JsonSerializer.Deserialize<List<Snippet>>(json);
                if (loaded is { Count: > 0 })
                {
                    _snippets = loaded;
                    SnippetsChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
        catch { /* malformed — fall through to seed */ }

        _snippets = GetSeedSnippets();
        _ = SaveAsync();
    }

    public Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_snippets, new JsonSerializerOptions { WriteIndented = true });
        return Task.Run(() => File.WriteAllText(_filePath, json));
    }

    public void Add(Snippet snippet)
    {
        _snippets.Add(snippet);
        _ = SaveAsync();
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Snippet snippet)
    {
        var idx = _snippets.FindIndex(s => s.Id == snippet.Id);
        if (idx >= 0)
            _snippets[idx] = snippet;
        _ = SaveAsync();
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        _snippets.RemoveAll(s => s.Id == id);
        _ = SaveAsync();
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static List<Snippet> GetSeedSnippets() => new()
    {
        new() { Title = "Link",           Category = "Formatting", Body = "[$1]($2)" },
        new() { Title = "Image",          Category = "Formatting", Body = "![$1]($2)" },
        new() { Title = "Bold",           Category = "Formatting", Body = "**$1**" },
        new() { Title = "Inline Code",    Category = "Formatting", Body = "`$1`" },
        new() { Title = "Code Block",     Category = "Formatting", Body = "```$1\n$2\n```" },
        new() { Title = "Heading 2",      Category = "Structure",  Body = "## $1\n\n$2" },
        new() { Title = "Table (3 col)",  Category = "Structure",  Body = "| $1 | $2 | $3 |\n| --- | --- | --- |\n| $4 | $5 | $6 |" },
        new() { Title = "Frontmatter",    Category = "Structure",  Body = "---\ntitle: $1\ndate: $2\ntags: [$3]\n---\n\n$0" },
        new() { Title = "Blockquote",     Category = "Formatting", Body = "> $1" },
        new() { Title = "Task List",      Category = "Structure",  Body = "- [ ] $1\n- [ ] $2\n- [ ] $3" },
    };
}
