using System.Collections.ObjectModel;
using System.Text.Json;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BENTradeReviewer.UI.ViewModels;

public sealed record AnalysisChartPoint(string Label, double Value);

public sealed class AnalysisBreakdownRow
{
    public string Label { get; init; } = string.Empty;
    public int Trades { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public double WinRate { get; init; }
    public decimal NetPnL { get; init; }
    public decimal Expectancy { get; init; }
    public decimal ProfitFactor { get; init; }
    public double AverageR { get; init; }
    public string SampleQuality { get; init; } = string.Empty;
}

public sealed class AnalysisTradeRow
{
    public string TradeId { get; init; } = string.Empty;
    public DateTime EntryTime { get; init; }
    public DateTime ExitTime { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal NetPnL { get; init; }
    public decimal? RealizedRR { get; init; }
    public decimal? MAETicks { get; init; }
    public decimal? MFETicks { get; init; }
    public decimal Commission { get; init; }
    public string Tags { get; init; } = string.Empty;
}

public sealed class AnalysisMetricSummary
{
    public int TotalTrades { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int BreakEven { get; init; }
    public decimal NetPnL { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal GrossLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public double WinRate { get; init; }
    public decimal Expectancy { get; init; }
    public double? AverageR { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal? AverageMfeTicks { get; init; }
    public decimal? AverageMaeTicks { get; init; }
    public decimal RecordedCommissions { get; init; }
    public int TradesWithTags { get; init; }
    public int TradesWithNotes { get; init; }
    public int TradesWithR { get; init; }
    public int TradesWithMfe { get; init; }
    public int TradesWithMae { get; init; }
    public int TimelineEventCount { get; init; }
    public TimeSpan AverageDuration { get; init; }
    public decimal BestTrade { get; init; }
    public decimal WorstTrade { get; init; }
}

/// <summary>
/// A single analysis scope drives every report in this view. Presets are inclusive by calendar date.
/// Raw L2/MBO, slippage and structured market-context metrics are deliberately not inferred when absent.
/// </summary>
public sealed class TradeAnalysisViewModel : ObservableObject
// SetupChecklistWinRatePatchV1
{
    private List<Trade> _baseTrades = new();
    private Dictionary<string, string> _journalChecklistJsonByTradeId = new(StringComparer.OrdinalIgnoreCase);
    // Names match the persisted per-trade Journal Checklist snapshot. Only checked SETUP items qualify.
    private static readonly (string Property, string Label)[] SetupChecklistItems =
    {
        ("HuntP3", "hunt-p3"), ("HuntP2", "hunt-p2"), ("ChopP2", "chop-p2"),
        ("ChopP3", "chop-p3"), ("OrderBlock", "ORDER BLOCK"),
        ("RunningHS", "Running-HS"), ("RunningLS", "Running-LS")
    };
    private List<Trade> _scopedTrades = new();
    private string _selectedPresetKey = "LAST_7_DAYS";
    private DateTime? _fromDate = DateTime.Today.AddDays(-6);
    private DateTime? _toDate = DateTime.Today;
    private bool _allHistory;
    private string _scopeDescription = "Last 7 calendar days";
    private string _statusMessage = "Waiting for trade records.";
    private string _validationMessage = string.Empty;
    private AnalysisMetricSummary _summary = new();

    public TradeAnalysisViewModel()
    {
        SelectPresetCommand = new RelayCommand<string>(SelectPreset);
        ApplyCustomRangeCommand = new RelayCommand(ApplyCustomRange);
        ResetRangeCommand = new RelayCommand(() => SelectPreset("LAST_7_DAYS"));
    }

    public IRelayCommand<string> SelectPresetCommand { get; }
    public IRelayCommand ApplyCustomRangeCommand { get; }
    public IRelayCommand ResetRangeCommand { get; }

    public string SelectedPresetKey { get => _selectedPresetKey; private set => SetProperty(ref _selectedPresetKey, value); }
    public DateTime? FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime? ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
    public string ScopeDescription { get => _scopeDescription; private set => SetProperty(ref _scopeDescription, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public AnalysisMetricSummary Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public IReadOnlyList<Trade> ScopedTrades => _scopedTrades;

    public ObservableCollection<AnalysisChartPoint> DailyPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> EquityCurveChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> DrawdownChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> SetupPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> TagPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> WeekdayPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> HourPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> SessionContextPnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> CommissionChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> MfeChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> MaeChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> EventTypeChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> MistakePnlChart { get; } = new();
    public ObservableCollection<AnalysisChartPoint> RollingExpectancyChart { get; } = new();

    public ObservableCollection<AnalysisBreakdownRow> SetupRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> TagRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> WeekdayRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> HourRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> MarketSessionRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> ContextRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> EventRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> MistakeRows { get; } = new();
    public ObservableCollection<AnalysisBreakdownRow> ValidationRows { get; } = new();
    public ObservableCollection<AnalysisTradeRow> TradeRows { get; } = new();

    public string DataCoverageText { get; private set; } = "";
    public string ContextCoverageText { get; private set; } = "";
    public string MicrostructureCoverageText { get; private set; } = "";
    public string ExecutionCoverageText { get; private set; } = "";
    public string MistakeCoverageText { get; private set; } = "";
    public string ResearchCoverageText { get; private set; } = "";

    public void UpdateTrades(IEnumerable<Trade> trades) => UpdateTrades(trades, null);

    public void UpdateTrades(IEnumerable<Trade> trades, IReadOnlyDictionary<string, string>? journalChecklistJsonByTradeId)
    {
        _journalChecklistJsonByTradeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (journalChecklistJsonByTradeId is not null)
        {
            foreach (var pair in journalChecklistJsonByTradeId)
                _journalChecklistJsonByTradeId[pair.Key] = pair.Value;
        }
        _baseTrades = trades.Where(t => t.EntryTime != DateTime.MinValue)
            .OrderBy(t => t.EntryTime).ToList();
        ApplyDateScope();
    }

    private void SelectPreset(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var today = DateTime.Today;
        ValidationMessage = string.Empty;
        _allHistory = false;
        SelectedPresetKey = key;

        switch (key)
        {
            case "LAST_7_DAYS":
                FromDate = today.AddDays(-6); ToDate = today; ScopeDescription = "Last 7 calendar days"; break;
            case "LAST_14_DAYS":
                FromDate = today.AddDays(-13); ToDate = today; ScopeDescription = "Last 14 calendar days"; break;
            case "LAST_30_DAYS":
                FromDate = today.AddDays(-29); ToDate = today; ScopeDescription = "Last 30 calendar days"; break;
            case "THIS_MONTH":
                FromDate = new DateTime(today.Year, today.Month, 1); ToDate = today; ScopeDescription = "Current calendar month to date"; break;
            case "PREVIOUS_MONTH":
                var previous = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                FromDate = previous; ToDate = previous.AddMonths(1).AddDays(-1); ScopeDescription = previous.ToString("MMMM yyyy"); break;
            case "TWO_MONTHS_AGO":
                var twoMonthsAgo = new DateTime(today.Year, today.Month, 1).AddMonths(-2);
                FromDate = twoMonthsAgo; ToDate = twoMonthsAgo.AddMonths(1).AddDays(-1); ScopeDescription = twoMonthsAgo.ToString("MMMM yyyy"); break;
            case "ALL_HISTORY":
                _allHistory = true;
                FromDate = _baseTrades.Count == 0 ? null : _baseTrades.Min(t => t.EntryTime).Date;
                ToDate = _baseTrades.Count == 0 ? today : _baseTrades.Max(t => t.EntryTime).Date;
                ScopeDescription = "All available history";
                break;
            case "CUSTOM":
                ScopeDescription = "Custom date range";
                break;
            default:
                return;
        }
        ApplyDateScope();
    }

    private void ApplyCustomRange()
    {
        if (!FromDate.HasValue || !ToDate.HasValue)
        {
            ValidationMessage = "Select both a start date and an end date, then apply the range.";
            return;
        }
        if (FromDate.Value.Date > ToDate.Value.Date)
        {
            ValidationMessage = "Start date must be on or before end date.";
            return;
        }
        _allHistory = false;
        SelectedPresetKey = "CUSTOM";
        ScopeDescription = $"{FromDate.Value:dd MMM yyyy} – {ToDate.Value:dd MMM yyyy}";
        ValidationMessage = string.Empty;
        ApplyDateScope();
    }

    private void ApplyDateScope()
    {
        IEnumerable<Trade> query = _baseTrades;
        if (!_allHistory && FromDate.HasValue && ToDate.HasValue)
        {
            var start = FromDate.Value.Date;
            var endExclusive = ToDate.Value.Date.AddDays(1);
            query = query.Where(t => t.EntryTime >= start && t.EntryTime < endExclusive);
        }
        _scopedTrades = query.OrderBy(t => t.EntryTime).ToList();
        RebuildReports();
        var startText = _scopedTrades.Count == 0 ? "no matching dates" : _scopedTrades.Min(t => t.EntryTime).ToString("dd MMM yyyy");
        var endText = _scopedTrades.Count == 0 ? "—" : _scopedTrades.Max(t => t.EntryTime).ToString("dd MMM yyyy");
        StatusMessage = $"{_scopedTrades.Count:N0} trades • {startText} to {endText} • all analysis modules use this same scope.";
        OnPropertyChanged(nameof(ScopedTrades));
    }

    private void RebuildReports()
    {
        var trades = _scopedTrades;
        var wins = trades.Where(t => t.NetPnL > 0).ToList();
        var losses = trades.Where(t => t.NetPnL < 0).ToList();
        var rr = trades.Where(t => t.RealizedRR.HasValue).Select(t => t.RealizedRR!.Value).ToList();
        var mfe = trades.Where(t => t.MFETicks.HasValue).Select(t => t.MFETicks!.Value).ToList();
        var mae = trades.Where(t => t.MAETicks.HasValue).Select(t => Math.Abs(t.MAETicks!.Value)).ToList();
        var grossProfit = wins.Sum(t => t.NetPnL);
        var grossLoss = Math.Abs(losses.Sum(t => t.NetPnL));
        var daily = trades.GroupBy(t => t.EntryTime.Date).OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key, Pnl = g.Sum(t => t.NetPnL), Count = g.Count() }).ToList();
        decimal equity = 0, peak = 0, maxDd = 0;
        foreach (var d in daily) { equity += d.Pnl; peak = Math.Max(peak, equity); maxDd = Math.Max(maxDd, peak - equity); }
        Summary = new AnalysisMetricSummary
        {
            TotalTrades = trades.Count, Wins = wins.Count, Losses = losses.Count,
            BreakEven = trades.Count - wins.Count - losses.Count,
            NetPnL = trades.Sum(t => t.NetPnL), GrossProfit = grossProfit, GrossLoss = grossLoss,
            ProfitFactor = grossLoss == 0 ? (grossProfit > 0 ? 99.99m : 0m) : Math.Round(grossProfit / grossLoss, 2),
            WinRate = trades.Count == 0 ? 0 : (double)wins.Count / trades.Count * 100.0,
            Expectancy = trades.Count == 0 ? 0 : trades.Average(t => t.NetPnL),
            AverageR = rr.Count == 0 ? null : (double)rr.Average(),
            MaxDrawdown = maxDd,
            AverageMfeTicks = mfe.Count == 0 ? null : mfe.Average(),
            AverageMaeTicks = mae.Count == 0 ? null : mae.Average(),
            RecordedCommissions = trades.Sum(t => t.Commission),
            TradesWithTags = trades.Count(t => t.Tags.Any(s => !string.IsNullOrWhiteSpace(s))),
            TradesWithNotes = trades.Count(t => !string.IsNullOrWhiteSpace(t.Notes)),
            TradesWithR = rr.Count,
            TradesWithMfe = mfe.Count,
            TradesWithMae = mae.Count,
            TimelineEventCount = trades.Sum(t => t.EventTimeline?.Count ?? 0),
            AverageDuration = trades.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(trades.Average(t => t.Duration.TotalSeconds)),
            BestTrade = trades.Count == 0 ? 0 : trades.Max(t => t.NetPnL),
            WorstTrade = trades.Count == 0 ? 0 : trades.Min(t => t.NetPnL)
        };

        string DateLabel(DateTime date) => daily.Count > 1 && daily[0].Date.Year != daily[^1].Date.Year ? date.ToString("yy-MM-dd") : date.ToString("MM-dd");
        Fill(DailyPnlChart, daily.Select(d => new AnalysisChartPoint(DateLabel(d.Date), (double)d.Pnl)));
        equity = 0; peak = 0;
        var equityPoints = new List<AnalysisChartPoint>(); var ddPoints = new List<AnalysisChartPoint>();
        foreach (var d in daily)
        {
            equity += d.Pnl; peak = Math.Max(peak, equity);
            equityPoints.Add(new AnalysisChartPoint(DateLabel(d.Date), (double)equity));
            ddPoints.Add(new AnalysisChartPoint(DateLabel(d.Date), (double)(equity - peak)));
        }
        Fill(EquityCurveChart, equityPoints); Fill(DrawdownChart, ddPoints);
        Fill(CommissionChart, daily.Select(d => new AnalysisChartPoint(DateLabel(d.Date), (double)trades.Where(t => t.EntryTime.Date == d.Date).Sum(t => t.Commission))));

        var setupAssignments = trades.SelectMany(trade => GetSetupLabels(trade).Select(label => new { Trade = trade, Label = label }));
        var setGroups = setupAssignments
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => MakeBreakdown(group.Key, group.Select(item => item.Trade)
                .DistinctBy(trade => trade.TradeId + "|" + trade.EntryTime.ToString("O"))))
            .OrderByDescending(row => row.NetPnL)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Replace(SetupRows, setGroups);
        Fill(SetupPnlChart, setGroups.Take(12).Select(row => new AnalysisChartPoint(ShortLabel(row.Label), (double)row.NetPnL)));        var tagGroups = trades.SelectMany(t => t.Tags.Where(s => !string.IsNullOrWhiteSpace(s)).Select(tag => new { Trade = t, Tag = tag.Trim() }))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => MakeBreakdown(g.Key, g.Select(x => x.Trade).DistinctBy(t => t.TradeId + "|" + t.EntryTime.ToString("O"))))
            .OrderByDescending(r => r.NetPnL).ToList();
        Replace(TagRows, tagGroups);
        var contextGroups = trades.SelectMany(t => ExtractContextLabels(t).Select(label => new { Trade = t, Label = label }))
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => MakeBreakdown(g.Key, g.Select(x => x.Trade).DistinctBy(t => t.TradeId + "|" + t.EntryTime.ToString("O"))))
            .OrderByDescending(r => r.NetPnL).ToList();
        var effectiveContextRows = contextGroups.Count > 0
            ? contextGroups
            : tagGroups.Select(r => new AnalysisBreakdownRow { Label = "TAG: " + r.Label, Trades = r.Trades, Wins = r.Wins, Losses = r.Losses, WinRate = r.WinRate, NetPnL = r.NetPnL, Expectancy = r.Expectancy, ProfitFactor = r.ProfitFactor, AverageR = r.AverageR, SampleQuality = r.SampleQuality }).ToList();
        Replace(ContextRows, effectiveContextRows);
        Fill(TagPnlChart, (contextGroups.Count > 0 ? contextGroups : tagGroups).Take(12).Select(r => new AnalysisChartPoint(ShortLabel(r.Label), (double)r.NetPnL)));

        var weekdays = trades.GroupBy(t => t.EntryTime.DayOfWeek)
            .Select(g => MakeBreakdown(g.Key.ToString(), g)).OrderBy(r => WeekdayOrder(r.Label)).ToList();
        Replace(WeekdayRows, weekdays); Fill(WeekdayPnlChart, weekdays.Select(r => new AnalysisChartPoint(r.Label[..Math.Min(3, r.Label.Length)], (double)r.NetPnL)));
        var hours = trades.GroupBy(t => (t.EntryTime.Hour / 4) * 4).Select(g => MakeBreakdown($"{g.Key:00}:00–{Math.Min(g.Key + 3, 23):00}:59", g))
            .OrderBy(r => r.Label).ToList();
        Replace(HourRows, hours); Fill(HourPnlChart, hours.Select(r => new AnalysisChartPoint(r.Label[..5], (double)r.NetPnL)));
        var sessionRows = trades.Select(t => new { Trade = t, Session = ReadContextString(t, "Session") })
            .Where(x => !string.IsNullOrWhiteSpace(x.Session))
            .GroupBy(x => x.Session!, StringComparer.OrdinalIgnoreCase)
            .Select(g => MakeBreakdown(g.Key, g.Select(x => x.Trade).DistinctBy(t => t.TradeId + "|" + t.EntryTime.ToString("O"))))
            .OrderBy(r => r.Label).ToList();
        Replace(MarketSessionRows, sessionRows);
        Fill(SessionContextPnlChart, sessionRows.Select(r => new AnalysisChartPoint(ShortLabel(r.Label), (double)r.NetPnL)));

        var mfeDaily = trades.Where(t => t.MFETicks.HasValue).GroupBy(t => t.EntryTime.Date).OrderBy(g => g.Key)
            .Select(g => new AnalysisChartPoint(DateLabel(g.Key), (double)g.Average(t => t.MFETicks!.Value)));
        var maeDaily = trades.Where(t => t.MAETicks.HasValue).GroupBy(t => t.EntryTime.Date).OrderBy(g => g.Key)
            .Select(g => new AnalysisChartPoint(DateLabel(g.Key), (double)g.Average(t => Math.Abs(t.MAETicks!.Value))));
        Fill(MfeChart, mfeDaily); Fill(MaeChart, maeDaily);

        var eventGroups = trades.SelectMany(t => (t.EventTimeline ?? new()).Select(e => e.EventType.ToString()))
            .GroupBy(x => x).Select(g => new AnalysisBreakdownRow { Label = g.Key, Trades = g.Count(), NetPnL = 0, Expectancy = 0, SampleQuality = "Event count, not order-book flow" })
            .OrderByDescending(r => r.Trades).ToList();
        Replace(EventRows, eventGroups); Fill(EventTypeChart, eventGroups.Select(r => new AnalysisChartPoint(ShortLabel(r.Label), r.Trades)));

        var mistakeTrades = trades.SelectMany(t => t.Tags.Where(IsMistakeTag).Select(tag => new { Trade = t, Tag = tag.Trim() }))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => MakeBreakdown(g.Key, g.Select(x => x.Trade).DistinctBy(t => t.TradeId + "|" + t.EntryTime.ToString("O"))))
            .OrderByDescending(r => r.Trades).ToList();
        Replace(MistakeRows, mistakeTrades); Fill(MistakePnlChart, mistakeTrades.Select(r => new AnalysisChartPoint(ShortLabel(r.Label), (double)r.NetPnL)));

        var ordered = trades.OrderBy(t => t.EntryTime).ToList();
        var rolling = new List<AnalysisChartPoint>();
        for (int start = 0; start < ordered.Count; start += 10)
        {
            var block = ordered.Skip(start).Take(10).ToList();
            rolling.Add(new AnalysisChartPoint($"{start + 1}–{start + block.Count}", block.Average(t => (double)t.NetPnL)));
        }
        Fill(RollingExpectancyChart, rolling);
        var splitAt = ordered.Count / 2;
        var halves = new List<AnalysisBreakdownRow>();
        if (ordered.Count > 0)
        {
            halves.Add(MakeBreakdown("Earlier half • exploratory", ordered.Take(splitAt)));
            halves.Add(MakeBreakdown("Later half • holdout-like", ordered.Skip(splitAt)));
        }
        Replace(ValidationRows, halves);

        Replace(TradeRows, trades.OrderByDescending(t => t.EntryTime).Select(t => new AnalysisTradeRow
        {
            TradeId = t.TradeId, EntryTime = t.EntryTime, ExitTime = t.ExitTime, Symbol = t.Symbol,
            Side = t.Side.ToString(), Quantity = t.Quantity, NetPnL = t.NetPnL, RealizedRR = t.RealizedRR,
            MAETicks = t.MAETicks, MFETicks = t.MFETicks, Commission = t.Commission,
            Tags = string.Join(", ", t.Tags)
        }));

        DataCoverageText = $"Scoped trades: {trades.Count:N0}. Tags: {Summary.TradesWithTags:N0}; notes: {Summary.TradesWithNotes:N0}; recorded R:R: {Summary.TradesWithR:N0}; MFE: {Summary.TradesWithMfe:N0}; MAE: {Summary.TradesWithMae:N0}. Missing values stay missing and are not replaced with zero.";
        var contextTrades = trades.Count(HasStructuredMarketContext);
        var contextObservations = contextGroups.Sum(r => r.Trades);
        ContextCoverageText = contextTrades == 0
            ? $"No structured MarketContext object was found on these trade records. {Summary.TradesWithTags:N0} tagged trade(s) are available as a limited fallback. Tag results are descriptive associations, not proof of causal confluence."
            : $"Structured MarketContext is present on {contextTrades:N0} of {trades.Count:N0} trades; {contextObservations:N0} feature/condition groupings were evaluated. Boolean features only count when explicitly true; default/unknown category values are excluded to reduce false inferences. Associations are not proof of causation.";
        var flaggedFlowTrades = trades.Count(t => ExtractContextLabels(t).Any(label => label.StartsWith("Iceberg", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Dom", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Footprint", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Box", StringComparison.OrdinalIgnoreCase)));
        MicrostructureCoverageText = $"{Summary.TimelineEventCount:N0} saved trade-timeline event(s) and {flaggedFlowTrades:N0} trade(s) with explicitly positive/manual DOM, iceberg, footprint or Box context flags are available. These are not raw historical CME MBP/MBO updates; queue evolution, cancellations, iceberg reload sequences and absorption cannot be reconstructed from flags alone.";
        var ratedExecutionTrades = trades.Count(t => ExtractContextLabels(t).Any(label => label.StartsWith("ExecutionQuality=", StringComparison.OrdinalIgnoreCase)));
        ExecutionCoverageText = $"Recorded commission total: {Summary.RecordedCommissions:C2}. Trades with explicit non-default execution self-rating: {ratedExecutionTrades:N0}. Zero commission values may mean either a true zero or incomplete commission import. Fill-level bid/ask snapshots, order acknowledgements and slippage are not confirmed, so Implementation Shortfall and slippage are not estimated.";
        MistakeCoverageText = mistakeTrades.Count == 0 ? "No standardized mistake tags were found. Add explicit tags such as MISTAKE:FOMO, MISTAKE:LATE_ENTRY or MISTAKE:PLAN_VIOLATION to enable this report; an untagged trade is not assumed mistake-free." : $"Found {mistakeTrades.Count} unique explicit mistake tag(s). Associations with P&L are descriptive and do not prove causation.";
        ResearchCoverageText = ordered.Count < 10 ? "Small sample: the chronological split and rolling values are descriptive only. Use a larger sample and a pre-defined hypothesis before treating them as evidence." : "The first/second chronological split and 10-trade rolling expectancy are stability diagnostics only, not a strategy backtest. The later half is not untouched if you repeatedly tune rules against it.";
        OnPropertyChanged(nameof(DataCoverageText)); OnPropertyChanged(nameof(ContextCoverageText));
        OnPropertyChanged(nameof(MicrostructureCoverageText)); OnPropertyChanged(nameof(ExecutionCoverageText));
        OnPropertyChanged(nameof(MistakeCoverageText)); OnPropertyChanged(nameof(ResearchCoverageText));
    }

    private static AnalysisBreakdownRow MakeBreakdown(string label, IEnumerable<Trade> source)
    {
        var trades = source.ToList(); var wins = trades.Count(t => t.NetPnL > 0); var losses = trades.Count(t => t.NetPnL < 0);
        var gp = trades.Where(t => t.NetPnL > 0).Sum(t => t.NetPnL); var gl = Math.Abs(trades.Where(t => t.NetPnL < 0).Sum(t => t.NetPnL));
        var r = trades.Where(t => t.RealizedRR.HasValue).Select(t => (double)t.RealizedRR!.Value).ToList();
        return new AnalysisBreakdownRow
        {
            Label = label, Trades = trades.Count, Wins = wins, Losses = losses,
            WinRate = trades.Count == 0 ? 0 : (double)wins / trades.Count * 100,
            NetPnL = trades.Sum(t => t.NetPnL), Expectancy = trades.Count == 0 ? 0 : trades.Average(t => t.NetPnL),
            ProfitFactor = gl == 0 ? (gp > 0 ? 99.99m : 0m) : Math.Round(gp / gl, 2),
            AverageR = r.Count == 0 ? 0 : r.Average(),
            SampleQuality = trades.Count < 5 ? "Very small" : trades.Count < 20 ? "Limited" : "More samples"
        };
    }

    private static bool HasStructuredMarketContext(Trade trade) => ReadContextObject(trade) is not null;

    private static object? ReadContextObject(Trade trade) => trade.GetType().GetProperty("MarketContext")?.GetValue(trade);

    private static string? ReadContextString(Trade trade, string propertyName)
    {
        var context = ReadContextObject(trade);
        var property = context?.GetType().GetProperty(propertyName);
        if (property is null || property.PropertyType != typeof(string)) return null;
        var value = property.GetValue(context)?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        var defaults = propertyName switch
        {
            "Session" => new[] { "Unknown" },
            "GexEnvironment" => new[] { "Neutral", "Unknown" },
            "GexRelation" => new[] { "Neutral", "Unknown" },
            "EventPhase" => new[] { "None", "Unknown" },
            "EventName" => new[] { "" },
            "TradeThesis" => new[] { "Other", "Unknown" },
            "ExecutionQuality" => new[] { "Acceptable", "Unknown" },
            _ => Array.Empty<string>()
        };
        return defaults.Contains(value, StringComparer.OrdinalIgnoreCase) ? null : value;
    }

    private static IEnumerable<string> ExtractContextLabels(Trade trade)
    {
        var context = ReadContextObject(trade);
        if (context is null) yield break;
        foreach (var property in context.GetType().GetProperties())
        {
            if (!property.CanRead) continue;
            if (property.PropertyType == typeof(bool) && property.GetValue(context) is true)
            {
                // A true flag is positive evidence. A false flag can be either a negative
                // observation or an untouched default, so it is deliberately not scored.
                yield return property.Name;
                continue;
            }
            if (property.PropertyType == typeof(string))
            {
                var value = ReadContextString(trade, property.Name);
                if (!string.IsNullOrWhiteSpace(value)) yield return $"{property.Name}={value}";
                continue;
            }
            if (property.PropertyType == typeof(int) && property.GetValue(context) is int number)
            {
                if (property.Name == "ContextConfidence" && number > 0)
                    yield return $"ContextConfidence={(number == 1 ? "Medium" : "High")}";
                else if (property.Name == "TradeQuality" && number > 0)
                    yield return $"TradeQuality={Math.Clamp(number, 1, 5)}/5★";
            }
        }
    }

    // Setup Edge is based on persisted checked SETUP checklist items. A multi-checked trade
    // participates in every corresponding setup sample; a trade with none checked is isolated.
    private IEnumerable<string> GetSetupLabels(Trade trade)
    {
        if (_journalChecklistJsonByTradeId.TryGetValue(trade.TradeId, out var checklistJson))
        {
            var checkedLabels = TryReadCheckedSetupLabels(checklistJson);
            if (checkedLabels is null)
            {
                yield return "Setup data unavailable";
                yield break;
            }
            if (checkedLabels.Count == 0)
            {
                yield return "No setup";
                yield break;
            }
            foreach (var label in checkedLabels) yield return label;
            yield break;
        }

        // No saved checked setup means this trade belongs in the dedicated No setup group.
        // Do not infer a checkbox selection from free-form tags or legacy text fields.
        yield return "No setup";
    }

    private static IReadOnlyList<string>? TryReadCheckedSetupLabels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var result = new List<string>();
            foreach (var item in SetupChecklistItems)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, item.Property, StringComparison.OrdinalIgnoreCase)) continue;
                    if (property.Value.ValueKind == JsonValueKind.True) result.Add(item.Label);
                    break;
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Conservative on purpose: do not infer a mistake from free-form wording.
    // Only explicit, standardized tag prefixes participate in mistake attribution.
    private static bool IsMistakeTag(string tag)
    {
        var t = tag.Trim();
        return t.StartsWith("MISTAKE:", StringComparison.OrdinalIgnoreCase) ||
               t.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase);
    }
    private static int WeekdayOrder(string label) => Enum.TryParse<DayOfWeek>(label, out var day) ? ((int)day + 6) % 7 : 99;
    private static string ShortLabel(string value) => value.Length <= 18 ? value : value[..15] + "…";
    private static void Fill(ObservableCollection<AnalysisChartPoint> collection, IEnumerable<AnalysisChartPoint> values) { collection.Clear(); foreach (var item in values) collection.Add(item); }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values) { collection.Clear(); foreach (var item in values) collection.Add(item); }
}
