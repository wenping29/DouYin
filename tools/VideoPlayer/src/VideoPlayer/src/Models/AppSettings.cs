namespace VideoPlayer.Models;

/// <summary>
/// 持久化到 %AppData%\VideoPlayer\settings.json 的用户配置。
/// </summary>
public sealed class AppSettings
{
    // 播放
    public int Volume { get; set; } = 80;
    public bool Muted { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public bool HardwareAcceleration { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public string AspectRatio { get; set; } = "默认";

    // 字幕
    public double SubtitleScale { get; set; } = 1.0;
    public int SubtitleDelayMs { get; set; }

    // 界面
    public bool ShowPlaylist { get; set; } = true;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 780;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    // 播放列表与进度
    public List<string> Playlist { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;
    public Dictionary<string, double> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // 系统集成
    public bool FileAssociationRegistered { get; set; }

    /// <summary>创建设置窗口使用的可编辑副本，取消时不影响原配置。</summary>
    public AppSettings ClonePreferences() => new()
    {
        Volume = Volume,
        Muted = Muted,
        PlaybackRate = PlaybackRate,
        HardwareAcceleration = HardwareAcceleration,
        RememberPosition = RememberPosition,
        AutoPlayNext = AutoPlayNext,
        AspectRatio = AspectRatio,
        SubtitleScale = SubtitleScale,
        SubtitleDelayMs = SubtitleDelayMs,
        ShowPlaylist = ShowPlaylist,
        FileAssociationRegistered = FileAssociationRegistered
    };

    /// <summary>把设置窗口中确认的改动写回原配置（窗口位置、播放列表等不覆盖）。</summary>
    public void CopyPreferencesFrom(AppSettings other)
    {
        Volume = other.Volume;
        Muted = other.Muted;
        PlaybackRate = other.PlaybackRate;
        HardwareAcceleration = other.HardwareAcceleration;
        RememberPosition = other.RememberPosition;
        AutoPlayNext = other.AutoPlayNext;
        AspectRatio = other.AspectRatio;
        SubtitleScale = other.SubtitleScale;
        SubtitleDelayMs = other.SubtitleDelayMs;
        ShowPlaylist = other.ShowPlaylist;
        FileAssociationRegistered = other.FileAssociationRegistered;
    }
}
