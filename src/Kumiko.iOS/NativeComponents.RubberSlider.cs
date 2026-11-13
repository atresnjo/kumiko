using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

internal sealed record NativeRubberSliderContent(string Label, ImmutableArray<NativeChoice> Items,
    int Index, int MinimumIndex, bool Disabled, bool ReduceMotion);

public static partial class NativeComponents
{
    /// <summary>
    /// A discrete, immediate-contact slider with alternating ticks, spring fill and anchored edge stretch.
    /// Selection is clamped and sent through messages; cancellation keeps the last touched selection.
    /// </summary>
    public static void NativeRubberSlider<TMessage>(this Ui<TMessage> ui, string label,
        ImmutableArray<NativeChoice> items, string selectedId, Func<string, TMessage> onChange,
        int minimumIndex = 0, bool disabled = false, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (items.IsDefault || items.Length is < 2 or > 128 || minimumIndex < 0 || minimumIndex >= items.Length - 1 ||
            items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Supply 2–128 unique choices and a valid minimum index.", nameof(items));
        var index = Enumerable.Range(minimumIndex, items.Length - minimumIndex).FirstOrDefault(i => items[i].Id == selectedId, -1);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(selectedId));
        var content = new NativeRubberSliderContent(label, items, index, minimumIndex, disabled, ui.Context.ReduceMotion);
        using var slider = Input(ui, content, label, Role.Slider, 44, disabled, key);
        Change(ui, slider, onChange, disabled, id => items.Skip(minimumIndex).Any(item => item.Id == id));
    }
}
