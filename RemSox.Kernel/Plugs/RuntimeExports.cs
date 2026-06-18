using System.Runtime;

namespace RemSox.Kernel.Plugs;

public static partial class RuntimeExports
{
    [RuntimeExport("fmod")]
    public static double fmod(double x, double y)
    {
        if (Math.Abs(y) < double.Epsilon)
        {
            return double.NaN;
        }

        double q = Math.Truncate(x / y);
        return x - (q * y);
    }
}
