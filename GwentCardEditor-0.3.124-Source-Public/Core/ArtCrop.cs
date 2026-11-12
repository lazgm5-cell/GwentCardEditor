namespace GwentCardEditor;

public readonly record struct ArtCrop(double X, double Y, double Width, double Height)
{
    public const double Ratio = 128d / 178;

    public static ArtCrop Calculate(int width, int height, double zoom = 1, double centerX = .5, double centerY = .5, double ratio = Ratio)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(zoom) || zoom < 1 || zoom > 4
            || !double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(ratio) || ratio is < .1 or > 10) throw new ArgumentException("ArtCropInvalid");
        double w = Math.Min(width, height * ratio) / zoom, h = w / ratio;
        double x = Math.Clamp(centerX * width - w / 2, 0, Math.Max(0, width - w));
        double y = Math.Clamp(centerY * height - h / 2, 0, Math.Max(0, height - h));
        return new(x, y, w, h);
    }
}
