using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Data;
using BENTradeReviewer.Services;
using Xunit;

namespace BENTradeReviewer.Tests;

public class RecorderImporterTests
{
    [Fact]
    public async Task ParseCurrentRecorderTrade_ES31August_ReadsRecordedMetricsCorrectly()
    {
        var tradeInfoPath =
            Environment.GetEnvironmentVariable("BEN_TRADE_RECORDER_TEST_FILE");

        if (!File.Exists(tradeInfoPath))
            return;

        var tradeFolder = Path.GetDirectoryName(tradeInfoPath)!;

        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"BENTradeReviewer_Test_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new TradeRepository(dbPath);
            var importer = new RecorderImporter(repository);

            var trade = await importer.ParseTradeFolderAsync(
                tradeFolder,
                tradeInfoPath,
                Array.Empty<string>());

            Assert.NotNull(trade);

            Assert.Equal("ES", trade!.Symbol);
            Assert.Equal(TradeSide.Long, trade.Side);

            Assert.Equal(8, trade.Quantity);

            Assert.Equal(7679.0000m, trade.EntryPrice);
            Assert.Equal(7682.7188m, trade.ExitPrice);

            Assert.Equal(7676.2500m, trade.InitialStopLoss);

            Assert.Equal(600.00m, trade.InitialRiskUSD);

            Assert.Equal(119m, trade.RealizedTicks);
            Assert.Equal(29.75m, trade.RealizedPoints);

            Assert.Equal(1487.50m, trade.GrossPnL);

            Assert.Equal(2.3125m, trade.RealizedRR);

            Assert.Equal(1.5000m, trade.PlannedRR);

            Assert.Equal(22.0000m, trade.MFETicks);
            Assert.Equal(1100.00m, trade.MFEUSD);

            Assert.Equal(4.0000m, trade.MAETicks);
            Assert.Equal(200.00m, trade.MAEUSD);
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch
            {
                // Test cleanup must not hide the test result.
            }
        }
    }
}
