using System.Numerics;

namespace Kumiko.Rendering;

internal enum IconOpKind
{
    Line,
    Loop,
    Fill,
    Cut,
    Circle,
    Disc,
    Box,
    Solid,
    Arc
}

internal readonly record struct IconOp(IconOpKind Kind, float[] Values);
internal static class IconData
{
    internal static readonly Dictionary<string, IconOp[]> All = new(StringComparer.Ordinal)
    {
        ["checkmark"] = [new(IconOpKind.Line, [5f, 12.5f, 10f, 17.5f, 19f, 7f])],
        ["xmark"] = [new(IconOpKind.Line, [6f, 6f, 18f, 18f]), new(IconOpKind.Line, [18f, 6f, 6f, 18f])],
        ["plus"] = [new(IconOpKind.Line, [12f, 5f, 12f, 19f]), new(IconOpKind.Line, [5f, 12f, 19f, 12f])],
        ["chevron.down"] = [new(IconOpKind.Line, [6f, 9f, 12f, 15f, 18f, 9f])],
        ["chevron.up"] = [new(IconOpKind.Line, [6f, 15f, 12f, 9f, 18f, 15f])],
        ["chevron.right"] = [new(IconOpKind.Line, [9f, 6f, 15f, 12f, 9f, 18f])],
        ["chevron.left"] = [new(IconOpKind.Line, [15f, 6f, 9f, 12f, 15f, 18f])],
        ["minus"] = [new(IconOpKind.Line, [5f, 12f, 19f, 12f])],
        ["arrow.left"] = [new(IconOpKind.Line, [19f, 12f, 5f, 12f]), new(IconOpKind.Line, [11f, 6f, 5f, 12f, 11f, 18f])],
        ["calendar"] = [
            new(IconOpKind.Box, [3.5f, 5f, 17f, 15.5f, 2.5f]),
            new(IconOpKind.Line, [3.5f, 10f, 20.5f, 10f]),
            new(IconOpKind.Line, [8f, 3f, 8f, 7f]),
            new(IconOpKind.Line, [16f, 3f, 16f, 7f])
        ],
        ["clock"] = [new(IconOpKind.Circle, [12f, 12f, 9f]), new(IconOpKind.Line, [12f, 7f, 12f, 12f, 15.5f, 14f])],
        ["chevron.up.chevron.down"] = [new(IconOpKind.Line, [8f, 9f, 12f, 5f, 16f, 9f]), new(IconOpKind.Line, [8f, 15f, 12f, 19f, 16f, 15f])],
        ["arrow.right"] = [new(IconOpKind.Line, [5f, 12f, 19f, 12f]), new(IconOpKind.Line, [13f, 6f, 19f, 12f, 13f, 18f])],
        ["arrow.clockwise"] = [new(IconOpKind.Arc, [12f, 12f, 7.5f, -60f, 230f]), new(IconOpKind.Line, [16f, 3.5f, 16.2f, 6.5f, 19.2f, 6.7f])],
        ["magnifyingglass"] = [new(IconOpKind.Circle, [10.5f, 10.5f, 6.5f]), new(IconOpKind.Line, [15.5f, 15.5f, 20f, 20f])],
        ["circle"] = [new(IconOpKind.Circle, [12f, 12f, 9f])],
        ["circle.dotted"] = [
            new(IconOpKind.Disc, [12f, 3f, 1.3f]),
            new(IconOpKind.Disc, [18.4f, 5.6f, 1.3f]),
            new(IconOpKind.Disc, [21f, 12f, 1.3f]),
            new(IconOpKind.Disc, [18.4f, 18.4f, 1.3f]),
            new(IconOpKind.Disc, [12f, 21f, 1.3f]),
            new(IconOpKind.Disc, [5.6f, 18.4f, 1.3f]),
            new(IconOpKind.Disc, [3f, 12f, 1.3f]),
            new(IconOpKind.Disc, [5.6f, 5.6f, 1.3f])
        ],
        ["checkmark.circle.fill"] = [new(IconOpKind.Disc, [12f, 12f, 10f]), new(IconOpKind.Cut, [7.5f, 12.5f, 10.5f, 15.5f, 16.5f, 9f])],
        ["stop.circle"] = [new(IconOpKind.Circle, [12f, 12f, 9f]), new(IconOpKind.Solid, [9f, 9f, 6f, 6f, 1f])],
        ["play.fill"] = [new(IconOpKind.Fill, [7f, 4.5f, 19.5f, 12f, 7f, 19.5f])],
        ["star.fill"] = [new(IconOpKind.Fill, Star(12f, 12.8f, 10.5f, 4.4f, 5))],
        ["sparkles"] = [new(IconOpKind.Fill, Star(10f, 13.5f, 8f, 2f, 4)), new(IconOpKind.Fill, Star(18.5f, 5.5f, 3.8f, 1f, 4))],
        ["wand.and.stars"] = [
            new(IconOpKind.Line, [4f, 20f, 15f, 9f]),
            new(IconOpKind.Fill, Star(18f, 5f, 3.5f, 1f, 4)),
            new(IconOpKind.Fill, Star(20f, 12f, 2.5f, 0.8f, 4)),
            new(IconOpKind.Fill, Star(11.5f, 3.5f, 2.5f, 0.8f, 4))
        ],
        ["person.fill"] = [new(IconOpKind.Disc, [12f, 7.5f, 4.2f]), new(
            IconOpKind.Fill,
            [4f, 21f, 4.6f, 17.6f, 6.4f, 15.2f, 9f, 13.8f, 12f, 13.4f, 15f, 13.8f, 17.6f, 15.2f, 19.4f, 17.6f, 20f, 21f]
        )],
        ["photo"] = [
            new(IconOpKind.Box, [3f, 4f, 18f, 16f, 2.5f]),
            new(IconOpKind.Circle, [8.5f, 9.5f, 1.8f]),
            new(IconOpKind.Line, [3.5f, 17f, 9f, 12.5f, 13f, 16f, 16f, 13.5f, 20.5f, 17.5f])
        ],
        ["doc.richtext"] = [
            new(IconOpKind.Loop, [6f, 3f, 14f, 3f, 19f, 8f, 19f, 21f, 6f, 21f]),
            new(IconOpKind.Line, [9f, 10f, 13f, 10f]),
            new(IconOpKind.Line, [9f, 14f, 16f, 14f]),
            new(IconOpKind.Line, [9f, 17.5f, 16f, 17.5f])
        ],
        ["chevron.left.forwardslash.chevron.right"] = [
            new(IconOpKind.Line, [7f, 7f, 2.5f, 12f, 7f, 17f]),
            new(IconOpKind.Line, [17f, 7f, 21.5f, 12f, 17f, 17f]),
            new(IconOpKind.Line, [14f, 5f, 10f, 19f])
        ],
        ["doc"] = [new(IconOpKind.Loop, [6f, 3f, 14f, 3f, 19f, 8f, 19f, 21f, 6f, 21f])],
        ["doc.text"] = [
            new(IconOpKind.Loop, [6f, 3f, 14f, 3f, 19f, 8f, 19f, 21f, 6f, 21f]),
            new(IconOpKind.Line, [9.5f, 12f, 15.5f, 12f]),
            new(IconOpKind.Line, [9.5f, 16f, 15.5f, 16f])
        ],
        ["doc.on.doc"] = [new(IconOpKind.Box, [8.5f, 8.5f, 12f, 12.5f, 2f]), new(IconOpKind.Line, [4.5f, 16f, 4.5f, 5.5f, 5.5f, 4.5f, 15.5f, 4.5f])],
        ["folder"] = [new(IconOpKind.Loop, [3f, 5.5f, 9f, 5.5f, 11f, 8f, 21f, 8f, 21f, 19f, 3f, 19f])],
        ["trash"] = [
            new(IconOpKind.Line, [4f, 7f, 20f, 7f]),
            new(IconOpKind.Line, [9f, 7f, 9f, 4f, 15f, 4f, 15f, 7f]),
            new(IconOpKind.Line, [6f, 7f, 7f, 20f, 17f, 20f, 18f, 7f])
        ],
        ["pencil"] = [new(IconOpKind.Loop, [4f, 20f, 5f, 15.5f, 16f, 4.5f, 19.5f, 8f, 8.5f, 19f])],
        ["paperclip"] = [
            new(IconOpKind.Line, [15.5f, 7f, 15.5f, 16f]),
            new(IconOpKind.Arc, [12f, 16f, 3.5f, 0f, 180f]),
            new(IconOpKind.Line, [8.5f, 16f, 8.5f, 6f]),
            new(IconOpKind.Arc, [10.75f, 6f, 2.25f, 180f, 360f]),
            new(IconOpKind.Line, [13f, 6f, 13f, 15.5f])
        ],
        ["globe"] = [
            new(IconOpKind.Circle, [12f, 12f, 9f]),
            new(IconOpKind.Line, [3f, 12f, 21f, 12f]),
            new(IconOpKind.Line, [12f, 3f, 15f, 7f, 16f, 12f, 15f, 17f, 12f, 21f]),
            new(IconOpKind.Line, [12f, 3f, 9f, 7f, 8f, 12f, 9f, 17f, 12f, 21f])
        ],
        ["terminal"] = [
            new(IconOpKind.Box, [3f, 4f, 18f, 16f, 2.5f]),
            new(IconOpKind.Line, [7f, 9f, 10f, 12f, 7f, 15f]),
            new(IconOpKind.Line, [12.5f, 15f, 16.5f, 15f])
        ],
        ["terminal.fill"] = [
            new(IconOpKind.Solid, [3f, 4f, 18f, 16f, 2.5f]),
            new(IconOpKind.Cut, [7f, 9f, 10f, 12f, 7f, 15f]),
            new(IconOpKind.Cut, [12.5f, 15f, 16.5f, 15f])
        ],
        ["command"] = [
            new(IconOpKind.Circle, [7f, 7f, 2.5f]),
            new(IconOpKind.Circle, [17f, 7f, 2.5f]),
            new(IconOpKind.Circle, [7f, 17f, 2.5f]),
            new(IconOpKind.Circle, [17f, 17f, 2.5f]),
            new(IconOpKind.Box, [9.5f, 9.5f, 5f, 5f, 0f])
        ],
        ["cpu"] = [
            new(IconOpKind.Box, [6f, 6f, 12f, 12f, 2f]),
            new(IconOpKind.Solid, [9.5f, 9.5f, 5f, 5f, 1f]),
            new(IconOpKind.Line, [9.5f, 3f, 9.5f, 6f]),
            new(IconOpKind.Line, [14.5f, 3f, 14.5f, 6f]),
            new(IconOpKind.Line, [9.5f, 18f, 9.5f, 21f]),
            new(IconOpKind.Line, [14.5f, 18f, 14.5f, 21f]),
            new(IconOpKind.Line, [3f, 9.5f, 6f, 9.5f]),
            new(IconOpKind.Line, [3f, 14.5f, 6f, 14.5f]),
            new(IconOpKind.Line, [18f, 9.5f, 21f, 9.5f]),
            new(IconOpKind.Line, [18f, 14.5f, 21f, 14.5f])
        ],
        ["brain"] = [
            new(IconOpKind.Circle, [8.5f, 9f, 4f]),
            new(IconOpKind.Circle, [15.5f, 9f, 4f]),
            new(IconOpKind.Circle, [8.5f, 15f, 4f]),
            new(IconOpKind.Circle, [15.5f, 15f, 4f])
        ],
        ["wrench.and.screwdriver"] = [
            new(IconOpKind.Line, [4.5f, 19.5f, 12.5f, 11.5f]),
            new(IconOpKind.Arc, [15.5f, 8.5f, 4f, 120f, 400f]),
            new(IconOpKind.Line, [19.5f, 19.5f, 13f, 13f]),
            new(IconOpKind.Line, [4f, 4f, 9f, 9f])
        ],
        ["exclamationmark.triangle"] = [
            new(IconOpKind.Loop, [12f, 3.5f, 21f, 19.5f, 3f, 19.5f]),
            new(IconOpKind.Line, [12f, 9.5f, 12f, 13.5f]),
            new(IconOpKind.Disc, [12f, 16.6f, 1.15f])
        ],
        ["questionmark.circle"] = [
            new(IconOpKind.Circle, [12f, 12f, 9f]),
            new(
            IconOpKind.Line,
            [9.3f, 9.6f, 9.9f, 8f, 12f, 7.2f, 14.1f, 8f, 14.6f, 9.8f, 12.9f, 11.4f, 12f, 12.6f, 12f, 13.4f]
        ),
            new(IconOpKind.Disc, [12f, 16.6f, 1.15f])
        ],
        ["network"] = [
            new(IconOpKind.Circle, [12f, 12f, 9f]),
            new(IconOpKind.Line, [3f, 12f, 21f, 12f]),
            new(IconOpKind.Line, [12f, 3f, 15f, 7f, 16f, 12f, 15f, 17f, 12f, 21f]),
            new(IconOpKind.Line, [12f, 3f, 9f, 7f, 8f, 12f, 9f, 17f, 12f, 21f])
        ],
        ["square.grid.2x2"] = [
            new(IconOpKind.Box, [3.5f, 3.5f, 7f, 7f, 1.5f]),
            new(IconOpKind.Box, [13.5f, 3.5f, 7f, 7f, 1.5f]),
            new(IconOpKind.Box, [3.5f, 13.5f, 7f, 7f, 1.5f]),
            new(IconOpKind.Box, [13.5f, 13.5f, 7f, 7f, 1.5f])
        ],
        ["square.grid.3x3"] = [
            new(IconOpKind.Solid, [3.5f, 3.5f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [9.75f, 3.5f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [16f, 3.5f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [3.5f, 9.75f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [9.75f, 9.75f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [16f, 9.75f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [3.5f, 16f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [9.75f, 16f, 4.5f, 4.5f, 1f]),
            new(IconOpKind.Solid, [16f, 16f, 4.5f, 4.5f, 1f])
        ],
        ["memorychip"] = [
            new(IconOpKind.Box, [3f, 6.5f, 18f, 11f, 2f]),
            new(IconOpKind.Line, [7f, 3f, 7f, 6.5f]),
            new(IconOpKind.Line, [12f, 3f, 12f, 6.5f]),
            new(IconOpKind.Line, [17f, 3f, 17f, 6.5f]),
            new(IconOpKind.Line, [7f, 17.5f, 7f, 21f]),
            new(IconOpKind.Line, [12f, 17.5f, 12f, 21f]),
            new(IconOpKind.Line, [17f, 17.5f, 17f, 21f])
        ],
        ["cube"] = [
            new(IconOpKind.Loop, [12f, 2.5f, 20.5f, 7f, 20.5f, 17f, 12f, 21.5f, 3.5f, 17f, 3.5f, 7f]),
            new(IconOpKind.Line, [3.5f, 7f, 12f, 11.5f, 20.5f, 7f]),
            new(IconOpKind.Line, [12f, 11.5f, 12f, 21.5f])
        ],
        ["internaldrive"] = [
            new(IconOpKind.Box, [2.5f, 7f, 19f, 10f, 2.5f]),
            new(IconOpKind.Disc, [17f, 12f, 1.2f]),
            new(IconOpKind.Line, [6f, 12f, 11f, 12f])
        ],
        ["desktopcomputer"] = [
            new(IconOpKind.Box, [2.5f, 3.5f, 19f, 13f, 2f]),
            new(IconOpKind.Line, [12f, 16.5f, 12f, 20.5f]),
            new(IconOpKind.Line, [8.5f, 20.5f, 15.5f, 20.5f])
        ],
        ["waveform.path.ecg"] = [new(IconOpKind.Line, [2f, 12f, 7f, 12f, 9f, 6f, 12f, 18f, 14.5f, 9f, 16f, 12f, 22f, 12f])],
        ["pause.fill"] = [new(IconOpKind.Solid, [6f, 4.5f, 4f, 15f, 1f]), new(IconOpKind.Solid, [14f, 4.5f, 4f, 15f, 1f])],
        ["gauge.with.dots.needle.33percent"] = [
            new(IconOpKind.Arc, [12f, 15f, 8.5f, 180f, 360f]),
            new(IconOpKind.Line, [12f, 15f, 8f, 10f]),
            new(IconOpKind.Disc, [12f, 15f, 1.4f])
        ],
        ["flame"] = [new(IconOpKind.Loop, [12f, 2.5f, 16.5f, 8f, 18f, 13f, 16.5f, 18.5f, 12f, 21.5f, 7.5f, 18.5f, 6f, 13f, 8f, 9f, 10f, 11f])],
        ["square.and.pencil"] = [new(
            IconOpKind.Line,
            [11f, 5f, 5.5f, 5f, 3.5f, 7f, 3.5f, 18.5f, 5.5f, 20.5f, 17f, 20.5f, 19f, 18.5f, 19f, 13f]
        ), new(IconOpKind.Line, [10f, 14f, 20f, 4f])],
        ["xmark.octagon"] = [
            new(
            IconOpKind.Loop,
            [8.3f, 3f, 15.7f, 3f, 21f, 8.3f, 21f, 15.7f, 15.7f, 21f, 8.3f, 21f, 3f, 15.7f, 3f, 8.3f]
        ),
            new(IconOpKind.Line, [9f, 9f, 15f, 15f]),
            new(IconOpKind.Line, [15f, 9f, 9f, 15f])
        ],
        ["info.circle"] = [
            new(IconOpKind.Circle, [12f, 12f, 9f]),
            new(IconOpKind.Line, [12f, 11f, 12f, 16.5f]),
            new(IconOpKind.Disc, [12f, 7.8f, 1.15f])
        ],
        ["ellipsis.circle"] = [
            new(IconOpKind.Circle, [12f, 12f, 9f]),
            new(IconOpKind.Disc, [8f, 12f, 1.2f]),
            new(IconOpKind.Disc, [12f, 12f, 1.2f]),
            new(IconOpKind.Disc, [16f, 12f, 1.2f])
        ],
        ["hand.raised"] = [
            new(IconOpKind.Line, [8f, 13f, 8f, 6f]),
            new(IconOpKind.Line, [11f, 12f, 11f, 4f]),
            new(IconOpKind.Line, [14f, 12f, 14f, 4.5f]),
            new(IconOpKind.Line, [17f, 13f, 17f, 7.5f]),
            new(IconOpKind.Loop, [7f, 12f, 18f, 12f, 18f, 16f, 15f, 20.5f, 9.5f, 20.5f, 5f, 15f])
        ],
        ["list.bullet"] = [
            new(IconOpKind.Disc, [5f, 7f, 1.4f]),
            new(IconOpKind.Disc, [5f, 12f, 1.4f]),
            new(IconOpKind.Disc, [5f, 17f, 1.4f]),
            new(IconOpKind.Line, [9.5f, 7f, 20f, 7f]),
            new(IconOpKind.Line, [9.5f, 12f, 20f, 12f]),
            new(IconOpKind.Line, [9.5f, 17f, 20f, 17f])
        ],
        ["sun.max"] = [
            new(IconOpKind.Circle, [12f, 12f, 4f]),
            new(IconOpKind.Line, [12f, 2.5f, 12f, 4.5f]),
            new(IconOpKind.Line, [12f, 19.5f, 12f, 21.5f]),
            new(IconOpKind.Line, [2.5f, 12f, 4.5f, 12f]),
            new(IconOpKind.Line, [19.5f, 12f, 21.5f, 12f]),
            new(IconOpKind.Line, [5.3f, 5.3f, 6.7f, 6.7f]),
            new(IconOpKind.Line, [17.3f, 17.3f, 18.7f, 18.7f]),
            new(IconOpKind.Line, [5.3f, 18.7f, 6.7f, 17.3f]),
            new(IconOpKind.Line, [17.3f, 6.7f, 18.7f, 5.3f])
        ],
        ["moon"] = [new(IconOpKind.Arc, [12f, 12f, 8f, 300f, 240f]), new(IconOpKind.Arc, [16.5f, 7.5f, 6.5f, 125f, 205f])],
        ["circle.lefthalf.filled"] = [
            new(IconOpKind.Circle, [12f, 12f, 8f]),
            new(IconOpKind.Disc, [12f, 12f, 8f]),
            new(IconOpKind.Cut, [12f, 2f, 12f, 22f])
        ],
        ["arrow.up"] = [new(IconOpKind.Line, [12f, 19f, 12f, 5f]), new(IconOpKind.Line, [6f, 11f, 12f, 5f, 18f, 11f])],
        ["chart.bar.xaxis"] = [
            new(IconOpKind.Line, [3f, 20f, 21f, 20f]),
            new(IconOpKind.Line, [6.5f, 16.5f, 6.5f, 12f]),
            new(IconOpKind.Line, [12f, 16.5f, 12f, 5f]),
            new(IconOpKind.Line, [17.5f, 16.5f, 17.5f, 9f])
        ],
        ["checklist"] = [
            new(IconOpKind.Line, [3.5f, 7f, 5.5f, 9f, 9f, 5f]),
            new(IconOpKind.Line, [3.5f, 15f, 5.5f, 17f, 9f, 13f]),
            new(IconOpKind.Line, [12.5f, 7.5f, 20.5f, 7.5f]),
            new(IconOpKind.Line, [12.5f, 15.5f, 20.5f, 15.5f])
        ],
        ["equal.square"] = [
            new(IconOpKind.Box, [3f, 3f, 18f, 18f, 3.5f]),
            new(IconOpKind.Line, [8f, 10f, 16f, 10f]),
            new(IconOpKind.Line, [8f, 14f, 16f, 14f])
        ],
        ["plus.square"] = [
            new(IconOpKind.Box, [3f, 3f, 18f, 18f, 3.5f]),
            new(IconOpKind.Line, [12f, 8f, 12f, 16f]),
            new(IconOpKind.Line, [8f, 12f, 16f, 12f])
        ],
        ["xmark.square"] = [
            new(IconOpKind.Box, [3f, 3f, 18f, 18f, 3.5f]),
            new(IconOpKind.Line, [9f, 9f, 15f, 15f]),
            new(IconOpKind.Line, [15f, 9f, 9f, 15f])
        ],
        ["arrow.right.square"] = [
            new(IconOpKind.Box, [3f, 3f, 18f, 18f, 3.5f]),
            new(IconOpKind.Line, [7.5f, 12f, 16f, 12f]),
            new(IconOpKind.Line, [12.5f, 8.5f, 16f, 12f, 12.5f, 15.5f])
        ],
        ["rectangle.split.2x1"] = [new(IconOpKind.Box, [3f, 4f, 18f, 16f, 2.5f]), new(IconOpKind.Line, [12f, 4f, 12f, 20f])],
        ["rectangle.split.1x2"] = [new(IconOpKind.Box, [3f, 4f, 18f, 16f, 2.5f]), new(IconOpKind.Line, [3f, 12f, 21f, 12f])],
        ["rectangle.on.rectangle"] = [new(IconOpKind.Box, [7.5f, 7.5f, 13.5f, 11f, 2f]), new(IconOpKind.Line, [3.5f, 14f, 3.5f, 5f, 4.5f, 4f, 16.5f, 4f])],
        ["arrow.up.left.and.arrow.down.right"] = [
            new(IconOpKind.Line, [5f, 5f, 10.5f, 10.5f]),
            new(IconOpKind.Line, [5f, 10f, 5f, 5f, 10f, 5f]),
            new(IconOpKind.Line, [19f, 19f, 13.5f, 13.5f]),
            new(IconOpKind.Line, [19f, 14f, 19f, 19f, 14f, 19f])
        ],
        ["arrow.triangle.branch"] = [
            new(IconOpKind.Circle, [6.5f, 5.5f, 2.2f]),
            new(IconOpKind.Circle, [6.5f, 18.5f, 2.2f]),
            new(IconOpKind.Circle, [17.5f, 7.5f, 2.2f]),
            new(IconOpKind.Line, [6.5f, 7.7f, 6.5f, 16.3f]),
            new(IconOpKind.Line, [17.5f, 9.7f, 17.5f, 11f, 15.5f, 13f, 8.5f, 15f])
        ],
        ["slider.horizontal.3"] = [
            new(IconOpKind.Line, [3f, 6f, 21f, 6f]),
            new(IconOpKind.Line, [3f, 12f, 21f, 12f]),
            new(IconOpKind.Line, [3f, 18f, 21f, 18f]),
            new(IconOpKind.Disc, [8f, 6f, 2.4f]),
            new(IconOpKind.Disc, [15f, 12f, 2.4f]),
            new(IconOpKind.Disc, [10f, 18f, 2.4f])
        ],
        ["text.cursor"] = [
            new(IconOpKind.Line, [12f, 4.5f, 12f, 19.5f]),
            new(IconOpKind.Line, [9f, 4f, 15f, 4f]),
            new(IconOpKind.Line, [9f, 20f, 15f, 20f])
        ],
        ["textformat"] = [
            new(IconOpKind.Line, [3f, 19f, 8.5f, 5f, 14f, 19f]),
            new(IconOpKind.Line, [5f, 14f, 12f, 14f]),
            new(IconOpKind.Circle, [18f, 15.5f, 3f]),
            new(IconOpKind.Line, [21f, 12f, 21f, 19f])
        ],
        ["code"] = [new(IconOpKind.Line, [8f, 7f, 3f, 12f, 8f, 17f]), new(IconOpKind.Line, [16f, 7f, 21f, 12f, 16f, 17f])],
        ["clear"] = [
            new(IconOpKind.Loop, [21f, 5f, 9f, 5f, 3f, 12f, 9f, 19f, 21f, 19f]),
            new(IconOpKind.Line, [12f, 9f, 17f, 15f]),
            new(IconOpKind.Line, [17f, 9f, 12f, 15f])
        ],
    };
    static float[] Star(float x, float y, float outer, float inner, int count)
    {
        var points = new float[count * 4];
        for (int i = 0; i < count * 2; i++)
        {
            float a = -MathF.PI / 2 + MathF.PI * i / count, r = i % 2 == 0 ? outer : inner;
            points[i * 2] = x + r * MathF.Cos(a);
            points[i * 2 + 1] = y + r * MathF.Sin(a);
        }

        return points;
    }
}
