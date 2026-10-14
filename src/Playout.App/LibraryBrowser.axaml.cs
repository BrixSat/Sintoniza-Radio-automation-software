using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Playout.App;

/// <summary>Folder tree + file list of the music library. Items can be dragged onto the playlist.</summary>
public partial class LibraryBrowser : UserControl
{
    readonly ObservableCollection<FolderNode> _roots = new();
    List<FileEntry> _allFiles = new();
    int _loadVersion;

    /// <summary>Raised when the user asks to add paths (files or folders); <c>asNext</c> means "play after the current item".</summary>
    public event Action<IReadOnlyList<string>, bool>? AddRequested;

    /// <summary>Raised when the user wants to set the cue points of a file.</summary>
    public event Action<string>? EditCuesRequested;

    /// <summary>Raised when the user wants to edit the tags of a file.</summary>
    public event Action<string>? EditTagsRequested;

    /// <summary>Raised when the user picks a different root folder.</summary>
    public event Action<string>? RootChanged;

    public LibraryBrowser()
    {
        InitializeComponent();
        FolderTree.ItemsSource = _roots;

        FolderTree.SelectionChanged += (_, _) =>
        {
            if (FolderTree.SelectedItem is FolderNode node) LoadFiles(node.Path);
        };
        FilterBox.TextChanged += (_, _) => ApplyFilter();
        ChooseRootButton.Click += async (_, _) => await ChooseRoot();
        RefreshButton.Click += (_, _) => Refresh();

        FileList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is FileEntry f) AddRequested?.Invoke([f.Path], false);
        };
        FileList.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var paths = SelectedFilePaths();
            if (paths.Count > 0) AddRequested?.Invoke(paths, false);
            e.Handled = true;
        };

        FileList.ContextMenu = BuildMenu(SelectedFilePaths);
        var editCues = new MenuItem { Header = "Edit cue points…" };
        editCues.Click += (_, _) => { if (SelectedFilePaths() is [var p, ..]) EditCuesRequested?.Invoke(p); };
        FileList.ContextMenu.Items.Add(new Separator());
        FileList.ContextMenu.Items.Add(editCues);
        var editTags = new MenuItem { Header = "Edit tags…" };
        editTags.Click += (_, _) => { if (SelectedFilePaths() is [var p, ..]) EditTagsRequested?.Invoke(p); };
        FileList.ContextMenu.Items.Add(editTags);
        FolderTree.ContextMenu = BuildMenu(() =>
            FolderTree.SelectedItem is FolderNode n ? [n.Path] : []);

        DragHelper.Attach(FileList,
            () => FileList.SelectedItems?.OfType<FileEntry>().ToList() ?? new(),
            files => DragHelper.Data(AudioFiles.DragFormat, files.Select(f => f.Path).ToArray()));
        DragHelper.Attach(FolderTree,
            () => FolderTree.SelectedItem is FolderNode n ? [n] : new List<FolderNode>(),
            nodes => DragHelper.Data(AudioFiles.DragFormat, nodes.Select(n => n.Path).ToArray()));
    }

    public void SetRoot(string folder)
    {
        _rootPath = folder;
        _roots.Clear();
        if (!Directory.Exists(folder)) return;
        var root = new FolderNode(folder, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : folder);
        _roots.Add(root);
        root.IsExpanded = true;
        FolderTree.SelectedItem = root;
    }

    string? _rootPath;

    /// <summary>Re-reads the tree and the file list from disk, keeping the open folders and the selected one.</summary>
    void Refresh()
    {
        if (_rootPath == null) return;
        var expanded = new HashSet<string>();
        foreach (var node in _roots) CollectExpanded(node, expanded);
        string? selected = (FolderTree.SelectedItem as FolderNode)?.Path;

        SetRoot(_rootPath);
        if (_roots.FirstOrDefault() is not { } root) { _allFiles = new(); ApplyFilter(); return; }

        // Expanding loads the children, so this walks down only as far as folders were open before.
        FolderNode best = root;
        var stack = new Stack<FolderNode>([root]);
        while (stack.TryPop(out var node))
        {
            if (node.Path == selected) best = node;
            if (!expanded.Contains(node.Path)) continue;
            node.IsExpanded = true;
            foreach (var child in node.Children) stack.Push(child);
        }
        // A folder that has gone falls back to the root.
        FolderTree.SelectedItem = best;
        LoadFiles(best.Path);
    }

    static void CollectExpanded(FolderNode node, HashSet<string> into)
    {
        if (!node.IsExpanded) return;
        into.Add(node.Path);
        foreach (var child in node.Children) CollectExpanded(child, into);
    }

    async Task ChooseRoot()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose music folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        SetRoot(path);
        RootChanged?.Invoke(path);
    }

    ContextMenu BuildMenu(Func<List<string>> paths)
    {
        var add = new MenuItem { Header = "Add to playlist" };
        add.Click += (_, _) => { var p = paths(); if (p.Count > 0) AddRequested?.Invoke(p, false); };
        var next = new MenuItem { Header = "Play next" };
        next.Click += (_, _) => { var p = paths(); if (p.Count > 0) AddRequested?.Invoke(p, true); };
        return new ContextMenu { Items = { add, next } };
    }

    // ───────────────────────── Files ─────────────────────────

    void LoadFiles(string folder)
    {
        int version = ++_loadVersion;
        FileCount.Text = "…";
        // Big folders (or network shares) must not freeze the UI.
        Task.Run(() =>
        {
            List<FileEntry> files;
            try { files = AudioFiles.InFolder(folder).Select(p => new FileEntry(p)).ToList(); }
            catch { files = new(); }
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _loadVersion) return;
                _allFiles = files;
                ApplyFilter();
            });
        });
    }

    void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim();
        var shown = string.IsNullOrEmpty(filter)
            ? _allFiles
            : _allFiles.Where(f => f.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        FileList.ItemsSource = shown;
        FileCount.Text = shown.Count == _allFiles.Count ? $"{shown.Count} files" : $"{shown.Count} / {_allFiles.Count}";
    }

    List<string> SelectedFilePaths() =>
        FileList.SelectedItems?.OfType<FileEntry>().Select(f => f.Path).ToList() ?? new();
}

public sealed record FileEntry(string Path)
{
    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>A folder in the tree. Sub-folders load lazily the first time the node is expanded.</summary>
public sealed class FolderNode : INotifyPropertyChanged
{
    bool _loaded, _isExpanded;

    public FolderNode(string path, string? name = null)
    {
        Path = path;
        Name = name ?? System.IO.Path.GetFileName(path);
        // A placeholder child makes the expander arrow appear without scanning the whole tree.
        if (HasSubFolders(path)) Children.Add(Placeholder);
        else _loaded = true;
    }

    FolderNode() { Path = ""; Name = ""; _loaded = true; }
    static readonly FolderNode Placeholder = new();

    public string Path { get; }
    public string Name { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value) EnsureLoaded();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in AudioFiles.SubFolders(Path)) Children.Add(new FolderNode(dir));
        }
        catch
        {
            // Unreadable folder: leave it empty.
        }
    }

    static bool HasSubFolders(string path)
    {
        try { return AudioFiles.SubFolders(path).Any(); }
        catch { return false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
