using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BENTradeReviewer.UI.ViewModels;

namespace BENTradeReviewer.UI.Controls;

public enum TradeAnalysisChartType { Bar, Line }

/// <summary>A dependency-free WPF chart for auditable journal analytics (bar or line).</summary>
public sealed class TradeAnalysisChart : FrameworkElement
{
    public static readonly DependencyProperty ChartTitleProperty = DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata("Chart", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ChartTypeProperty = DependencyProperty.Register(nameof(ChartType), typeof(TradeAnalysisChartType), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(TradeAnalysisChartType.Bar, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IEnumerable), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(null, OnPointsChanged));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(177, 190, 208)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(49, 61, 78)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PositiveBrushProperty = DependencyProperty.Register(nameof(PositiveBrush), typeof(Brush), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(51, 196, 139)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NegativeBrushProperty = DependencyProperty.Register(nameof(NegativeBrush), typeof(Brush), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(244, 96, 111)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PanelBrushProperty = DependencyProperty.Register(nameof(PanelBrush), typeof(Brush), typeof(TradeAnalysisChart), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(18, 26, 38)), FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observable;
    public string ChartTitle { get => (string)GetValue(ChartTitleProperty); set => SetValue(ChartTitleProperty, value); }
    public TradeAnalysisChartType ChartType { get => (TradeAnalysisChartType)GetValue(ChartTypeProperty); set => SetValue(ChartTypeProperty, value); }
    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush PositiveBrush { get => (Brush)GetValue(PositiveBrushProperty); set => SetValue(PositiveBrushProperty, value); }
    public Brush NegativeBrush { get => (Brush)GetValue(NegativeBrushProperty); set => SetValue(NegativeBrushProperty, value); }
    public Brush PanelBrush { get => (Brush)GetValue(PanelBrushProperty); set => SetValue(PanelBrushProperty, value); }

    public TradeAnalysisChart() { MinHeight = 210; SnapsToDevicePixels = true; }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (TradeAnalysisChart)d;
        if (chart._observable is not null) chart._observable.CollectionChanged -= chart.OnCollectionChanged;
        chart._observable = e.NewValue as INotifyCollectionChanged;
        if (chart._observable is not null) chart._observable.CollectionChanged += chart.OnCollectionChanged;
        chart.InvalidateVisual();
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(PanelBrush, new Pen(GridBrush, 1), bounds, 6, 6);
        DrawText(dc, ChartTitle ?? string.Empty, 13, FontWeights.SemiBold, TextBrush, new Point(14, 10));
        var values = (Points ?? Array.Empty<AnalysisChartPoint>()).Cast<object>()
            .OfType<AnalysisChartPoint>().ToList();
        if (values.Count == 0)
        {
            DrawText(dc, "No data available for the selected range", 12, FontWeights.Normal, TextBrush,
                new Point(Math.Max(16, ActualWidth / 2 - 115), Math.Max(42, ActualHeight / 2 - 8)));
            return;
        }
        var left = 58d; var right = 14d; var top = 38d; var bottom = 35d;
        var plot = new Rect(left, top, Math.Max(10, ActualWidth - left - right), Math.Max(10, ActualHeight - top - bottom));
        double min = Math.Min(0d, values.Min(p => p.Value));
        double max = Math.Max(0d, values.Max(p => p.Value));
        if (Math.Abs(max - min) < 0.000001) { max = min + 1; }
        else { var pad = (max - min) * 0.08; max += pad; min -= pad * 0.25; }
        double MapY(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;
        for (int i = 0; i <= 4; i++)
        {
            var fraction = i / 4d; var y = plot.Top + fraction * plot.Height; var val = max - fraction * (max - min);
            dc.DrawLine(new Pen(GridBrush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Math.Abs(val) >= 1000 ? val.ToString("#,0", CultureInfo.CurrentCulture) : val.ToString("0.##", CultureInfo.CurrentCulture);
            var ft = CreateText(label, 10, FontWeights.Normal, TextBrush);
            dc.DrawText(ft, new Point(Math.Max(1, left - ft.Width - 7), y - ft.Height / 2));
        }
        var zeroY = MapY(0);
        dc.DrawLine(new Pen(TextBrush, 1.1), new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));
        var slot = plot.Width / values.Count;
        if (ChartType == TradeAnalysisChartType.Bar)
        {
            for (int i = 0; i < values.Count; i++)
            {
                var p = values[i]; var x = plot.Left + slot * i + slot * 0.18;
                var y = MapY(p.Value); var topBar = Math.Min(y, zeroY); var height = Math.Max(1, Math.Abs(zeroY - y));
                var width = Math.Max(2, slot * 0.64);
                dc.DrawRoundedRectangle(p.Value < 0 ? NegativeBrush : PositiveBrush, null, new Rect(x, topBar, width, height), 2, 2);
            }
        }
        else
        {
            var pen = new Pen(PositiveBrush, 2); var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = 0; i < values.Count; i++)
                {
                    var point = new Point(plot.Left + (values.Count == 1 ? plot.Width / 2 : slot * i + slot / 2), MapY(values[i].Value));
                    if (i == 0) ctx.BeginFigure(point, false, false); else ctx.LineTo(point, true, false);
                }
            }
            geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
            for (int i = 0; i < values.Count; i++)
            {
                var point = new Point(plot.Left + (values.Count == 1 ? plot.Width / 2 : slot * i + slot / 2), MapY(values[i].Value));
                dc.DrawEllipse(PositiveBrush, null, point, 2.6, 2.6);
            }
        }
        var labelStep = Math.Max(1, (int)Math.Ceiling(values.Count / 7d));
        for (int i = 0; i < values.Count; i += labelStep)
        {
            var label = values[i].Label ?? string.Empty; if (label.Length > 11) label = label[..10] + "…";
            var ft = CreateText(label, 9, FontWeights.Normal, TextBrush);
            var x = plot.Left + (ChartType == TradeAnalysisChartType.Line ? (values.Count == 1 ? plot.Width / 2 : slot * i + slot / 2) : slot * i + slot / 2) - ft.Width / 2;
            dc.DrawText(ft, new Point(Math.Max(plot.Left, Math.Min(plot.Right - ft.Width, x)), plot.Bottom + 7));
        }
    }

    private void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point point) => dc.DrawText(CreateText(text, size, weight, brush), point);
    private FormattedText CreateText(string text, double size, FontWeight weight, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
