using System.Diagnostics;

namespace GhsMarkdown.Cross.Services;

public enum GitLineState { Added, Modified, Deleted }

public record GitDiffHunk(int NewStart, int NewCount, int OldStart, int OldCount);

public class GitDiffService
{
    /// <summary>
    /// Returns a dictionary mapping 1-based line numbers in the current file
    /// to their diff state. Also returns deletion markers as line 0 entries
    /// keyed to the line AFTER which the deletion occurs (fractional stored
    /// as negative: -N means "deletion marker after line N").
    /// </summary>
    public async Task<Dictionary<int, GitLineState>> GetDiffAsync(string filePath)
    {
        var result = new Dictionary<int, GitLineState>();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return result;

        try
        {
            var gitPath = FindGit();
            if (gitPath is null) return result;

            var dir = Path.GetDirectoryName(filePath)!;
            var fileName = Path.GetFileName(filePath);
            var diff = await RunGitAsync(dir, new[] { "diff", "HEAD", "--", fileName });

            if (string.IsNullOrEmpty(diff))
                return result;

            ParseUnifiedDiff(diff, result);
        }
        catch
        {
            // git not installed, not a repo, or any other error — silent fail
        }

        return result;
    }

    /// <summary>
    /// Gets diff for an untracked file (all lines shown as Added).
    /// </summary>
    public async Task<Dictionary<int, GitLineState>> GetUntrackedAsync(string filePath)
    {
        var result = new Dictionary<int, GitLineState>();
        if (!File.Exists(filePath)) return result;
        var lines = await File.ReadAllLinesAsync(filePath);
        for (int i = 1; i <= lines.Length; i++)
            result[i] = GitLineState.Added;
        return result;
    }

    private static void ParseUnifiedDiff(string diff, Dictionary<int, GitLineState> result)
    {
        // Parse @@ -oldStart,oldCount +newStart,newCount @@ hunk headers
        var lines = diff.Split('\n');
        int newLine = 0;
        int oldLine = 0;

        // Track which new-file lines had a corresponding old-file line (for Modified vs Added)
        var oldLinesInHunk = new HashSet<int>();
        var newLinesInHunk = new HashSet<int>();

        GitDiffHunk? currentHunk = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@ "))
            {
                // Flush previous hunk
                FlushHunk(currentHunk, oldLinesInHunk, newLinesInHunk, result);
                oldLinesInHunk.Clear();
                newLinesInHunk.Clear();

                currentHunk = ParseHunkHeader(line);
                if (currentHunk is null) continue;

                newLine = currentHunk.NewStart;
                oldLine = currentHunk.OldStart;
                continue;
            }

            if (currentHunk is null) continue;

            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                newLinesInHunk.Add(newLine);
                newLine++;
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                oldLinesInHunk.Add(oldLine);
                oldLine++;
            }
            else if (!line.StartsWith("\\"))
            {
                newLine++;
                oldLine++;
            }
        }

        FlushHunk(currentHunk, oldLinesInHunk, newLinesInHunk, result);
    }

    private static void FlushHunk(
        GitDiffHunk? hunk,
        HashSet<int> oldLines,
        HashSet<int> newLines,
        Dictionary<int, GitLineState> result)
    {
        if (hunk is null) return;

        // Pure deletion (newCount == 0): mark deletion point after hunk start - 1
        if (newLines.Count == 0 && oldLines.Count > 0)
        {
            // Use negative key to signal deletion marker after line (hunk.NewStart - 1)
            var markerLine = Math.Max(0, hunk.NewStart - 1);
            result[-markerLine] = GitLineState.Deleted;
            return;
        }

        // Modified: hunk has both added and removed lines
        // Added: hunk has only added lines
        var state = oldLines.Count > 0 ? GitLineState.Modified : GitLineState.Added;
        foreach (var nl in newLines)
            result[nl] = state;

        // Also mark deletion point if there were removed lines alongside additions
        if (oldLines.Count > 0 && hunk.OldCount > hunk.NewCount)
        {
            // Extra deletions — mark after the last modified line
        }
    }

    private static GitDiffHunk? ParseHunkHeader(string line)
    {
        // Format: @@ -oldStart[,oldCount] +newStart[,newCount] @@
        try
        {
            var start = line.IndexOf('-');
            var end   = line.IndexOf(" @@", 3);
            if (start < 0 || end < 0) return null;

            var parts = line.Substring(start, end - start).Split(' ');
            if (parts.Length < 2) return null;

            var oldPart = parts[0].TrimStart('-').Split(',');
            var newPart = parts[1].TrimStart('+').Split(',');

            int oldStart = int.Parse(oldPart[0]);
            int oldCount = oldPart.Length > 1 ? int.Parse(oldPart[1]) : 1;
            int newStart = int.Parse(newPart[0]);
            int newCount = newPart.Length > 1 ? int.Parse(newPart[1]) : 1;

            return new GitDiffHunk(newStart, newCount, oldStart, oldCount);
        }
        catch { return null; }
    }

    private static async Task<string> RunGitAsync(string workingDir, string[] args)
    {
        var gitPath = FindGit();
        if (gitPath is null) return string.Empty;

        var psi = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null) return string.Empty;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static string? FindGit()
    {
        // 1. Try PATH first
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "git.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.Trim(), "git");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        // 2. Common Windows install locations
        var windowsPaths = new[]
        {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Git\cmd\git.exe")
        };
        foreach (var p in windowsPaths)
            if (File.Exists(p)) return p;

        return null;
    }
}
