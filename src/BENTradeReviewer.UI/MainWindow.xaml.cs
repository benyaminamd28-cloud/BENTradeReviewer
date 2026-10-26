using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BENTradeReviewer.UI.ViewModels;

namespace BENTradeReviewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isVideoPanning;
    private Point _videoPanStart;
    private double _videoPanStartX;
    private double _videoPanStartY;

    private const double MinVideoZoom = 1.0;
    private const double MaxVideoZoom = 10.0;
    private const double VideoZoomStep = 1.20;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.DayReviewVM.SyncEngine.RegisterPlayer(VideoPlayer0);
        TimelineControl.UserScrubbed += seconds => _viewModel.DayReviewVM.SyncEngine.SeekTo(seconds);
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _viewModel.DayReviewVM.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void TradesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is BENTradeReviewer.Core.Models.Trade trade)
            _viewModel.DayReviewVM.SelectTrade(trade);
    }

    // ============================================================
    // Composite Video Viewer
    // Mouse Wheel       = Zoom 1x .. 10x
    // Left Click + Drag = Pan
    // Double Click      = Reset
    // ============================================================

    private void VideoViewport_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        var oldZoom = VideoScale.ScaleX;

        var newZoom = e.Delta > 0
            ? Math.Min(MaxVideoZoom, oldZoom * VideoZoomStep)
            : Math.Max(MinVideoZoom, oldZoom / VideoZoomStep);

        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            e.Handled = true;
            return;
        }

        var mouse = e.GetPosition(VideoViewport);

        var centerX = VideoViewport.ActualWidth / 2.0;
        var centerY = VideoViewport.ActualHeight / 2.0;

        var oldX = VideoTranslate.X;
        var oldY = VideoTranslate.Y;

        var ratio = newZoom / oldZoom;

        var newX =
            mouse.X -
            centerX -
            ((mouse.X - centerX - oldX) * ratio);

        var newY =
            mouse.Y -
            centerY -
            ((mouse.Y - centerY - oldY) * ratio);

        VideoScale.ScaleX = newZoom;
        VideoScale.ScaleY = newZoom;

        VideoTranslate.X = newX;
        VideoTranslate.Y = newY;

        e.Handled = true;
    }

    private void VideoViewport_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        // Double-click resets the viewer.
        if (e.ClickCount == 2)
        {
            ResetVideoView();
            e.Handled = true;
            return;
        }

        // Pan is only needed when zoomed.
        if (VideoScale.ScaleX <= MinVideoZoom + 0.0001)
            return;

        _isVideoPanning = true;

        _videoPanStart =
            e.GetPosition(VideoViewport);

        _videoPanStartX = VideoTranslate.X;
        _videoPanStartY = VideoTranslate.Y;

        VideoViewport.CaptureMouse();

        Mouse.OverrideCursor = Cursors.SizeAll;

        e.Handled = true;
    }

    private void VideoViewport_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isVideoPanning ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current =
            e.GetPosition(VideoViewport);

        var dx =
            current.X - _videoPanStart.X;

        var dy =
            current.Y - _videoPanStart.Y;

        VideoTranslate.X =
            _videoPanStartX + dx;

        VideoTranslate.Y =
            _videoPanStartY + dy;

        e.Handled = true;
    }

    private void VideoViewport_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        EndVideoPan();
        e.Handled = true;
    }

    private void EndVideoPan()
    {
        if (!_isVideoPanning)
            return;

        _isVideoPanning = false;

        if (VideoViewport.IsMouseCaptured)
            VideoViewport.ReleaseMouseCapture();

        Mouse.OverrideCursor = null;
    }


    private void ResetVideoView()
    {
        EndVideoPan();

        VideoScale.ScaleX = 1.0;
        VideoScale.ScaleY = 1.0;

        VideoTranslate.X = 0.0;
        VideoTranslate.Y = 0.0;
    }

    private void TimelineControlButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        // Restore previous button to its normal style.
        if (_lastTimelineControlButton is not null &&
            !ReferenceEquals(_lastTimelineControlButton, button))
        {
            _lastTimelineControlButton.ClearValue(
                Button.BackgroundProperty);

            _lastTimelineControlButton.ClearValue(
                Button.ForegroundProperty);
        }

        // Highlight the last clicked button.
        button.Background =
            (System.Windows.Media.Brush)FindResource(
                "BrushAccentBlue");

        button.Foreground =
            (System.Windows.Media.Brush)FindResource(
                "BrushTextPrimary");

        _lastTimelineControlButton = button;
    }
    private Button? _lastTimelineControlButton;

    private void FitVideo_Click(
        object sender,
        RoutedEventArgs e)
    {
        VideoScale.ScaleX = 1.0;
        VideoScale.ScaleY = 1.0;

        VideoTranslate.X = 0.0;
        VideoTranslate.Y = 0.0;

        TimelineControlButton_Click(sender, e);
    }}
