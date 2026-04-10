using Avalonia.Controls;
using Avalonia.Media;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class TopologyView : UserControl
{
    // Heading level → color mapping from spec
    private static readonly IBrush[] LevelBrushes =
    {
        Brushes.Transparent,                               // 0 (unused)
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)), // H1 — syntax-h1
        new SolidColorBrush(Color.FromRgb(0x5A, 0xB8, 0x65)), // H2 — syntax-h2
        new SolidColorBrush(Color.FromRgb(0xB8, 0x95, 0x4A)), // H3 — syntax-h3
        new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), // H4 — syntax-h4
        new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), // H5 — syntax-h5
        new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), // H6 — syntax-h6
    };

    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));

    public TopologyView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TopologyViewModel vm)
        {
            vm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(TopologyViewModel.ActiveNode))
                    ScrollActiveNodeIntoView();
            };
        }
    }

    private void ScrollActiveNodeIntoView()
    {
        if (DataContext is not TopologyViewModel vm || vm.ActiveNode is null) return;

        var itemsControl = this.FindControl<ItemsControl>("NodeItemsControl");
        if (itemsControl is null) return;

        var index = vm.Nodes.IndexOf(vm.ActiveNode);
        if (index < 0) return;

        var container = itemsControl.ContainerFromIndex(index);
        if (container is Control control)
        {
            control.BringIntoView();
        }
    }

    /// <summary>
    /// Returns the color brush for a given heading level (1-6).
    /// </summary>
    public static IBrush GetLevelBrush(int level)
    {
        if (level >= 1 && level <= 6) return LevelBrushes[level];
        return LevelBrushes[1];
    }

    public static IBrush GetActiveBrush() => AccentBrush;
}
