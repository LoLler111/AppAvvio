using System.IO;
using System.Net.Http;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace AppAvvio;

public sealed class UpdateService
{
    public const string Repository = "LoLler111/AppAvvio";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private readonly UpdateManager manager;
    public UpdateService(Velopack.Locators.IVelopackLocator? locator = null)
    {
        manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false), locator: locator);
    }
    public bool IsInstalled => manager.IsInstalled;
    public Task<UpdateInfo?> CheckAsync() => manager.CheckForUpdatesAsync();
    public Task DownloadAsync(UpdateInfo update, Action<int> progress) => manager.DownloadUpdatesAsync(update, progress);
    public void ApplyAndRestart(UpdateInfo update) => manager.ApplyUpdatesAndRestart(update);

    public async Task<string> LoadChangelogAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AppAvvio/" + CurrentVersion);
        var json = await client.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=30");
        var notes = FormatChangelog(json);
        Directory.CreateDirectory(SettingsStore.Folder);
        var path = Path.Combine(SettingsStore.Folder, "changelog.txt");
        await File.WriteAllTextAsync(path + ".tmp", notes);
        File.Move(path + ".tmp", path, true);
        return notes;
    }

    public static string FormatChangelog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var notes = document.RootElement.EnumerateArray()
            .Where(x => !x.GetProperty("draft").GetBoolean() && !x.GetProperty("prerelease").GetBoolean())
            .Select(x =>
            {
                var date = x.GetProperty("published_at").GetString();
                if (DateTimeOffset.TryParse(date, out var parsed)) date = parsed.ToString("dd/MM/yyyy");
                return $"{x.GetProperty("tag_name").GetString()}  ·  {date}\n\n{x.GetProperty("body").GetString()}";
            });
        var text = string.Join("\n\n────────────────────────────\n\n", notes);
        return string.IsNullOrWhiteSpace(text) ? "Nessuna versione stabile pubblicata." : text;
    }
}
