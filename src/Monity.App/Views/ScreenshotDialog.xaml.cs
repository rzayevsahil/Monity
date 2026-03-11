using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Monity.App.Helpers;

namespace Monity.App.Views;

public partial class ScreenshotDialog : Window
{
    private BitmapSource? _currentBitmap;
    private bool _isVisibleArea = true;

    public ScreenshotDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
    }

    private MainWindow? GetMainWindow()
    {
        return Owner as MainWindow;
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        _isVisibleArea = RadioVisible?.IsChecked == true;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var main = GetMainWindow();
        if (main == null) return;
        try
        {
            _currentBitmap = _isVisibleArea ? CaptureVisible(main) : CaptureFullPage(main);
            if (_currentBitmap != null)
            {
                PreviewImage.Source = _currentBitmap;
                PreviewImage.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            _currentBitmap = null;
            PreviewImage.Source = null;
        }
    }

    private static BitmapSource? CaptureVisible(MainWindow main)
    {
        var root = main.Content as Visual;
        if (root is not FrameworkElement fe || fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
            return null;
        fe.UpdateLayout();
        var w = Math.Max(1, (int)fe.ActualWidth);
        var h = Math.Max(1, (int)fe.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        return rtb;
    }

    private static BitmapSource? CaptureFullPage(MainWindow main)
    {
        if (main.Content is not Grid rootGrid || rootGrid.ActualWidth <= 0)
            return CaptureVisible(main);
        rootGrid.UpdateLayout();
        var width = rootGrid.ActualWidth;
        // MainWindow grid çocukları: [0]=header, [1]=Frame, [2]=footer
        var headerEl = rootGrid.Children.Count > 0 && rootGrid.Children[0] is FrameworkElement h ? h : null;
        var footerEl = rootGrid.Children.Count > 2 && rootGrid.Children[2] is FrameworkElement f ? f : null;
        RenderTargetBitmap? bmpHeader = null;
        RenderTargetBitmap? bmpFooter = null;
        if (headerEl != null && headerEl.ActualWidth > 0 && headerEl.ActualHeight > 0)
        {
            bmpHeader = new RenderTargetBitmap(
                Math.Max(1, (int)headerEl.ActualWidth),
                Math.Max(1, (int)headerEl.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            bmpHeader.Render(headerEl);
        }
        // Footer: layout'u bozmadan VisualBrush ile yakala
        if (footerEl != null)
        {
            footerEl.UpdateLayout();
            var footerHeight = footerEl.ActualHeight > 0
                ? footerEl.ActualHeight
                : footerEl.DesiredSize.Height;
            var fh = Math.Max(1, (int)footerHeight);
            var fw = Math.Max(1, (int)width);
            var dvFooter = new DrawingVisual();
            using (var dc = dvFooter.RenderOpen())
            {
                var vb = new VisualBrush(footerEl);
                dc.DrawRectangle(vb, null, new Rect(0, 0, fw, fh));
            }
            bmpFooter = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
            bmpFooter.Render(dvFooter);
        }
        var frame = main.MainFrame;
        if (frame?.Content is not Page page || frame.ActualWidth <= 0)
            return CaptureVisible(main);
        frame.UpdateLayout();
        var scrollViewer = FindFirstScrollViewer(page);
        FrameworkElement? contentToRender = null;
        double contentHeight;
        if (scrollViewer?.Content is FrameworkElement scrollContent)
        {
            contentToRender = scrollContent;
            scrollContent.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            contentHeight = Math.Max(1, scrollContent.DesiredSize.Height);
        }
        else
        {
            if (page.Content is FrameworkElement pageContent)
                contentToRender = pageContent;
            else
                contentToRender = page;
            contentToRender.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            contentHeight = Math.Max(1, contentToRender.DesiredSize.Height);
        }
        if (contentToRender == null || contentHeight > 16000)
            return CaptureVisible(main);
        // Canlı arayüzü hiç etkilememek için içeriği geçici olarak ekran dışı panele taşıyıp orada render al
        RenderTargetBitmap? bmpContent = null;
        if (scrollViewer != null)
        {
            scrollViewer.Content = null;
            var offscreenBorder = new Border
            {
                Width = width,
                Height = contentHeight,
                Child = contentToRender,
                Background = main.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush
            };
            var offscreenCanvas = new Canvas
            {
                Width = rootGrid.ActualWidth,
                Height = rootGrid.ActualHeight,
                IsHitTestVisible = false,
                ClipToBounds = false
            };
            System.Windows.Controls.Canvas.SetLeft(offscreenBorder, -width * 3);
            System.Windows.Controls.Canvas.SetTop(offscreenBorder, 0);
            offscreenCanvas.Children.Add(offscreenBorder);
            Grid.SetRowSpan(offscreenCanvas, 3);
            Grid.SetRow(offscreenCanvas, 0);
            rootGrid.Children.Add(offscreenCanvas);
            try
            {
                rootGrid.UpdateLayout();
                bmpContent = new RenderTargetBitmap(
                    Math.Max(1, (int)width),
                    Math.Max(1, (int)contentHeight),
                    96, 96, PixelFormats.Pbgra32);
                bmpContent.Render(contentToRender);
            }
            finally
            {
                rootGrid.Children.Remove(offscreenCanvas);
                offscreenBorder.Child = null;
                scrollViewer.Content = contentToRender;
                rootGrid.UpdateLayout();
            }
        }
        else
        {
            try
            {
                contentToRender.Arrange(new Rect(0, 0, width, contentHeight));
                bmpContent = new RenderTargetBitmap(
                    Math.Max(1, (int)width),
                    Math.Max(1, (int)contentHeight),
                    96, 96, PixelFormats.Pbgra32);
                bmpContent.Render(contentToRender);
            }
            finally
            {
                frame.UpdateLayout();
            }
        }
        var hHeader = bmpHeader?.PixelHeight ?? 0;
        var hFooter = bmpFooter?.PixelHeight ?? 0;
        var hContent = bmpContent?.PixelHeight ?? 0;
        var totalHeight = hHeader + hContent + hFooter;
        if (totalHeight <= 0) return CaptureVisible(main);
        var totalWidth = Math.Max(1, (int)width);
        var backgroundBrush = main.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, totalWidth, totalHeight));
            var y = 0;
            if (bmpHeader != null && hHeader > 0)
            {
                dc.DrawImage(bmpHeader, new Rect(0, y, totalWidth, hHeader));
                y += hHeader;
            }
            if (bmpContent != null && hContent > 0)
            {
                dc.DrawImage(bmpContent, new Rect(0, y, totalWidth, hContent));
                y += hContent;
            }
            if (bmpFooter != null && hFooter > 0)
                dc.DrawImage(bmpFooter, new Rect(0, y, totalWidth, hFooter));
        }
        var composite = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        composite.Render(dv);
        rootGrid.UpdateLayout();
        return composite;
    }

    private static ScrollViewer? FindFirstScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindFirstScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG görüntü|*.png|Tüm dosyalar|*.*",
            DefaultExt = ".png",
            FileName = $"Monity-ekran-goruntusu-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_currentBitmap));
            using (var stream = File.Create(dlg.FileName))
                encoder.Save(stream);
            System.Windows.MessageBox.Show(
                Strings.Get("Screenshot_Saved"),
                "Monity",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Monity",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null) return;
        try
        {
            System.Windows.Clipboard.SetImage(_currentBitmap);
            System.Windows.MessageBox.Show(
                Strings.Get("Main_ScreenshotCopied"),
                "Monity",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            System.Windows.MessageBox.Show(
                Strings.Get("Main_ScreenshotFailed"),
                "Monity",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
