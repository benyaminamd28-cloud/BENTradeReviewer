using Xunit;
using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Services;

namespace BENTradeReviewer.Tests;

public class RiskRewardAndAnalyticsTests
{
    [Fact]
    public void CalculatePlannedRiskReward_Long6E_ReturnsAccurateMetrics()
    {
        var spec = new InstrumentSpec { Symbol = "6E", TickSize = 0.00005m, TickValue = 6.25m, PointValue = 125000m };
        var result = RiskRewardEngine.CalculatePlannedRiskReward(TradeSide.Long, 1.08500m, 1.08400m, 1.08700m, 1, spec);
        Assert.Equal(125m, result.PlannedRiskUSD);
        Assert.Equal(250m, result.PlannedRewardUSD);
        Assert.Equal(2m, result.PlannedRR);
    }

    [Fact]
    public void CalculateRealizedPerformance_ShortES_CalculatesProfitAndRRCorrectly()
    {
        var spec = new InstrumentSpec { Symbol = "ES", TickSize = 0.25m, TickValue = 12.50m, PointValue = 50m };
        var result = RiskRewardEngine.CalculateRealizedPerformance(TradeSide.Short, 5500m, 5480m, 5510m, 2, 5m, spec);
        Assert.Equal(20m, result.RealizedPoints);
        Assert.Equal(80m, result.RealizedTicks);
        Assert.Equal(2000m, result.GrossPnL);
        Assert.Equal(1995m, result.NetPnL);
        Assert.Equal(2m, result.RealizedRR);
    }

    [Fact]
    public void AnalyticsEngine_CalculatesSummaryAndDrawdown()
    {
        var baseTime = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Local);
        var trades = new List<Trade>
        {
            new() { NetPnL = 300, Quantity = 2, RealizedRR = 2m, EntryTime = baseTime, ExitTime = baseTime.AddMinutes(5) },
            new() { NetPnL = -100, Quantity = 1, RealizedRR = -1m, EntryTime = baseTime.AddMinutes(6), ExitTime = baseTime.AddMinutes(8) },
            new() { NetPnL = 150, Quantity = 1, RealizedRR = 1.5m, EntryTime = baseTime.AddMinutes(9), ExitTime = baseTime.AddMinutes(12) }
        };
        var summary = AnalyticsEngine.CalculateSummary(trades);
        Assert.Equal(3, summary.TotalTrades);
        Assert.Equal(350m, summary.NetPnL);
        Assert.Equal(2, summary.WinningTrades);
        Assert.Equal(1, summary.LosingTrades);
        Assert.Equal(66.67, Math.Round(summary.WinRate, 2));
        Assert.Equal(4.5m, summary.ProfitFactor);
        Assert.Equal(100m, summary.MaxDrawdown);
    }
}

