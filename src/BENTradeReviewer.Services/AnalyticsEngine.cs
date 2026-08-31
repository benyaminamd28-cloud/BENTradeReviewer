using BENTradeReviewer.Core.Models;

namespace BENTradeReviewer.Services;

public sealed class PerformanceSummary
{
    public decimal NetPnL { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public double WinRate { get; set; }
    public double LossRate { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public double AverageRR { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int BreakEvenTrades { get; set; }
    public decimal MaxWin { get; set; }
    public decimal MaxLoss { get; set; }
    public decimal MaxDrawdown { get; set; }
    public int TotalVolume { get; set; }
    public TimeSpan AverageDuration { get; set; }
}

public static class AnalyticsEngine
{
    public static PerformanceSummary CalculateSummary(IEnumerable<Trade> source)
    {
        var trades = source.ToList();
        if (trades.Count == 0) return new();
        var wins = trades.Where(t => t.NetPnL > 0).ToList();
        var losses = trades.Where(t => t.NetPnL < 0).ToList();
        var summary = new PerformanceSummary
        {
            TotalTrades = trades.Count,
            TotalVolume = trades.Sum(t => t.Quantity),
            NetPnL = trades.Sum(t => t.NetPnL),
            WinningTrades = wins.Count,
            LosingTrades = losses.Count,
            BreakEvenTrades = trades.Count - wins.Count - losses.Count,
            GrossProfit = wins.Sum(t => t.NetPnL),
            GrossLoss = Math.Abs(losses.Sum(t => t.NetPnL)),
            AverageWin = wins.Count == 0 ? 0 : wins.Average(t => t.NetPnL),
            AverageLoss = losses.Count == 0 ? 0 : losses.Average(t => t.NetPnL),
            MaxWin = wins.Count == 0 ? 0 : wins.Max(t => t.NetPnL),
            MaxLoss = losses.Count == 0 ? 0 : losses.Min(t => t.NetPnL),
            WinRate = (double)wins.Count / trades.Count * 100.0,
            LossRate = (double)losses.Count / trades.Count * 100.0,
            ProfitFactor = losses.Count == 0 ? (wins.Count == 0 ? 0 : 99.99m) : Math.Round(wins.Sum(t => t.NetPnL) / Math.Abs(losses.Sum(t => t.NetPnL)), 2),
            AverageDuration = TimeSpan.FromSeconds(trades.Average(t => t.Duration.TotalSeconds))
        };
        var rrs = trades.Where(t => t.RealizedRR.HasValue).Select(t => (double)t.RealizedRR!.Value).ToList();
        summary.AverageRR = rrs.Count == 0 ? 0 : Math.Round(rrs.Average(), 2);
        decimal equity = 0, peak = 0, maxDd = 0;
        foreach (var t in trades.OrderBy(t => t.ExitTime))
        {
            equity += t.NetPnL;
            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, peak - equity);
        }
        summary.MaxDrawdown = maxDd;
        return summary;
    }
}
