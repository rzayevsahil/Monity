using System.Windows.Media.Imaging;

namespace Monity.App.Services;

/// <summary>
/// Result of creating a share card: image and caption for social media.
/// </summary>
public record ShareResult(BitmapSource? Image, byte[]? ImagePngBytes, string CaptionText);
