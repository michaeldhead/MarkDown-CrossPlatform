using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GhsMarkdown.Cross.Services;

public class JumpListService
{
    public void UpdateJumpList(IEnumerable<string> recentFilePaths)
    {
        if (!OperatingSystem.IsWindows()) return;

        var files = recentFilePaths.Where(File.Exists).Take(10).ToList();
        Debug.WriteLine($"[JumpList] UpdateJumpList called with {files.Count} files");

        if (files.Count == 0) return;

        try
        {
            UpdateJumpListWindows(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JumpList] Error: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void UpdateJumpListWindows(List<string> files)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? "";

        if (string.IsNullOrEmpty(exePath)) return;

        // Create the destination list COM object
        var destList = (ICustomDestinationList)new DestinationList();

        // Begin building the list
        destList.BeginList(out _, typeof(IObjectArray).GUID, out _);

        // Create a collection for our items
        var collection = (IObjectCollection)new EnumerableObjectCollection();

        foreach (var filePath in files)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exePath);
            link.SetArguments($"\"{filePath}\"");
            link.SetDescription(Path.GetFileName(filePath));
            link.SetIconLocation(exePath, 0);

            // Set the title via IPropertyStore
            if (link is IPropertyStore propStore)
            {
                var titleKey = new PropertyKey(
                    new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2); // System.Title
                var pv = new PropVariant(Path.GetFileName(filePath));
                propStore.SetValue(ref titleKey, ref pv);
                propStore.Commit();
                pv.Clear();
            }

            collection.AddObject(link);
        }

        destList.AppendCategory("Recent Files", (IObjectArray)collection);
        destList.CommitList();

        Debug.WriteLine($"[JumpList] CommitList succeeded with {files.Count} items");
    }

    // ─── COM Interop ─────────────────────────────────────────────────────────

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    [SupportedOSPlatform("windows")]
    private class DestinationList { }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    [SupportedOSPlatform("windows")]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    [SupportedOSPlatform("windows")]
    private class ShellLink { }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SupportedOSPlatform("windows")]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMinSlots,
                       [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                       [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory(
            [MarshalAs(UnmanagedType.LPWStr)] string pszCategory,
            [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
        void CommitList();
    }

    [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SupportedOSPlatform("windows")]
    private interface IObjectArray
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                   [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SupportedOSPlatform("windows")]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint pcObjects);
        new void GetAt(uint uiIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                       [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SupportedOSPlatform("windows")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] out string pszFile,
                     int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] out string pszName,
                            int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] out string pszDir,
                                 int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] out string pszArgs,
                          int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] out string pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SupportedOSPlatform("windows")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant propvar);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    [SupportedOSPlatform("windows")]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Sequential)]
    [SupportedOSPlatform("windows")]
    private struct PropVariant
    {
        public ushort vt;
        private ushort _pad1, _pad2, _pad3;
        public IntPtr ptr;

        public PropVariant(string value)
        {
            vt = 31; // VT_LPWSTR
            _pad1 = _pad2 = _pad3 = 0;
            ptr = Marshal.StringToCoTaskMemUni(value);
        }

        public void Clear()
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
                ptr = IntPtr.Zero;
            }
        }
    }
}
