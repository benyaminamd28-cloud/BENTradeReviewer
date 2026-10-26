using System.Text.Json;
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

    private bool _isLoadingJournalChecklist;
    private readonly object _journalChecklistSaveLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _journalChecklistSaveTokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _journalChecklistWriteGate = new(1, 1);
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

    [ObservableProperty] private bool _huntP3;
    [ObservableProperty] private bool _majorSwing;
    [ObservableProperty] private bool _huntP2;
    [ObservableProperty] private bool _chopP2;
    [ObservableProperty] private bool _chopP3;
    [ObservableProperty] private bool _orderBlock;
    [ObservableProperty] private bool _runningHS;
    [ObservableProperty] private bool _runningLS;
    [ObservableProperty] private bool _boxBullishDomPressure;
    [ObservableProperty] private bool _boxBearishDomPressure;
    [ObservableProperty] private bool _boxLargeVolumeByBid;
    [ObservableProperty] private bool _boxLargeVolumeByAsk;
    [ObservableProperty] private bool _boxBigTradeSweepAgBuyer;
    [ObservableProperty] private bool _boxBigTradeSweepAgSeller;
    [ObservableProperty] private bool _boxBigTradeAbsorptionAgBuyer;
    [ObservableProperty] private bool _boxBigTradeAbsorptionAgSeller;
    [ObservableProperty] private bool _boxTradeSizeConvergenceBullish;
    [ObservableProperty] private bool _boxTradeSizeConvergenceBearish;
    [ObservableProperty] private bool _boxSellTradeSizeCCBearish;
    [ObservableProperty] private bool _boxSellTradeSizeCCBullish;
    [ObservableProperty] private bool _boxBothTradeSizeCCBullish;
    [ObservableProperty] private bool _boxBothTradeSizeCCBearish;
    [ObservableProperty] private bool _boxBuyTradeSizeCCBearish;
    [ObservableProperty] private bool _boxBuyTradeSizeCCBullish;
    [ObservableProperty] private bool _footprintCoverCandle;
    [ObservableProperty] private bool _footprintDeltaConvergence10;
    [ObservableProperty] private bool _footprintDeltaDivergence8;
    [ObservableProperty] private bool _footprintAgBuyerAbsorption;
    [ObservableProperty] private bool _footprintAgSellerAbsorption;
    [ObservableProperty] private bool _footprintAgBuyerExpulsion;
    [ObservableProperty] private bool _footprintBullishValueLow;
    [ObservableProperty] private bool _footprintBullishValueMid;
    [ObservableProperty] private bool _footprintBullishValueTop;
    [ObservableProperty] private bool _footprintBearishValueLow;
    [ObservableProperty] private bool _footprintBearishValueMid;
    [ObservableProperty] private bool _footprintBearishValueTop;
    [ObservableProperty] private bool _isJournalChecklistLoaded;
    [ObservableProperty] private string _checklistSaveStatus = string.Empty;
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

    partial void OnHuntP3Changed(bool value) => QueueJournalChecklistSave();
    partial void OnMajorSwingChanged(bool value) => QueueJournalChecklistSave();
    partial void OnHuntP2Changed(bool value) => QueueJournalChecklistSave();
    partial void OnChopP2Changed(bool value) => QueueJournalChecklistSave();
    partial void OnChopP3Changed(bool value) => QueueJournalChecklistSave();
    partial void OnOrderBlockChanged(bool value) => QueueJournalChecklistSave();
    partial void OnRunningHSChanged(bool value) => QueueJournalChecklistSave();
    partial void OnRunningLSChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBullishDomPressureChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBearishDomPressureChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxLargeVolumeByBidChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxLargeVolumeByAskChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBigTradeSweepAgBuyerChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBigTradeSweepAgSellerChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBigTradeAbsorptionAgBuyerChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBigTradeAbsorptionAgSellerChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxTradeSizeConvergenceBullishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxTradeSizeConvergenceBearishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxSellTradeSizeCCBearishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxSellTradeSizeCCBullishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBothTradeSizeCCBullishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBothTradeSizeCCBearishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBuyTradeSizeCCBearishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnBoxBuyTradeSizeCCBullishChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintCoverCandleChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintDeltaConvergence10Changed(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintDeltaDivergence8Changed(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintAgBuyerAbsorptionChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintAgSellerAbsorptionChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintAgBuyerExpulsionChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBullishValueLowChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBullishValueMidChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBullishValueTopChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBearishValueLowChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBearishValueMidChanged(bool value) => QueueJournalChecklistSave();
    partial void OnFootprintBearishValueTopChanged(bool value) => QueueJournalChecklistSave();
    private sealed class JournalChecklistSnapshot
    {
        public bool MajorSwing { get; set; }
        public bool HuntP3 { get; set; }
        public bool HuntP2 { get; set; }
        public bool ChopP2 { get; set; }
        public bool ChopP3 { get; set; }
        public bool OrderBlock { get; set; }
        public bool RunningHS { get; set; }
        public bool RunningLS { get; set; }
        public bool BoxBullishDomPressure { get; set; }
        public bool BoxBearishDomPressure { get; set; }
        public bool BoxLargeVolumeByBid { get; set; }
        public bool BoxLargeVolumeByAsk { get; set; }
        public bool BoxBigTradeSweepAgBuyer { get; set; }
        public bool BoxBigTradeSweepAgSeller { get; set; }
        public bool BoxBigTradeAbsorptionAgBuyer { get; set; }
        public bool BoxBigTradeAbsorptionAgSeller { get; set; }
        public bool BoxTradeSizeConvergenceBullish { get; set; }
        public bool BoxTradeSizeConvergenceBearish { get; set; }
        public bool BoxSellTradeSizeCCBearish { get; set; }
        public bool BoxSellTradeSizeCCBullish { get; set; }
        public bool BoxBothTradeSizeCCBullish { get; set; }
        public bool BoxBothTradeSizeCCBearish { get; set; }
        public bool BoxBuyTradeSizeCCBearish { get; set; }
        public bool BoxBuyTradeSizeCCBullish { get; set; }
        public bool FootprintCoverCandle { get; set; }
        public bool FootprintDeltaConvergence10 { get; set; }
        public bool FootprintDeltaDivergence8 { get; set; }
        public bool FootprintAgBuyerAbsorption { get; set; }
        public bool FootprintAgSellerAbsorption { get; set; }
        public bool FootprintAgBuyerExpulsion { get; set; }
        public bool FootprintBullishValueLow { get; set; }
        public bool FootprintBullishValueMid { get; set; }
        public bool FootprintBullishValueTop { get; set; }
        public bool FootprintBearishValueLow { get; set; }
        public bool FootprintBearishValueMid { get; set; }
        public bool FootprintBearishValueTop { get; set; }
    }

    private JournalChecklistSnapshot CaptureJournalChecklist() => new()
    {
        MajorSwing = MajorSwing,
        HuntP3 = HuntP3,
        HuntP2 = HuntP2,
        ChopP2 = ChopP2,
        ChopP3 = ChopP3,
        OrderBlock = OrderBlock,
        RunningHS = RunningHS,
        RunningLS = RunningLS,
        BoxBullishDomPressure = BoxBullishDomPressure,
        BoxBearishDomPressure = BoxBearishDomPressure,
        BoxLargeVolumeByBid = BoxLargeVolumeByBid,
        BoxLargeVolumeByAsk = BoxLargeVolumeByAsk,
        BoxBigTradeSweepAgBuyer = BoxBigTradeSweepAgBuyer,
        BoxBigTradeSweepAgSeller = BoxBigTradeSweepAgSeller,
        BoxBigTradeAbsorptionAgBuyer = BoxBigTradeAbsorptionAgBuyer,
        BoxBigTradeAbsorptionAgSeller = BoxBigTradeAbsorptionAgSeller,
        BoxTradeSizeConvergenceBullish = BoxTradeSizeConvergenceBullish,
        BoxTradeSizeConvergenceBearish = BoxTradeSizeConvergenceBearish,
        BoxSellTradeSizeCCBearish = BoxSellTradeSizeCCBearish,
        BoxSellTradeSizeCCBullish = BoxSellTradeSizeCCBullish,
        BoxBothTradeSizeCCBullish = BoxBothTradeSizeCCBullish,
        BoxBothTradeSizeCCBearish = BoxBothTradeSizeCCBearish,
        BoxBuyTradeSizeCCBearish = BoxBuyTradeSizeCCBearish,
        BoxBuyTradeSizeCCBullish = BoxBuyTradeSizeCCBullish,
        FootprintCoverCandle = FootprintCoverCandle,
        FootprintDeltaConvergence10 = FootprintDeltaConvergence10,
        FootprintDeltaDivergence8 = FootprintDeltaDivergence8,
        FootprintAgBuyerAbsorption = FootprintAgBuyerAbsorption,
        FootprintAgSellerAbsorption = FootprintAgSellerAbsorption,
        FootprintAgBuyerExpulsion = FootprintAgBuyerExpulsion,
        FootprintBullishValueLow = FootprintBullishValueLow,
        FootprintBullishValueMid = FootprintBullishValueMid,
        FootprintBullishValueTop = FootprintBullishValueTop,
        FootprintBearishValueLow = FootprintBearishValueLow,
        FootprintBearishValueMid = FootprintBearishValueMid,
        FootprintBearishValueTop = FootprintBearishValueTop,
    };

    private void ResetJournalChecklistValues()
    {
        _isLoadingJournalChecklist = true;
        try
        {
            MajorSwing = false;
            HuntP3 = false;
            HuntP2 = false;
            ChopP2 = false;
            ChopP3 = false;
            OrderBlock = false;
            RunningHS = false;
            RunningLS = false;
            BoxBullishDomPressure = false;
            BoxBearishDomPressure = false;
            BoxLargeVolumeByBid = false;
            BoxLargeVolumeByAsk = false;
            BoxBigTradeSweepAgBuyer = false;
            BoxBigTradeSweepAgSeller = false;
            BoxBigTradeAbsorptionAgBuyer = false;
            BoxBigTradeAbsorptionAgSeller = false;
            BoxTradeSizeConvergenceBullish = false;
            BoxTradeSizeConvergenceBearish = false;
            BoxSellTradeSizeCCBearish = false;
            BoxSellTradeSizeCCBullish = false;
            BoxBothTradeSizeCCBullish = false;
            BoxBothTradeSizeCCBearish = false;
            BoxBuyTradeSizeCCBearish = false;
            BoxBuyTradeSizeCCBullish = false;
            FootprintCoverCandle = false;
            FootprintDeltaConvergence10 = false;
            FootprintDeltaDivergence8 = false;
            FootprintAgBuyerAbsorption = false;
            FootprintAgSellerAbsorption = false;
            FootprintAgBuyerExpulsion = false;
            FootprintBullishValueLow = false;
            FootprintBullishValueMid = false;
            FootprintBullishValueTop = false;
            FootprintBearishValueLow = false;
            FootprintBearishValueMid = false;
            FootprintBearishValueTop = false;
        }
        finally { _isLoadingJournalChecklist = false; }
    }

    private void ApplyJournalChecklist(JournalChecklistSnapshot value)
    {
        _isLoadingJournalChecklist = true;
        try
        {
            MajorSwing = value.MajorSwing;
            HuntP3 = value.HuntP3;
            HuntP2 = value.HuntP2;
            ChopP2 = value.ChopP2;
            ChopP3 = value.ChopP3;
            OrderBlock = value.OrderBlock;
            RunningHS = value.RunningHS;
            RunningLS = value.RunningLS;
            BoxBullishDomPressure = value.BoxBullishDomPressure;
            BoxBearishDomPressure = value.BoxBearishDomPressure;
            BoxLargeVolumeByBid = value.BoxLargeVolumeByBid;
            BoxLargeVolumeByAsk = value.BoxLargeVolumeByAsk;
            BoxBigTradeSweepAgBuyer = value.BoxBigTradeSweepAgBuyer;
            BoxBigTradeSweepAgSeller = value.BoxBigTradeSweepAgSeller;
            BoxBigTradeAbsorptionAgBuyer = value.BoxBigTradeAbsorptionAgBuyer;
            BoxBigTradeAbsorptionAgSeller = value.BoxBigTradeAbsorptionAgSeller;
            BoxTradeSizeConvergenceBullish = value.BoxTradeSizeConvergenceBullish;
            BoxTradeSizeConvergenceBearish = value.BoxTradeSizeConvergenceBearish;
            BoxSellTradeSizeCCBearish = value.BoxSellTradeSizeCCBearish;
            BoxSellTradeSizeCCBullish = value.BoxSellTradeSizeCCBullish;
            BoxBothTradeSizeCCBullish = value.BoxBothTradeSizeCCBullish;
            BoxBothTradeSizeCCBearish = value.BoxBothTradeSizeCCBearish;
            BoxBuyTradeSizeCCBearish = value.BoxBuyTradeSizeCCBearish;
            BoxBuyTradeSizeCCBullish = value.BoxBuyTradeSizeCCBullish;
            FootprintCoverCandle = value.FootprintCoverCandle;
            FootprintDeltaConvergence10 = value.FootprintDeltaConvergence10;
            FootprintDeltaDivergence8 = value.FootprintDeltaDivergence8;
            FootprintAgBuyerAbsorption = value.FootprintAgBuyerAbsorption;
            FootprintAgSellerAbsorption = value.FootprintAgSellerAbsorption;
            FootprintAgBuyerExpulsion = value.FootprintAgBuyerExpulsion;
            FootprintBullishValueLow = value.FootprintBullishValueLow;
            FootprintBullishValueMid = value.FootprintBullishValueMid;
            FootprintBullishValueTop = value.FootprintBullishValueTop;
            FootprintBearishValueLow = value.FootprintBearishValueLow;
            FootprintBearishValueMid = value.FootprintBearishValueMid;
            FootprintBearishValueTop = value.FootprintBearishValueTop;
        }
        finally { _isLoadingJournalChecklist = false; }
    }

    private async Task LoadJournalChecklistAsync(string tradeId)
    {
        try
        {
            var json = await _repository.GetJournalChecklistJsonAsync(tradeId);
            if (ActiveTrade?.TradeId != tradeId) return;
            JournalChecklistSnapshot snapshot;
            try
            {
                snapshot = string.IsNullOrWhiteSpace(json)
                    ? new JournalChecklistSnapshot()
                    : (System.Text.Json.JsonSerializer.Deserialize<JournalChecklistSnapshot>(json) ?? new JournalChecklistSnapshot());
            }
            catch (System.Text.Json.JsonException ex)
            {
                ChecklistSaveStatus = "CHECKLIST LOAD FAILED: " + ex.Message;
                IsJournalChecklistLoaded = false;
                return;
            }
            ApplyJournalChecklist(snapshot);
            IsJournalChecklistLoaded = true;
            ChecklistSaveStatus = string.IsNullOrWhiteSpace(json) ? "Checklist ready" : "Checklist loaded";
        }
        catch (Exception ex)
        {
            if (ActiveTrade?.TradeId == tradeId)
            {
                ChecklistSaveStatus = "CHECKLIST LOAD FAILED: " + ex.Message;
                IsJournalChecklistLoaded = false;
            }
        }
    }

    private void QueueJournalChecklistSave()
    {
        if (_isLoadingJournalChecklist || !IsJournalChecklistLoaded || ActiveTrade is null) return;
        var tradeId = ActiveTrade.TradeId;
        var json = System.Text.Json.JsonSerializer.Serialize(CaptureJournalChecklist());
        CancellationTokenSource cts;
        lock (_journalChecklistSaveLock)
        {
            if (_journalChecklistSaveTokens.TryGetValue(tradeId, out var previous)) previous.Cancel();
            cts = new CancellationTokenSource();
            _journalChecklistSaveTokens[tradeId] = cts;
        }
        ChecklistSaveStatus = "Saving checklist...";
        _ = SaveJournalChecklistAfterDelayAsync(tradeId, json, cts);
    }

    private async Task SaveJournalChecklistAfterDelayAsync(string tradeId, string json, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(250, cts.Token);
            await _journalChecklistWriteGate.WaitAsync(cts.Token);
            try { await _repository.SaveJournalChecklistJsonAsync(tradeId, json); }
            finally { _journalChecklistWriteGate.Release(); }
            if (ActiveTrade?.TradeId == tradeId) ChecklistSaveStatus = "Checklist saved " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ActiveTrade?.TradeId == tradeId) ChecklistSaveStatus = "CHECKLIST SAVE FAILED: " + ex.Message;
        }
        finally
        {
            lock (_journalChecklistSaveLock)
            {
                if (_journalChecklistSaveTokens.TryGetValue(tradeId, out var current) && ReferenceEquals(current, cts))
                    _journalChecklistSaveTokens.Remove(tradeId);
            }
            cts.Dispose();
        }
    }

    public void LoadDay(DateTime date, List<Trade> trades)
    {
        CurrentDate = date;
        DayTrades = new ObservableCollection<Trade>(trades);
        DaySummary = AnalyticsEngine.CalculateSummary(trades);
        if (DayTrades.Count > 0)
        {
            SelectTrade(DayTrades[0]);
        }
        else
        {
            ActiveTrade = null;
            ResetJournalChecklistValues();
            IsJournalChecklistLoaded = false;
            ChecklistSaveStatus = string.Empty;
            SyncEngine.Stop();
        }
    }

    public void SelectTrade(Trade trade)
    {
        ActiveTrade = trade;
        NotesText = trade.Notes;
        TagsList = new ObservableCollection<string>(trade.Tags);
        ResetJournalChecklistValues();
        IsJournalChecklistLoaded = false;
        ChecklistSaveStatus = "Loading checklist...";
        _ = LoadJournalChecklistAsync(trade.TradeId);
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
