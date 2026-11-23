namespace NexaDock.Core.Layout;

public enum BarSlotKind
{
    /// <summary>A tile with its natural width.</summary>
    Fixed,

    /// <summary>Flexible space: shares whatever width is left in its part of the bar.</summary>
    Flexible,

    /// <summary>
    /// The tile kept in the middle of the bar (the Apps widget, like the Windows 11 taskbar).
    /// Tiles before it start at the left edge, tiles after it end at the right edge.
    /// </summary>
    Center,
}

public readonly record struct BarSlot(double Width, BarSlotKind Kind);

/// <summary>Where a slot goes; <see cref="Hidden"/> slots moved to the overflow menu.</summary>
public readonly record struct BarPlacement(double X, double Width, bool Hidden);

/// <summary>Pure horizontal layout of the bar's tiles (no WPF), so it can be tested.</summary>
public static class BarLayout
{
    public const double MinFlexibleWidth = 12;

    /// <param name="width">Full width of the bar's tile area.</param>
    /// <param name="endReserve">Width kept free at the right end (for the overflow button).</param>
    public static BarPlacement[] Arrange(IReadOnlyList<BarSlot> slots, double width, double gap, double endReserve = 0)
    {
        var centerIndex = -1;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].Kind == BarSlotKind.Center)
            {
                centerIndex = i;
                break;
            }
        }

        var usable = Math.Max(0, width - endReserve);
        return centerIndex < 0 ? ArrangeFromLeft(slots, usable, gap) : ArrangeCentered(slots, width, usable, gap, centerIndex);
    }

    /// <summary>Taskbar style: tiles from the left, flexible spaces share what is left.</summary>
    private static BarPlacement[] ArrangeFromLeft(IReadOnlyList<BarSlot> slots, double width, double gap)
    {
        var result = new BarPlacement[slots.Count];
        double fixedWidth = 0;
        var flexible = 0;
        foreach (var s in slots)
        {
            if (s.Kind == BarSlotKind.Flexible)
            {
                flexible++;
            }
            else
            {
                fixedWidth += s.Width;
            }
        }

        var gaps = Math.Max(0, slots.Count - 1) * gap;
        var flexWidth = flexible == 0 ? 0 : Math.Max(MinFlexibleWidth, (width - fixedWidth - gaps) / flexible);

        double x = 0;
        var overflowing = false;
        for (var i = 0; i < slots.Count; i++)
        {
            var w = slots[i].Kind == BarSlotKind.Flexible ? flexWidth : slots[i].Width;

            // A tile that would only partly fit is not shown at all; everything after it goes too,
            // so the order is kept.
            if (overflowing || x + w > width + 0.5)
            {
                overflowing = true;
                result[i] = new BarPlacement(0, 0, true);
                continue;
            }

            result[i] = new BarPlacement(x, w, false);
            x += w + gap;
        }

        return result;
    }

    /// <summary>
    /// The center tile sits in the middle of the bar; the tiles before it are packed from the left
    /// edge towards it, the tiles after it from the right edge towards it. A flexible space inside
    /// a side takes that side's free width. When one side is too long the center tile moves over
    /// just enough; when everything still doesn't fit, tiles are hidden from the outer ends
    /// (right side first), never the center tile.
    /// </summary>
    /// <param name="width">Full width: the center tile is centred on it.</param>
    /// <param name="usable">Right end of the usable area (left of the overflow button).</param>
    private static BarPlacement[] ArrangeCentered(IReadOnlyList<BarSlot> slots, double width, double usable, double gap, int centerIndex)
    {
        var result = new BarPlacement[slots.Count];
        var visible = Enumerable.Repeat(true, slots.Count).ToArray();
        var center = slots[centerIndex];

        // Hide outer tiles until the two sides and the center tile fit.
        var left = Enumerable.Range(0, centerIndex).ToList();
        var right = Enumerable.Range(centerIndex + 1, slots.Count - centerIndex - 1).ToList();
        while (Needed(slots, left, gap) + center.Width + Needed(slots, right, gap) > usable + 0.5)
        {
            var side = right.Count > 0 ? right : left;
            if (side.Count == 0)
            {
                break;
            }

            // Right side: drop its last (outermost) tile; left side: its first (outermost) tile.
            var drop = ReferenceEquals(side, right) ? side[^1] : side[0];
            side.Remove(drop);
            visible[drop] = false;
        }

        var leftNeeded = Needed(slots, left, gap);
        var rightNeeded = Needed(slots, right, gap);
        var ideal = (width - center.Width) / 2;
        var maxX = Math.Max(leftNeeded, usable - rightNeeded - center.Width);
        var cx = Math.Clamp(ideal, leftNeeded, maxX);
        result[centerIndex] = new BarPlacement(cx, center.Width, false);

        // Left side: [0, cx - gap], packed from the left.
        Pack(slots, left, 0, cx - (left.Count > 0 ? gap : 0), alignEnd: false, gap, result);

        // Right side: [cx + center + gap, width], packed towards the right edge.
        var rightStart = cx + center.Width + (right.Count > 0 ? gap : 0);
        Pack(slots, right, rightStart, usable, alignEnd: true, gap, result);

        for (var i = 0; i < slots.Count; i++)
        {
            if (!visible[i])
            {
                result[i] = new BarPlacement(0, 0, true);
            }
        }

        return result;
    }

    /// <summary>Width a side needs: fixed tiles plus gaps (flexible spaces may shrink to nothing).</summary>
    private static double Needed(IReadOnlyList<BarSlot> slots, List<int> side, double gap)
    {
        if (side.Count == 0)
        {
            return 0;
        }

        var w = side.Sum(i => slots[i].Kind == BarSlotKind.Flexible ? 0 : slots[i].Width);
        return w + side.Count * gap; // gaps between tiles plus the one next to the center tile
    }

    private static void Pack(IReadOnlyList<BarSlot> slots, List<int> side, double start, double end, bool alignEnd, double gap, BarPlacement[] result)
    {
        if (side.Count == 0)
        {
            return;
        }

        var fixedWidth = side.Sum(i => slots[i].Kind == BarSlotKind.Flexible ? 0 : slots[i].Width);
        var flexible = side.Count(i => slots[i].Kind == BarSlotKind.Flexible);
        var free = Math.Max(0, end - start - fixedWidth - (side.Count - 1) * gap);
        var flexWidth = flexible > 0 ? free / flexible : 0;

        // Without flexible space the right side hugs the right edge and the left side the left edge.
        var x = flexible == 0 && alignEnd ? start + free : start;
        foreach (var i in side)
        {
            var w = slots[i].Kind == BarSlotKind.Flexible ? flexWidth : slots[i].Width;
            result[i] = new BarPlacement(x, w, false);
            x += w + gap;
        }
    }
}
