using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

using BENTradeReviewer.UI.MetaTrader;

namespace BENTradeReviewer;

public sealed class MetaTraderViewModel :
    INotifyPropertyChanged
{
    private readonly MetaTraderRepository _repository;
    private readonly MetaTraderImporter _importer;

    private string _dataFolder = string.Empty;
    private string _statusMessage = "MetaTrader journal ready.";

    private DateTime _selectedMonth =
        new(
            DateTime.Today.Year,
            DateTime.Today.Month,
            1);

    private string _selectedInstrumentFilter = "ALL";
    private string _selectedSideFilter = "ALL";
    private string _selectedOutcomeFilter = "ALL";

    private MetaTraderTrade? _selectedTrade;

    private DateTime? _selectedDate;

    public ObservableCollection<MetaTraderTrade> Trades { get; } =
        new();

    public ObservableCollection<MetaTraderTrade> FilteredTrades { get; } =
        new();

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } =
        new();

    public ObservableCollection<CalendarWeekViewModel> WeeklyCalendarRows { get; } =
        new();

    public ObservableCollection<MetaTraderTrade> SelectedDayTrades { get; } =
        new();

    public string DataFolder
    {
        get => _dataFolder;

        private set
        {
            if (_dataFolder == value)
                return;

            _dataFolder = value;
            OnPropertyChanged();
        }
    }

    public DateTime SelectedMonth
    {
        get => _selectedMonth;

        private set
        {
            if (_selectedMonth == value)
                return;

            _selectedMonth = value;
            OnPropertyChanged();

            RefreshView();
        }
    }

    public string SelectedInstrumentFilter
    {
        get => _selectedInstrumentFilter;

        set
        {
            var valueToUse =
                string.IsNullOrWhiteSpace(value)
                    ? "ALL"
                    : value;

            if (_selectedInstrumentFilter ==
                valueToUse)
            {
                return;
            }

            _selectedInstrumentFilter =
                valueToUse;

            OnPropertyChanged();

            RefreshView();
        }
    }

    public string SelectedSideFilter
    {
        get => _selectedSideFilter;

        set
        {
            var valueToUse =
                string.IsNullOrWhiteSpace(value)
                    ? "ALL"
                    : value;

            if (_selectedSideFilter ==
                valueToUse)
            {
                return;
            }

            _selectedSideFilter =
                valueToUse;

            OnPropertyChanged();

            RefreshView();
        }
    }

    public string SelectedOutcomeFilter
    {
        get => _selectedOutcomeFilter;

        set
        {
            var valueToUse =
                string.IsNullOrWhiteSpace(value)
                    ? "ALL"
                    : value;

            if (_selectedOutcomeFilter ==
                valueToUse)
            {
                return;
            }

            _selectedOutcomeFilter =
                valueToUse;

            OnPropertyChanged();

            RefreshView();
        }
    }

    public MetaTraderTrade? SelectedTrade
    {
        get => _selectedTrade;

        set
        {
            if (ReferenceEquals(
                    _selectedTrade,
                    value))
            {
                return;
            }

            _selectedTrade = value;

            OnPropertyChanged();
        }
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;

        private set
        {
            if (_selectedDate == value)
                return;

            _selectedDate = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;

        private set
        {
            if (_statusMessage == value)
                return;

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    // ============================================================
    // DASHBOARD
    // ============================================================

    public int TotalTrades =>
        FilteredTrades.Count;

    public int WinningTrades =>
        FilteredTrades.Count(
            x => x.NetPnl > 0m);

    public int LosingTrades =>
        FilteredTrades.Count(
            x => x.NetPnl < 0m);

    public double WinRate =>
        TotalTrades == 0
            ? 0d
            : WinningTrades * 100d / TotalTrades;

    public decimal GrossPnl =>
        FilteredTrades.Sum(
            x => x.GrossPnl);

    public decimal Commission =>
        FilteredTrades.Sum(
            x => x.Commission);

    public decimal Swap =>
        FilteredTrades.Sum(
            x => x.Swap);

    public decimal Fee =>
        FilteredTrades.Sum(
            x => x.Fee);

    public decimal NetPnl =>
        FilteredTrades.Sum(
            x => x.NetPnl);

    public decimal GrossProfit =>
        FilteredTrades
            .Where(x => x.NetPnl > 0m)
            .Sum(x => x.NetPnl);

    public decimal GrossLoss =>
        Math.Abs(
            FilteredTrades
                .Where(x => x.NetPnl < 0m)
                .Sum(x => x.NetPnl));

    public decimal ProfitFactor =>
        GrossLoss == 0m
            ? GrossProfit > 0m
                ? decimal.MaxValue
                : 0m
            : GrossProfit / GrossLoss;

    public decimal AverageWin =>
        WinningTrades == 0
            ? 0m
            : GrossProfit / WinningTrades;

    public decimal AverageLoss =>
        LosingTrades == 0
            ? 0m
            : -GrossLoss / LosingTrades;

    // ============================================================
    // SELECTED DAY
    // ============================================================

    public int SelectedDayTradeCount =>
        SelectedDayTrades.Count;

    public decimal SelectedDayNetPnl =>
        SelectedDayTrades.Sum(x => x.NetPnl);

    public decimal SelectedDayCommission =>
        SelectedDayTrades.Sum(x => x.Commission);

    public double SelectedDayWinRate =>
        SelectedDayTrades.Count == 0
            ? 0d
            : SelectedDayTrades.Count(x => x.NetPnl > 0m)
              * 100d
              / SelectedDayTrades.Count;

    // ============================================================
    // IMPORT
    // ============================================================

    public int LastFilesFound { get; private set; }

    public int LastImported { get; private set; }

    public int LastFailed { get; private set; }

    public string LastImportErrors { get; private set; } =
        string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MetaTraderViewModel()
    {
        var local =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var dbDirectory =
            Path.Combine(
                local,
                "BENTradeReviewer");

        Directory.CreateDirectory(
            dbDirectory);

        var databasePath =
            Path.Combine(
                dbDirectory,
                "metatrader.db");

        _repository =
            new MetaTraderRepository(
                databasePath);

        _importer =
            new MetaTraderImporter(
                _repository);

        DataFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "MetaQuotes",
                "Terminal",
                "Common",
                "Files",
                "BEN_MetaTrader");

        Directory.CreateDirectory(
            DataFolder);

        LoadTrades();
    }

    public void Import()
    {
        var result =
            _importer.ImportFolder(
                DataFolder);

        LastFilesFound =
            result.FilesFound;

        LastImported =
            result.Imported;

        LastFailed =
            result.Failed;

        LastImportErrors =
            string.Join(
                Environment.NewLine,
                result.Errors.Take(10));

        LoadTrades();

        StatusMessage =
            $"Found {LastFilesFound} JSON | " +
            $"Imported {LastImported} | " +
            $"Failed {LastFailed}";

        OnPropertyChanged(nameof(LastFilesFound));
        OnPropertyChanged(nameof(LastImported));
        OnPropertyChanged(nameof(LastFailed));
        OnPropertyChanged(nameof(LastImportErrors));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public void LoadTrades()
    {
        Trades.Clear();

        foreach (var trade in
                 _repository.GetAllTrades())
        {
            Trades.Add(trade);
        }

        RefreshView();
    }

    private void RefreshView()
    {
        var query =
            Trades.AsEnumerable();

        // --------------------------------------------------------
        // Global symbol filter
        // --------------------------------------------------------

        if (!SelectedInstrumentFilter.Equals(
                "ALL",
                StringComparison.OrdinalIgnoreCase))
        {
            query =
                query.Where(
                    x => x.Symbol.Equals(
                        SelectedInstrumentFilter,
                        StringComparison.OrdinalIgnoreCase));
        }

        // --------------------------------------------------------
        // Side
        // --------------------------------------------------------

        if (!SelectedSideFilter.Equals(
                "ALL",
                StringComparison.OrdinalIgnoreCase))
        {
            query =
                query.Where(
                    x => x.Side.Equals(
                        SelectedSideFilter,
                        StringComparison.OrdinalIgnoreCase));
        }

        // --------------------------------------------------------
        // Outcome
        // --------------------------------------------------------

        if (SelectedOutcomeFilter.Equals(
                "WIN",
                StringComparison.OrdinalIgnoreCase))
        {
            query =
                query.Where(
                    x => x.NetPnl > 0m);
        }
        else if (SelectedOutcomeFilter.Equals(
                     "LOSS",
                     StringComparison.OrdinalIgnoreCase))
        {
            query =
                query.Where(
                    x => x.NetPnl < 0m);
        }

        var list =
            query.ToList();

        FilteredTrades.Clear();

        foreach (var trade in list)
            FilteredTrades.Add(trade);

        BuildCalendarGrid(
            list,
            SelectedMonth);

        if (SelectedDate.HasValue)
            LoadSelectedDay(
                SelectedDate.Value);

        RaiseDashboardProperties();
    }

    private void RaiseDashboardProperties()
    {
        OnPropertyChanged(nameof(TotalTrades));
        OnPropertyChanged(nameof(WinningTrades));
        OnPropertyChanged(nameof(LosingTrades));
        OnPropertyChanged(nameof(WinRate));
        OnPropertyChanged(nameof(GrossPnl));
        OnPropertyChanged(nameof(Commission));
        OnPropertyChanged(nameof(Swap));
        OnPropertyChanged(nameof(Fee));
        OnPropertyChanged(nameof(NetPnl));
        OnPropertyChanged(nameof(GrossProfit));
        OnPropertyChanged(nameof(GrossLoss));
        OnPropertyChanged(nameof(ProfitFactor));
        OnPropertyChanged(nameof(AverageWin));
        OnPropertyChanged(nameof(AverageLoss));

        OnPropertyChanged(nameof(SelectedDayTradeCount));
        OnPropertyChanged(nameof(SelectedDayNetPnl));
        OnPropertyChanged(nameof(SelectedDayCommission));
    }

    public void NavigateMonth(int delta)
    {
        SelectedMonth =
            SelectedMonth.AddMonths(delta);
    }

    public void SelectDay(DateTime date)
    {
        SelectedDate = date;

        LoadSelectedDay(date);
    }

    private void LoadSelectedDay(DateTime date)
    {
        SelectedDayTrades.Clear();

        foreach (var trade in
                 FilteredTrades
                     .Where(
                         x => x.EntryTime.Date ==
                              date.Date)
                     .OrderBy(
                         x => x.EntryTime))
        {
            SelectedDayTrades.Add(trade);
        }

        if (SelectedDayTrades.Count > 0)
        {
            SelectedTrade =
                SelectedDayTrades[0];
        }

        OnPropertyChanged(nameof(SelectedDayTradeCount));
        OnPropertyChanged(nameof(SelectedDayNetPnl));
        OnPropertyChanged(nameof(SelectedDayCommission));
        OnPropertyChanged(nameof(SelectedDayWinRate));
    }

    private void BuildCalendarGrid(
        List<MetaTraderTrade> trades,
        DateTime month)
    {
        CalendarDays.Clear();
        WeeklyCalendarRows.Clear();

        var firstDay =
            new DateTime(
                month.Year,
                month.Month,
                1);

        var firstOffset =
            ((int)firstDay.DayOfWeek + 6) % 7;

        var firstMonday =
            firstDay.AddDays(
                -firstOffset);

        var lastDay =
            firstDay
                .AddMonths(1)
                .AddDays(-1);

        var lastOffset =
            ((int)lastDay.DayOfWeek + 6) % 7;

        var lastMonday =
            lastDay.AddDays(
                -lastOffset);

        for (
            var monday = firstMonday;
            monday <= lastMonday;
            monday = monday.AddDays(7))
        {
            var days =
                new List<CalendarDayViewModel>();

            for (int i = 0; i < 5; i++)
            {
                var date =
                    monday.AddDays(i);

                var isCurrentMonth =
                    date.Year == month.Year &&
                    date.Month == month.Month;

                var dayTrades =
                    trades
                        .Where(
                            x => x.EntryTime.Date ==
                                 date.Date)
                        .ToList();

                var day =
                    new CalendarDayViewModel(
                        date,
                        isCurrentMonth,
                        dayTrades);

                days.Add(day);
                CalendarDays.Add(day);
            }

            var weeklyPnl =
                days.Sum(x => x.DailyPnL);

            WeeklyCalendarRows.Add(
                new CalendarWeekViewModel(
                    monday,
                    days,
                    weeklyPnl));
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}


//==================================================================
// CALENDAR DAY
//==================================================================

public sealed class CalendarDayViewModel
{
    public DateTime Date { get; }

    public int DayNumber =>
        Date.Day;

    public bool IsCurrentMonth { get; }

    public int TradeCount { get; }

    public decimal DailyPnL { get; }

    public bool HasTrades =>
        TradeCount > 0;

    public bool IsProfitable =>
        DailyPnL > 0m;

    public bool IsLosing =>
        DailyPnL < 0m;

    public CalendarDayViewModel(
        DateTime date,
        bool isCurrentMonth,
        List<MetaTraderTrade> trades)
    {
        Date =
            date;

        IsCurrentMonth =
            isCurrentMonth;

        TradeCount =
            trades.Count;

        DailyPnL =
            trades.Sum(
                x => x.NetPnl);
    }
}


//==================================================================
// CALENDAR WEEK
//==================================================================

public sealed class CalendarWeekViewModel
{
    public DateTime WeekStart { get; }

    public IReadOnlyList<CalendarDayViewModel> Days { get; }

    public decimal WeeklyPnL { get; }

    public bool HasTrades =>
        Days.Any(x => x.HasTrades);

    public bool IsProfitable =>
        WeeklyPnL > 0m;

    public bool IsLosing =>
        WeeklyPnL < 0m;

    public CalendarWeekViewModel(
        DateTime weekStart,
        IReadOnlyList<CalendarDayViewModel> days,
        decimal weeklyPnL)
    {
        WeekStart =
            weekStart;

        Days =
            days;

        WeeklyPnL =
            weeklyPnL;
    }
}
