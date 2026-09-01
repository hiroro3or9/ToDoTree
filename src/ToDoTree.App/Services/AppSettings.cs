using System.IO;
using System.Text.Json;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.Services;

/// <summary>次に起動したときに前回の続きから始めるための小さな設定。</summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ToDoTree",
        "settings.json");

    // 直列化のたびに作り直すと、内部キャッシュが毎回捨てられて遅くなる。
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string? LastFilePath { get; set; }

    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // 設定が壊れていても起動を止めない。
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch
        {
            // 保存できなくても致命的ではない。
        }
    }
}
