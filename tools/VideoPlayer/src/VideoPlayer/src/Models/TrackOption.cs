namespace VideoPlayer.Models;

/// <summary>
/// 音轨 / 字幕轨选项。
/// </summary>
public sealed class TrackOption
{
    public TrackOption(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }

    public string Name { get; }

    public override string ToString() => Name;
}
