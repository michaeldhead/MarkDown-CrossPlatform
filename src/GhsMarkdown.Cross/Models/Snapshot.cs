namespace GhsMarkdown.Cross.Models;

public class Snapshot
{
    public required string FilePath { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string SnapshotPath { get; init; }
}
