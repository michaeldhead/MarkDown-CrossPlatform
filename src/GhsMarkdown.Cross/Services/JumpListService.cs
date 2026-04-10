using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GhsMarkdown.Cross.Services;

public class JumpListService
{
    public void UpdateJumpList(IEnumerable<string> recentFilePaths)
    {
        if (!OperatingSystem.IsWindows()) return;

        foreach (var path in recentFilePaths.Take(10))
        {
            try
            {
                NativeMethods.SHAddToRecentDocs(NativeMethods.SHARD_PATHW, path);
            }
            catch { }
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
