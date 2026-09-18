using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VSCodeRecent;

/// <summary>
/// 扩展设置。持久化到 %LOCALAPPDATA%\VSCodeRecent\settings.json。
/// </summary>
internal sealed class VSCodeRecentSettings : JsonSettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VSCodeRecent");

    /// <summary>默认不显示文件 —— 最近记录里绝大多数是单独打开的文件，会淹没项目。</summary>
    public ToggleSetting ShowFiles { get; } = new(
        "showFiles",
        "显示文件",
        "在列表中显示最近单独打开的文件",
        false);

    public VSCodeRecentSettings()
    {
        Directory.CreateDirectory(SettingsDir);
        FilePath = Path.Combine(SettingsDir, "settings.json");
        Settings.Add(ShowFiles);

        // JsonSettingsManager 本身不监听 SettingsChanged（只有 LoadSettings/SaveSettings
        // 两个公开方法），不自己接这一步的话，点 Save 只改内存，重启就丢。
        Settings.SettingsChanged += (_, _) => SaveSettings();

        LoadSettings();
    }
}
