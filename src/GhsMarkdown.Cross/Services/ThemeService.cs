using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GhsMarkdown.Cross.Services;

public enum GhsTheme { Dark, Light, Custom, Auto }

public class ThemeService : ObservableObject
{
    private static readonly Uri DarkUri   = new("avares://GhsMarkdown.Cross/Themes/GhsDark.axaml");
    private static readonly Uri LightUri = new("avares://GhsMarkdown.Cross/Themes/GhsLight.axaml");
    private static readonly Uri CustomUri = new("avares://GhsMarkdown.Cross/Themes/GhsCustom.axaml");

    private readonly SettingsService _settingsService;

    // Track the currently loaded theme dictionary so we can remove it cleanly
    private IResourceProvider? _activeThemeDict;

    private GhsTheme _currentTheme = GhsTheme.Dark;

    public GhsTheme CurrentTheme => _currentTheme;

    public string CurrentThemeName => ResolveTheme() switch
    {
        GhsTheme.Dark   => "GHS Dark",
        GhsTheme.Light  => "GHS Light",
        GhsTheme.Custom => "GHS Custom",
        _               => "GHS Dark"
    };

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = _settingsService.Load();
        _currentTheme = settings.Theme switch
        {
            "Light"    => GhsTheme.Light,
            "Custom"   => GhsTheme.Custom,
            "Ink"      => GhsTheme.Light,   // migration: old "Ink" → Light
            "Glass"    => GhsTheme.Dark,    // migration: old "Glass" → Dark
            "GHSInk"   => GhsTheme.Light,   // migration: old saved value
            "GHSGlass" => GhsTheme.Dark,    // migration: old saved value
            "Auto"     => GhsTheme.Auto,
            _          => GhsTheme.Dark
        };
    }

    public event EventHandler? ThemeChanged;

    public void SetTheme(GhsTheme theme)
    {
        // Clear custom overrides before switching to any theme
        if (_currentTheme == GhsTheme.Custom || theme != GhsTheme.Custom)
            ClearCustomColorsFromChrome();

        _currentTheme = theme;
        ApplyTheme(ResolveTheme());
        OnPropertyChanged(nameof(CurrentTheme));
        OnPropertyChanged(nameof(CurrentThemeName));
        ThemeChanged?.Invoke(this, EventArgs.Empty);

        // Persist theme setting
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { Theme = theme.ToString() });
    }

    public string GetThemeCss()
    {
        return ResolveTheme() switch
        {
            GhsTheme.Light  => GetLightCss(),
            GhsTheme.Custom => GetCustomCss(),
            _               => GetDarkCss()
        };
    }

    private GhsTheme ResolveTheme()
    {
        if (_currentTheme != GhsTheme.Auto)
            return _currentTheme;

        var variant = Application.Current?.ActualThemeVariant;
        return variant == ThemeVariant.Light ? GhsTheme.Light : GhsTheme.Dark;
    }

    private void ApplyTheme(GhsTheme resolved)
    {
        if (Application.Current is null) return;

        var uri = resolved switch
        {
            GhsTheme.Light  => LightUri,
            GhsTheme.Custom => CustomUri,
            _               => DarkUri
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Remove any existing GHS theme ResourceIncludes
        var toRemove = merged
            .OfType<Avalonia.Markup.Xaml.Styling.ResourceInclude>()
            .Where(r => r.Source == DarkUri || r.Source == LightUri || r.Source == CustomUri)
            .ToList();

        if (_activeThemeDict is not null && !toRemove.Contains(_activeThemeDict))
            toRemove.Add((Avalonia.Markup.Xaml.Styling.ResourceInclude)_activeThemeDict);

        foreach (var r in toRemove)
            merged.Remove(r);

        // Load new theme via ResourceInclude
        var newTheme = new Avalonia.Markup.Xaml.Styling.ResourceInclude(uri) { Source = uri };
        merged.Add(newTheme);
        _activeThemeDict = newTheme;
    }

    private static string GetDarkCss() => """
        :root {
          --bg-shell: #1E1E1E;
          --bg-panel: #252526;
          --bg-toolbar: #2D2D2D;
          --bg-gutter: #232323;
          --bg-editor: #1E1E1E;
          --bg-preview: #212121;
          --accent: #4A9EFF;
          --accent-dim: #1E3A5F;
          --border: #3E3E42;
          --border-subtle: #2A2A2A;
          --text-primary: #E8E8E8;
          --text-secondary: #ADADAD;
          --text-hint: #909090;
          --syntax-h1: #4A9EFF;
          --syntax-h2: #5AB865;
          --syntax-h3: #B8954A;
          --syntax-h4: #888888;
          --syntax-h5: #666666;
          --syntax-h6: #555555;
          --syntax-code: #C792EA;
          --active-block: #0D1F35;
          --active-border: #2A4A6A;
        }
        body        { background: var(--bg-preview); color: var(--text-primary); font-family: sans-serif; }
        h1          { color: var(--syntax-h1); }
        h2          { color: var(--syntax-h2); }
        h3          { color: var(--syntax-h3); }
        h4          { color: var(--syntax-h4); }
        h5          { color: var(--syntax-h5); }
        h6          { color: var(--syntax-h6); }
        code        { color: var(--syntax-code); background: var(--bg-editor); padding: 2px 5px; border-radius: 3px; }
        pre         { background: var(--bg-editor); padding: 1rem; border-radius: 6px; border-left: 3px solid var(--accent); }
        pre code    { background: none; padding: 0; }
        blockquote  { border-left: 3px solid var(--accent); padding-left: 1rem; color: var(--text-secondary); margin-left: 0; }
        a           { color: var(--accent); }
        hr          { border: none; border-top: 1px solid var(--border); margin: 1.5rem 0; }
        table       { border-collapse: collapse; width: 100%; }
        th, td      { border: 1px solid var(--border); padding: 0.5rem 1rem; }
        th          { background: var(--bg-panel); }
        .ghs-active { outline: 2px solid var(--accent); border-radius: 3px; background: var(--active-block); }
        """;

    public void NotifyThemeChanged() =>
        ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void ApplyCustomColorsToChrome(Dictionary<string, string> colors)
    {
        if (Application.Current is null) return;
        if (ResolveTheme() != GhsTheme.Custom) return;

        // Clear first to avoid stale values
        ClearCustomColorsFromChrome();

        foreach (var (key, hex) in colors)
        {
            try
            {
                if (!Avalonia.Media.Color.TryParse(hex, out var color)) continue;
                Application.Current.Resources[key] =
                    new Avalonia.Media.SolidColorBrush(color);
            }
            catch { }
        }
    }

    public void ClearCustomColorsFromChrome()
    {
        if (Application.Current is null) return;

        var keysToRemove = new[]
        {
            "bg-shell", "bg-panel", "bg-toolbar", "bg-gutter",
            "bg-editor", "bg-preview", "accent", "accent-dim",
            "border", "border-subtle", "text-primary",
            "text-secondary", "text-hint"
        };

        foreach (var key in keysToRemove)
        {
            try { Application.Current.Resources.Remove(key); }
            catch { }
        }
    }

    private string GetCustomCss()
    {
        var settings = _settingsService.Load();
        var c = settings.CustomThemeColors;

        string Get(string key, string def) =>
            c.TryGetValue(key, out var v) ? v : def;

        return $$"""
            :root {
              --bg-shell:     {{Get("bg-shell",     "#1E1E1E")}};
              --bg-panel:     {{Get("bg-panel",     "#252526")}};
              --bg-toolbar:   {{Get("bg-toolbar",   "#2D2D2D")}};
              --bg-gutter:    {{Get("bg-gutter",    "#232323")}};
              --bg-editor:    {{Get("bg-editor",    "#1E1E1E")}};
              --bg-preview:   {{Get("bg-preview",   "#212121")}};
              --accent:       {{Get("accent",       "#4A9EFF")}};
              --accent-dim:   #1E3A5F;
              --border:       {{Get("border",       "#3E3E42")}};
              --border-subtle:#2A2A2A;
              --text-primary: {{Get("text-primary", "#E8E8E8")}};
              --text-secondary:{{Get("text-secondary","#ADADAD")}};
              --text-hint:    {{Get("text-hint",    "#909090")}};
              --syntax-h1: #4A9EFF; --syntax-h2: #5AB865;
              --syntax-h3: #B8954A; --syntax-h4: #888888;
              --syntax-h5: #666666; --syntax-h6: #555555;
              --syntax-code: #C792EA;
              --active-block: #0D1F35; --active-border: #2A4A6A;
            }
            body        { background: var(--bg-preview); color: var(--text-primary); font-family: sans-serif; }
            h1          { color: var(--syntax-h1); }
            h2          { color: var(--syntax-h2); }
            h3          { color: var(--syntax-h3); }
            h4          { color: var(--syntax-h4); }
            h5          { color: var(--syntax-h5); }
            h6          { color: var(--syntax-h6); }
            code        { color: var(--syntax-code); background: var(--bg-editor); padding: 2px 5px; border-radius: 3px; }
            pre         { background: var(--bg-editor); padding: 1rem; border-radius: 6px; border-left: 3px solid var(--accent); }
            pre code    { background: none; padding: 0; }
            blockquote  { border-left: 3px solid var(--accent); padding-left: 1rem; color: var(--text-secondary); margin-left: 0; }
            a           { color: var(--accent); }
            hr          { border: none; border-top: 1px solid var(--border); margin: 1.5rem 0; }
            table       { border-collapse: collapse; width: 100%; }
            th, td      { border: 1px solid var(--border); padding: 0.5rem 1rem; }
            th          { background: var(--bg-panel); }
            .ghs-active { outline: 2px solid var(--accent); border-radius: 3px; background: var(--active-block); }
            """;
    }

    private static string GetLightCss() => """
        :root {
          --bg-shell: #F9F6F0;
          --bg-panel: #F2EFE8;
          --bg-toolbar: #EAE7E0;
          --bg-gutter: #EDE9E2;
          --bg-editor: #F9F6F0;
          --bg-preview: #F4F1EB;
          --accent: #1A6BC4;
          --accent-dim: #D0E2F7;
          --border: #D8D4CC;
          --border-subtle: #E4E0D8;
          --text-primary: #1A1A1A;
          --text-secondary: #5A5A5A;
          --text-hint: #999999;
          --syntax-h1: #1A6BC4;
          --syntax-h2: #2E7D32;
          --syntax-h3: #7B5E20;
          --syntax-h4: #5A5A5A;
          --syntax-h5: #777777;
          --syntax-h6: #888888;
          --syntax-code: #7C3AED;
          --active-block: #E0EDFF;
          --active-border: #A8C8F0;
        }
        body        { background: var(--bg-preview); color: var(--text-primary); font-family: sans-serif; }
        h1          { color: var(--syntax-h1); }
        h2          { color: var(--syntax-h2); }
        h3          { color: var(--syntax-h3); }
        h4          { color: var(--syntax-h4); }
        h5          { color: var(--syntax-h5); }
        h6          { color: var(--syntax-h6); }
        code        { color: var(--syntax-code); background: var(--bg-editor); padding: 2px 5px; border-radius: 3px; }
        pre         { background: var(--bg-editor); padding: 1rem; border-radius: 6px; border-left: 3px solid var(--accent); }
        pre code    { background: none; padding: 0; }
        blockquote  { border-left: 3px solid var(--accent); padding-left: 1rem; color: var(--text-secondary); margin-left: 0; }
        a           { color: var(--accent); }
        hr          { border: none; border-top: 1px solid var(--border); margin: 1.5rem 0; }
        table       { border-collapse: collapse; width: 100%; }
        th, td      { border: 1px solid var(--border); padding: 0.5rem 1rem; }
        th          { background: var(--bg-panel); }
        .ghs-active { outline: 2px solid var(--accent); border-radius: 3px; background: var(--active-block); }
        """;

}
