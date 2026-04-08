namespace ResolutionManager.Models;

public class DisplayResolution
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public int BitsPerPixel { get; set; }

    public override string ToString() => $"{Width} x {Height}  @  {RefreshRate} Hz";

    public override bool Equals(object? obj)
    {
        if (obj is DisplayResolution other)
            return Width == other.Width && Height == other.Height && RefreshRate == other.RefreshRate;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Width, Height, RefreshRate);
}
