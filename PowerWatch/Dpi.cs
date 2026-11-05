namespace PowerWatch;

/// <summary>
/// 布局一律按 96 DPI 的像素写，运行时乘上实际缩放比例。
/// 字体的字号是"点"，本身就随 DPI 变大；如果格子不跟着放大，文字就会压住下面的内容。
/// </summary>
internal static class Dpi
{
    public static readonly float F = Detect();

    private static float Detect()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96f;
        }
        catch { return 1f; }
    }

    public static int S(double v) => (int)Math.Round(v * F);
    public static Size Sz(double w, double h) => new(S(w), S(h));
    public static Point P(double x, double y) => new(S(x), S(y));
    public static Padding Pad(double l, double t, double r, double b) => new(S(l), S(t), S(r), S(b));
}
