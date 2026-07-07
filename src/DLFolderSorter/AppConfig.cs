using System.Text.Json;
using DLFolderSorter.Core;

namespace DLFolderSorter;

/// <summary>アプリ設定の永続化（%LOCALAPPDATA%\DLFolderSorter\config.json）。</summary>
internal sealed class AppConfig
{
    public SortConfig Sort { get; set; } = new();
    public bool DarkMode { get; set; } = true;

    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLFolderSorter");

    private static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 壊れた設定は既定値で起動する
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
