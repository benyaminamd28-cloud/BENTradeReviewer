using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using BENTradeReviewer.UI.MetaTrader;

namespace BENTradeReviewer;

public sealed class MetaTraderTradeDetailWindow : Window
{
    public MetaTraderTradeDetailWindow(
        MetaTraderTrade trade)
    {
        Title =
            $"MT5 TRADE DETAIL — {trade.TradeId}";

        Width = 900;
        Height = 620;

        MinWidth = 760;
        MinHeight = 500;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        Background =
            new SolidColorBrush(
                Color.FromRgb(24, 27, 32));

        Foreground =
            Brushes.White;

        Build(trade);
    }

    private void Build(
        MetaTraderTrade trade)
    {
        var root =
            new Grid
            {
                Margin =
                    new Thickness(18)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var header =
            new TextBlock
            {
                Text =
                    $"META TRADER TRADE — {trade.TradeId}",
                FontSize = 18,
                FontWeight =
                    FontWeights.Bold,
                Foreground =
                    Brushes.White,
                Margin =
                    new Thickness(0, 0, 0, 14)
            };

        Grid.SetRow(header, 0);

        root.Children.Add(header);

        var content =
            new Grid();

        content.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(270)
            });

        content.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        // ====================================================
        // LEFT: TRADE SUMMARY
        // ====================================================

        var summaryBorder =
            new Border
            {
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(31, 36, 43)),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(58, 64, 73)),

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(6),

                Padding =
                    new Thickness(14),

                Margin =
                    new Thickness(0, 0, 12, 0)
            };

        var summary =
            new StackPanel();

        AddField(
            summary,
            "SYMBOL",
            trade.Symbol);

        AddField(
            summary,
            "SIDE",
            trade.Side);

        AddField(
            summary,
            "INITIAL QTY",
            trade.Quantity.ToString(
                "F2",
                CultureInfo.InvariantCulture));

        AddField(
            summary,
            "ENTRY TIME",
            trade.EntryTime.ToString(
                "yyyy-MM-dd HH:mm:ss"));

        AddField(
            summary,
            "ENTRY PRICE",
            trade.EntryPrice.ToString(
                "F5",
                CultureInfo.InvariantCulture));

        AddField(
            summary,
            "STOP LOSS",
            trade.StopLoss.HasValue
                ? trade.StopLoss.Value.ToString(
                    "F5",
                    CultureInfo.InvariantCulture)
                : "N/A");

        AddField(
            summary,
            "CLOSED QTY",
            trade.ClosedQuantity.ToString(
                "F2",
                CultureInfo.InvariantCulture));

        AddField(
            summary,
            "REMAINING QTY",
            trade.RemainingQuantity.ToString(
                "F2",
                CultureInfo.InvariantCulture));

        AddField(
            summary,
            "TOTAL EXITS",
            trade.Exits.Count.ToString());

        AddField(
            summary,
            "FINAL EXIT",
            trade.FinalExit is null
                ? "N/A"
                : trade.FinalExit.Price.ToString(
                    "F5",
                    CultureInfo.InvariantCulture));

        AddField(
            summary,
            "P&L",
            trade.PnL.ToString(
                "C2",
                CultureInfo.CurrentCulture));

        AddField(
            summary,
            "OUTCOME",
            trade.Outcome);

        summaryBorder.Child =
            summary;

        Grid.SetColumn(
            summaryBorder,
            0);

        content.Children.Add(
            summaryBorder);

        // ====================================================
        // RIGHT: EXIT EVENTS
        // ====================================================

        var exitsBorder =
            new Border
            {
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(31, 36, 43)),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(58, 64, 73)),

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(6),

                Padding =
                    new Thickness(12)
            };

        var exitsGrid =
            new Grid();

        exitsGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        exitsGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var exitsTitle =
            new TextBlock
            {
                Text =
                    "EXIT EVENTS — SOURCE: MT5 EA",
                FontSize = 13,
                FontWeight =
                    FontWeights.Bold,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            150,
                            255)),
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10)
            };

        Grid.SetRow(
            exitsTitle,
            0);

        exitsGrid.Children.Add(
            exitsTitle);

        var exitsGridControl =
            new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility =
                    DataGridHeadersVisibility.Column,

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                GridLinesVisibility =
                    DataGridGridLinesVisibility.None,

                ItemsSource =
                    trade.Exits
                        .OrderBy(x => x.Time)
                        .ToList()
            };

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "#",
                Binding =
                    new System.Windows.Data.Binding(
                        "Time"),

                Width = 50
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "TIME",
                Binding =
                    new System.Windows.Data.Binding(
                        "Time")
                    {
                        StringFormat =
                            "yyyy-MM-dd HH:mm:ss"
                    },

                Width = 180
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "QUANTITY",
                Binding =
                    new System.Windows.Data.Binding(
                        "Quantity")
                    {
                        StringFormat = "F2"
                    },

                Width = 100
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "PRICE",
                Binding =
                    new System.Windows.Data.Binding(
                        "Price")
                    {
                        StringFormat = "F5"
                    },

                Width = 120
            });

        var exitRows =
            trade.Exits
                .OrderBy(x => x.Time)
                .Select(
                    (x, i) =>
                        new MetaTraderExitRow
                        {
                            Number = i + 1,
                            Time = x.Time,
                            Quantity = x.Quantity,
                            Price = x.Price
                        })
                .ToList();

        exitsGridControl.ItemsSource =
            exitRows;

        // Rebuild columns for row wrapper.
        exitsGridControl.Columns.Clear();

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "#",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(MetaTraderExitRow.Number)),
                Width = 55
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "TIME",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(MetaTraderExitRow.Time))
                    {
                        StringFormat =
                            "yyyy-MM-dd HH:mm:ss"
                    },
                Width = 190
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "QUANTITY",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(MetaTraderExitRow.Quantity))
                    {
                        StringFormat = "F2"
                    },
                Width = 110
            });

        exitsGridControl.Columns.Add(
            new DataGridTextColumn
            {
                Header = "PRICE",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(MetaTraderExitRow.Price))
                    {
                        StringFormat = "F5"
                    },
                Width = 130
            });

        Grid.SetRow(
            exitsGridControl,
            1);

        exitsGrid.Children.Add(
            exitsGridControl);

        exitsBorder.Child =
            exitsGrid;

        Grid.SetColumn(
            exitsBorder,
            1);

        content.Children.Add(
            exitsBorder);

        Grid.SetRow(
            content,
            1);

        root.Children.Add(
            content);

        var closeButton =
            new Button
            {
                Content = "CLOSE",
                Width = 90,
                Height = 30,
                HorizontalAlignment =
                    HorizontalAlignment.Right,

                Margin =
                    new Thickness(0, 12, 0, 0)
            };

        closeButton.Click +=
            (_, _) => Close();

        Grid.SetRow(
            closeButton,
            2);

        root.Children.Add(
            closeButton);

        Content = root;
    }

    private static void AddField(
        StackPanel panel,
        string label,
        string value)
    {
        var block =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10)
            };

        block.Children.Add(
            new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight =
                    FontWeights.Bold,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            145,
                            153,
                            165))
            });

        block.Children.Add(
            new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground =
                    Brushes.White,
                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        0)
            });

        panel.Children.Add(block);
    }

    private sealed class MetaTraderExitRow
    {
        public int Number { get; init; }

        public DateTime Time { get; init; }

        public decimal Quantity { get; init; }

        public decimal Price { get; init; }
    }
}
