using System.Numerics;
using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct Instance
{
    public Vector4 Rect, Color, BorderColor, UV, Clip;
    public float Radius, BorderWidth, Kind, ClipRound;
    /// <summary>
    /// Turns the clipped instance by <c>X</c> radians, clockwise on screen, about the point (<c>Y</c>, <c>Z</c>).
    /// Clipping happens before the turn, so instances sharing a turn rotate together with their clips. Zero is
    /// upright. Glass panes ignore it.
    /// </summary>
    public Vector4 Turn;
    /// <summary>Instances a reusable frame buffer may keep without ever shrinking: about 0.9 MB.</summary>
    internal const int RetainedFloor = 8192;

    /// <summary>
    /// Capacity a reusable frame buffer should have to draw <paramref name="count"/> instances. It grows by powers of
    /// two, and after a spike frame it shrinks once a frame uses under a quarter of it, so one huge frame does not
    /// keep its memory for the life of the window.
    /// </summary>
    internal static int Retained(int capacity, int count)
    {
        if (count > capacity)
            return Math.Max(256, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count));
        if (capacity > RetainedFloor && count < capacity / 4)
            return Math.Max(RetainedFloor, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, count)));
        return capacity;
    }

    public static Instance Quad(UI.Rect rect, UI.Color color, UI.Rect clip, float radius = 0) => new()
    {
        Rect = new(rect.X, rect.Y, rect.Width, rect.Height),
        Color = color.Vector,
        Clip = new(clip.X, clip.Y, clip.Right, clip.Bottom),
        Radius = radius
    };
}
