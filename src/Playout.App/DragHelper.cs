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

    public static DataTransfer Data<TValue>(DataFormat<TValue> format, TValue value) where TValue : class
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(format, value));
        return data;
    }
}
