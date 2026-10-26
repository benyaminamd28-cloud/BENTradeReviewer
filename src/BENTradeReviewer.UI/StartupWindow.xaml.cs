using System.Windows;

namespace BENTradeReviewer;

public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
    }

    private void NinjaTrader_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new MainWindow();

        Application.Current.MainWindow = window;

        window.Show();

        Close();
    }

    private void MetaTrader_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new MetaTraderWindow();

        Application.Current.MainWindow = window;

        window.Show();

        Close();
    }
}
