using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BENTradeReviewer;

public partial class SplashWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _fading;

    public SplashWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        _timer.Tick += Timer_Tick;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_fading)
            return;

        _fading = true;
        _timer.Stop();

        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromSeconds(3)
        };

        animation.Completed += FadeCompleted;
        BeginAnimation(OpacityProperty, animation);
    }

    private void FadeCompleted(object? sender, EventArgs e)
    {
        Close();
    }
}
