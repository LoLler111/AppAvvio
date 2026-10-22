using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.IO;

namespace AppAvvio;

public sealed class LaunchItem : INotifyPropertyChanged
{
    private string status = "Da avviare";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int DelaySeconds { get; set; }
    public StartupEntry? OriginalStartup { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Status { get => status; set { status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class Configuration
{
    public List<LaunchItem> Items { get; set; } = [];
    public string Repository { get; set; } = "";
}

public static class SettingsStore
{
    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppAvvio", "Data");
    public static string FilePath => System.IO.Path.Combine(Folder, "settings.json");
    public static Configuration Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Configurazione vuota.")
        : new();
    public static void Save(Configuration config)
    {
        Directory.CreateDirectory(Folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(temporary, FilePath, true);
    }
}
