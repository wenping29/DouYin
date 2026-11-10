using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlayer.Models;
using VideoPlayer.Services;

namespace VideoPlayer;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppSettings _draft;

    public SettingsWindow(AppSettings settings, string appVersion, string engineVersion)
    {
        InitializeComponent();

        _settings = settings;
        _draft = settings.ClonePreferences();
        DataContext = _draft;

        AboutText.Text = $"VideoPlayer {appVersion}\nLibVLC {engineVersion}\n\n多格式视频播放器 · WPF + LibVLCSharp";

        SourceInitialized += (_, _) => WindowEffects.Apply(this);
        UpdateAssociationStatus();
    }

    /// <summary>打开应用日志目录，便于查看 error.log 与当天的 app-yyyyMMdd.log。</summary>
    private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogDirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"打开日志目录失败：{ex.Message}");
        }
    }

    private void UpdateAssociationStatus()
    {
        var registered = FileAssociationService.IsRegistered();
        AssociationStatus.Text = registered
            ? "当前状态：已注册为本程序的打开方式"
            : "当前状态：未注册";
        AssociationStatus.Foreground = registered
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextMutedBrush");
    }

    private void RegisterAssociation_Click(object sender, RoutedEventArgs e)
    {
        if (FileAssociationService.Register())
        {
            _draft.FileAssociationRegistered = true;
            UpdateAssociationStatus();
        }
        else
        {
            ShowAssociationError("注册失败：无法写入当前用户注册表");
        }
    }

    private void UnregisterAssociation_Click(object sender, RoutedEventArgs e)
    {
        if (FileAssociationService.Unregister())
        {
            _draft.FileAssociationRegistered = false;
            UpdateAssociationStatus();
        }
        else
        {
            ShowAssociationError("取消注册失败：无法写入当前用户注册表");
        }
    }

    private void ShowAssociationError(string message)
    {
        AssociationStatus.Text = message;
        AssociationStatus.Foreground = (Brush)FindResource("DangerBrush");
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        FileAssociationService.OpenDefaultAppsSettings();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _draft.Volume = Math.Clamp(_draft.Volume, 0, 100);
        _draft.SubtitleScale = Math.Clamp(_draft.SubtitleScale, 0.5, 2.5);
        _draft.SubtitleDelayMs = Math.Clamp(_draft.SubtitleDelayMs, -5000, 5000);
        _draft.PlaybackRate = Math.Clamp(_draft.PlaybackRate, 0.25, 4.0);

        _settings.CopyPreferencesFrom(_draft);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignore
            }
        }
    }
}
