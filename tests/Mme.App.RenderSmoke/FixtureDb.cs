using System.IO;
using Microsoft.Data.Sqlite;

namespace Mme.App.RenderSmoke;

/// <summary>Minimal schema the app can open — enough rows for every
/// grid to bind. Mirrors the Wave12 test fixture.</summary>
public static class FixtureDb
{
    public static string Create()
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"mme-smoke-{Guid.NewGuid():N}.db");
        using var con = new SqliteConnection($"Data Source={path}");
        con.Open();
        using var c = con.CreateCommand();
        c.CommandText = """
            CREATE TABLE "Monsters" ("Number" INTEGER, "Name" TEXT);
            CREATE TABLE "Items" ("Number" INTEGER, "Name" TEXT);
            CREATE TABLE "Spells" ("Number" INTEGER, "Name" TEXT);
            """;
        try { c.ExecuteNonQuery(); } catch { }
        return path;
    }
}
