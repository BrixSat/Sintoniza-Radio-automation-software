using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Playout.App.Controls;

/// <summary>A rectangle filled from the left to <see cref="Value"/> (0..1); used as the on-air row's background.</summary>
public sealed class ProgressFill : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<double> CornerRadiusProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(CornerRadius), 4);

    static ProgressFill() => AffectsRender<ProgressFill>(ValueProperty, FillProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width * Math.Clamp(Value, 0, 1);
        if (Fill == null || w <= 0) return;
        ctx.FillRectangle(Fill, new Rect(0, 0, w, Bounds.Height), (float)CornerRadius);
    }
}
