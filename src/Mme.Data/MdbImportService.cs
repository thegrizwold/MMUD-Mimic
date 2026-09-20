using Microsoft.Data.Sqlite;

namespace Mme.Data;

/// <summary>
/// Native NMR .mdb open (Option A): import an Access database via a
/// table reader (ACE OLEDB on Windows) into a sibling SQLite cache,
/// mirroring the Jackcess converter's semantics exactly
/// (MME_REWRITE_STRATEGY §0.3 tooling):
/// - column names verbatim, including "Markup%" / "Att%-0" style;
/// - integers/longs → INTEGER;
/// - MONEY (Currency) → exact invariant decimal TEXT, never floating;
/// - booleans → VB6 −1 / 0;
/// - text/date/other → TEXT (dates invariant "yyyy-MM-dd HH:mm:ss");
/// - NULL passes through.
/// The reader is a seam so the conversion logic stays testable off
/// Windows; the OLEDB adapter itself is a thin Windows-only shim.
/// </summary>
public interface IMdbTableReader : IDisposable
{
    IEnumerable<string> TableNames();
    /// <summary>Column names + typed rows for one table. Values arrive as
    /// CLR types: integral, decimal (MONEY), bool, DateTime, string, or null.</summary>
    (IReadOnlyList<string> Columns, IEnumerable<object?[]> Rows) ReadTable(string table);
}

public static class MdbImportService
{
    /// <summary>Cache path for an .mdb: sibling file, same name, .db.</summary>
    public static string CachePathFor(string mdbPath) =>
        Path.ChangeExtension(mdbPath, ".db");

    /// <summary>True when the cache exists and is not older than the mdb.</summary>
    public static bool CacheIsFresh(string mdbPath) =>
        File.Exists(CachePathFor(mdbPath)) &&
        File.GetLastWriteTimeUtc(CachePathFor(mdbPath)) >=
            File.GetLastWriteTimeUtc(mdbPath);

    /// <summary>Convert all tables into the SQLite cache (overwrites).</summary>
    public static string Import(IMdbTableReader reader, string mdbPath)
    {
        string dbPath = CachePathFor(mdbPath);
        string tmp = dbPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        // Pooling=False: Microsoft.Data.Sqlite otherwise keeps the file
        // handle in its pool after Dispose, and the File.Move below fails
        // on Windows with "being used by another process".
        using (var con = new SqliteConnection($"Data Source={tmp};Pooling=False"))
        {
            con.Open();
            using (var pragma = con.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF;";
                pragma.ExecuteNonQuery();
            }
            foreach (var table in reader.TableNames())
            {
                var (columns, rows) = reader.ReadTable(table);
                using var tx = con.BeginTransaction();
                using (var create = con.CreateCommand())
                {
                    create.Transaction = tx;
                    create.CommandText =
                        $"CREATE TABLE \"{table}\" (" +
                        string.Join(", ", columns.Select(c => $"\"{c}\"")) + ")";
                    create.ExecuteNonQuery();
                }
                using var insert = con.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = $"INSERT INTO \"{table}\" VALUES (" +
                    string.Join(",", columns.Select((_, i) => $"$p{i}")) + ")";
                var pars = columns.Select((_, i) =>
                {
                    var pr = insert.CreateParameter();
                    pr.ParameterName = $"$p{i}";
                    insert.Parameters.Add(pr);
                    return pr;
                }).ToArray();
                foreach (var row in rows)
                {
                    for (int i = 0; i < pars.Length; i++)
                        pars[i].Value = Convert(row[i]);
                    insert.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }
        // Release any pooled handles on the cache path (e.g. it was opened
        // and closed earlier in this session) before replacing it.
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        File.Move(tmp, dbPath);
        return dbPath;
    }

    /// <summary>Jackcess-parity value mapping (see class doc).</summary>
    internal static object Convert(object? value) => value switch
    {
        null or DBNull => DBNull.Value,
        bool b => b ? -1L : 0L,                       // VB6 True = −1
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        float f => (double)f,
        double d => d,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long =>
            System.Convert.ToInt64(value),
        string s => s,
        _ => value.ToString() ?? (object)DBNull.Value,
    };
}
