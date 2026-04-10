using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GhsMarkdown.Cross.Services;

public enum GhsTheme { Dark, Light, Auto }

public class ThemeService : ObservableObject
{
    private static readonly Uri DarkUri  = new("avares://GhsMarkdown.Cross/Themes/GhsDark.axaml");
    private static readonly Uri LightUri = new("avares://GhsMarkdown.Cross/Themes/GhsLight.axaml");

    private readonly SettingsService _settingsService;

    // Track the currently loaded theme dictionary so we can remove it cleanly
    private IResourceProvider? _activeThemeDict;

    private GhsTheme _currentTheme = GhsTheme.Dark;

    public GhsTheme CurrentTheme => _currentTheme;

    public string CurrentThemeName => ResolveTheme() switch
    {
        GhsTheme.Dark  => "GHS Dark",
        GhsTheme.Light => "GHS Light",
        _              => "GHS Dark"
    };

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = _settingsService.Load();
        _currentTheme = settings.Theme switch
        {
            "Light"  => GhsTheme.Light,
            "Ink"    => GhsTheme.Light,   // migration: old "Ink" → Light
            "Glass"  => GhsTheme.Dark,    // migration: old "Glass" → Dark
            "GHSInk" => GhsTheme.Light,   // migration: old saved value
            "GHSGlass" => GhsTheme.Dark,  // migration: old saved value
            "Auto"   => GhsTheme.Auto,
            _        => GhsTheme.Dark
        };
    }

    public event EventHandler? ThemeChanged;

    public void SetTheme(GhsTheme theme)
    {
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
            GhsTheme.Light => GetLightCss(),
            _              => GetDarkCss()
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
            GhsTheme.Light => LightUri,
            _              => DarkUri
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Remove any existing GHS theme ResourceIncludes
        var toRemove = merged
            .OfType<Avalonia.Markup.Xaml.Styling.ResourceInclude>()
            .Where(r => r.Source == DarkUri || r.Source == LightUri)
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
          --bg-shell: #141414;
          --bg-panel: #181818;
          --bg-toolbar: #1A1A1A;
          --bg-gutter: #161616;
          --bg-editor: #141414;
          --bg-preview: #161616;
          --accent: #4A9EFF;
          --accent-dim: #1E3A5F;
          --border: #222222;
          --border-subtle: #1E1E1E;
          --text-primary: #E8E8E8;
          --text-secondary: #888888;
          --text-hint: #444444;
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
