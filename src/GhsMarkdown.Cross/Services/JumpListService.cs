using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GhsMarkdown.Cross.Services;

public class JumpListService
{
    /// <summary>
    /// Updates the Windows taskbar Jump List with recent files.
    /// No-op on non-Windows platforms.
    /// </summary>
    public void UpdateJumpList(IEnumerable<string> recentFilePaths)
    {
        if (!OperatingSystem.IsWindows()) return;
        UpdateJumpListWindows(recentFilePaths);
    }

    [SupportedOSPlatform("windows")]
    private static void UpdateJumpListWindows(IEnumerable<string> recentFilePaths)
    {
        try
        {
            foreach (var path in recentFilePaths.Take(10))
            {
                if (File.Exists(path))
                    NativeMethods.SHAddToRecentDocs(NativeMethods.SHARD_PATHW, path);
            }
        }
        catch
        {
            // Silently ignore — Jump List is non-critical
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        public const uint SHARD_PATHW = 0x00000003;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHAddToRecentDocs(uint uFlags, string pv);
    }
}
