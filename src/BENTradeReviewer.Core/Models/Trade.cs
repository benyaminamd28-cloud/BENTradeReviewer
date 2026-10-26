using BENTradeReviewer.Core.Enums;

namespace BENTradeReviewer.Core.Models;

public sealed class Trade
{
    // Observed / imported
    public string TradeId { get; set; } = string.Empty;
    public string FolderDirectory { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public int Quantity { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal? InitialStopLoss { get; set; }
    public decimal? InitialTakeProfit { get; set; }
    public double PreRollSeconds { get; set; } = 5.0;
    public double PostRollSeconds { get; set; } = 5.0;

    // Calculated
    public decimal NetPnL { get; set; }
    public decimal GrossPnL { get; set; }
    public decimal Commission { get; set; }
    public decimal? PlannedRR { get; set; }
    public decimal? RealizedRR { get; set; }
    public decimal RealizedPoints { get; set; }
    public decimal RealizedTicks { get; set; }
    public decimal InitialRiskUSD { get; set; }
    public decimal ExpectedRewardUSD { get; set; }

    // Recorded by BEN Trade Recorder
    public decimal? MAETicks { get; set; }
    public decimal? MAEUSD { get; set; }
    public decimal? MFETicks { get; set; }
    public decimal? MFEUSD { get; set; }
    public TradeOutcome Outcome => NetPnL > 0 ? TradeOutcome.Win : NetPnL < 0 ? TradeOutcome.Loss : TradeOutcome.BreakEven;
    public bool IsWin => NetPnL > 0;
    public TimeSpan Duration => ExitTime >= EntryTime ? ExitTime - EntryTime : TimeSpan.Zero;

    // User annotations
    public string Notes { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public TradeMarketContext MarketContext { get; set; } = new();

    // Related
    public List<TradeVideo> Videos { get; set; } = new();
    public List<TradeEvent> EventTimeline { get; set; } = new();
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TradeVideo
{
    public int VideoId { get; set; }
    public string TradeId { get; set; } = string.Empty;
    public int MonitorIndex { get; set; }
    public string MonitorName { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

public sealed class TradeEvent
{
    public int EventId { get; set; }
    public string TradeId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TradeEventType EventType { get; set; }
    public decimal? Price { get; set; }
    public int? Quantity { get; set; }
    public string Message { get; set; } = string.Empty;
    public double VideoTimestampOffsetSeconds { get; set; }
}
