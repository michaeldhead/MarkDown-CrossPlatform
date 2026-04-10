using GhsMarkdown.Cross.Models;

namespace GhsMarkdown.Cross.Services;

public class SnapshotService
{
    private readonly string _rootDir;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SnapshotService()
    {
        var baseDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHSMarkdownEditor")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "GHSMarkdownEditor");
        _rootDir = Path.Combine(baseDir, "snapshots");
    }

    public Task SaveSnapshot(string filePath, string content)
    {
        if (string.IsNullOrEmpty(filePath)) return Task.CompletedTask;

        return Task.Run(async () =>
        {
            await _writeLock.WaitAsync();
            try
            {
                var dir = GetSnapshotDir(filePath);
                Directory.CreateDirectory(dir);
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmss");
                var snapshotPath = Path.Combine(dir, $"{timestamp}.md");
                await File.WriteAllTextAsync(snapshotPath, content);
            }
            finally
            {
                _writeLock.Release();
            }
        });
    }

    public Task<IEnumerable<Snapshot>> GetSnapshots(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult<IEnumerable<Snapshot>>(Array.Empty<Snapshot>());

        return Task.Run<IEnumerable<Snapshot>>(() =>
        {
            var dir = GetSnapshotDir(filePath);
            if (!Directory.Exists(dir))
                return Array.Empty<Snapshot>();

            var files = Directory.GetFiles(dir, "*.md");
            var snapshots = new List<Snapshot>();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(name, "yyyy-MM-ddTHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var ts))
                {
                    snapshots.Add(new Snapshot
                    {
                        FilePath = filePath,
                        Timestamp = ts,
                        SnapshotPath = file
                    });
                }
            }

            return snapshots.OrderByDescending(s => s.Timestamp).ToList();
        });
    }

    public Task<string> LoadSnapshotContent(Snapshot snapshot)
    {
        return Task.Run(() => File.ReadAllTextAsync(snapshot.SnapshotPath));
    }

    public Task Prune(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return Task.CompletedTask;

        return Task.Run(() =>
        {
            var dir = GetSnapshotDir(filePath);
            if (!Directory.Exists(dir)) return;

            var files = Directory.GetFiles(dir, "*.md")
                .Select(f => new { Path = f, Name = System.IO.Path.GetFileNameWithoutExtension(f) })
                .Where(f => DateTime.TryParseExact(f.Name, "yyyy-MM-ddTHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out _))
                .Select(f =>
                {
                    DateTime.TryParseExact(f.Name, "yyyy-MM-ddTHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var ts);
                    return new { f.Path, Timestamp = ts };
                })
                .OrderByDescending(f => f.Timestamp)
                .ToList();

            var cutoff = DateTime.UtcNow.AddDays(-7);

            // Delete files older than 7 days
            foreach (var f in files.Where(f => f.Timestamp < cutoff))
            {
                try { File.Delete(f.Path); } catch { }
            }

            // Re-enumerate remaining and cap at 200
            var remaining = files.Where(f => f.Timestamp >= cutoff).ToList();
            if (remaining.Count > 200)
            {
                foreach (var f in remaining.Skip(200))
                {
                    try { File.Delete(f.Path); } catch { }
                }
            }
        });
    }

    public Task PruneAll()
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(_rootDir)) return;

            foreach (var subDir in Directory.GetDirectories(_rootDir))
            {
                var files = Directory.GetFiles(subDir, "*.md");
                if (files.Length == 0) continue;

                // Find the filePath from any snapshot — we just need to prune the directory
                PruneDirectory(subDir);
            }
        });
    }

    private void PruneDirectory(string dir)
    {
        var files = Directory.GetFiles(dir, "*.md")
            .Select(f => new { Path = f, Name = Path.GetFileNameWithoutExtension(f) })
            .Where(f => DateTime.TryParseExact(f.Name, "yyyy-MM-ddTHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out _))
            .Select(f =>
            {
                DateTime.TryParseExact(f.Name, "yyyy-MM-ddTHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var ts);
                return new { f.Path, Timestamp = ts };
            })
            .OrderByDescending(f => f.Timestamp)
            .ToList();

        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var f in files.Where(f => f.Timestamp < cutoff))
        {
            try { File.Delete(f.Path); } catch { }
        }

        var remaining = files.Where(f => f.Timestamp >= cutoff).ToList();
        if (remaining.Count > 200)
        {
            foreach (var f in remaining.Skip(200))
            {
                try { File.Delete(f.Path); } catch { }
            }
        }
    }

    private string GetSnapshotDir(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        var sanitized = SanitizeFileName(baseName);
        var hash = fullPath.GetHashCode().ToString("X");
        var shortHash = hash.Length >= 6 ? hash[..6] : hash;
        return Path.Combine(_rootDir, $"{sanitized}_{shortHash}");
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
