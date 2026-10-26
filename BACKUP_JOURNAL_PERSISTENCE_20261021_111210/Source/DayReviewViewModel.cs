using System.Collections.ObjectModel;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Data;
using BENTradeReviewer.Replay;
using BENTradeReviewer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BENTradeReviewer.UI.ViewModels;

public partial class DayReviewViewModel : ObservableObject, IDisposable
{
    private readonly TradeRepository _repository;
    [ObservableProperty] private DateTime _currentDate;
    [ObservableProperty] private PerformanceSummary _daySummary = new();
    [ObservableProperty] private ObservableCollection<Trade> _dayTrades = new();
    [ObservableProperty] private Trade? _activeTrade;
    [ObservableProperty] private string _notesText = string.Empty;
    [ObservableProperty] private string _newTagInput = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _tagsList = new();
    [ObservableProperty] private double _currentVideoTime;
    [ObservableProperty] private double _totalVideoDuration = 1;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _selectedPlaybackSpeed = 1;

    public VideoSyncEngine SyncEngine { get; } = new();
    public IAsyncRelayCommand SaveNotesCommand { get; }    public IAsyncRelayCommand SaveMarketContextCommand { get; }
    public IRelayCommand AddTagCommand { get; }
    public IRelayCommand<string> RemoveTagCommand { get; }
    public IRelayCommand TogglePlayPauseCommand { get; }
    public IRelayCommand<object> SeekRelativeCommand { get; }
    public IRelayCommand<object> ChangeSpeedCommand { get; }

    public DayReviewViewModel(TradeRepository repository)
    {
        _repository = repository;
        SyncEngine.PositionChanged += (current, total) => { CurrentVideoTime = current; TotalVideoDuration = total; };
        SyncEngine.PlayStateChanged += playing => IsPlaying = playing;
        SaveNotesCommand = new AsyncRelayCommand(SaveNotesAsync);        SaveMarketContextCommand = new AsyncRelayCommand(SaveMarketContextAsync);
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
        TogglePlayPauseCommand = new RelayCommand(SyncEngine.TogglePlayPause);
        SeekRelativeCommand = new RelayCommand<object>(parameter =>
        {
            if (!double.TryParse(parameter?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sec))
                return;

            SyncEngine.SeekTo(CurrentVideoTime + sec);
        });
        ChangeSpeedCommand = new RelayCommand<object>(parameter =>
        {
            if (!double.TryParse(parameter?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
                return;

            SelectedPlaybackSpeed = speed;
            SyncEngine.SetSpeed(speed);
        });
    }

    public void LoadDay(DateTime date, List<Trade> trades)
    {
        CurrentDate = date; DayTrades = new ObservableCollection<Trade>(trades); DaySummary = AnalyticsEngine.CalculateSummary(trades);
        if (DayTrades.Count > 0) SelectTrade(DayTrades[0]); else { ActiveTrade = null; SyncEngine.Stop(); }
    }

    public void SelectTrade(Trade trade)
    {
        ActiveTrade = trade; NotesText = trade.Notes; TagsList = new ObservableCollection<string>(trade.Tags);
        SyncEngine.Stop();
        var duration = Math.Max(0.1, trade.PreRollSeconds + trade.Duration.TotalSeconds + trade.PostRollSeconds);
        SyncEngine.SetDuration(duration);
        SyncEngine.LoadVideos(trade.Videos.OrderBy(v => v.MonitorIndex).Select(v => v.VideoPath).ToList());
    }

    private void AddTag()
    {
        var tag = NewTagInput.Trim();
        if (tag.Length == 0 || TagsList.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))) return;
        TagsList.Add(tag); NewTagInput = string.Empty; _ = SaveNotesAsync();
    }
    private void RemoveTag(string? tag) { if (!string.IsNullOrWhiteSpace(tag)) { TagsList.Remove(tag); _ = SaveNotesAsync(); } }
    private async Task SaveNotesAsync()
    {
        if (ActiveTrade is null) return;
        ActiveTrade.Notes = NotesText; ActiveTrade.Tags = TagsList.ToList();
        await _repository.UpdateTradeNotesAndTagsAsync(ActiveTrade.TradeId, NotesText, ActiveTrade.Tags);
    }
    private async Task SaveMarketContextAsync()
    {
        if (ActiveTrade is null)
            return;

        ActiveTrade.MarketContext ??= new TradeMarketContext();
        await _repository.UpsertTradeMarketContextAsync(
            ActiveTrade.TradeId,
            ActiveTrade.MarketContext);
    }
    public void Dispose() => SyncEngine.Dispose();
}
