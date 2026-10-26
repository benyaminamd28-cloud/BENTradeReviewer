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
    private CancellationTokenSource? _notesSaveCancellation;
    private readonly SemaphoreSlim _notesSaveGate = new(1, 1);
    [ObservableProperty] private DateTime _currentDate;
    [ObservableProperty] private PerformanceSummary _daySummary = new();
    [ObservableProperty] private ObservableCollection<Trade> _dayTrades = new();
    [ObservableProperty] private Trade? _activeTrade;
    [ObservableProperty] private string _notesText = string.Empty;
    [ObservableProperty] private string _notesSaveStatus = string.Empty;
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
        var trade = ActiveTrade;
        if (trade is null) return;

        _notesSaveCancellation?.Cancel();
        _notesSaveCancellation = null;
        await PersistNotesSnapshotAsync(trade.TradeId, NotesText ?? string.Empty, TagsList.ToList(), false);
    }

    partial void OnNotesTextChanged(string value)
    {
        if (ActiveTrade is null) return;
        ScheduleNotesAutosave(value ?? string.Empty);
    }

    private void ScheduleNotesAutosave(string notes)
    {
        var trade = ActiveTrade;
        if (trade is null) return;

        _notesSaveCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _notesSaveCancellation = cts;
        var tradeId = trade.TradeId;
        var tags = TagsList.ToList();
        _ = SaveNotesAfterDelayAsync(tradeId, notes, tags, cts);
    }

    private async Task SaveNotesAfterDelayAsync(string tradeId, string notes, List<string> tags, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(800, cts.Token);
            if (cts.IsCancellationRequested) return;
            await PersistNotesSnapshotAsync(tradeId, notes, tags, true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_notesSaveCancellation, cts)) _notesSaveCancellation = null;
            cts.Dispose();
        }
    }

    private async Task PersistNotesSnapshotAsync(string tradeId, string notes, List<string> tags, bool isAutosave)
    {
        await _notesSaveGate.WaitAsync();
        try
        {
            await _repository.UpdateTradeNotesAndTagsAsync(tradeId, notes, tags);
            if (ActiveTrade?.TradeId == tradeId)
            {
                ActiveTrade.Notes = notes;
                ActiveTrade.Tags = tags;
                NotesSaveStatus = (isAutosave ? "Auto-saved " : "Saved ") + DateTime.Now.ToString("HH:mm:ss");
            }
        }
        catch (Exception ex)
        {
            if (ActiveTrade?.TradeId == tradeId)
                NotesSaveStatus = "SAVE FAILED: " + ex.Message;
            if (!isAutosave) throw;
        }
        finally
        {
            _notesSaveGate.Release();
        }
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
