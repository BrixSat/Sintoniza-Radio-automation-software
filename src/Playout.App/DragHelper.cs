using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Playout.App;

static class DragHelper
{
    const double Threshold = 6;

    /// <summary>
    /// Makes the items of a list/tree draggable. The drag starts once the left button has moved a few pixels
    /// from an item. Dragging an item that was part of a multi-selection drags the whole selection
    /// (the selection is captured in the tunnel phase, before the control collapses it on press).
    /// </summary>
    public static void Attach<T>(Control source, Func<List<T>> selection, Func<List<T>, IDataTransfer> build,
        DragDropEffects effects = DragDropEffects.Copy) where T : class
    {
        PointerPressedEventArgs? press = null;
        Point start = default;
        T? pressed = null;
        List<T> before = new();

        source.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            press = null;
            if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
            if ((e.Source as Control)?.DataContext is not T item) return; // scrollbars, empty space
            press = e;
            start = e.GetPosition(source);
            pressed = item;
            before = selection();
        }, RoutingStrategies.Tunnel);

        source.AddHandler(InputElement.PointerReleasedEvent, (_, _) => press = null, RoutingStrategies.Tunnel);

        source.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (press is not { } p || pressed is not { } item) return;
            if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) { press = null; return; }
            var d = e.GetPosition(source) - start;
            if (Math.Abs(d.X) < Threshold && Math.Abs(d.Y) < Threshold) return;
            press = null;

            var items = before.Contains(item) ? before : [item];
            await DragDrop.DoDragDropAsync(p, build(items), effects);
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// What a drag carries: one item with the app's own format and the same as plain text (<paramref name="text"/>).
    /// macOS puts on its pasteboard only what it knows (the text), and AppKit ends the app when the drag's items and
    /// the pasteboard's don't match ("There are 0 items on the pasteboard, but 1 drag images"): so one item, with both.
    /// </summary>
    public static DataTransfer Data<TValue>(DataFormat<TValue> format, TValue value, string text) where TValue : class
    {
        var item = new DataTransferItem();
        item.Set(format, value);
        item.SetText(text.Length > 0 ? text : AppInfo.Name);
        var data = new DataTransfer();
        data.Add(item);
        return data;
    }

    /// <summary>File paths: the app's format, and the paths one per line as text.</summary>
    public static DataTransfer Paths(IEnumerable<string> paths)
    {
        var list = paths.ToArray();
        return Data(AudioFiles.DragFormat, list, string.Join("\n", list));
    }
}
