using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.App.Services;
using Monity.Domain.Entities;

namespace Monity.App.Views;

public partial class AchievementsPage : Page
{
    private readonly IAchievementService _achievementService;

    public AchievementsPage(IServiceProvider services)
    {
        InitializeComponent();
        _achievementService = services.GetRequiredService<IAchievementService>();
        Loaded += AchievementsPage_Loaded;
    }

    private async void AchievementsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAchievementsAsync();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService.CanGoBack)
        {
            NavigationService.GoBack();
        }
    }

    private async System.Threading.Tasks.Task RefreshAchievementsAsync()
    {
        var statuses = await _achievementService.GetAchievementsAsync();
        var vms = statuses.Select(s => new AchievementViewModel(s.Achievement, s.Progress)).ToList();
        AchievementsItemsControl.ItemsSource = vms;
    }
}

public class AchievementViewModel
{
    private readonly Achievement _achievement;
    private readonly UserAchievement _progress;

    public AchievementViewModel(Achievement achievement, UserAchievement progress)
    {
        _achievement = achievement;
        _progress = progress;
    }

    public string Title => Strings.Get($"Achievement_{_achievement.Key}_Title");
    public string Description => Strings.Get($"Achievement_{_achievement.Key}_Desc");

    public double ProgressValue
    {
        get
        {
            if (_progress.IsUnlocked) return 100;
            if (_achievement.GoalValue <= 0) return 0;
            return Math.Min(100, (double)_progress.CurrentValue / _achievement.GoalValue * 100);
        }
    }

    public string ProgressText
    {
        get
        {
            if (_progress.IsUnlocked) return "100%";
            if (_achievement.Type == "streak")
                return $"{_progress.CurrentValue} / {_achievement.GoalValue} gün";
            if (_achievement.Type == "session_total")
                return $"{_progress.CurrentValue / 3600} / {_achievement.GoalValue / 3600} saat";
            return $"{_progress.CurrentValue} / {_achievement.GoalValue}";
        }
    }

    public string StatusText => _progress.IsUnlocked 
        ? (_progress.UnlockedAt?.ToString("dd.MM.yyyy") ?? "Açıldı") 
        : "Devam Ediyor";

    public System.Windows.Media.Brush StatusBrush => _progress.IsUnlocked 
        ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryBrush"] 
        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextMutedBrush"];

    public System.Windows.Media.Brush IconBackground => _progress.IsUnlocked 
        ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 67, 128, 246)) // Light blue tint
        : new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 128, 128, 128)); // Faint gray

    public System.Windows.Media.Brush IconColor => _progress.IsUnlocked 
        ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryBrush"] 
        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextMutedBrush"];

    public string IconPath => _achievement.Key switch
    {
        "steady_hand" => "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z", // Star
        "deep_focus" => "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z", // Eye
        "early_bird" => "M12,7L11,10H13L12,7M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6L14,12L12,18L10,12L12,6Z", // Sun/Clock
        "night_owl" => "M12.1,22C10.3,22 8.6,21.5 7,20.5C4,18.7 2,15.5 2,12C2,8.5 4,5.3 7,3.5C8.4,2.7 10,2.2 11.6,2.2C12.1,2.2 12.5,2.7 12.3,3.1C11,5.6 10.3,8.3 10.3,11C10.3,14 11,16.9 12.3,19.5C12.5,19.9 12.1,20.4 11.6,20.4C11.1,20.4 10.6,20.4 10.1,20.4C10.1,20.4 10.1,20.4 10.1,20.4C10.1,20.4 10.2,20.4 10.2,20.4C11.3,20.4 12.3,20.3 13.3,20.1C13.8,20 14.1,20.5 13.8,20.9C13,21.6 12,22 11,22C11.4,22 11.7,22 12.1,22Z", // Moon
        _ => "M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z" // Rocket/Default
    };
}
