using System.Text.Json.Serialization;

namespace BENTradeReviewer.UI.MetaTrader;

public sealed class MetaTraderExit
{
    public int Number { get; set; }

    public long DealTicket { get; set; }

    public long OrderTicket { get; set; }

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime Time { get; set; }

    public decimal GrossPnl { get; set; }

    public decimal Commission { get; set; }

    public decimal Swap { get; set; }

    public decimal Fee { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;
}

public sealed class MetaTraderTrade
{
    public string TradeId { get; set; } = string.Empty;

    public long PositionId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public DateTime EntryTime { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal? StopLoss { get; set; }

    public decimal? TakeProfit { get; set; }

    public List<MetaTraderExit> Exits { get; set; } = new();

    // Gross P&L supplied by MT5.
    public decimal PnL { get; set; }

    public decimal GrossPnl { get; set; }

    public decimal Commission { get; set; }

    public decimal Swap { get; set; }

    public decimal Fee { get; set; }

    public decimal NetPnl { get; set; }

    public string Outcome { get; set; } = string.Empty;

    [JsonIgnore]
    public decimal ClosedQuantity =>
        Exits.Sum(x => x.Quantity);

    [JsonIgnore]
    public decimal RemainingQuantity =>
        Math.Max(
            0m,
            Quantity - ClosedQuantity);

    [JsonIgnore]
    public MetaTraderExit? FinalExit =>
        Exits.Count == 0
            ? null
            : Exits
                .OrderBy(x => x.Time)
                .Last();

    [JsonIgnore]
    public DateTime? ExitTime =>
        FinalExit?.Time;

    [JsonIgnore]
    public string CalculatedOutcome =>
        NetPnl > 0m
            ? "WIN"
            : NetPnl < 0m
                ? "LOSS"
                : "BREAK EVEN";
}
