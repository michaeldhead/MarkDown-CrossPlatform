using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class TimelineScrubberView : UserControl
{
    public TimelineScrubberView()
    {
        InitializeComponent();

        var slider = this.FindControl<Slider>("TimelineSlider");
        if (slider is not null)
        {
            slider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
            slider.PropertyChanged += OnSliderValueChanged;
        }
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is TimelineViewModel vm)
            vm.BeginDrag();
    }

    private async void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is TimelineViewModel vm)
            await vm.EndDrag();
    }

    private void OnSliderValueChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name != "Value") return;
        if (DataContext is TimelineViewModel { IsDragging: true } vm && e.NewValue is double newVal)
            vm.UpdateDragPosition(newVal);
    }
}
