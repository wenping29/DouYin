using System.IO;
using System.Text.Json;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

/// <summary>
/// 配置读写服务。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        Settings = Load();
    }

    public AppSettings Settings { get; }

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoPlayer");

    public static string SettingsPath { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string SnapshotDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VideoPlayer");

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    // 反序列化后字典的比较器会丢失，重新包一层
                    settings.Positions = new Dictionary<string, double>(
                        settings.Positions ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);
                    settings.Playlist ??= new List<string>();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            // 配置损坏时回退到默认值，不影响启动，但要留下可排查的记录
            AppLog.Warn($"读取配置失败，已使用默认设置：{SettingsPath} | {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);

            if (Settings.Positions.Count > 1000)
            {
                // 简单限制体积，避免长期使用后无限增长
                var trimmed = Settings.Positions
                    .OrderByDescending(p => File.Exists(p.Key) ? File.GetLastWriteTimeUtc(p.Key) : DateTime.MinValue)
                    .Take(500)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                Settings.Positions = trimmed;
            }

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, SettingsPath, overwrite: true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            // 保存失败不应导致崩溃，但要留下可排查的记录
            AppLog.Warn($"保存配置失败：{SettingsPath} | {ex.Message}");
        }
    }
}
