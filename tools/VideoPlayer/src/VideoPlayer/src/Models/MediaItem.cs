using System.IO;
using VideoPlayer.Common;

namespace VideoPlayer.Models;

/// <summary>
/// 播放列表中的一项媒体。
/// </summary>
public sealed class MediaItem : ObservableObject
{
    private string _title;
    private TimeSpan? _duration;
    private bool _isCurrent;
    private int _index;
    private bool _isMissing;

    public MediaItem(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        _title = Path.GetFileNameWithoutExtension(filePath);
        _isMissing = !File.Exists(FilePath);

        if (MediaFormats.TryGetInfo(filePath, out var info))
        {
            Kind = info.Kind;
            Container = info.Container;
        }
        else
        {
            Kind = MediaKind.Unknown;
            Container = "未知";
        }
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public MediaKind Kind { get; }

    public string Container { get; }

    /// <summary>显示标题，默认取文件名，探测到元数据后会更新。</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string DisplayTitle => Title;

    /// <summary>媒体时长，未探测时为 null。</summary>
    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public string DurationText => Duration is { } d && d > TimeSpan.Zero
        ? (d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss"))
        : "--:--";

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public int Index
    {
        get => _index;
        set
        {
            if (SetProperty(ref _index, value))
            {
                OnPropertyChanged(nameof(IndexText));
            }
        }
    }

    public string IndexText => _index > 0 ? _index.ToString("00") : string.Empty;

    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (SetProperty(ref _isMissing, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsMissing ? "文件不存在" : Container;

    public string ToolTip => FilePath;
}
