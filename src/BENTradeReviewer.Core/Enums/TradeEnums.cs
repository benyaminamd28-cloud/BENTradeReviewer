namespace BENTradeReviewer.Core.Enums;

public enum TradeSide { Long, Short }
public enum TradeOutcome { Win, Loss, BreakEven }
public enum TradeEventType
{
    PreTradeRecordingStart,
    OrderSubmitted,
    EntryFilled,
    StopLossModified,
    TakeProfitModified,
    ScaleOut,
    ExitFilled,
    PostTradeRecordingEnd,
    GenericEvent
}
