using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Data;
using BENTradeReviewer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BENTradeReviewer.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TradeRepository _repository;
    private readonly RecorderImporter _importer;

    [ObservableProperty] private string _windowTitle = "BEN TRADE REVIEWER — Institutional Trade Journal";
    [ObservableProperty] private int _selectedNavigationIndex;
    [ObservableProperty] private string _recorderFolderPath = @"C:\Users\REDLINE2\Documents\BEN_TradeRecorder";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private DateTime _selectedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _selectedInstrumentFilter = "ALL";
    [ObservableProperty] private string _selectedSideFilter = "ALL";
    [ObservableProperty] private string _selectedOutcomeFilter = "ALL";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private PerformanceSummary _summary = new();
    [ObservableProperty] private ObservableCollection<CalendarDayViewModel> _calendarDays = new();
[ObservableProperty] private ObservableCollection<CalendarWeekViewModel> _weeklyCalendarRows = new();
    [ObservableProperty] private ObservableCollection<Trade> _filteredTrades = new();
    [ObservableProperty] private Trade? _selectedTrade;
    [ObservableProperty] private DateTime? _selectedDate;

    public DayReviewViewModel DayReviewVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public IAsyncRelayCommand RefreshAllCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand<object> NavigateMonthCommand { get; }
    public IRelayCommand<DateTime> SelectDayCommand { get; }

    public MainViewModel()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BENTradeReviewer");
        _repository = new TradeRepository(Path.Combine(appData, "trades.db"));
        _importer = new RecorderImporter(_repository);
        DayReviewVM = new DayReviewViewModel(_repository);
        SettingsVM = new SettingsViewModel();
        SettingsVM.RecorderPath = RecorderFolderPath;
        SettingsVM.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingsViewModel.RecorderPath) && !string.IsNullOrWhiteSpace(SettingsVM.RecorderPath)) RecorderFolderPath = SettingsVM.RecorderPath; };
        RefreshAllCommand = new AsyncRelayCommand(RefreshDataAsync);
        ImportCommand = new AsyncRelayCommand(ImportNewTradesAsync);
        NavigateMonthCommand = new RelayCommand<object>(NavigateMonth);
        SelectDayCommand = new RelayCommand<DateTime>(OpenDayReview);
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Scanning recorder archive...";
            var count = await _importer.ScanAndImportAsync(RecorderFolderPath);
            await RefreshDataAsync(false);
            StatusMessage = count > 0 ? $"Ready. Imported/updated {count} trade folder(s)." : "Ready. No new trade folders found.";
        }
        catch (Exception ex) { StatusMessage = $"Initialization error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task RefreshDataAsync() => await RefreshDataAsync(true);

    private async Task RefreshDataAsync(bool toggleBusy)
    {
        if (toggleBusy) IsBusy = true;
        try
        {
            var allTrades = await _repository.GetAllTradesAsync();
            IEnumerable<Trade> query = allTrades;
            if (!string.Equals(SelectedInstrumentFilter, "ALL", StringComparison.OrdinalIgnoreCase)) query = query.Where(t => t.Symbol.Equals(SelectedInstrumentFilter, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(SelectedSideFilter, "ALL", StringComparison.OrdinalIgnoreCase)) query = query.Where(t => t.Side.ToString().Equals(SelectedSideFilter, StringComparison.OrdinalIgnoreCase));
            if (SelectedOutcomeFilter == "WIN") query = query.Where(t => t.NetPnL > 0);
            else if (SelectedOutcomeFilter == "LOSS") query = query.Where(t => t.NetPnL < 0);
            if (!string.IsNullOrWhiteSpace(SearchQuery)) query = query.Where(t => t.Symbol.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || t.Notes.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || t.Tags.Any(tag => tag.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)));
            var list = query.ToList();
            FilteredTrades = new ObservableCollection<Trade>(list);

            // Dashboard KPIs are scoped to the selected calendar month.
            var monthTrades = list
                .Where(t => t.EntryTime.Date >= SelectedMonth.Date &&
                            t.EntryTime.Date < SelectedMonth.AddMonths(1).Date)
                .ToList();

            Summary = AnalyticsEngine.CalculateSummary(monthTrades);
            BuildCalendarGrid(list, SelectedMonth);
        }
        catch (Exception ex) { StatusMessage = $"Refresh error: {ex.Message}"; }
        finally { if (toggleBusy) IsBusy = false; }
    }

    partial void OnSearchQueryChanged(string value) => _ = RefreshDataAsync();
    partial void OnSelectedInstrumentFilterChanged(string value) => _ = RefreshDataAsync();
    partial void OnSelectedSideFilterChanged(string value) => _ = RefreshDataAsync();
    partial void OnSelectedOutcomeFilterChanged(string value) => _ = RefreshDataAsync();

    private void BuildCalendarGrid(List<Trade> trades, DateTime month)
    {
        CalendarDays.Clear();
        WeeklyCalendarRows.Clear();

        var firstDayOfMonth = new DateTime(month.Year, month.Month, 1);

        // Monday = 0 ... Sunday = 6
        var firstDayOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Start from the Monday of the week containing the first day of the month.
        var firstWeekMonday = firstDayOfMonth.AddDays(-firstDayOffset);

        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

        // Find the Monday of the week containing the last day.
        var lastDayOffset = ((int)lastDayOfMonth.DayOfWeek + 6) % 7;
        var lastWeekMonday = lastDayOfMonth.AddDays(-lastDayOffset);

        for (var weekMonday = firstWeekMonday;
             weekMonday <= lastWeekMonday;
             weekMonday = weekMonday.AddDays(7))
        {
            var weekDays = new List<CalendarDayViewModel>(5);

            // ONLY Monday-Friday.
            for (var offset = 0; offset < 5; offset++)
            {
                var date = weekMonday.AddDays(offset);

                var isCurrentMonth =
                    date.Year == month.Year &&
                    date.Month == month.Month;

                // Always use the real trade data for the actual calendar date,
                // even when the date belongs to the adjacent month.
                //
                // Example:
                // September 2026 starts on Tuesday, so Monday 2026-08-31
                // appears as an adjacent-month day in the first September week.
                // Its real trades must still be visible there and included in
                // that week's Monday-Friday P&L.
                var dayTrades = trades
                    .Where(t => t.EntryTime.Date == date.Date)
                    .ToList();

                var day = new CalendarDayViewModel(
                    date,
                    isCurrentMonth,
                    dayTrades);

                weekDays.Add(day);

                // Keep legacy collection populated for compatibility.
                CalendarDays.Add(day);
            }

            // Weekly P&L = sum of the five displayed trading days only.
            var weeklyPnL = weekDays.Sum(d => d.DailyPnL);

            WeeklyCalendarRows.Add(
                new CalendarWeekViewModel(
                    weekMonday,
                    weekDays,
                    weeklyPnL));
        }
    }
    private void NavigateMonth(object? parameter) { if (!int.TryParse(parameter?.ToString(), out int delta)) return; SelectedMonth = SelectedMonth.AddMonths(delta); _ = RefreshDataAsync(); }
    private void OpenDayReview(DateTime date)
    {
        SelectedDate = date;
        DayReviewVM.LoadDay(date, FilteredTrades.Where(t => t.EntryTime.Date == date.Date).ToList());
        SelectedNavigationIndex = 1;
    }
    private async Task ImportNewTradesAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Importing recorder folders...";
            var count = await _importer.ScanAndImportAsync(RecorderFolderPath);
            await RefreshDataAsync(false);
            StatusMessage = $"Import complete: {count} trade folder(s) processed.";
        }
        catch (Exception ex) { StatusMessage = $"Import error: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}

public sealed class CalendarDayViewModel : ObservableObject
{
    public DateTime Date { get; }
    public int DayNumber => Date.Day;
    public bool IsCurrentMonth { get; }
    public int TradeCount { get; }
    public decimal DailyPnL { get; }
    public bool HasTrades => TradeCount > 0;
    public bool IsProfitable => DailyPnL > 0;
    public bool IsLosing => DailyPnL < 0;
    public CalendarDayViewModel(DateTime date, bool isCurrentMonth, List<Trade> trades) { Date = date; IsCurrentMonth = isCurrentMonth; TradeCount = trades.Count; DailyPnL = trades.Sum(t => t.NetPnL); }
}

public sealed class CalendarWeekViewModel : ObservableObject
{
    public DateTime WeekStart { get; }
    public IReadOnlyList<CalendarDayViewModel> Days { get; }
    public decimal WeeklyPnL { get; }

    public bool IsProfitable => WeeklyPnL > 0;
    public bool HasTrades => Days.Any(d => d.HasTrades);
    public bool IsLosing => WeeklyPnL < 0;

    public CalendarWeekViewModel(
        DateTime weekStart,
        IReadOnlyList<CalendarDayViewModel> days,
        decimal weeklyPnL)
    {
        WeekStart = weekStart;
        Days = days;
        WeeklyPnL = weeklyPnL;
    }
}
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _recorderPath = @"C:\Users\REDLINE2\Documents\BEN_TradeRecorder";
    [ObservableProperty] private bool _enableHardwareAcceleration = true;
    [ObservableProperty] private bool _autoWatchFolder = true;
    [ObservableProperty] private double _defaultSpeed = 1.0;
}









