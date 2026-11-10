namespace VideoPlayer.Models;

/// <summary>
/// 单个媒体格式（容器）的注册信息。
/// </summary>
/// <param name="Extension">带点号的扩展名，例如 ".mp4"。</param>
/// <param name="Container">容器名称，例如 "MP4"。</param>
/// <param name="Kind">媒体类型。</param>
/// <param name="Description">人类可读描述。</param>
public sealed record MediaFormatInfo(string Extension, string Container, MediaKind Kind, string Description)
{
    /// <summary>用于文件对话框的显示名，例如 "MP4 (*.mp4)"。</summary>
    public string Display => $"{Container} (*{Extension})";
}
