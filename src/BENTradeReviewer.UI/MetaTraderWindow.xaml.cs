using System.Windows;

namespace BENTradeReviewer;

public partial class MetaTraderWindow : Window
{
    private readonly MetaTraderViewModel _viewModel;

    public MetaTraderWindow()
    {
        InitializeComponent();

        _viewModel =
            new MetaTraderViewModel();

        DataContext =
            _viewModel;
    }

    private void Import_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _viewModel.Import();

            MessageBox.Show(
                this,
                $"JSON files found: {_viewModel.LastFilesFound}"
                + Environment.NewLine
                + $"Imported / updated: {_viewModel.LastImported}"
                + Environment.NewLine
                + $"Failed: {_viewModel.LastFailed}",
                "MetaTrader Import",
                MessageBoxButton.OK,
                _viewModel.LastFailed > 0
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "MetaTrader Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PreviousMonth_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.NavigateMonth(-1);
    }

    private void NextMonth_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.NavigateMonth(1);
    }

    private void CalendarDay_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;

        if (button.Tag is DateTime date)
        {
            _viewModel.SelectDay(date);
        }
    }

    private void ViewDetail_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.SelectedTrade is null)
        {
            MessageBox.Show(
                this,
                "Select a trade first.",
                "MetaTrader Journal",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var detail =
            new MetaTraderTradeDetailWindow(
                _viewModel.SelectedTrade);

        detail.Owner = this;

        detail.ShowDialog();
    }

    private void Back_Click(
        object sender,
        RoutedEventArgs e)
    {
        var startup =
            new StartupWindow();

        Application.Current.MainWindow =
            startup;

        startup.Show();

        Close();
    }
}
