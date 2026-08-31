using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;

namespace BENTradeReviewer.Services;

public static class RiskRewardEngine
{
    public static (decimal PlannedRiskUSD, decimal PlannedRewardUSD, decimal? PlannedRR) CalculatePlannedRiskReward(
        TradeSide side, decimal entryPrice, decimal? stopLoss, decimal? takeProfit, int quantity, InstrumentSpec? spec)
    {
        if (stopLoss is null || spec is null || spec.TickSize <= 0 || quantity <= 0) return (0, 0, null);
        var riskPoints = side == TradeSide.Long ? entryPrice - stopLoss.Value : stopLoss.Value - entryPrice;
        if (riskPoints <= 0) return (0, 0, null);
        var riskTicks = riskPoints / spec.TickSize;
        var risk = riskTicks * spec.TickValue * quantity;
        decimal reward = 0;
        decimal? rr = null;
        if (takeProfit is not null)
        {
            var rewardPoints = side == TradeSide.Long ? takeProfit.Value - entryPrice : entryPrice - takeProfit.Value;
            if (rewardPoints > 0)
            {
                reward = rewardPoints / spec.TickSize * spec.TickValue * quantity;
                if (risk > 0) rr = Math.Round(reward / risk, 2);
            }
        }
        return (risk, reward, rr);
    }

    public static (decimal RealizedPoints, decimal RealizedTicks, decimal GrossPnL, decimal NetPnL, decimal? RealizedRR) CalculateRealizedPerformance(
        TradeSide side, decimal entryPrice, decimal exitPrice, decimal? stopLoss, int quantity, decimal commission, InstrumentSpec? spec)
    {
        var points = side == TradeSide.Long ? exitPrice - entryPrice : entryPrice - exitPrice;
        if (spec is null || spec.TickSize <= 0)
        {
            var gross = points * quantity;
            return (points, 0, gross, gross - commission, null);
        }
        var ticks = points / spec.TickSize;
        var grossPnL = ticks * spec.TickValue * quantity;
        var netPnL = grossPnL - commission;
        decimal? realizedRR = null;
        if (stopLoss is not null)
        {
            var riskPoints = side == TradeSide.Long ? entryPrice - stopLoss.Value : stopLoss.Value - entryPrice;
            if (riskPoints > 0) realizedRR = Math.Round(points / riskPoints, 2);
        }
        return (points, ticks, grossPnL, netPnL, realizedRR);
    }
}
