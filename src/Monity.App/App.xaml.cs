using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Power;
using Monity.App.Services;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;
using Monity.Infrastructure.WinApi;
using Serilog;

namespace Monity.App;

public partial class App : System.Windows.Application
{
    private IServiceProvider _services = null!;
    public IServiceProvider Services => _services;
    private UsageTrackingService _trackingService = null!;
    private PowerEventHandler _powerHandler = null!;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Monity", "Logs", "monity-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("Monity starting");

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        _trackingService = _services.GetRequiredService<UsageTrackingService>();

        var repo = _services.GetRequiredService<IUsageRepository>();
        var idleStr = await repo.GetSettingAsync("idle_threshold_seconds") ?? "60";
        if (uint.TryParse(idleStr, out var idleSec) && idleSec >= 10 && idleSec <= 600)
            _trackingService.IdleThresholdMs = idleSec * 1000;

        var ignored = await repo.GetSettingAsync("ignored_processes") ?? "";
        var userList = ignored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var engine = _services.GetRequiredService<ITrackingEngine>();
        engine.SetIgnoredProcesses(["Monity.App", "explorer"], userList);

        var themeService = _services.GetRequiredService<ThemeService>();
        await themeService.ApplyStoredThemeAsync();

        _trackingService.Start();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        _powerHandler = _services.GetRequiredService<PowerEventHandler>();
        _powerHandler.Attach(mainWindow);

        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDatabasePathProvider, DatabasePathProvider>();
        services.AddSingleton<IUsageRepository, UsageRepository>();
        services.AddSingleton<IWindowTracker, WindowTracker>();
        services.AddSingleton<ITrayNotifier, TrayNotifier>();
        services.AddSingleton<IDailyLimitCheckService, DailyLimitCheckService>();
        services.AddSingleton<SessionBuffer>(sp => new SessionBuffer(
            sp.GetRequiredService<IUsageRepository>(),
            async (appIds) => { await sp.GetRequiredService<IDailyLimitCheckService>().CheckAndNotifyAsync(appIds); }));
        services.AddSingleton<ITrackingEngine>(sp =>
        {
            var wt = sp.GetRequiredService<IWindowTracker>();
            return new TrackingEngine(wt);
        });
        services.AddSingleton<UsageTrackingService>(sp =>
        {
            var engine = sp.GetRequiredService<ITrackingEngine>();
            var repo = sp.GetRequiredService<IUsageRepository>();
            var buffer = sp.GetRequiredService<SessionBuffer>();
            var wt = sp.GetRequiredService<IWindowTracker>();
            return new UsageTrackingService(engine, repo, buffer, wt);
        });
        services.AddSingleton<PowerEventHandler>(sp =>
        {
            var engine = sp.GetRequiredService<ITrackingEngine>();
            return new PowerEventHandler(
                onSuspend: () => engine.HandlePowerSuspend(),
                onResume: () => engine.HandlePowerResume());
        });
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ThemeService>();
        services.AddTransient<MainWindow>();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        Log.Information("Monity shutting down");
        _powerHandler.Detach();
        await _trackingService.StopAsync();
        Log.CloseAndFlush();
    }

    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Session ending - flushing buffer");
        _powerHandler.Detach();
        _trackingService.StopAsync().GetAwaiter().GetResult();
    }
}
