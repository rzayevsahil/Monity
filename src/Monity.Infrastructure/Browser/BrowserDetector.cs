using System.Text.RegularExpressions;
using Serilog;

namespace Monity.Infrastructure.Browser;

public static class BrowserDetector
{
    public static string DetectBrowserFromWindowTitle(string windowTitle, string processName, string? processPath = null)
    {
        Log.Debug("Browser detection: Title='{WindowTitle}', Process='{ProcessName}', Path='{ProcessPath}'", 
            windowTitle, processName, processPath);

        if (string.IsNullOrEmpty(windowTitle))
            return DetectBrowserFromProcessName(processName, processPath);

        // Opera GX detection - Check for Opera GX in window title or process path
        if (windowTitle.Contains("Opera GX", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(processPath) && processPath.Contains("Opera GX", StringComparison.OrdinalIgnoreCase)) ||
            processName.Contains("operagx", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("Detected Opera GX browser");
            return "operagx";
        }

        // Chrome detection
        if (windowTitle.Contains("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.EndsWith("- Google Chrome", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        // Firefox detection
        if (windowTitle.Contains("Mozilla Firefox", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.EndsWith("- Mozilla Firefox", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("firefox", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        // Edge detection
        if (windowTitle.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.EndsWith("- Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        // Safari detection
        if (windowTitle.Contains("Safari", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("safari", StringComparison.OrdinalIgnoreCase))
        {
            return "safari";
        }

        // Regular Opera detection (after Opera GX check)
        if (windowTitle.Contains("Opera", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("opera", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("Detected regular Opera browser");
            return "opera";
        }

        var result = DetectBrowserFromProcessName(processName, processPath);
        Log.Debug("Browser detection result: '{BrowserType}'", result);
        return result;
    }

    public static string DetectBrowserFromProcessName(string processName, string? processPath = null)
    {
        if (string.IsNullOrEmpty(processName))
            return "unknown";

        var lowerProcess = processName.ToLowerInvariant();

        var result = lowerProcess switch
        {
            var p when p.Contains("operagx") => "operagx",
            var p when p.Contains("opera") && !string.IsNullOrEmpty(processPath) && processPath.Contains("Opera GX", StringComparison.OrdinalIgnoreCase) => "operagx",
            var p when p.Contains("chrome") => "chrome",
            var p when p.Contains("firefox") => "firefox",
            var p when p.Contains("msedge") => "edge",
            var p when p.Contains("safari") => "safari",
            var p when p.Contains("opera") => "opera",
            _ => "unknown"
        };

        Log.Debug("Process name detection: '{ProcessName}' -> '{BrowserType}'", processName, result);
        return result;
    }

    public static string ExtractUrlFromWindowTitle(string windowTitle, string browserType)
    {
        if (string.IsNullOrEmpty(windowTitle))
            return "";

        Log.Debug("Extracting URL from title: '{WindowTitle}' for browser: '{BrowserType}'", windowTitle, browserType);

        // Try to extract URL from common browser title patterns
        // Most browsers show: "Page Title - Browser Name" or "Page Title | Browser Name"
        
        // Look for URL patterns in the title
        var urlPattern = @"https?://[^\s\-\|]+";
        var match = Regex.Match(windowTitle, urlPattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            Log.Debug("Found direct URL in title: '{Url}'", match.Value);
            return match.Value;
        }

        // If no direct URL found, try to extract domain from common patterns
        var domainPattern = browserType switch
        {
            // Opera GX shows as "Page Title - Site - Opera" (not "Opera GX")
            "operagx" => @"(.+?)\s*[-–—]\s*(.+?)\s*[-–—]\s*Opera",
            "chrome" => @"(.+?)\s*[-–—]\s*Google Chrome",
            "firefox" => @"(.+?)\s*[-–—]\s*Mozilla Firefox",
            "edge" => @"(.+?)\s*[-–—]\s*Microsoft Edge",
            "safari" => @"(.+?)\s*[-–—]\s*Safari",
            "opera" => @"(.+?)\s*[-–—]\s*Opera",
            _ => @"(.+?)\s*[-–—]\s*.+"
        };

        var titleMatch = Regex.Match(windowTitle, domainPattern, RegexOptions.IgnoreCase);
        if (titleMatch.Success)
        {
            Log.Debug("Title pattern matched. Groups: {Groups}", string.Join(", ", titleMatch.Groups.Cast<Group>().Select(g => g.Value)));
            
            // For Opera GX, we have 3 groups: full match, page title, site name
            if (browserType == "operagx" && titleMatch.Groups.Count >= 3)
            {
                var siteName = titleMatch.Groups[2].Value.Trim();
                Log.Debug("Extracted site name for Opera GX: '{SiteName}'", siteName);
                
                // Check if site name looks like a domain or known site
                if (siteName.Contains(".com") || siteName.Contains(".org") || 
                    siteName.Contains(".net") || siteName.Contains(".edu") ||
                    siteName.Contains("www.") ||
                    siteName.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ||
                    siteName.Equals("Google", StringComparison.OrdinalIgnoreCase) ||
                    siteName.Equals("Facebook", StringComparison.OrdinalIgnoreCase) ||
                    siteName.Equals("Twitter", StringComparison.OrdinalIgnoreCase))
                {
                    // Convert known site names to domains
                    var domain = siteName.ToLowerInvariant() switch
                    {
                        "youtube" => "youtube.com",
                        "google" => "google.com",
                        "facebook" => "facebook.com",
                        "twitter" => "twitter.com",
                        _ => siteName.StartsWith("http") ? siteName : $"https://{siteName}"
                    };
                    Log.Debug("Converted to domain: '{Domain}'", domain);
                    return domain;
                }
            }
            else
            {
                // For other browsers, use the first group (page title)
                var pageTitle = titleMatch.Groups[1].Value.Trim();
                
                // If the page title looks like a domain, return it as URL
                if (pageTitle.Contains(".com") || pageTitle.Contains(".org") || 
                    pageTitle.Contains(".net") || pageTitle.Contains(".edu") ||
                    pageTitle.Contains("www."))
                {
                    return pageTitle.StartsWith("http") ? pageTitle : $"https://{pageTitle}";
                }
            }
        }

        Log.Debug("No URL extracted from window title");
        return "";
    }

    public static string GetBrowserDisplayName(string browserType)
    {
        return browserType switch
        {
            "operagx" => "Opera GX",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            "edge" => "Edge",
            "safari" => "Safari",
            "opera" => "Opera",
            _ => "Unknown"
        };
    }
}