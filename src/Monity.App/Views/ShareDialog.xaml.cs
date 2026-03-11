using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Monity.App.Helpers;
using Monity.App.Services;

namespace Monity.App.Views;

public partial class ShareDialog : Window
{
    private readonly IShareService _shareService;
    private ShareResult? _lastResult;
    private bool _isLoadingPreview;

    public ShareDialog(IShareService shareService)
    {
        _shareService = shareService;
        InitializeComponent();
        LoadPeriods();
        Loaded += (_, _) => _ = RefreshPreviewAsync();
    }

    private void LoadPeriods()
    {
        var items = new[]
        {
            new PeriodOption(SharePeriod.Today, Strings.Get("Share_PeriodToday")),
            new PeriodOption(SharePeriod.Week, Strings.Get("Share_PeriodWeek")),
            new PeriodOption(SharePeriod.Month, Strings.Get("Share_PeriodMonth"))
        };
        PeriodCombo.ItemsSource = items;
        PeriodCombo.DisplayMemberPath = "Display";
        PeriodCombo.SelectedValuePath = "Period";
        PeriodCombo.SelectedIndex = 0;
    }

    private SharePeriod GetSelectedPeriod()
    {
        if (PeriodCombo.SelectedItem is PeriodOption o) return o.Period;
        return SharePeriod.Today;
    }

    private sealed record PeriodOption(SharePeriod Period, string Display);

    private async Task<ShareResult> CreateResultAsync()
    {
        var context = new ShareContext(GetSelectedPeriod());
        return await _shareService.CreateShareCardAsync(context);
    }

    private async Task RefreshPreviewAsync()
    {
        if (_isLoadingPreview) return;
        _isLoadingPreview = true;
        TxtLoading.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        TxtCaptionPreview.Text = "";
        BtnCopyImage.IsEnabled = false;
        BtnCopyText.IsEnabled = false;
        BtnSaveImage.IsEnabled = false;
        try
        {
            _lastResult = await CreateResultAsync();
            if (_lastResult.Image != null)
            {
                PreviewImage.Source = _lastResult.Image;
                TxtCaptionPreview.Text = _lastResult.CaptionText ?? "";
                BtnCopyImage.IsEnabled = true;
                BtnCopyText.IsEnabled = true;
                BtnSaveImage.IsEnabled = _lastResult.ImagePngBytes != null && _lastResult.ImagePngBytes.Length > 0;
            }
            else
                TxtCaptionPreview.Text = "";
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            TxtLoading.Visibility = Visibility.Collapsed;
            _isLoadingPreview = false;
        }
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodCombo.SelectedItem == null) return;
        _ = RefreshPreviewAsync();
    }

    private void ShowStatus(string text, bool visible = true)
    {
        TxtStatus.Text = text;
        TxtStatus.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnCopyImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastResult?.Image != null)
            {
                System.Windows.Clipboard.SetImage(_lastResult.Image);
                ShowStatus(Strings.Get("Share_ImageCopied"));
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void BtnCopyText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastResult != null && !string.IsNullOrEmpty(_lastResult.CaptionText))
            {
                System.Windows.Clipboard.SetText(_lastResult.CaptionText);
                ShowStatus(Strings.Get("Share_TextCopied"));
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastResult?.ImagePngBytes == null || _lastResult.ImagePngBytes.Length == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG image|*.png",
                DefaultExt = ".png",
                FileName = $"Monity_share_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllBytes(dlg.FileName, _lastResult.ImagePngBytes);
                ShowStatus(string.Format(Strings.Get("Share_Saved"), dlg.FileName));
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
