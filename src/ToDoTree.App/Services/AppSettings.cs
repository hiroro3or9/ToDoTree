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

    /// <summary>前回終了時に開いていたプロジェクト。LastFilePath は旧形式との互換用に残す。</summary>
    public List<ProjectSessionState> OpenProjects { get; set; } = [];

    /// <summary>前回選択していたタブ。</summary>
    public Guid? ActiveDocumentId { get; set; }

    /// <summary>複数タブ形式でセッションを書き込んだことがあるか。</summary>
    public bool HasWorkspaceSession { get; set; }

    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;

    /// <summary>明るい配色か暗い配色か。</summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
                settings.OpenProjects ??= [];
                return settings;
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

/// <summary>再起動後にプロジェクトタブを復元するための表示状態。</summary>
public sealed class ProjectSessionState
{
    public Guid DocumentId { get; set; }

    public string? FilePath { get; set; }

    public double Zoom { get; set; } = 1;

    public double PanX { get; set; }

    public double PanY { get; set; }

    public bool HasViewportState { get; set; }
}
