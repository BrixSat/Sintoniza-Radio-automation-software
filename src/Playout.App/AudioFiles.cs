using Avalonia.Input;

namespace Playout.App;

static class AudioFiles
{
    public static readonly string[] Patterns = ["*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.mp2", "*.aiff", "*.aif"];

    static readonly HashSet<string> Extensions = Patterns.Select(p => p[1..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static readonly EnumerationOptions Shallow = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
    static readonly EnumerationOptions Deep = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, RecurseSubdirectories = true };

    /// <summary>In-app drag format: absolute file/folder paths dragged from the music browser.</summary>
    public static readonly DataFormat<string[]> DragFormat = DataFormat.CreateInProcessFormat<string[]>("RadioPlayout.Paths");

    public static bool IsAudio(string path) => Extensions.Contains(Path.GetExtension(path));

    public static IEnumerable<string> InFolder(string folder) =>
        Directory.EnumerateFiles(folder, "*", Shallow).Where(IsAudio).Order(StringComparer.CurrentCultureIgnoreCase);

    public static IEnumerable<string> SubFolders(string folder) =>
        Directory.EnumerateDirectories(folder, "*", Shallow)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Order(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Files stay as they are; folders expand to all audio files below them, in name order.</summary>
    public static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                foreach (var f in Directory.EnumerateFiles(p, "*", Deep).Where(IsAudio).Order(StringComparer.CurrentCultureIgnoreCase))
                    yield return f;
            }
            else if (File.Exists(p) && IsAudio(p)) yield return p;
        }
    }
}
