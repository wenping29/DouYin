using LibVLCSharp.Shared;

namespace VideoPlayer.Services;

/// <summary>
/// 使用 LibVLC 在后台解析媒体元数据（标题、时长）。
/// </summary>
public static class MediaProbe
{
    public static Task<(string? Title, TimeSpan? Duration)> ProbeAsync(
        LibVLC libVlc,
        string path,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ((string?)null, (TimeSpan?)null);
                }

                using var media = new Media(libVlc, path, FromType.FromPath);

                // Parse 返回 Task，并会注册 ParsedChanged 事件与取消回调。
                // 必须 await：否则这里立刻退出 using 释放 Media，取消回调再注销事件时
                // 会对已释放的 native 句柄调用 libvlc_event_detach，触发 0xC0000005。
                await media.Parse(MediaParseOptions.ParseLocal, 3000, cancellationToken)
                           .ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    return ((string?)null, (TimeSpan?)null);
                }

                var title = media.Meta(MetadataType.Title);
                TimeSpan? duration = media.Duration > 0
                    ? TimeSpan.FromMilliseconds(media.Duration)
                    : null;

                return (string.IsNullOrWhiteSpace(title) ? null : title, duration);
            }
            catch
            {
                return ((string?)null, (TimeSpan?)null);
            }
        }, cancellationToken);
    }
}
