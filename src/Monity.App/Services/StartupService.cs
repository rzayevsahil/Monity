using System.IO;
using Microsoft.Win32;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

/// <summary>
/// Manages "start with Windows" via the Registry Run key.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Monity";

    private readonly IUsageRepository _repository;

    public StartupService(IUsageRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> GetIsEnabledAsync(CancellationToken ct = default)
    {
        var stored = await _repository.GetSettingAsync("start_with_windows", ct);
        if (string.Equals(stored, "1", StringComparison.Ordinal) || string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                Serilog.Log.Warning("Could not determine exe path for startup registry");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Serilog.Log.Warning("Could not open Run key for startup");
                return;
            }

            if (enabled)
            {
                var value = exePath.Contains(' ') ? $"\"{exePath}\"" : exePath;
                key.SetValue(ValueName, value);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            await _repository.SetSettingAsync("start_with_windows", enabled ? "1" : "0", ct);
            Serilog.Log.Information("Start with Windows: {Enabled}", enabled);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to set start with Windows");
            throw;
        }
    }
}
