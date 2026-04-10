namespace GhsMarkdown.Cross.Models;

public class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Category { get; set; } = "General";
    public string Body { get; set; } = "";
}
