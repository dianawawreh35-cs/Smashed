using System.Windows;
using System.Windows.Media;
using CallCenter.AgentApp.Audio;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The holds in a recording, drawn as marks under its seek bar (A-51).
/// </summary>
/// <remarks>
/// A hold records as silence on both sides: the PBX plays its music to the
/// customer and sends the laptop nothing. Marked here so a stretch of silence
/// reads as "on hold" rather than as a dead line.
///
/// Drawn rather than built from controls: a handful of rectangles at fractions
/// of the width is simpler to draw than to lay out. In Arabic the whole player
/// is mirrored, so the marks follow the seek bar from the right with no extra
/// work.
/// </remarks>
public sealed class HoldBand : FrameworkElement
{
    public static readonly DependencyProperty HoldsProperty = DependencyProperty.Register(
        nameof(Holds), typeof(IReadOnlyList<HoldPeriod>), typeof(HoldBand),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds), typeof(double), typeof(HoldBand),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(HoldBand),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HoldPeriod>? Holds
    {
        get => (IReadOnlyList<HoldPeriod>?)GetValue(HoldsProperty);
        set => SetValue(HoldsProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Holds is not { Count: > 0 } holds || DurationSeconds <= 0 || ActualWidth <= 0)
        {
            return;
        }

        foreach (var hold in holds)
        {
            var x = hold.Start.TotalSeconds / DurationSeconds * ActualWidth;

            // At least a few pixels, so a two-second hold in an hour-long call
            // is still there to see.
            var width = Math.Max(3, hold.Length.TotalSeconds / DurationSeconds * ActualWidth);

            drawing.DrawRoundedRectangle(
                Fill, null, new Rect(x, 0, Math.Min(width, ActualWidth - x), ActualHeight), 2, 2);
        }
    }
}
