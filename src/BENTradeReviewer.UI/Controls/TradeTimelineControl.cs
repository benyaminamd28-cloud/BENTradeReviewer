using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BENTradeReviewer.Core.Models;

namespace BENTradeReviewer.UI.Controls;

public sealed class TradeTimelineControl : FrameworkElement
{
    public static readonly DependencyProperty CurrentPositionSecondsProperty = DependencyProperty.Register(nameof(CurrentPositionSeconds), typeof(double), typeof(TradeTimelineControl), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TotalDurationSecondsProperty = DependencyProperty.Register(nameof(TotalDurationSeconds), typeof(double), typeof(TradeTimelineControl), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveTradeProperty = DependencyProperty.Register(nameof(ActiveTrade), typeof(Trade), typeof(TradeTimelineControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public event Action<double>? UserScrubbed;
    public double CurrentPositionSeconds { get => (double)GetValue(CurrentPositionSecondsProperty); set => SetValue(CurrentPositionSecondsProperty, value); }
    public double TotalDurationSeconds { get => (double)GetValue(TotalDurationSecondsProperty); set => SetValue(TotalDurationSecondsProperty, value); }
    public Trade? ActiveTrade { get => (Trade?)GetValue(ActiveTradeProperty); set => SetValue(ActiveTradeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var w = ActualWidth; var h = ActualHeight; if (w <= 0 || h <= 0) return;
        var bg = new SolidColorBrush(Color.FromRgb(22, 27, 34)); var border = new Pen(new SolidColorBrush(Color.FromRgb(48, 54, 61)), 1); dc.DrawRoundedRectangle(bg, border, new Rect(0, 0, w, h), 4, 4);
        var total = Math.Max(0.1, TotalDurationSeconds);
        if (ActiveTrade is not null)
        {
            var entryX = ActiveTrade.PreRollSeconds / total * w; var exitX = (ActiveTrade.PreRollSeconds + ActiveTrade.Duration.TotalSeconds) / total * w;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 139, 148, 158)), null, new Rect(0, 0, Math.Clamp(entryX, 0, w), h));
            var tradeColor = ActiveTrade.IsWin ? Color.FromArgb(65, 46, 160, 67) : ActiveTrade.NetPnL < 0 ? Color.FromArgb(65, 218, 54, 51) : Color.FromArgb(45, 88, 166, 255);
            dc.DrawRectangle(new SolidColorBrush(tradeColor), null, new Rect(Math.Clamp(entryX, 0, w), 0, Math.Max(0, Math.Clamp(exitX, 0, w) - Math.Clamp(entryX, 0, w)), h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 139, 148, 158)), null, new Rect(Math.Clamp(exitX, 0, w), 0, Math.Max(0, w - Math.Clamp(exitX, 0, w)), h));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(88, 166, 255)), 2), new Point(entryX, 0), new Point(entryX, h));
            dc.DrawLine(new Pen(new SolidColorBrush(ActiveTrade.IsWin ? Color.FromRgb(63, 185, 80) : Color.FromRgb(248, 81, 73)), 2), new Point(exitX, 0), new Point(exitX, h));
        }
        var x = Math.Clamp(CurrentPositionSeconds / total * w, 0, w); var pen = new Pen(new SolidColorBrush(Color.FromRgb(240, 246, 252)), 2); dc.DrawLine(pen, new Point(x, 0), new Point(x, h)); dc.DrawEllipse(Brushes.White, null, new Point(x, h / 2), 4, 4);
    }
    protected override void OnMouseDown(MouseButtonEventArgs e) { base.OnMouseDown(e); CaptureMouse(); Scrub(e); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured) Scrub(e); }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); if (IsMouseCaptured) ReleaseMouseCapture(); }
    private void Scrub(MouseEventArgs e) { if (ActualWidth <= 0 || TotalDurationSeconds <= 0) return; var p = e.GetPosition(this); UserScrubbed?.Invoke(Math.Clamp(p.X / ActualWidth * TotalDurationSeconds, 0, TotalDurationSeconds)); }
}
