using System.Data;
using System.Data.OleDb;
using System.Runtime.Versioning;
using Mme.Data;

namespace Mme.App;

/// <summary>
/// Windows-only IMdbTableReader over Microsoft's Access Database Engine
/// (ACE OLEDB). Thin adapter: all conversion semantics live in
/// MdbImportService. Throws AceNotInstalledException when no ACE/Jet
/// provider is registered so the UI can offer the download link.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AceMdbTableReader : IMdbTableReader
{
    private readonly OleDbConnection _con;

    public AceMdbTableReader(string mdbPath)
    {
        Exception? last = null;
        foreach (var provider in new[]
                 { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0",
                   "Microsoft.Jet.OLEDB.4.0" })
        {
            try
            {
                var con = new OleDbConnection(
                    $"Provider={provider};Data Source={mdbPath};Persist Security Info=False;Mode=Read;");
                con.Open();
                _con = con;
                return;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new AceNotInstalledException(last);
    }

    public IEnumerable<string> TableNames()
    {
        var schema = _con.GetSchema("Tables");
        foreach (DataRow row in schema.Rows)
        {
            if (!string.Equals(row["TABLE_TYPE"] as string, "TABLE",
                    StringComparison.OrdinalIgnoreCase)) continue;
            var name = (string)row["TABLE_NAME"];
            if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue;
            yield return name;
        }
    }

    public (IReadOnlyList<string> Columns, IEnumerable<object?[]> Rows)
        ReadTable(string table)
    {
        var cmd = _con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM [{table}]";
        var reader = cmd.ExecuteReader()!;
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName).ToList();
        return (columns, Iterate(reader, cmd));
    }

    private static IEnumerable<object?[]> Iterate(OleDbDataReader reader,
        OleDbCommand cmd)
    {
        using (cmd)
        using (reader)
        {
            var buf = new object[reader.FieldCount];
            while (reader.Read())
            {
                reader.GetValues(buf);
                yield return (object?[])buf.Clone();
            }
        }
    }

    public void Dispose() => _con.Dispose();
}

public sealed class AceNotInstalledException(Exception? inner) : Exception(
    "No Access Database Engine (ACE OLEDB) provider is installed.", inner);
