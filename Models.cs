using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.IO;

namespace AppAvvio;

public sealed class LaunchItem : INotifyPropertyChanged
{
    private string status = "Da avviare";
    private string health = "Da verificare";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int DelaySeconds { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DelayLabel => DelaySeconds == 0 ? "Subito" : DelaySeconds % 60 == 0 ? $"Dopo {DelaySeconds / 60} min" : $"Dopo {DelaySeconds} s";
    [System.Text.Json.Serialization.JsonIgnore]
    public string HealthLabel => !Enabled ? "In pausa" : Health == "Configurazione OK" ? "Pronta" : Health == "Da verificare" ? "Da controllare" : "Da controllare · " + Health;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.ImageSource? Icon => AppIcons.Get(Path);
    [System.Text.Json.Serialization.JsonIgnore]
    public int Order { get; set; }
    public StartupEntry? OriginalStartup { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Status { get => status; set { status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Health { get => health; set { health = value; Refresh(); } }
}

public sealed class Configuration
{
    public bool QuietStartup { get; set; } = true;
    public List<LaunchItem> Items { get; set; } = [];
    public List<StartupEntry> DisabledStartup { get; set; } = [];
    public string Repository { get; set; } = "";
}

public static class SettingsStore
{
    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppAvvio", "Data");
    public static string FilePath => System.IO.Path.Combine(Folder, "settings.json");
    public static Configuration Load(string? filePath = null)
    {
        var path = filePath ?? FilePath;
        var config = File.Exists(path) ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path)) ?? throw new InvalidDataException("Configurazione vuota.") : new();
        Validate(config);
        return config;
    }
    private static void Validate(Configuration config)
    {
        if (config.Items == null || config.DisabledStartup == null || config.Items.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Path) || x.Arguments == null || x.WorkingDirectory == null || x.DelaySeconds < 0 || x.DelaySeconds > 86400))
            throw new InvalidDataException("Una delle app contiene dati incompleti o un’attesa non valida.");
    }
    public static void Save(Configuration config, string? filePath = null)
    {
        Validate(config);
        var path = System.IO.Path.GetFullPath(filePath ?? FilePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
}
