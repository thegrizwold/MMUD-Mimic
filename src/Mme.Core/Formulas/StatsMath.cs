namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modMMudDatabase.bas :: RemoveOutliers / GetMedian / GetMedianAbsDev /
/// GetStdDev / QuickSort (read line-by-line, :392–562). Pure math pulled
/// forward into Mme.Core per repo convention. Feeds GetLairAveragesFromLocs.
///
/// PINS:
/// - RemoveOutliers: cutoff = 3·MAD around the median; MAD == 0 falls back to
///   SAMPLE standard deviation; if EVERY element is an outlier the array is
///   left untouched (VB6 comments out the would-be empty ReDim).
/// - GetMedian: copy + QuickSort; odd n → middle, even n → mean of the two
///   middles (integer \ division on indexes).
/// - GetStdDev: sample (n−1) denominator; n &lt; 2 → 0; squares via ^ (Pow).
/// - QuickSort: Hoare partition with middle pivot ((first+last)\2), in-place.
/// </summary>
public static class StatsMath
{
    /// <summary>VB6: RemoveOutliers(ByRef arrData() As Double).</summary>
    public static void RemoveOutliers(ref double[] arrData)
    {
        if (arrData.Length == 0) return; // ub < lb

        double med = GetMedian(arrData);

        double mad = GetMedianAbsDev(arrData, med);
        if (mad == 0)
            mad = GetStdDev(arrData); // fallback to SD
        double cutoff = 3 * mad;

        var tmp = new double[arrData.Length];
        int cnt = 0;
        for (int i = 0; i < arrData.Length; i++)
        {
            if (Math.Abs(arrData[i] - med) <= cutoff)
            {
                tmp[cnt] = arrData[i];
                cnt++;
            }
        }

        if (cnt > 0)
        {
            Array.Resize(ref tmp, cnt);
            arrData = tmp;
        }
        // else: all outliers → do not touch (PIN)
    }

    /// <summary>VB6: GetMedian (copies + sorts; never mutates input).</summary>
    public static double GetMedian(double[] vals)
    {
        var tmp = (double[])vals.Clone();
        if (tmp.Length > 0) QuickSort(tmp, 0, tmp.Length - 1);

        int n = tmp.Length;
        if (n < 1) return 0;

        if (n % 2 == 1)
            return tmp[n / 2];
        return (tmp[n / 2 - 1] + tmp[n / 2]) / 2;
    }

    /// <summary>VB6: GetMedianAbsDev — median of |x − median|.</summary>
    public static double GetMedianAbsDev(double[] vals, double medianValue)
    {
        var devs = new double[vals.Length];
        for (int i = 0; i < vals.Length; i++)
            devs[i] = Math.Abs(vals[i] - medianValue);
        return GetMedian(devs);
    }

    /// <summary>VB6: GetStdDev — SAMPLE stddev (n−1); n &lt; 2 → 0.</summary>
    public static double GetStdDev(double[] vals)
    {
        int n = vals.Length;
        if (n < 2) return 0;

        double sum = 0;
        for (int i = 0; i < n; i++) sum += vals[i];
        double mean = sum / n;

        double sumsq = 0;
        for (int i = 0; i < n; i++)
            sumsq += Math.Pow(vals[i] - mean, 2); // VB6 ^ operator
        return Math.Sqrt(sumsq / (n - 1));
    }

    /// <summary>
    /// VB6: modMain.bas :: CalcAverageNonZero — mean of the non-zero
    /// elements; all-zero (or empty) → 0. Lives here with the stats stack it
    /// pairs with (RemoveOutliers → CalcAverageNonZero in
    /// GetLairAveragesFromLocs).
    /// </summary>
    public static double CalcAverageNonZero(double[] arrData)
    {
        double sum = 0;
        long cnt = 0;
        for (int i = 0; i < arrData.Length; i++)
        {
            if (arrData[i] != 0)
            {
                sum += arrData[i];
                cnt++;
            }
        }
        return cnt > 0 ? sum / cnt : 0;
    }

    /// <summary>VB6: QuickSort — in-place, middle pivot, Hoare partition.</summary>
    public static void QuickSort(double[] a, int first, int last)
    {
        int i = first, j = last;
        double pivot = a[(first + last) / 2];

        while (i <= j)
        {
            while (a[i] < pivot) i++;
            while (a[j] > pivot) j--;
            if (i <= j)
            {
                (a[i], a[j]) = (a[j], a[i]);
                i++;
                j--;
            }
        }
        if (first < j) QuickSort(a, first, j);
        if (i < last) QuickSort(a, i, last);
    }
}
