using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Velopack;

namespace AppAvvio;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LaunchItem> items = [];
    private readonly ObservableCollection<StartupEntry> windowsItems = [];
    private readonly List<StartupEntry> disabledStartup = [];
    private readonly UpdateService updates = new();
    private readonly bool previewMode;
    private List<StartupEntry> snapshot = [];
    private List<StartupEntry> allWindowsItems = [];
    private bool scanPartial = true, scanning, scanCompleted, loadFailed, saveFailed, updateBusy, closing;
    private CancellationTokenSource? running;
    private UpdateInfo? availableUpdate;
    private Point dragOrigin;
    private bool CanEdit => running == null && !updateBusy && !loadFailed && !closing;
    public bool QuietStartup => !loadFailed && QuietCheck.IsChecked == true;
    public Action<string>? NotifyProblem { get; set; }

    public MainWindow(bool preview = false)
    {
        previewMode = preview;
        InitializeComponent();
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 14;
        Foreground = (System.Windows.Media.Brush)FindResource("Ink");
        AppsGrid.ItemsSource = items;
        WindowsApps.ItemsSource = windowsItems;
        VersionText.Text = "Versione installata: " + UpdateService.CurrentVersion;
        SidebarVersion.Text = "Versione " + UpdateService.CurrentVersion;
        try
        {
            var config = preview ? new Configuration() : SettingsStore.Load();
            foreach (var item in config.Items) items.Add(item);
            disabledStartup.AddRange(config.DisabledStartup);
            QuietCheck.IsChecked = config.QuietStartup;
            LoginCheck.IsChecked = !preview && StartupService.IsRegistered();
            var cache = Path.Combine(SettingsStore.Folder, "changelog.txt");
            if (!preview && File.Exists(cache)) Changelog.Text = File.ReadAllText(cache);
        }
        catch (Exception ex)
        {
            loadFailed = true;
            StatusText.Text = "Non riesco a leggere le impostazioni. I dati sono stati conservati. " + ex.Message;
            SaveState.Text = "Impostazioni non caricate";
        }
        items.CollectionChanged += (_, _) => UpdateControls();
        UpdateControls();
        if (!preview) Loaded += async (_, _) => { if (!scanCompleted) await RefreshCatalogAsync(); };
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (updateBusy) { e.Cancel = true; StatusText.Text = "Attendi il completamento dell’aggiornamento."; return; }
        if (saveFailed && !SaveConfiguration())
        {
            e.Cancel = MessageBox.Show(this, "Non riesco a salvare. Vuoi chiudere comunque? Le ultime modifiche potrebbero non essere conservate.", "Salvataggio non riuscito", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
            if (e.Cancel) return;
        }
        running?.Cancel();
        closing = true;
    }

    private void UpdateControls()
    {
        if (StopButton == null) return;
        var selected = AppsGrid.SelectedItem as LaunchItem;
        var index = AppsGrid.SelectedIndex;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SequenceSummary.Text = items.Count == 0 ? "Un piccolo passo per iniziare meglio" : $"{items.Count} app · {items.Count(x => x.Enabled)} attive";
        var problems = items.Count(x => x.Enabled && x.Health != "Configurazione OK");
        if (problems > 0) SequenceSummary.Text += $" · {problems} da controllare";
        AppsGrid.IsEnabled = CanEdit;
        AddButton.IsEnabled = CanEdit;
        LoginCheck.IsEnabled = CanEdit;
        QuietCheck.IsEnabled = CanEdit;
        UpButton.IsEnabled = CanEdit && index > 0;
        DownButton.IsEnabled = CanEdit && index >= 0 && index < items.Count - 1;
        EditButton.IsEnabled = CanEdit && selected != null;
        RemoveButton.IsEnabled = CanEdit && !scanning && selected != null;
        RemoveButton.Content = selected?.OriginalStartup != null ? "Torna all’avvio di Windows" : "Togli dalla sequenza";
        RunButton.IsEnabled = CanEdit && items.Any(x => x.Enabled);
        StopButton.IsEnabled = running != null;
        RefreshButton.IsEnabled = !scanning && !updateBusy && running == null;
        InstallUpdateButton.IsEnabled = availableUpdate != null && CanEdit;
        for (var i = 0; i < items.Count; i++)
            if (items[i].Order != i + 1) { items[i].Order = i + 1; items[i].Refresh(); }
        WindowsSelectionChanged(this, new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }

    private void SequenceSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateControls();

    private bool SaveConfiguration()
    {
        if (previewMode) return true;
        if (loadFailed) { StatusText.Text = "Le impostazioni non sono leggibili. Il salvataggio è bloccato per conservare i dati."; return false; }
        try
        {
            SettingsStore.Save(new Configuration { Items = items.ToList(), DisabledStartup = disabledStartup.ToList(), Repository = UpdateService.Repository, QuietStartup = QuietCheck.IsChecked == true });
            saveFailed = false;
            SaveState.Text = "Tutte le modifiche sono salvate";
            return true;
        }
        catch (Exception ex)
        {
            saveFailed = true;
            SaveState.Text = "Salvataggio non riuscito";
            StatusText.Text = "Non riesco a salvare le modifiche. " + ex.Message;
            return false;
        }
    }

    private void AddApp(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var picker = new AppPicker { Owner = this };
        if (picker.ShowDialog() != true) return;
        var added = picker.Selected.Where(app => !items.Any(x => string.Equals(x.Path, app.Path, StringComparison.OrdinalIgnoreCase)))
            .Select(app => new LaunchItem { Name = app.Name, Path = app.Path }).ToList();
        foreach (var item in added) items.Add(item);
        if (!SaveConfiguration()) { foreach (var item in added) items.Remove(item); }
        else StatusText.Text = added.Count == 0 ? "Queste app sono già nella sequenza." : $"{added.Count} app aggiunte. Seleziona un’app e premi Modifica per scegliere l’attesa.";
        AuditCurrent();
    }

    private void EditSelected(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || AppsGrid.SelectedItem is not LaunchItem item) return;
        if (e is MouseButtonEventArgs mouse && ItemsControl.ContainerFromElement(AppsGrid, mouse.OriginalSource as DependencyObject) is not DataGridRow) return;
        var editor = new AppEditor(item, edited =>
        {
            var index = items.IndexOf(item);
            items[index] = edited;
            if (!SaveConfiguration()) { items[index] = item; AppsGrid.SelectedItem = item; return false; }
            AppsGrid.SelectedItem = edited;
            AuditCurrent();
            StatusText.Text = edited.Name + ": modifiche salvate.";
            return true;
        }) { Owner = this };
        editor.ShowDialog();
    }

    private void ToggleItem(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not LaunchItem item) return;
        if (!CanEdit) { item.Refresh(); return; }
        var previous = item.Enabled;
        item.Enabled = box.IsChecked == true;
        if (!SaveConfiguration()) item.Enabled = previous;
        else StatusText.Text = item.Enabled ? item.Name + " sarà avviata nella sequenza." : item.Name + " è in pausa e non sarà avviata da AppAvvio.";
        item.Refresh();
        UpdateControls();
    }

    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int offset)
    {
        if (!CanEdit) return;
        var index = AppsGrid.SelectedIndex;
        if (index >= 0 && index + offset >= 0 && index + offset < items.Count) Reorder(index, index + offset);
    }
    private void Reorder(int from, int to)
    {
        var selected = items[from];
        items.Move(from, to);
        if (!SaveConfiguration()) items.Move(to, from);
        AppsGrid.SelectedItem = selected;
        UpdateControls();
    }
    private void DragStart(object sender, MouseButtonEventArgs e) => dragOrigin = e.GetPosition(AppsGrid);
    private void DragMove(object sender, MouseEventArgs e)
    {
        if (CanEdit && e.LeftButton == MouseButtonState.Pressed && Math.Abs(e.GetPosition(AppsGrid).Y - dragOrigin.Y) > SystemParameters.MinimumVerticalDragDistance && AppsGrid.SelectedItem is LaunchItem item)
            DragDrop.DoDragDrop(AppsGrid, item, DragDropEffects.Move);
    }
    private void DropItem(object sender, DragEventArgs e)
    {
        if (!CanEdit || e.Data.GetData(typeof(LaunchItem)) is not LaunchItem item) return;
        var row = ItemsControl.ContainerFromElement(AppsGrid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row?.Item is LaunchItem target && target != item && items.Contains(item)) Reorder(items.IndexOf(item), items.IndexOf(target));
    }

    private void RemoveApp(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || scanning || AppsGrid.SelectedItem is not LaunchItem item) return;
        if (item.OriginalStartup != null && MessageBox.Show(this,
            "Questa app tornerà ad avviarsi direttamente con Windows, senza l’ordine e l’attesa di AppAvvio. Vuoi continuare?",
            "Torna all’avvio di Windows", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!SaveConfiguration()) return;
        try
        {
            if (item.OriginalStartup != null) StartupCatalog.Restore(item.OriginalStartup);
            var index = items.IndexOf(item);
            items.Remove(item);
            if (!SaveConfiguration())
            {
                items.Insert(index, item);
                if (item.OriginalStartup != null) StartupCatalog.Disable(item.OriginalStartup);
                return;
            }
            StatusText.Text = item.OriginalStartup == null ? "App tolta dalla sequenza. Non è stata disinstallata." : item.Name + " tornerà ad avviarsi con Windows.";
        }
        catch (Exception ex) { StatusText.Text = "Non riesco a completare la rimozione. " + ex.Message; }
        RefreshWindows(this, new RoutedEventArgs());
    }

    private async void RefreshWindows(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();
    private async Task RefreshCatalogAsync()
    {
        if (scanning || previewMode) return;
        scanning = true;
        WindowsStatus.Text = "Sto cercando le app…";
        UpdateControls();
        try
        {
            var result = await Task.Run(() =>
            {
                var entries = StartupCatalog.Read(includeDisabled: true);
                return (Entries: entries, Warnings: StartupCatalog.Warnings.ToList());
            });
            if (closing) return;
            snapshot = result.Entries;
            scanPartial = result.Warnings.Count > 0;
            allWindowsItems = snapshot.Where(x => x.IsEnabled).ToList();
            foreach (var entry in disabledStartup.Where(x => !snapshot.Any(s => s.Identity == x.Identity && s.IsEnabled)))
            {
                var disabled = snapshot.FirstOrDefault(x => x.Identity == entry.Identity) ?? entry;
                disabled.IsEnabled = false;
                allWindowsItems.Add(disabled);
            }
            FilterWindows(this, new RoutedEventArgs());
            WindowsStatus.ToolTip = scanPartial ? string.Join(Environment.NewLine, result.Warnings) : null;
            AuditCurrent();
        }
        catch (Exception ex)
        {
            scanPartial = true;
            WindowsStatus.Text = "Non riesco a completare la ricerca. Premi Aggiorna elenco per riprovare.";
            WindowsStatus.ToolTip = ex.Message;
            AuditCurrent();
        }
        finally { scanning = false; scanCompleted = true; UpdateControls(); }
    }

    private void AuditCurrent()
    {
        foreach (var item in items) item.Health = SequenceAudit.Check(item, snapshot, scanPartial);
        var count = items.Count(x => x.Enabled && x.Health != "Configurazione OK");
        SequenceHealthText.Text = count == 0 ? "" : $"{count} app da controllare. Seleziona un’app e premi Modifica per i dettagli.";
        SequenceHealthText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateControls();
    }

    private void FilterWindows(object sender, RoutedEventArgs e)
    {
        if (WindowsApps == null || ShowSystemCheck == null || WindowsSearch == null) return;
        var selected = (WindowsApps.SelectedItem as StartupEntry)?.Identity;
        windowsItems.Clear();
        foreach (var item in allWindowsItems.Where(x => (ShowSystemCheck.IsChecked == true || x.CanManage) && x.Name.Contains(WindowsSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            windowsItems.Add(item);
        WindowsApps.SelectedItem = windowsItems.FirstOrDefault(x => x.Identity == selected);
        WindowsStatus.Text = windowsItems.Count == 0 ? "Nessuna app trovata con questi filtri." : $"{windowsItems.Count} voci nell’elenco.";
        if (scanPartial) WindowsStatus.Text += " Alcune voci non sono state verificate. Puoi riprovare con Aggiorna elenco.";
    }
    private void WindowsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToggleStartupButton == null || ImportButton == null) return;
        var entry = WindowsApps.SelectedItem as StartupEntry;
        ImportButton.IsEnabled = CanEdit && !scanning && entry?.CanManage == true && entry.IsEnabled;
        ToggleStartupButton.Content = entry?.IsEnabled == false ? "Riattiva avvio" : "Disattiva avvio";
        ToggleStartupButton.IsEnabled = CanEdit && !scanning && entry?.CanManage == true && (entry.IsEnabled || disabledStartup.Any(x => x.Identity == entry.Identity));
        ToggleStartupButton.ToolTip = entry?.CanManage == false ? entry.FriendlyDescription : "Cambia solo l’avvio automatico dell’app selezionata";
    }
    private void ImportSelected(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || scanning || WindowsApps.SelectedItem is not StartupEntry entry || !entry.IsEnabled || !entry.CanManage || !SaveConfiguration()) return;
        if (MessageBox.Show(this, "AppAvvio aprirà questa app al posto di Windows, rispettando l’ordine e l’attesa della sequenza. Verrà attivato anche l’avvio automatico di AppAvvio.", "Sposta " + entry.Name, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        LaunchItem? item = null;
        bool originalDisabled = false;
        try
        {
            if (items.Any(x => x.OriginalStartup?.Identity == entry.Identity))
                throw new InvalidOperationException("Questa app è già nella sequenza. Controlla i suoi dettagli.");
            item = StartupCatalog.Prepare(entry);
            items.Add(item);
            if (!SaveConfiguration()) { items.Remove(item); return; }
            StartupService.Register(true);
            LoginCheck.IsChecked = true;
            StartupCatalog.Disable(entry);
            originalDisabled = true;
            disabledStartup.RemoveAll(x => x.Identity == entry.Identity);
            if (!SaveConfiguration()) return; // Keep the durable item and its recovery data.
            StatusText.Text = entry.Name + " è nella tua sequenza. Puoi scegliere l’attesa con Modifica.";
            SequenceNav.IsChecked = true;
            AppsGrid.SelectedItem = item;
        }
        catch (Exception ex)
        {
            if (item != null && !originalDisabled) { items.Remove(item); SaveConfiguration(); }
            StatusText.Text = "Spostamento non completato. " + ex.Message;
        }
        finally { RefreshWindows(this, new RoutedEventArgs()); }
    }

    private void ToggleWindowsStartup(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || scanning || WindowsApps.SelectedItem is not StartupEntry entry || !entry.CanManage || !SaveConfiguration()) return;
        try
        {
            if (entry.IsEnabled)
            {
                if (entry.Source == "Cartella utente") StartupCatalog.BackupShortcut(entry);
                var previous = disabledStartup.FirstOrDefault(x => x.Identity == entry.Identity);
                if (previous != null) disabledStartup.Remove(previous);
                disabledStartup.Add(entry);
                if (!SaveConfiguration())
                {
                    disabledStartup.Remove(entry);
                    if (previous != null) disabledStartup.Add(previous);
                    return;
                }
                try { StartupCatalog.Disable(entry); }
                catch
                {
                    disabledStartup.Remove(entry);
                    if (previous != null) disabledStartup.Add(previous);
                    SaveConfiguration();
                    throw;
                }
                StatusText.Text = entry.Name + " non si aprirà più con Windows. Puoi riattivarla qui.";
            }
            else
            {
                var backup = disabledStartup.FirstOrDefault(x => x.Identity == entry.Identity) ?? throw new InvalidOperationException("Usa le impostazioni di Windows per riattivare questa voce.");
                StartupCatalog.Restore(backup);
                disabledStartup.Remove(backup);
                if (!SaveConfiguration()) { disabledStartup.Add(backup); return; }
                StatusText.Text = entry.Name + " si aprirà di nuovo con Windows.";
            }
        }
        catch (Exception ex) { StatusText.Text = "Operazione non completata. " + ex.Message; }
        finally { RefreshWindows(this, new RoutedEventArgs()); }
    }

    private async void Run(object sender, RoutedEventArgs e) => await RunSequence();
    private async Task RunSequence()
    {
        if (!CanEdit || !items.Any(x => x.Enabled) || !SaveConfiguration()) return;
        running = new CancellationTokenSource();
        foreach (var item in items) item.Status = item.Enabled ? "Da avviare" : "In pausa";
        UpdateControls();
        try
        {
            StatusText.Text = "Sto avviando le tue app…";
            await StartupService.RunAsync(items.ToList(), running.Token);
            var failures = items.Count(x => x.Enabled && (x.Status == "File non trovato" || x.Status.StartsWith("Errore:")));
            StatusText.Text = failures == 0 ? "Sequenza completata." : $"Sequenza completata: {failures} app non avviate. Controlla la colonna Esecuzione.";
            if (failures > 0) NotifyProblem?.Invoke(StatusText.Text);
        }
        catch (OperationCanceledException) { StatusText.Text = "Sequenza interrotta. Le app già aperte rimangono aperte."; }
        finally { running.Dispose(); running = null; UpdateControls(); }
    }
    private void Stop(object sender, RoutedEventArgs e) => running?.Cancel();
    public void StopSequence() => running?.Cancel();
    private void ToggleQuiet(object sender, RoutedEventArgs e)
    {
        if (!SaveConfiguration()) QuietCheck.IsChecked = QuietCheck.IsChecked != true;
    }
    private void OpenStartupOptions(object sender, RoutedEventArgs e) => StartupOptions.IsOpen = !StartupOptions.IsOpen;
    private void ToggleLogin(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        try
        {
            if (!SaveConfiguration()) { LoginCheck.IsChecked = StartupService.IsRegistered(); return; }
            StartupService.Register(LoginCheck.IsChecked == true);
            StatusText.Text = LoginCheck.IsChecked == true ? "La sequenza partirà al prossimo accesso a Windows." : "Avvio automatico disattivato. Puoi usare Avvia adesso.";
        }
        catch (Exception ex) { StatusText.Text = "Non riesco a cambiare l’avvio automatico. " + ex.Message; LoginCheck.IsChecked = StartupService.IsRegistered(); }
    }
    public async void StartAtLogin()
    {
        if (loadFailed || closing) return;
        await RefreshCatalogAsync();
        if (closing) return;
        await RunSequence();
    }
    private void OpenStartupSettings(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = "Non riesco ad aprire le impostazioni di Windows. " + ex.Message; }
    }
    private void ShowSequence(object sender, RoutedEventArgs e) => ShowPage(SequencePage);
    private void ShowWindows(object sender, RoutedEventArgs e) => ShowPage(WindowsPage);
    private void ShowUpdates(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage);
    private void ShowPage(Grid? page)
    {
        if (SequencePage == null || WindowsPage == null || UpdatesPage == null || page == null) return;
        SequencePage.Visibility = page == SequencePage ? Visibility.Visible : Visibility.Collapsed;
        WindowsPage.Visibility = page == WindowsPage ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPage.Visibility = page == UpdatesPage ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Visibility = page == UpdatesPage ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        if (updateBusy) return;
        updateBusy = true;
        CheckUpdateButton.IsEnabled = false;
        availableUpdate = null;
        UpdateControls();
        UpdateStatus.Text = "Cerco una nuova versione…";
        try
        {
            var notesUnavailable = false;
            try { Changelog.Text = await updates.LoadChangelogAsync(); }
            catch { notesUnavailable = true; }
            if (!updates.IsInstalled)
            {
                UpdateStatus.Text = "Questa è una copia di sviluppo. Per ricevere aggiornamenti automatici usa la versione installata.";
                if (notesUnavailable) UpdateStatus.Text += " Non riesco a scaricare le novità.";
                return;
            }
            availableUpdate = await updates.CheckAsync();
            UpdateStatus.Text = availableUpdate == null ? "AppAvvio è aggiornato." : $"È disponibile la versione {availableUpdate.TargetFullRelease.Version}.";
            if (notesUnavailable) UpdateStatus.Text += " Le novità non sono disponibili al momento.";
        }
        catch (Exception ex) { UpdateStatus.Text = "Non riesco a controllare gli aggiornamenti. Riprova più tardi."; UpdateStatus.ToolTip = ex.Message; }
        finally { updateBusy = false; CheckUpdateButton.IsEnabled = true; UpdateControls(); }
    }
    private async void InstallUpdate(object sender, RoutedEventArgs e)
    {
        if (updateBusy || running != null || availableUpdate == null || !SaveConfiguration()) return;
        updateBusy = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateControls();
        DownloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<int>(value => { DownloadProgress.Value = value; UpdateStatus.Text = $"Scarico e verifico l’aggiornamento… {value}%"; });
        try
        {
            await updates.DownloadAsync(availableUpdate, value => ((IProgress<int>)progress).Report(value));
            UpdateStatus.Text = "Installazione e riavvio in corso…";
            updateBusy = false;
            updates.ApplyAndRestart(availableUpdate);
        }
        catch (Exception ex) { UpdateStatus.Text = "Aggiornamento non completato. Puoi riprovare."; UpdateStatus.ToolTip = ex.Message; }
        finally { updateBusy = false; CheckUpdateButton.IsEnabled = true; DownloadProgress.Visibility = Visibility.Collapsed; UpdateControls(); }
    }
}
