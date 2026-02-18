namespace Monity.App;

/// <summary>
/// Sabit kategori listesi. Boş string = Kategorisiz.
/// </summary>
public static class AppCategories
{
    /// <summary>Filtre için "Tümü" = null; dropdown'da "Kategorisiz" = "".</summary>
    public static readonly IReadOnlyList<string> All = ["", "Diğer", "Tarayıcı", "Geliştirme", "Sosyal", "Eğlence", "Ofis"];

    public static string GetDisplayName(string? categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
            return "Kategorisiz";
        return categoryName;
    }
}
