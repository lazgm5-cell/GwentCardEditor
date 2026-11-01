using GwentCardEditor;

internal static class ArtCropTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        foreach (var (width, height) in new[] { (4000,3000), (3000,4000), (128,178), (2048,512), (16,10000) })
        foreach (double zoom in new[] { 1d, 1.5, 4 })
        foreach (var (x,y) in new[] { (.5,.5), (-2d,3d), (1d,0d) })
        foreach (double ratio in new[] { ArtCrop.Ratio, 2d / 3 })
        {
            var crop = ArtCrop.Calculate(width, height, zoom, x, y, ratio);
            if (crop.X < -1e-8 || crop.Y < -1e-8 || crop.X + crop.Width > width + 1e-8
                || crop.Y + crop.Height > height + 1e-8 || crop.Width <= 0 || crop.Height <= 0
                || Math.Abs(crop.Width / crop.Height - ratio) > 1e-8)
                throw new Exception("Unsafe art crop bounds");
            passed.Add($"art crop bounded and ratio preserved {width}x{height}/{zoom}/{x}/{y}/{ratio}");
        }
        foreach (var input in new[] { (0,100,1d,.5), (100,0,1d,.5), (100,100,.5,.5), (100,100,5d,.5),
            (100,100,double.NaN,.5), (100,100,1d,double.PositiveInfinity) })
        {
            bool rejected = false;
            try { ArtCrop.Calculate(input.Item1, input.Item2, input.Item3, input.Item4); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid art crop accepted");
            passed.Add("invalid art crop rejected " + input);
        }
        foreach (double ratio in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, 11d })
        {
            bool rejected = false;
            try { ArtCrop.Calculate(100, 100, ratio: ratio); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid crop ratio accepted");
            passed.Add("invalid crop ratio rejected " + ratio);
        }
        return passed;
    }
}
