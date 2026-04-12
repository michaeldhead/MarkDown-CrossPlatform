using System.Runtime.InteropServices;
using System.Text.Json;

namespace GhsMarkdown.Cross.Services;

public record AppSettings
{
    public double SplitRatio { get; init; } = 0.5;
    public string ViewMode   { get; init; } = "Split";
    public string Theme      { get; init; } = "Dark";

    // Phase 9
    public string EditorFontFamily       { get; init; } = "Cascadia Code, Consolas, monospace";
    public double EditorFontSize         { get; init; } = 14.0;
    public int    AutoSaveIntervalSeconds { get; init; } = 60;
    public string SnippetLibraryPath     { get; init; } = "";
    public bool   FocusMode             { get; init; } = false;
    public bool   ShowFormattingToolbar { get; init; } = true;
    public bool   LeftPanelOpen         { get; init; } = true;
    public string ActiveIcon            { get; init; } = "Topology";
    public bool   WordWrap             { get; init; } = false;
    public bool   ShowLineNumbers      { get; init; } = true;
    public bool   HighlightCurrentLine { get; init; } = true;
    public string AnthropicApiKey        { get; init; } = "";
    public List<string> RecentFiles      { get; init; } = new();
    public Dictionary<string, string> CustomThemeColors { get; init; } = new();

    // BL-03: Detached preview window position/size
    public double DetachedPreviewX      { get; init; } = 100;
    public double DetachedPreviewY      { get; init; } = 100;
    public double DetachedPreviewWidth  { get; init; } = 800;
    public double DetachedPreviewHeight { get; init; } = 600;
}

public class SettingsService
{
    private static readonly string SettingsPath = BuildSettingsPath();

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Load failed: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Save failed: {ex.Message}");
            }
        });
    }

    private static string BuildSettingsPath()
    {
        var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "GHSMarkdownEditor", "settings.json");
    }
}
