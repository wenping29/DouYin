using System.IO;

namespace VideoPlayer.Models;

/// <summary>
/// 支持的媒体格式注册表。
/// 这里只负责“识别”扩展名、生成文件过滤器与类型判断；
/// 真正的解码由 LibVLC 完成，因此只要容器能被 VLC 解析即可播放。
/// </summary>
public static class MediaFormats
{
    private static readonly List<MediaFormatInfo> Formats = Build();

    private static readonly Dictionary<string, MediaFormatInfo> ByExtension =
        Formats
            .GroupBy(f => f.Extension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>全部已注册格式。</summary>
    public static IReadOnlyList<MediaFormatInfo> All => Formats;

    /// <summary>全部受支持的扩展名（小写、带点号）。</summary>
    public static IReadOnlyCollection<string> SupportedExtensions => ByExtension.Keys;

    /// <summary>视频格式扩展名。</summary>
    public static IReadOnlyList<string> VideoExtensions { get; } =
        Formats.Where(f => f.Kind == MediaKind.Video).Select(f => f.Extension).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>音频格式扩展名。</summary>
    public static IReadOnlyList<string> AudioExtensions { get; } =
        Formats.Where(f => f.Kind == MediaKind.Audio).Select(f => f.Extension).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>适合注册为文件关联的扩展名（排除原始基本流与 Flash 等不常见关联）。</summary>
    private static readonly HashSet<string> NonAssociableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".h264", ".264", ".h265", ".265", ".hevc", ".av1", ".ivf",
        ".y4m", ".yuv", ".m1v", ".m2v", ".mpv", ".h261", ".h263",
        ".swf", ".mid", ".midi"
    };

    /// <summary>用于“注册为打开方式”的扩展名（小写、带点号）。</summary>
    public static IReadOnlyCollection<string> AssociableExtensions { get; } =
        ByExtension.Keys.Where(e => !NonAssociableExtensions.Contains(e)).ToList();

    /// <summary>OpenFileDialog 使用的过滤器字符串。</summary>
    public static string OpenFileFilter { get; } = BuildFilter();

    public static bool IsSupported(string? path)
        => !string.IsNullOrWhiteSpace(path) && TryGetInfo(path, out _);

    public static bool TryGetInfo(string path, out MediaFormatInfo info)
    {
        info = default!;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ByExtension.TryGetValue(ext, out info!);
    }

    public static MediaKind GetKind(string? path)
        => !string.IsNullOrWhiteSpace(path) && TryGetInfo(path, out var info) ? info.Kind : MediaKind.Unknown;

    public static string GetContainer(string? path)
        => !string.IsNullOrWhiteSpace(path) && TryGetInfo(path, out var info) ? info.Container : "未知";

    /// <summary>根据扩展名从磁盘枚举目录下的受支持文件（不递归）。</summary>
    public static IEnumerable<string> EnumerateMediaFiles(string directory, bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, "*", option))
        {
            if (IsSupported(file))
            {
                yield return file;
            }
        }
    }

    private static string BuildFilter()
    {
        static string Join(IEnumerable<string> exts) => string.Join(";", exts.Select(e => "*" + e));

        var all = Join(ByExtension.Keys.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
        var video = Join(VideoExtensions);
        var audio = Join(AudioExtensions);

        return $"所有支持的媒体|{all}|视频文件|{video}|音频文件|{audio}|所有文件|*.*";
    }

    private static List<MediaFormatInfo> Build()
    {
        var list = new List<MediaFormatInfo>();

        void Add(string container, MediaKind kind, string description, params string[] extensions)
        {
            foreach (var ext in extensions)
            {
                list.Add(new MediaFormatInfo(ext, container, kind, description));
            }
        }

        // ---------- 视频容器 ----------
        Add("MP4", MediaKind.Video, "MPEG-4 视频", ".mp4", ".m4v", ".m4p");
        Add("Matroska", MediaKind.Video, "Matroska 视频", ".mkv", ".mk3d");
        Add("WebM", MediaKind.Video, "WebM 视频", ".webm");
        Add("AVI", MediaKind.Video, "Audio Video Interleave", ".avi");
        Add("DivX", MediaKind.Video, "DivX 视频", ".divx", ".xvid");
        Add("QuickTime", MediaKind.Video, "QuickTime 影片", ".mov", ".qt");
        Add("Windows Media", MediaKind.Video, "Windows Media 视频", ".wmv", ".asf");
        Add("Flash Video", MediaKind.Video, "Flash 视频", ".flv", ".f4v", ".f4p");
        Add("MPEG-TS", MediaKind.Video, "MPEG 传输流", ".ts", ".m2ts", ".mts", ".m2t", ".trp", ".tp", ".tod");
        Add("MPEG-PS", MediaKind.Video, "MPEG 节目流", ".mpg", ".mpeg", ".mpe", ".mpv", ".vob", ".vro");
        Add("MPEG Video", MediaKind.Video, "MPEG 基本流", ".m2v", ".m1v");
        Add("3GPP", MediaKind.Video, "3GPP 手机视频", ".3gp", ".3g2");
        Add("Ogg", MediaKind.Video, "Ogg 媒体", ".ogv", ".ogm");
        Add("RealMedia", MediaKind.Video, "RealMedia 视频", ".rm", ".rmvb");
        Add("MXF", MediaKind.Video, "Material Exchange Format", ".mxf");
        Add("DV", MediaKind.Video, "数字视频", ".dv");
        Add("WTV", MediaKind.Video, "Windows 电视录制", ".wtv");
        Add("DAV", MediaKind.Video, "DAV 视频", ".dav");
        Add("H.264", MediaKind.Video, "H.264 基本流", ".h264", ".264");
        Add("H.265", MediaKind.Video, "H.265/HEVC 基本流", ".h265", ".265", ".hevc");
        Add("AV1", MediaKind.Video, "AV1 基本流", ".av1", ".ivf");
        Add("YUV", MediaKind.Video, "原始 YUV 视频", ".y4m", ".yuv");
        Add("Motion JPEG", MediaKind.Video, "Motion JPEG", ".mjpg", ".mjpeg");
        Add("H.261", MediaKind.Video, "H.261 视频", ".h261");
        Add("H.263", MediaKind.Video, "H.263 视频", ".h263");
        Add("AMV", MediaKind.Video, "AMV 视频", ".amv");
        Add("MTV", MediaKind.Video, "MTV 视频", ".mtv");
        Add("NSV", MediaKind.Video, "NSV 视频", ".nsv");
        Add("SVI", MediaKind.Video, "SVI 视频", ".svi");
        Add("RoQ", MediaKind.Video, "RoQ 视频", ".roq");
        Add("FLI", MediaKind.Video, "FLI/FLC 动画", ".fli", ".flc");
        Add("Bink", MediaKind.Video, "Bink 视频", ".bik");
        Add("Smacker", MediaKind.Video, "Smacker 视频", ".smk");
        Add("Flash", MediaKind.Video, "Shockwave Flash", ".swf");
        Add("LRV", MediaKind.Video, "GoPro 低码率预览", ".lrv");

        // ---------- 音频容器 ----------
        Add("MP3", MediaKind.Audio, "MPEG 音频", ".mp3", ".mp2");
        Add("FLAC", MediaKind.Audio, "无损音频", ".flac");
        Add("AAC", MediaKind.Audio, "AAC 音频", ".aac");
        Add("M4A", MediaKind.Audio, "MPEG-4 音频", ".m4a", ".m4b");
        Add("WAV", MediaKind.Audio, "波形音频", ".wav");
        Add("Ogg", MediaKind.Audio, "Ogg 音频", ".ogg", ".oga");
        Add("Opus", MediaKind.Audio, "Opus 音频", ".opus");
        Add("WMA", MediaKind.Audio, "Windows Media 音频", ".wma");
        Add("APE", MediaKind.Audio, "Monkey's Audio", ".ape");
        Add("ALAC", MediaKind.Audio, "Apple 无损音频", ".alac");
        Add("AIFF", MediaKind.Audio, "AIFF 音频", ".aiff", ".aif", ".aifc");
        Add("AC-3", MediaKind.Audio, "Dolby Digital", ".ac3", ".eac3");
        Add("DTS", MediaKind.Audio, "DTS 音频", ".dts");
        Add("Matroska Audio", MediaKind.Audio, "Matroska 音频", ".mka");
        Add("AMR", MediaKind.Audio, "AMR 语音", ".amr", ".awb");
        Add("MIDI", MediaKind.Audio, "MIDI 音乐", ".mid", ".midi");
        Add("WavPack", MediaKind.Audio, "WavPack 音频", ".wv");
        Add("TTA", MediaKind.Audio, "TTA 无损音频", ".tta");
        Add("DSD", MediaKind.Audio, "DSD 音频", ".dsf", ".dff");
        Add("RealAudio", MediaKind.Audio, "RealAudio", ".ra");
        Add("AU", MediaKind.Audio, "Sun 音频", ".au", ".snd");
        Add("CAF", MediaKind.Audio, "Core Audio 格式", ".caf");
        Add("Speex", MediaKind.Audio, "Speex 语音", ".spx");
        Add("Musepack", MediaKind.Audio, "Musepack 音频", ".mpc");
        Add("OptimFROG", MediaKind.Audio, "OptimFROG 音频", ".ofr", ".ofs");
        Add("Shorten", MediaKind.Audio, "Shorten 无损音频", ".shn");
        Add("VOC", MediaKind.Audio, "Creative VOC", ".voc");
        Add("Tracker", MediaKind.Audio, "Tracker 音乐", ".xm", ".it", ".s3m", ".mod");

        return list;
    }
}
