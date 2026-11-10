using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VideoPlayer.Common;
using VideoPlayer.Models;
using VideoPlayer.Services;

namespace VideoPlayer.ViewModels;

/// <summary>
/// 主窗口 ViewModel：播放控制、播放列表、轨道与画面调节。
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly PlaybackService _playback;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _probeTasks = new();

    private MediaItem? _currentItem;
    private bool _isPlaying;
    private bool _isBuffering;
    private bool _isSeeking;
    private double _position;
    private TimeSpan _currentTime;
    private TimeSpan _duration;
    private int _volume;
    private bool _isMuted;
    private double _rate;
    private string _statusText = "就绪";
    private string _mediaInfoText = string.Empty;
    private string _title = "VideoPlayer";
    private bool _isPlaylistVisible;
    private bool _isFullscreen;
    private bool _hasMedia;
    private bool _suppressTrackSelection;
    private TrackOption? _selectedSubtitle;
    private TrackOption? _selectedAudio;
    private string _aspectRatio;
    private double _brightness = 1.0;
    private double _contrast = 1.0;
    private double _saturation = 1.0;
    private double _gamma = 1.0;
    private double _hue;
    private double? _pendingSeekSeconds;
    private string? _pendingSubtitlePath;
    private DateTime _lastPositionPersist = DateTime.MinValue;
    private bool _isDisposing;
    private CancellationTokenSource? _playWatchCts;

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Settings;

        _playback = new PlaybackService();

        _volume = Math.Clamp(_settings.Volume, 0, 100);
        _isMuted = _settings.Muted;
        _rate = _settings.PlaybackRate is >= 0.25 and <= 4.0 ? _settings.PlaybackRate : 1.0;
        _isPlaylistVisible = _settings.ShowPlaylist;
        _aspectRatio = string.IsNullOrWhiteSpace(_settings.AspectRatio) ? "默认" : _settings.AspectRatio;

        var player = _playback.MediaPlayer;
        player.Volume = _volume;
        player.Mute = _isMuted;
        player.SetRate((float)_rate);

        player.Playing += OnPlaying;
        player.Paused += OnPaused;
        player.Stopped += OnStopped;
        player.EndReached += OnEndReached;
        player.EncounteredError += OnEncounteredError;
        player.Buffering += OnBuffering;
        player.ESAdded += OnEsAdded;
        player.LengthChanged += OnLengthChanged;
        player.SnapshotTaken += OnSnapshotTaken;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        OpenFilesCommand = new RelayCommand(OpenFiles);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        PlayPauseCommand = new RelayCommand(TogglePlayPause);
        StopCommand = new RelayCommand(Stop);
        NextCommand = new RelayCommand(PlayNext, () => CurrentIndex >= 0 && CurrentIndex < Playlist.Count - 1);
        PreviousCommand = new RelayCommand(PlayPrevious, () => CurrentIndex > 0 || HasMedia);
        PlayItemCommand = new RelayCommand(p => PlayItem(p as MediaItem), p => p is MediaItem);
        RemoveItemCommand = new RelayCommand(p => RemoveItem(p as MediaItem), p => p is MediaItem);
        ClearPlaylistCommand = new RelayCommand(ClearPlaylist, () => Playlist.Count > 0);
        TogglePlaylistCommand = new RelayCommand(() => IsPlaylistVisible = !IsPlaylistVisible);
        ToggleFullscreenCommand = new RelayCommand(() => ToggleFullscreenRequested?.Invoke(this, EventArgs.Empty));
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        SnapshotCommand = new RelayCommand(TakeSnapshot, () => HasMedia);
        OpenSubtitleCommand = new RelayCommand(OpenSubtitle);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        OpenFileLocationCommand = new RelayCommand(p => OpenFileLocation(p as MediaItem), p => p is MediaItem);
        ResetAdjustmentsCommand = new RelayCommand(ResetAdjustments);

        RestorePlaylist();
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ToggleFullscreenRequested;

    public event Action<string>? PlaybackFailed;

    // ---------------- 命令 ----------------

    public ICommand OpenFilesCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand PlayPauseCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand NextCommand { get; }

    public ICommand PreviousCommand { get; }

    public ICommand PlayItemCommand { get; }

    public ICommand RemoveItemCommand { get; }

    public ICommand ClearPlaylistCommand { get; }

    public ICommand TogglePlaylistCommand { get; }

    public ICommand ToggleFullscreenCommand { get; }

    public ICommand ToggleMuteCommand { get; }

    public ICommand SnapshotCommand { get; }

    public ICommand OpenSubtitleCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenFileLocationCommand { get; }

    public ICommand ResetAdjustmentsCommand { get; }

    // ---------------- 数据 ----------------

    public ObservableCollection<MediaItem> Playlist { get; } = new();

    public ObservableCollection<TrackOption> SubtitleTracks { get; } = new();

    public ObservableCollection<TrackOption> AudioTracks { get; } = new();

    public IReadOnlyList<double> Rates { get; } = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

    public IReadOnlyList<string> AspectRatios { get; } = new[] { "默认", "16:9", "4:3", "1:1", "5:4", "21:9", "2.35:1" };

    public MediaPlayer MediaPlayer => _playback.MediaPlayer;

    public string LibVlcVersion => _playback.LibVlc.Version;

    public MediaItem? CurrentItem => _currentItem;

    public int CurrentIndex => _currentItem is null ? -1 : Playlist.IndexOf(_currentItem);

    // ---------------- 属性 ----------------

    public bool HasMedia
    {
        get => _hasMedia;
        private set => SetProperty(ref _hasMedia, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public bool IsBuffering
    {
        get => _isBuffering;
        private set => SetProperty(ref _isBuffering, value);
    }

    public bool IsSeeking
    {
        get => _isSeeking;
        set => SetProperty(ref _isSeeking, value);
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set
        {
            if (SetProperty(ref _isPlaylistVisible, value))
            {
                _settings.ShowPlaylist = value;
            }
        }
    }

    public double Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }

    public TimeSpan CurrentTime
    {
        get => _currentTime;
        set
        {
            if (SetProperty(ref _currentTime, value))
            {
                OnPropertyChanged(nameof(CurrentTimeText));
            }
        }
    }

    public TimeSpan Duration
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

    public string CurrentTimeText => FormatTime(CurrentTime);

    public string DurationText => FormatTime(Duration);

    public int Volume
    {
        get => _volume;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _volume, v))
            {
                return;
            }

            _playback.SetVolume(v);
            _settings.Volume = v;

            if (v > 0 && _isMuted)
            {
                IsMuted = false;
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _playback.SetMute(value);
                _settings.Muted = value;
            }
        }
    }

    public double Rate
    {
        get => _rate;
        set
        {
            if (!SetProperty(ref _rate, value))
            {
                return;
            }

            _settings.PlaybackRate = value;
            if (HasMedia)
            {
                _playback.SetRate(value);
            }
        }
    }

    public string AspectRatio
    {
        get => _aspectRatio;
        set
        {
            if (SetProperty(ref _aspectRatio, value))
            {
                _settings.AspectRatio = value;
                _playback.SetAspectRatio(value);
            }
        }
    }

    public TrackOption? SelectedSubtitle
    {
        get => _selectedSubtitle;
        set
        {
            if (!SetProperty(ref _selectedSubtitle, value) || _suppressTrackSelection || value is null)
            {
                return;
            }

            if (HasMedia)
            {
                _playback.SetSubtitleTrack(value.Id);
                StatusText = value.Id < 0 ? "字幕已关闭" : $"字幕：{value.Name}";
            }
        }
    }

    public TrackOption? SelectedAudio
    {
        get => _selectedAudio;
        set
        {
            if (!SetProperty(ref _selectedAudio, value) || _suppressTrackSelection || value is null)
            {
                return;
            }

            if (HasMedia)
            {
                _playback.SetAudioTrack(value.Id);
                StatusText = $"音轨：{value.Name}";
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value))
            {
                ApplyAdjustment(VideoAdjustOption.Brightness, (float)value);
            }
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            if (SetProperty(ref _contrast, value))
            {
                ApplyAdjustment(VideoAdjustOption.Contrast, (float)value);
            }
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (SetProperty(ref _saturation, value))
            {
                ApplyAdjustment(VideoAdjustOption.Saturation, (float)value);
            }
        }
    }

    public double Gamma
    {
        get => _gamma;
        set
        {
            if (SetProperty(ref _gamma, value))
            {
                ApplyAdjustment(VideoAdjustOption.Gamma, (float)value);
            }
        }
    }

    public double Hue
    {
        get => _hue;
        set
        {
            if (SetProperty(ref _hue, value))
            {
                ApplyAdjustment(VideoAdjustOption.Hue, (float)value);
            }
        }
    }

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string WindowTitle => HasMedia ? $"{Title} — VideoPlayer" : "VideoPlayer";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>供窗口层显示一次性状态提示。</summary>
    public void ShowStatus(string text) => StatusText = text;

    /// <summary>是否已进入资源释放流程，供窗口层判断能否继续接收文件。</summary>
    public bool IsDisposing => _isDisposing;

    public string MediaInfoText
    {
        get => _mediaInfoText;
        private set => SetProperty(ref _mediaInfoText, value);
    }

    // ---------------- 对外方法 ----------------

    public void OpenPaths(IEnumerable<string> paths)
    {
        if (_isDisposing)
        {
            AppLog.Warn("OpenPaths 被忽略：播放器正在释放资源");
            return;
        }

        var requested = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var files = new List<string>();
        foreach (var path in requested)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    files.AddRange(MediaFormats.EnumerateMediaFiles(path));
                }
                else if (File.Exists(path) && MediaFormats.IsSupported(path))
                {
                    files.Add(path);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"打开路径时不可访问，已跳过：{path} | {ex.Message}");
            }
        }

        if (files.Count == 0)
        {
            AppLog.Warn($"没有找到受支持的媒体文件，请求 {requested.Length} 个路径："
                        + $"[{string.Join(" | ", requested.Take(5))}]");
            StatusText = "没有找到受支持的媒体文件";
            return;
        }

        AppLog.Info($"准备打开 {files.Count} 个媒体文件");
        AddFiles(files, playFirst: true);
    }

    public void AddFiles(IEnumerable<string> paths, bool playFirst = true)
    {
        if (_isDisposing)
        {
            AppLog.Warn("AddFiles 被忽略：播放器正在释放资源");
            return;
        }

        var added = new List<MediaItem>();
        MediaItem? firstRequestedItem = null;

        foreach (var path in paths)
        {
            if (!MediaFormats.IsSupported(path))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            var existing = Playlist.FirstOrDefault(
                i => string.Equals(i.FilePath, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                firstRequestedItem ??= existing;
                continue;
            }

            var item = new MediaItem(full);
            Playlist.Add(item);
            added.Add(item);
            firstRequestedItem ??= item;
            QueueProbe(item, defer: true);
        }

        if (firstRequestedItem is null)
        {
            return;
        }

        if (added.Count == 0)
        {
            if (playFirst)
            {
                PlayItem(firstRequestedItem);
            }

            return;
        }

        RenumberPlaylist();
        SavePlaylist();
        RaiseCommandStates();

        if (playFirst || _currentItem is null)
        {
            PlayItem(firstRequestedItem);
        }
        else
        {
            StatusText = $"已添加 {added.Count} 个文件";
        }
    }

    public void TogglePlayPause()
    {
        if (!HasMedia)
        {
            var item = _currentItem ?? Playlist.FirstOrDefault();
            if (item is not null)
            {
                PlayItem(item);
            }

            return;
        }

        var state = _playback.MediaPlayer.State;
        if (state is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            if (_currentItem is not null)
            {
                PlayItem(_currentItem, resume: false);
            }

            return;
        }

        if (_playback.IsPlaying)
        {
            _playback.Pause();
        }
        else
        {
            _playback.Resume();
        }
    }

    public void Stop()
    {
        // 手动停止不能让起播监视在 12 秒后把文件重新播起来
        CancelPlaybackWatch();
        UpdatePositionMemory(persist: true);
        _playback.Stop();
        IsPlaying = false;
        IsBuffering = false;
        Position = 0;
        CurrentTime = TimeSpan.Zero;
        StatusText = "已停止";
    }

    public void SeekToPosition(double position)
    {
        if (!HasMedia)
        {
            return;
        }

        var clamped = Math.Clamp(position, 0, 1);
        Position = clamped;
        _playback.Position = clamped;
        if (Duration > TimeSpan.Zero)
        {
            CurrentTime = Duration * clamped;
        }
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (!HasMedia)
        {
            return;
        }

        var target = CurrentTime + delta;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        if (Duration > TimeSpan.Zero && target > Duration)
        {
            target = Duration;
        }

        CurrentTime = target;
        _playback.SeekTo(target);
    }

    public void AdjustVolume(int delta) => Volume += delta;

    public void TakeSnapshot()
    {
        if (!HasMedia)
        {
            StatusText = "当前没有可截图的画面";
            return;
        }

        try
        {
            Directory.CreateDirectory(SettingsService.SnapshotDirectory);
            var path = Path.Combine(
                SettingsService.SnapshotDirectory,
                $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

            _playback.TakeSnapshot(path);
            StatusText = $"截图已保存：{path}";
        }
        catch (Exception ex)
        {
            StatusText = "截图失败：" + ex.Message;
        }
    }

    /// <summary>设置窗口关闭时调用，落盘所有状态。</summary>
    public void SaveState()
    {
        UpdatePositionMemory(persist: true);
        SavePlaylist();
        _settingsService.Save();
    }

    /// <summary>设置窗口确认后调用。</summary>
    public void ApplySettings()
    {
        // 音量、播放列表面板与字幕延迟立即生效
        if (Volume != _settings.Volume)
        {
            Volume = _settings.Volume;
        }

        IsPlaylistVisible = _settings.ShowPlaylist;

        if (HasMedia)
        {
            _playback.SetAspectRatio(AspectRatio);
            _playback.SetSubtitleDelay(_settings.SubtitleDelayMs);
        }

        _settingsService.Save();
        StatusText = "设置已保存";
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposing)
        {
            return;
        }

        _isDisposing = true;
        CancelPlaybackWatch();
        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        SaveState();

        var player = _playback.MediaPlayer;
        player.Playing -= OnPlaying;
        player.Paused -= OnPaused;
        player.Stopped -= OnStopped;
        player.EndReached -= OnEndReached;
        player.EncounteredError -= OnEncounteredError;
        player.Buffering -= OnBuffering;
        player.ESAdded -= OnEsAdded;
        player.LengthChanged -= OnLengthChanged;
        player.SnapshotTaken -= OnSnapshotTaken;

        try
        {
            await Task.Run(_cts.Cancel);
            await Task.WhenAll(_probeTasks);
        }
        finally
        {
            _cts.Dispose();
            _probeGate.Dispose();
            await Task.Run(_playback.Dispose);
        }
    }

    // ---------------- 播放流程 ----------------

    private void PlayItem(MediaItem? item, bool resume = true)
    {
        if (item is null)
        {
            return;
        }

        if (!File.Exists(item.FilePath))
        {
            item.IsMissing = true;
            StatusText = $"文件不存在：{item.FileName}";
            AppLog.Warn($"播放失败，文件不存在：{item.FilePath}");
            return;
        }

        // 切歌 / 重新打开时先撤销上一次的起播监视，避免误触发重试
        CancelPlaybackWatch();

        UpdatePositionMemory(persist: true);
        SetCurrentItem(item);

        var startSeconds = 0d;
        if (resume && _settings.RememberPosition
            && _settings.Positions.TryGetValue(item.FilePath, out var saved)
            && saved > 5)
        {
            startSeconds = saved;
        }

        _pendingSeekSeconds = startSeconds > 5 ? startSeconds : null;
        _pendingSubtitlePath = null;

        Title = item.Title;
        HasMedia = true;
        Duration = item.Duration ?? TimeSpan.Zero;
        Position = 0;
        CurrentTime = TimeSpan.Zero;
        IsBuffering = true;
        StatusText = "正在打开…";

        ResetTracks();
        StartPlayback(item, _settings.HardwareAcceleration, watchStartup: true);
    }

    /// <summary>发起一次播放。首播会监视起播状态，失败可关闭硬解重试一次。</summary>
    private void StartPlayback(MediaItem item, bool hardware, bool watchStartup)
    {
        LogOpen(item, hardware);

        bool started;
        try
        {
            started = _playback.Play(item.FilePath, new PlaybackOptions(
                hardware,
                _settings.SubtitleScale,
                _settings.SubtitleDelayMs));
        }
        catch (Exception ex)
        {
            started = false;
            _playback.LogInfo($"INFO [VideoPlayer] PLAY_EXCEPTION {ex.Message}");
            AppLog.Error($"LibVLC 启动媒体时抛出异常：{item.FilePath}", ex);
        }

        if (!started)
        {
            if (watchStartup)
            {
                RetryPlayback(item, "LibVLC 未能启动该媒体");
            }
            else
            {
                ReportPlaybackFailure("LibVLC 未能启动该媒体。");
            }

            return;
        }

        _playback.SetVolume(_volume);
        _playback.SetMute(_isMuted);
        _playback.SetRate(_rate);
        _playback.SetAspectRatio(_aspectRatio);
        RaiseCommandStates();

        if (watchStartup)
        {
            WatchPlaybackStart(item);
        }
    }

    /// <summary>大文件硬解初始化偶发卡住：12 秒仍未进入播放则自动用软解重开一次。</summary>
    private void WatchPlaybackStart(MediaItem item)
    {
        var cts = new CancellationTokenSource();
        _playWatchCts = cts;
        _ = WatchPlaybackStartAsync(item, cts);
    }

    private async Task WatchPlaybackStartAsync(MediaItem item, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatch(() =>
        {
            if (_isDisposing || !ReferenceEquals(_playWatchCts, cts) || !ReferenceEquals(item, _currentItem))
            {
                return;
            }

            VLCState state;
            try
            {
                state = _playback.MediaPlayer.State;
            }
            catch
            {
                return;
            }

            if (state is VLCState.Playing or VLCState.Paused or VLCState.Ended)
            {
                CancelPlaybackWatch();
                return;
            }

            RetryPlayback(item, $"12 秒未进入播放（state={state}）");
        });
    }

    private void RetryPlayback(MediaItem item, string reason)
    {
        if (_isDisposing || !ReferenceEquals(item, _currentItem))
        {
            return;
        }

        CancelPlaybackWatch();

        // 第二次尝试关闭硬件解码：4K HEVC 的 D3D11/DXVA2 初始化失败是最常见的起播卡死原因
        _playback.LogInfo($"INFO [VideoPlayer] RETRY {item.FileName} (software decoding, {reason})");
        StatusText = "正在重新打开（兼容模式）…";

        try
        {
            _playback.Stop();
        }
        catch
        {
            // ignore
        }

        StartPlayback(item, hardware: false, watchStartup: false);
    }

    private void CancelPlaybackWatch()
    {
        var cts = _playWatchCts;
        _playWatchCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignore
        }

        cts.Dispose();
    }

    /// <summary>记录每次打开请求，便于定位「双击没反应」时到底走到了哪一步。</summary>
    private void LogOpen(MediaItem item, bool hardware)
    {
        string size;
        try
        {
            size = $"{new FileInfo(item.FilePath).Length / 1048576d:0.0} MB";
        }
        catch
        {
            size = "未知大小";
        }

        _playback.LogInfo($"INFO [VideoPlayer] OPEN {item.FileName} ({size}) hw={hardware}");
    }

    private void SetCurrentItem(MediaItem? item)
    {
        if (_currentItem is not null)
        {
            _currentItem.IsCurrent = false;
        }

        _currentItem = item;

        if (_currentItem is not null)
        {
            _currentItem.IsCurrent = true;
        }

        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(CurrentIndex));
        RaiseCommandStates();
    }

    private void PlayNext()
    {
        var index = CurrentIndex;
        if (index >= 0 && index < Playlist.Count - 1)
        {
            PlayItem(Playlist[index + 1]);
        }
    }

    private void PlayPrevious()
    {
        if (HasMedia && CurrentTime.TotalSeconds > 3)
        {
            _playback.SeekTo(TimeSpan.Zero);
            CurrentTime = TimeSpan.Zero;
            Position = 0;
            return;
        }

        var index = CurrentIndex;
        if (index > 0)
        {
            PlayItem(Playlist[index - 1]);
        }
    }

    private void RemoveItem(MediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        var index = Playlist.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var wasCurrent = ReferenceEquals(item, _currentItem);
        Playlist.RemoveAt(index);
        RenumberPlaylist();

        if (wasCurrent)
        {
            CancelPlaybackWatch();
            _playback.Stop();
            HasMedia = false;
            IsPlaying = false;
            IsBuffering = false;
            Position = 0;
            CurrentTime = TimeSpan.Zero;
            Duration = TimeSpan.Zero;

            var next = Playlist.Count > 0 ? Playlist[Math.Min(index, Playlist.Count - 1)] : null;
            SetCurrentItem(next);
            Title = next?.Title ?? "VideoPlayer";
            StatusText = "就绪";
        }

        SavePlaylist();
        RaiseCommandStates();
    }

    private void ClearPlaylist()
    {
        CancelPlaybackWatch();
        _playback.Stop();
        Playlist.Clear();
        SetCurrentItem(null);
        HasMedia = false;
        IsPlaying = false;
        IsBuffering = false;
        Position = 0;
        CurrentTime = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        Title = "VideoPlayer";
        SubtitleTracks.Clear();
        AudioTracks.Clear();
        SelectedSubtitle = null;
        SelectedAudio = null;
        StatusText = "播放列表已清空";
        SavePlaylist();
        RaiseCommandStates();
    }

    private void RenumberPlaylist()
    {
        for (var i = 0; i < Playlist.Count; i++)
        {
            Playlist[i].Index = i + 1;
        }
    }

    private void RaiseCommandStates()
    {
        (NextCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviousCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearPlaylistCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SnapshotCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RestorePlaylist()
    {
        foreach (var path in _settings.Playlist)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var item = new MediaItem(path);
            Playlist.Add(item);
            QueueProbe(item, defer: true);
        }

        RenumberPlaylist();
        RaiseCommandStates();

        if (_settings.CurrentIndex >= 0 && _settings.CurrentIndex < Playlist.Count)
        {
            SetCurrentItem(Playlist[_settings.CurrentIndex]);
            Title = _currentItem?.Title ?? "VideoPlayer";
        }
    }

    private async Task ProbeAsync(MediaItem item, bool defer)
    {
        // 4K 大文件（>= 256 MB）跳过主动 ParseLocal：它会读取索引与样本数据，
        // 与播放争抢磁盘 IO 和解码线程池。时长由播放时的 LengthChanged 补齐。
        if (IsLargeFile(item.FilePath))
        {
            return;
        }

        try
        {
            // 双击冷启动时会同时发生两件事：恢复历史播放列表并逐个 ParseLocal 解析，
            // 以及立即播放命令行传入的文件。两者共用同一个 LibVLC 实例的 preparser /
            // 解码线程池与磁盘 IO，4K HEVC 等大文件解析耗时最长，会把刚发起的播放挤到
            // 超时，表现为「大文件双击打不开；先开播放器再打开就正常」。
            // 因此元数据探测一律延后，并在打开/播放期间让路。
            if (defer)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), _cts.Token);
            }

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (IsPlaybackBusy() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, _cts.Token);
            }

            if (_cts.IsCancellationRequested)
            {
                return;
            }

            // 当前项的时长由 VLC 的 LengthChanged 事件提供，避免对同一文件二次解析
            if (ReferenceEquals(item, _currentItem) && HasMedia)
            {
                return;
            }

            await _probeGate.WaitAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var (title, duration) = await MediaProbe.ProbeAsync(_playback.LibVlc, item.FilePath, _cts.Token);
            if (!string.IsNullOrWhiteSpace(title))
            {
                item.Title = title!;
                if (ReferenceEquals(item, _currentItem))
                {
                    Title = item.Title;
                }
            }

            if (duration is not null)
            {
                item.Duration = duration;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private const long LargeFileProbeThresholdBytes = 256L * 1024 * 1024;

    private static bool IsLargeFile(string path)
    {
        try
        {
            return new FileInfo(path).Length >= LargeFileProbeThresholdBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>播放/打开期间不与 LibVLC 争抢解析资源。</summary>
    private bool IsPlaybackBusy()
    {
        try
        {
            var state = _playback.MediaPlayer.State;
            return state is VLCState.Opening or VLCState.Buffering or VLCState.Playing or VLCState.Paused;
        }
        catch
        {
            return false;
        }
    }

    private void QueueProbe(MediaItem item, bool defer = false)
    {
        _probeTasks.RemoveAll(task => task.IsCompleted);
        _probeTasks.Add(ProbeAsync(item, defer));
    }

    private void OpenFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开媒体文件",
            Filter = MediaFormats.OpenFileFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            OpenPaths(dialog.FileNames);
        }
    }

    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "打开文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            OpenPaths(new[] { dialog.FolderName });
        }
    }

    private async void OpenSubtitle()
    {
        var dialog = new OpenFileDialog
        {
            Title = "加载字幕",
            Filter = "字幕文件|*.srt;*.ass;*.ssa;*.sub;*.vtt;*.smi;*.txt|所有文件|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;

        if (!HasMedia)
        {
            _pendingSubtitlePath = path;
            StatusText = "将在开始播放时加载该字幕";
            return;
        }

        if (_playback.AddSubtitleFile(path))
        {
            StatusText = "已加载字幕：" + Path.GetFileName(path);
            await Task.Delay(400);
            RefreshTracks();
        }
        else
        {
            StatusText = "字幕加载失败";
        }
    }

    private void OpenFileLocation(MediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    // ---------------- 引擎事件 ----------------

    private void OnPlaying(object? sender, EventArgs e)
    {
        Dispatch(() =>
        {
            _playback.LogInfo($"INFO [VideoPlayer] PLAYING {_currentItem?.FileName ?? "?"}");
            CancelPlaybackWatch();
            IsPlaying = true;
            IsBuffering = false;
            StatusText = "正在播放";
            RefreshTracks();
            RefreshMediaInfo();
            ApplyAdjustments();
            _ = SchedulePostPlayRefreshAsync();
        });
    }

    private async Task SchedulePostPlayRefreshAsync()
    {
        try
        {
            await Task.Delay(700, _cts.Token);
            Dispatch(() =>
            {
                RefreshTracks();
                RefreshMediaInfo();
                ApplyAdjustments();
            });
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        Dispatch(() =>
        {
            IsPlaying = false;
            IsBuffering = false;
            StatusText = "已暂停";
            UpdatePositionMemory(persist: true);
        });
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        Dispatch(() =>
        {
            if (_playback.MediaPlayer.State != VLCState.Stopped)
            {
                return;
            }

            IsPlaying = false;
            IsBuffering = false;
            Position = 0;
            CurrentTime = TimeSpan.Zero;
        });
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatch(() =>
        {
            IsPlaying = false;
            IsBuffering = false;
            UpdatePositionMemory(persist: true);

            if (_settings.AutoPlayNext)
            {
                var index = CurrentIndex;
                if (index >= 0 && index < Playlist.Count - 1)
                {
                    PlayItem(Playlist[index + 1]);
                    return;
                }
            }

            Position = 1;
            StatusText = "播放结束";
        });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        Dispatch(() =>
        {
            IsPlaying = false;
            IsBuffering = false;
            var detail = _playback.LastErrorMessage;

            // 首播阶段出错先静默用软解重试一次，不直接弹错误框打扰用户
            if (_playWatchCts is not null && _currentItem is not null)
            {
                RetryPlayback(_currentItem,
                    string.IsNullOrWhiteSpace(detail) ? "EncounteredError" : detail);
                return;
            }

            ReportPlaybackFailure(string.IsNullOrWhiteSpace(detail)
                ? "LibVLC 播放过程中遇到错误。"
                : detail);
        });
    }

    private void ReportPlaybackFailure(string detail)
    {
        CancelPlaybackWatch();
        var fileName = _currentItem?.FileName ?? "未知文件";
        var message = $"无法播放“{fileName}”：{detail}";
        _playback.LogPlaybackFailure(message);
        AppLog.Error(message);
        StatusText = $"{message}（日志：{AppLog.ErrorLogPath}）";
        PlaybackFailed?.Invoke(
            $"无法播放文件：{fileName}\n\n原因：{detail}\n\n"
            + $"错误日志：{AppLog.ErrorLogPath}\n播放引擎日志：{_playback.LogFilePath}");
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        Dispatch(() =>
        {
            var cache = e.Cache;
            if (cache >= 100)
            {
                IsBuffering = false;
            }
            else
            {
                IsBuffering = true;
                StatusText = $"缓冲中 {cache:0}%";
            }
        });
    }

    private void OnEsAdded(object? sender, MediaPlayerESAddedEventArgs e)
    {
        Dispatch(RefreshTracks);
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Dispatch(() =>
        {
            if (e.Length > 0)
            {
                Duration = TimeSpan.FromMilliseconds(e.Length);
                if (_currentItem is not null)
                {
                    _currentItem.Duration = Duration;
                }
            }
        });
    }

    private void OnSnapshotTaken(object? sender, MediaPlayerSnapshotTakenEventArgs e)
    {
        Dispatch(() => StatusText = $"截图已保存：{e.Filename}");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!HasMedia)
        {
            return;
        }

        var player = _playback.MediaPlayer;

        if (_pendingSeekSeconds is { } seek && player.Length > 0)
        {
            _pendingSeekSeconds = null;
            player.Time = (long)(seek * 1000);
            StatusText = $"已从 {FormatTime(TimeSpan.FromSeconds(seek))} 继续播放";
        }

        if (_pendingSubtitlePath is { } subtitle && player.Length > 0)
        {
            _pendingSubtitlePath = null;
            if (_playback.AddSubtitleFile(subtitle))
            {
                StatusText = "已加载字幕：" + Path.GetFileName(subtitle);
            }
        }

        if (!_isSeeking)
        {
            var position = player.Position;
            if (position >= 0)
            {
                Position = position;
            }

            var time = player.Time;
            if (time >= 0)
            {
                CurrentTime = TimeSpan.FromMilliseconds(time);
            }
        }

        var length = player.Length;
        if (length > 0)
        {
            Duration = TimeSpan.FromMilliseconds(length);
            if (_currentItem is not null)
            {
                _currentItem.Duration = Duration;
            }
        }

        var state = player.State;
        if (state == VLCState.Playing)
        {
            IsPlaying = true;
            IsBuffering = false;
        }
        else if (state is VLCState.Paused or VLCState.Stopped or VLCState.Ended or VLCState.Error)
        {
            IsPlaying = false;
        }

        if ((DateTime.UtcNow - _lastPositionPersist).TotalSeconds > 15)
        {
            _lastPositionPersist = DateTime.UtcNow;
            UpdatePositionMemory(persist: false);
        }
    }

    private void RefreshTracks()
    {
        if (!HasMedia)
        {
            return;
        }

        _suppressTrackSelection = true;
        try
        {
            var subtitles = _playback.GetSubtitleTracks();
            SubtitleTracks.Clear();
            foreach (var track in subtitles)
            {
                SubtitleTracks.Add(track);
            }

            var currentSpu = _playback.MediaPlayer.Spu;
            SelectedSubtitle = SubtitleTracks.FirstOrDefault(t => t.Id == currentSpu)
                               ?? SubtitleTracks.FirstOrDefault(t => t.Id < 0);

            var audios = _playback.GetAudioTracks();
            AudioTracks.Clear();
            foreach (var track in audios)
            {
                AudioTracks.Add(track);
            }

            var currentAudio = _playback.MediaPlayer.AudioTrack;
            SelectedAudio = AudioTracks.FirstOrDefault(t => t.Id == currentAudio)
                            ?? AudioTracks.FirstOrDefault();
        }
        finally
        {
            _suppressTrackSelection = false;
        }
    }

    private void ResetTracks()
    {
        _suppressTrackSelection = true;
        try
        {
            SubtitleTracks.Clear();
            AudioTracks.Clear();
            SelectedSubtitle = null;
            SelectedAudio = null;
        }
        finally
        {
            _suppressTrackSelection = false;
        }
    }

    private void RefreshMediaInfo()
    {
        var info = _playback.GetMediaInfoText();
        if (!string.IsNullOrEmpty(info))
        {
            MediaInfoText = info;
        }
    }

    private void ApplyAdjustments()
    {
        if (!HasMedia)
        {
            return;
        }

        _playback.EnableAdjustments();
        _playback.SetAdjustment(VideoAdjustOption.Brightness, (float)Brightness);
        _playback.SetAdjustment(VideoAdjustOption.Contrast, (float)Contrast);
        _playback.SetAdjustment(VideoAdjustOption.Saturation, (float)Saturation);
        _playback.SetAdjustment(VideoAdjustOption.Gamma, (float)Gamma);
        _playback.SetAdjustment(VideoAdjustOption.Hue, (float)Hue);
    }

    private void ApplyAdjustment(VideoAdjustOption option, float value)
    {
        if (!HasMedia)
        {
            return;
        }

        _playback.EnableAdjustments();
        _playback.SetAdjustment(option, value);
    }

    private void ResetAdjustments()
    {
        Brightness = 1.0;
        Contrast = 1.0;
        Saturation = 1.0;
        Gamma = 1.0;
        Hue = 0;
        StatusText = "画面调节已重置";
    }

    // ---------------- 辅助 ----------------

    private void UpdatePositionMemory(bool persist)
    {
        if (_settings.RememberPosition && _currentItem is not null && Duration > TimeSpan.Zero)
        {
            var seconds = CurrentTime.TotalSeconds;
            if (seconds < 5)
            {
                // 开头不记录
            }
            else if (seconds > Duration.TotalSeconds - 10)
            {
                _settings.Positions.Remove(_currentItem.FilePath);
            }
            else
            {
                _settings.Positions[_currentItem.FilePath] = seconds;
            }
        }

        if (persist)
        {
            SavePlaylist();
            _settingsService.Save();
        }
    }

    private void SavePlaylist()
    {
        _settings.Playlist = Playlist.Select(i => i.FilePath).ToList();
        _settings.CurrentIndex = CurrentIndex;
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private void Dispatch(Action action)
    {
        if (_isDisposing)
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            action();
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(() =>
            {
                if (!_isDisposing)
                {
                    action();
                }
            });
        }
    }
}
