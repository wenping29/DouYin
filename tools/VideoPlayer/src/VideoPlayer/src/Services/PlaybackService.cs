using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LibVLCSharp.Shared;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

/// <summary>
/// 单次播放所需的选项。
/// </summary>
public readonly record struct PlaybackOptions(
    bool HardwareAcceleration,
    double SubtitleScale,
    int SubtitleDelayMs);

/// <summary>
/// LibVLC 播放引擎封装。所有 UI 线程调用都应通过此类完成。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private static readonly object LogLock = new();
    private readonly LibVLC _libVlc;
    private string? _lastErrorMessage;

    public PlaybackService()
    {
        // 定位随 NuGet 包复制的 libvlc/win-x64 原生库
        Core.Initialize();

        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--no-snapshot-preview",
            "--no-osd",
            "--file-caching=2000",
            "--network-caching=2000");
        _libVlc.Log += OnLibVlcLog;

        MediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableKeyInput = false,
            EnableMouseInput = false
        };
        AppLog.Info("LibVLC 播放引擎初始化完成");
    }

    public MediaPlayer MediaPlayer { get; }

    public LibVLC LibVlc => _libVlc;

    public bool HasMedia => MediaPlayer.Media is not null;

    public bool IsPlaying => MediaPlayer.IsPlaying;

    public string LogFilePath { get; } = Path.Combine(SettingsService.DataDirectory, "libvlc.log");

    public string? LastErrorMessage => Volatile.Read(ref _lastErrorMessage);

    public long LengthMs => MediaPlayer.Length;

    public long TimeMs => MediaPlayer.Time;

    public double Position
    {
        get => MediaPlayer.Position;
        set => MediaPlayer.Position = (float)Math.Clamp(value, 0d, 1d);
    }

    /// <summary>开始播放指定文件。</summary>
    public bool Play(string path, PlaybackOptions options)
    {
        Volatile.Write(ref _lastErrorMessage, null);
        using var media = new Media(_libVlc, path, FromType.FromPath);

        // iPhone 4K/60fps HEVC 的码率可达 50~80 Mbps，1.5 秒缓存很容易 underrun，
        // 表现为起播掉帧、buffer too late。按文件体积放大本地缓存（2s ~ 5s）。
        media.AddOption(new MediaConfiguration
        {
            EnableHardwareDecoding = options.HardwareAcceleration,
            FileCaching = GetFileCachingMs(path),
            NetworkCaching = 2000
        });

        if (options.SubtitleScale > 0 && Math.Abs(options.SubtitleScale - 1.0) > 0.001)
        {
            media.AddOption($":sub-text-scale={options.SubtitleScale.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        if (options.SubtitleDelayMs != 0)
        {
            media.AddOption($":sub-delay={options.SubtitleDelayMs}");
        }

        return MediaPlayer.Play(media);
    }

    /// <summary>按文件体积计算本地缓存时长（毫秒），大文件给更深的缓冲。</summary>
    private static uint GetFileCachingMs(string path)
    {
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch
        {
            return 2000u;
        }

        // 每 100 MB 追加 400 ms，上限 5 秒；小文件仍维持 2 秒，起播不会明显变慢
        var extra = (uint)Math.Min(3000L, size / 104857600L * 400L);
        return 2000u + extra;
    }

    public void LogPlaybackFailure(string message) => LogInfo($"ERROR [VideoPlayer] {message}");

    /// <summary>写入诊断日志（追加到 libvlc.log），不干扰播放流程。</summary>
    public void LogInfo(string message) => WriteLog(message);

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        if (e.Level < LogLevel.Warning)
        {
            return;
        }

        var line = $"{e.Level} [{e.Module}] {e.Message}";
        WriteLog(line);
        if (e.Level == LogLevel.Error)
        {
            Volatile.Write(ref _lastErrorMessage, e.Message);
            AppLog.Error($"[LibVLC/{e.Module}] {e.Message}");
        }
    }

    private void WriteLog(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(SettingsService.DataDirectory);
                File.AppendAllText(
                    LogFilePath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            Trace.WriteLine($"Unable to write LibVLC log to '{LogFilePath}': {message}");
        }
        catch (UnauthorizedAccessException)
        {
            Trace.WriteLine($"Unable to write LibVLC log to '{LogFilePath}': {message}");
        }
    }

    public void Pause() => MediaPlayer.SetPause(true);

    public void Resume()
    {
        if (HasMedia)
        {
            MediaPlayer.Play();
        }
    }

    public void Stop() => MediaPlayer.Stop();

    public void SeekTo(TimeSpan time)
    {
        if (!HasMedia)
        {
            return;
        }

        MediaPlayer.Time = (long)Math.Clamp(time.TotalMilliseconds, 0, Math.Max(0, LengthMs));
    }

    public void SetVolume(int volume) => MediaPlayer.Volume = Math.Clamp(volume, 0, 100);

    public void SetMute(bool muted) => MediaPlayer.Mute = muted;

    public void SetRate(double rate) => MediaPlayer.SetRate((float)Math.Clamp(rate, 0.25, 4.0));

    public void SetSubtitleTrack(int id) => MediaPlayer.SetSpu(id);

    public void SetAudioTrack(int id) => MediaPlayer.SetAudioTrack(id);

    public void SetSubtitleDelay(int milliseconds) => MediaPlayer.SetSpuDelay(milliseconds * 1000L);

    public void SetAspectRatio(string? aspectRatio)
        => MediaPlayer.AspectRatio = string.IsNullOrWhiteSpace(aspectRatio) || aspectRatio == "默认" ? null : aspectRatio;

    public void EnableAdjustments() => MediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);

    public void SetAdjustment(VideoAdjustOption option, float value)
        => MediaPlayer.SetAdjustFloat(option, value);

    /// <summary>加载外部字幕文件。</summary>
    public bool AddSubtitleFile(string path)
    {
        if (!HasMedia || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return MediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>截图到指定路径，宽高为 0 表示使用原始尺寸。</summary>
    public void TakeSnapshot(string path) => MediaPlayer.TakeSnapshot(0, path, 0, 0);

    public IReadOnlyList<TrackOption> GetSubtitleTracks()
    {
        var result = new List<TrackOption> { new(-1, "禁用") };
        var descriptions = MediaPlayer.SpuDescription;
        if (descriptions is not null)
        {
            foreach (var d in descriptions)
            {
                if (d.Id >= 0 && !result.Any(t => t.Id == d.Id))
                {
                    result.Add(new TrackOption(d.Id, string.IsNullOrWhiteSpace(d.Name) ? $"字幕 {d.Id}" : d.Name));
                }
            }
        }

        return result;
    }

    public IReadOnlyList<TrackOption> GetAudioTracks()
    {
        var result = new List<TrackOption>();
        var descriptions = MediaPlayer.AudioTrackDescription;
        if (descriptions is not null)
        {
            foreach (var d in descriptions)
            {
                if (d.Id >= 0 && !result.Any(t => t.Id == d.Id))
                {
                    result.Add(new TrackOption(d.Id, string.IsNullOrWhiteSpace(d.Name) ? $"音轨 {d.Id}" : d.Name));
                }
            }
        }

        if (result.Count == 0)
        {
            // 无音轨时给一个占位项，避免下拉框空白
            result.Add(new TrackOption(-1, "无音轨"));
        }
        else
        {
            result.Insert(0, new TrackOption(-1, "禁用"));
        }

        return result;
    }

    /// <summary>当前媒体信息：分辨率、帧率、编码、声道等。</summary>
    public string GetMediaInfoText()
    {
        var media = MediaPlayer.Media;
        if (media is null)
        {
            return string.Empty;
        }

        try
        {
            var tracks = media.Tracks;
            var parts = new List<string>();

            var video = default(MediaTrack);
            var hasVideo = false;
            var audio = default(MediaTrack);
            var hasAudio = false;

            foreach (var track in tracks)
            {
                if (!hasVideo && track.TrackType == TrackType.Video)
                {
                    video = track;
                    hasVideo = true;
                }
                else if (!hasAudio && track.TrackType == TrackType.Audio)
                {
                    audio = track;
                    hasAudio = true;
                }
            }

            if (hasVideo)
            {
                var v = video.Data.Video;
                if (v.Width > 0 && v.Height > 0)
                {
                    parts.Add($"{v.Width}×{v.Height}");
                }

                if (v.FrameRateDen > 0)
                {
                    var fps = (double)v.FrameRateNum / v.FrameRateDen;
                    if (fps > 0)
                    {
                        parts.Add($"{fps:0.##} fps");
                    }
                }

                var vcodec = FourCcToName(video.Codec);
                if (!string.IsNullOrEmpty(vcodec))
                {
                    parts.Add(vcodec);
                }
            }

            if (hasAudio)
            {
                var acodec = FourCcToName(audio.Codec);
                if (!string.IsNullOrEmpty(acodec))
                {
                    parts.Add(acodec);
                }

                var a = audio.Data.Audio;
                if (a.Channels > 0)
                {
                    parts.Add(a.Channels == 1 ? "单声道" : a.Channels == 2 ? "立体声" : $"{a.Channels} 声道");
                }
            }

            return string.Join("  ·  ", parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FourCcToName(uint codec)
    {
        if (codec == 0)
        {
            return string.Empty;
        }

        var chars = new[]
        {
            (char)(codec & 0xFF),
            (char)((codec >> 8) & 0xFF),
            (char)((codec >> 16) & 0xFF),
            (char)((codec >> 24) & 0xFF)
        };

        var raw = new string(chars).Trim('\0', ' ').Trim();
        if (raw.Length < 3 || !raw.All(c => char.IsLetterOrDigit(c)))
        {
            return string.Empty;
        }

        return raw.ToLowerInvariant() switch
        {
            "h264" or "avc1" => "H.264",
            "h265" or "hevc" or "hvc1" => "H.265",
            "av01" => "AV1",
            "vp09" or "vp90" => "VP9",
            "vp80" => "VP8",
            "mp4v" or "mp4a" => "MPEG-4",
            "xvid" => "Xvid",
            "divx" => "DivX",
            "mjpg" => "Motion JPEG",
            "wmv3" or "wvc1" => "WMV",
            "aac " => "AAC",
            "ac-3" or "ac3 " => "AC-3",
            "eac3" => "E-AC-3",
            "dts " => "DTS",
            "flac" => "FLAC",
            "opus" => "Opus",
            "vorb" => "Vorbis",
            "mp3 " => "MP3",
            _ => raw
        };
    }

    public void Dispose()
    {
        _libVlc.Log -= OnLibVlcLog;

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
            // ignore
        }

        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
