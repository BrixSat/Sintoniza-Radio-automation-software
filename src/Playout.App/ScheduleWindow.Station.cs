using Playout.Library;
using Avalonia.Controls;
using Avalonia.LogicalTree;
namespace Playout.App;

/// <summary>Sintoniza Pro's station tools: its settings, categories, cartwalls and music import.</summary>
public partial class ScheduleWindow
{
    readonly Dictionary<TabItem, PageWindow> _pages = new();

    /// <summary>
    /// The station's tools as pages in the middle (made the first time they are opened): Categories, Import music,
    /// Station settings and Cartwalls. Saving or cancelling the settings or the cartwalls shows them afresh.
    /// </summary>
    void WireStation(ScheduleStore? store)
    {
        bool online = store != null;
        foreach (var b in new[] { StationSettingsButton, CategoriesButton, CartwallsButton, ImportButton, NavStationSettings, NavCategories, NavCartwalls, NavImport })
            b.IsEnabled = online;
        MenuStationSettings.IsEnabled = MenuCategories.IsEnabled = MenuCartwalls.IsEnabled = MenuImport.IsEnabled = online;
        if (store == null) return;
        var db = store.Db;
        Tabs.SelectionChanged += async (_, _) =>
        {
            if (Tabs.SelectedItem is not TabItem tab || _pages.ContainsKey(tab)) return;
            if (tab == CategoriesTab && _library != null)
            {
                // The categories' folders are kept in the database; every playout scans them by itself.
                var folders = new CategoryFolders(() => _library.Db?.FolderCategories() ?? new(), (folder, id) => _library.Db?.SetFolderRule(folder, id), _ => { }, () => _library.NotifyChanged());
                Show(tab, new CategoriesWindow(_library, folders), hide: "CloseButton");
            }
            else if (tab == ImportTab) Show(tab, new ImportWindow(db, null), hide: "CloseButton");
            else if (tab == StationSettingsTab) await ShowStationSettings(new StationStore(db));
            else if (tab == CartwallsTab) await ShowCartwalls(new StationStore(db));
            else if (tab == UsersTab && tab.Content == null) tab.Content = new UsersView(new UserStore(db), _user?.Name ?? "");
        };
        Closed += (_, _) => { foreach (var page in _pages.Values) page.Discard(); };
    }

    /// <summary>Puts a tool in its page (in place of what it showed), without the button that would close a window.</summary>
    void Show(TabItem tab, PageWindow window, string? hide = null)
    {
        if (_pages.Remove(tab, out var old)) old.Discard();
        var content = window.Embed();
        if (hide != null && content.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == hide) is { } button) button.IsVisible = false;
        tab.Content = content;
        _pages[tab] = window;
    }

    async Task ShowStationSettings(StationStore store)
    {
        if (await LoadStation(store) is not var (station, version)) return;
        // The settings window works on a whole set of settings: the station's are put in a blank one.
        var edited = new AppSettings();
        station.ApplyTo(edited);
        var window = new SettingsWindow(edited, null, null, SettingsScope.Station, s =>
        {
            var updated = StationSettings.From(s);
            updated.Cartwalls = station.Cartwalls; // edited on their own (Cartwalls)
            return SaveStation(store, updated, version);
        });
        window.Finished += async saved =>
        {
            if (saved is true) SavedText.Text = L.F("Station settings saved at {0:HH:mm:ss}. The playouts take them within a minute.", DateTime.Now);
            await ShowStationSettings(store); // as saved now (or undone)
        };
        Show(StationSettingsTab, window);
        if (window.FindControl<Button>("CancelButton") is { } cancel) cancel.Content = L.T("Undo changes");
    }

    async Task ShowCartwalls(StationStore store)
    {
        if (await LoadStation(store) is not var (station, version)) return;
        var window = new CartwallsWindow(station.Cartwalls, _settings?.Routing ?? new AppSettings().Routing, pages =>
        {
            var updated = station.Clone();
            updated.Cartwalls = pages;
            return SaveStation(store, updated, version);
        });
        window.Finished += async saved =>
        {
            if (saved is true) SavedText.Text = L.F("Cartwalls saved at {0:HH:mm:ss}. The playouts take them within a minute.", DateTime.Now);
            await ShowCartwalls(store);
        };
        Show(CartwallsTab, window);
        if (window.FindControl<Button>("CancelButton") is { } cancel) cancel.Content = L.T("Undo changes");
    }

    /// <summary>The station's settings, as last saved (this computer's own, the first time).</summary>
    async Task<(StationSettings Settings, long Version)?> LoadStation(StationStore store)
    {
        try
        {
            var (settings, version) = await Task.Run(store.Load);
            return (settings ?? StationSettings.From(_settings ?? AppSettings.Load()), version);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("The station's settings can't be read: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>Saves the station's settings over the version read; returns why not, or null.</summary>
    static string? SaveStation(StationStore store, StationSettings settings, long version)
    {
        try
        {
            store.Save(settings, version, Who);
            return null;
        }
        catch (ScheduleStore.ChangedException ex)
        {
            return L.F("They were saved by {0} at {1:HH:mm} meanwhile: close and open them again.", ex.By.Length > 0 ? ex.By : L.T("another computer"), ex.At);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
