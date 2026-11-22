namespace NexaDock.Core.Models;

/// <summary>Rectangle in physical (device) pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
