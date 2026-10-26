using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BENTradeReviewer.Replay;

public sealed class VideoSyncEngine : IDisposable
{
    private readonly List<MediaElement> _players = new();
    private readonly DispatcherTimer _clockTimer;

    private double _currentPosition;
    private double _totalDuration = 1;
    private double _speed = 1;
    private bool _playing;

    public event Action<double, double>? PositionChanged;
    public event Action<bool>? PlayStateChanged;

    public bool IsPlaying => _playing;

    public VideoSyncEngine()
    {
        _clockTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _clockTimer.Tick += (_, _) => Tick();
    }

    public void RegisterPlayer(MediaElement player)
    {
        if (_players.Contains(player))
            return;

        player.LoadedBehavior = MediaState.Manual;
        player.UnloadedBehavior = MediaState.Manual;
        player.SpeedRatio = _speed;

        player.MediaOpened += Player_MediaOpened;

        _players.Add(player);
    }

    public void UnregisterAll()
    {
        Stop();

        foreach (var player in _players)
        {
            player.MediaOpened -= Player_MediaOpened;
        }

        _players.Clear();
    }

    public void LoadVideos(IReadOnlyList<string> paths)
    {
        for (var i = 0; i < _players.Count; i++)
        {
            var player = _players[i];

            player.Stop();
            player.Source = null;

            if (i < paths.Count && File.Exists(paths[i]))
            {
                player.Source = new Uri(
                    paths[i],
                    UriKind.Absolute);
            }
        }

        _currentPosition = 0;
        PositionChanged?.Invoke(
            _currentPosition,
            _totalDuration);
    }

    public void SetDuration(double seconds)
    {
        _totalDuration = Math.Max(0.1, seconds);

        PositionChanged?.Invoke(
            _currentPosition,
            _totalDuration);
    }

    public void Play()
    {
        if (_playing)
            return;

        foreach (var player in _players)
            player.Play();

        _playing = true;
        _clockTimer.Start();

        PlayStateChanged?.Invoke(true);
    }

    public void Pause()
    {
        if (!_playing)
            return;

        foreach (var player in _players)
            player.Pause();

        _playing = false;
        _clockTimer.Stop();

        PlayStateChanged?.Invoke(false);
    }

    public void TogglePlayPause()
    {
        if (_playing)
            Pause();
        else
            Play();
    }

    public void Stop()
    {
        if (_playing)
            Pause();
        else
            _clockTimer.Stop();

        SeekTo(0);

        foreach (var player in _players)
            player.Stop();
    }

    public void SeekTo(double seconds)
    {
        _currentPosition = Math.Clamp(
            seconds,
            0,
            _totalDuration);

        var target =
            TimeSpan.FromSeconds(_currentPosition);

        foreach (var player in _players)
            player.Position = target;

        PositionChanged?.Invoke(
            _currentPosition,
            _totalDuration);
    }

    public void StepFrames(
        int frames,
        double assumedFps = 30)
    {
        Pause();

        SeekTo(
            _currentPosition +
            frames / Math.Max(1, assumedFps));
    }

    public void SetSpeed(double speed)
    {
        _speed = Math.Clamp(
            speed,
            0.25,
            4);

        foreach (var player in _players)
            player.SpeedRatio = _speed;
    }

    private void Player_MediaOpened(
        object? sender,
        System.Windows.RoutedEventArgs e)
    {
        if (sender is not MediaElement player)
            return;

        if (player != _players.FirstOrDefault())
            return;

        if (!player.NaturalDuration.HasTimeSpan)
            return;

        var realDuration =
            player.NaturalDuration.TimeSpan.TotalSeconds;

        if (realDuration <= 0)
            return;

        _totalDuration = realDuration;

        _currentPosition = Math.Clamp(
            _currentPosition,
            0,
            _totalDuration);

        PositionChanged?.Invoke(
            _currentPosition,
            _totalDuration);
    }

    private void Tick()
    {
        if (!_playing)
            return;

        if (_players.Count > 0 &&
            _players[0].NaturalDuration.HasTimeSpan)
        {
            // Real media position.
            _currentPosition =
                _players[0].Position.TotalSeconds;

            // Keep total duration synchronized with
            // the actual first video's duration.
            var naturalSeconds =
                _players[0]
                    .NaturalDuration
                    .TimeSpan
                    .TotalSeconds;

            if (naturalSeconds > 0 &&
                Math.Abs(
                    naturalSeconds -
                    _totalDuration) > 0.01)
            {
                _totalDuration = naturalSeconds;
            }
        }
        else
        {
            _currentPosition +=
                _clockTimer.Interval.TotalSeconds *
                _speed;
        }

        if (_currentPosition >= _totalDuration)
        {
            Pause();
            SeekTo(_totalDuration);
            return;
        }

        foreach (var player in _players)
        {
            if (Math.Abs(
                    player.Position.TotalSeconds -
                    _currentPosition) > 0.10)
            {
                player.Position =
                    TimeSpan.FromSeconds(
                        _currentPosition);
            }
        }

        PositionChanged?.Invoke(
            _currentPosition,
            _totalDuration);
    }

    public void Dispose()
    {
        _clockTimer.Stop();

        foreach (var player in _players)
        {
            player.MediaOpened -= Player_MediaOpened;

            try
            {
                player.Stop();
            }
            catch
            {
                // Ignore player shutdown errors.
            }
        }

        _players.Clear();
    }
}
