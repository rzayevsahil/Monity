using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Monity.App.Helpers;
using Monity.App.ViewModels;

namespace Monity.App.Views;

public partial class ShareCard : System.Windows.Controls.UserControl
{
    public ShareCard()
    {
        InitializeComponent();
        DataContextChanged += (s, _) => UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (DataContext is not ShareCardViewModel vm) return;
        ContentPanel.Visibility = vm.HasData ? Visibility.Visible : Visibility.Collapsed;
        TxtNoData.Visibility = vm.HasData ? Visibility.Collapsed : Visibility.Visible;
        if (!vm.HasData) TxtNoData.Text = Strings.Get("Share_NoData");

        TopCategoryCard.Visibility = vm.TopCategories.Count > 0 || !string.IsNullOrEmpty(vm.TopCategoryName) ? Visibility.Visible : Visibility.Collapsed;
        TopAppsCard.Visibility = vm.TopAppBars.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HighlightsCard.Visibility = vm.Highlights.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class BarWidthConverter : IValueConverter
{
    private const double MaxBarWidth = 200;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double pct)
            return Math.Max(20, pct * MaxBarWidth / 100.0);
        return 20.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
