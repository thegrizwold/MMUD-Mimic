using System.Globalization;
using System.Text;
using Mme.Core.Text;

// Mme.ParityHarness — dumps deterministic CSVs of Mme.Core outputs so they can be
// diffed against equivalent dumps generated from the VB6 side (strategy §8.2).
//
// Usage:  dotnet run --project src/Mme.ParityHarness [outputDir]
// Emits one CSV per function family. Add a new Dump* call per Phase 1 sub-phase.

string outDir = args.Length > 0 ? args[0] : "parity-dumps";
Directory.CreateDirectory(outDir);
var inv = CultureInfo.InvariantCulture;

DumpTextUtils();
DumpExpTables();
Console.WriteLine($"Parity dumps written to: {Path.GetFullPath(outDir)}");

void DumpExpTables()
{
    // Strategy §8.2: CalcExpNeeded(level 1..255 × engine) sweep.
    // VB6 side: dump the same grid from CalcExpNeeded_STOCK/_GMUD/_GMUD_1_8_5 and diff.
    var sb = new StringBuilder("table,level,stock,gmud,gmud185\n");
    foreach (int table in new[] { 100, 200, 290, 400, 600 })
        for (int lvl = 1; lvl <= 255; lvl++)
            sb.Append(table).Append(',').Append(lvl).Append(',')
              .Append(Mme.Core.Formulas.ExpTables.CalcExpNeededStock(lvl, table).ToString(inv)).Append(',')
              .Append(Mme.Core.Formulas.ExpTables.CalcExpNeededGmud(lvl, table).ToString("R", inv)).Append(',')
              .Append(Mme.Core.Formulas.ExpTables.CalcExpNeededGmud185(lvl, table).ToString("R", inv)).Append('\n');
    File.WriteAllText(Path.Combine(outDir, "expneeded.csv"), sb.ToString());
}

void DumpTextUtils()
{
    // --- Val ---
    var val = new StringBuilder("input,val\n");
    string[] valInputs =
    {
        "", "abc", "12abc", "  42  ", "+5", "-5", "-", ".", ".5", "-.5", "3.25",
        "1.2.3", "123%", " 1615 198th Street", "1 2\t3", "1e3", "1E-2", "2d2",
        "1e", "1e+", "4.809E+23", "&HFF", "&HFFFF", "&H8000", "&H10000",
        "&HFFFFFFFF", "&O17", "&H", "0.0000001", "999999999", "1234567890123",
    };
    foreach (var s in valInputs)
        val.Append(Csv(s)).Append(',').Append(VbRuntime.Val(s).ToString("R", inv)).Append('\n');
    File.WriteAllText(Path.Combine(outDir, "val.csv"), val.ToString());

    // --- PutCommas (both modes) ---
    var pc = new StringBuilder("input,shorten,result\n");
    string[] pcInputs =
    {
        "0", "1", "12", "123", "1234", "12345", "1234567", "-1234.56", "+1234",
        "1,234", "1 234 567", "", "  ", "2500000000000", "999999999999",
        "-1234567890123456", "1234567890123456", "1000000000000", ".5", "-.5",
    };
    foreach (var s in pcInputs)
    {
        pc.Append(Csv(s)).Append(",False,").Append(Csv(TextUtils.PutCommas(s))).Append('\n');
        pc.Append(Csv(s)).Append(",True,").Append(Csv(TextUtils.PutCommas(s, true))).Append('\n');
    }
    File.WriteAllText(Path.Combine(outDir, "putcommas.csv"), pc.ToString());

    // --- FormatWithCommas / FormatBigIntWithCommas ---
    var fw = new StringBuilder("input,formatWithCommas,formatBigInt\n");
    string[] fwInputs =
    {
        "0", "0.5", "1.5", "2.5", "-2.5", "1234.4", "1234.99", "-1234.99",
        "1234567", "4.809E+23", "1e-3", "007", "2.5e2", "-0.5", "9876543210",
    };
    foreach (var s in fwInputs)
    {
        string a;
        try { a = TextUtils.FormatWithCommas(decimal.Parse(s, NumberStyles.Float, inv)); }
        catch { a = TextUtils.FormatWithCommas(VbRuntime.Val(s)); }
        fw.Append(Csv(s)).Append(',').Append(Csv(a)).Append(',')
          .Append(Csv(TextUtils.FormatBigIntWithCommas(s))).Append('\n');
    }
    File.WriteAllText(Path.Combine(outDir, "formatcommas.csv"), fw.ToString());

    // --- Extract* ---
    var ex = new StringBuilder("input,extractNumbers\n");
    string[] exInputs =
    {
        "Level: 42", "abc-3.5x", "1-2", ".5", "a-b5", "nothing", "-", "10 20",
        "HP 123/456", "(-12)", "x9.9.9y",
    };
    foreach (var s in exInputs)
        ex.Append(Csv(s)).Append(',').Append(TextUtils.ExtractNumbersFromString(s).ToString("R", inv)).Append('\n');
    File.WriteAllText(Path.Combine(outDir, "extractnumbers.csv"), ex.ToString());

    // --- Rounding family ---
    var rd = new StringBuilder("input,roundUp,roundUpTo5,truncate2\n");
    double[] rdInputs = { 2.1, 2.0, -2.5, -3.0, 0.0001, 3.456, -3.456, 1.005, 11, -6, 0, 1, 15 };
    foreach (var d in rdInputs)
        rd.Append(d.ToString("R", inv)).Append(',')
          .Append(TextUtils.RoundUp(d).ToString("R", inv)).Append(',')
          .Append(TextUtils.RoundUpTo5((int)d).ToString(inv)).Append(',')
          .Append(TextUtils.Truncate(d).ToString("R", inv)).Append('\n');
    File.WriteAllText(Path.Combine(outDir, "rounding.csv"), rd.ToString());
}

static string Csv(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\t", "\\t").Replace("\n", "\\n") + "\"";
