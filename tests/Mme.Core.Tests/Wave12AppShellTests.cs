using Microsoft.Data.Sqlite;
using Mme.App.ViewModels;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 2 first cut: MmeDatabase (SQLite gateway) + MainViewModel (app shell
// logic). Fixture database mirrors the converter's schema (verbatim Access
// column names, MONEY as exact decimal TEXT). A guarded smoke test runs
// against the real converted 1.11p database when present.
// ---------------------------------------------------------------------------

public class AppShellTests : IDisposable
{
    private readonly string _fixturePath;

    public AppShellTests()
    {
        _fixturePath = Path.Combine(Path.GetTempPath(),
            $"mme-fixture-{Guid.NewGuid():N}.db");
        using var con = new SqliteConnection($"Data Source={_fixturePath}");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE "Monsters" ("Number" INTEGER, "Name" TEXT, "HP" INTEGER,
                "EXP" REAL, "ArmourClass" INTEGER, "DamageResist" INTEGER,
                "MagicRes" INTEGER, "AvgDmg" TEXT, "HPRegen" INTEGER,
                "RegenTime" REAL, "GameLimit" INTEGER, "ExpMulti" REAL,
                "Summoned By" TEXT);
            CREATE TABLE "Items" ("Number" INTEGER, "Name" TEXT, "ItemType" INTEGER,
                "Min" INTEGER, "Max" INTEGER, "Speed" INTEGER, "ArmourClass" INTEGER,
                "DamageResist" INTEGER, "Accy" INTEGER, "StrReq" INTEGER,
                "Encum" INTEGER, "Limit" INTEGER,
                "Abil-0" INTEGER DEFAULT 0,"AbilVal-0" INTEGER DEFAULT 0,"Abil-1" INTEGER DEFAULT 0,"AbilVal-1" INTEGER DEFAULT 0,"Abil-2" INTEGER DEFAULT 0,"AbilVal-2" INTEGER DEFAULT 0,"Abil-3" INTEGER DEFAULT 0,"AbilVal-3" INTEGER DEFAULT 0,"Abil-4" INTEGER DEFAULT 0,"AbilVal-4" INTEGER DEFAULT 0,"Abil-5" INTEGER DEFAULT 0,"AbilVal-5" INTEGER DEFAULT 0,"Abil-6" INTEGER DEFAULT 0,"AbilVal-6" INTEGER DEFAULT 0,"Abil-7" INTEGER DEFAULT 0,"AbilVal-7" INTEGER DEFAULT 0,"Abil-8" INTEGER DEFAULT 0,"AbilVal-8" INTEGER DEFAULT 0,"Abil-9" INTEGER DEFAULT 0,"AbilVal-9" INTEGER DEFAULT 0,"Abil-10" INTEGER DEFAULT 0,"AbilVal-10" INTEGER DEFAULT 0,"Abil-11" INTEGER DEFAULT 0,"AbilVal-11" INTEGER DEFAULT 0,"Abil-12" INTEGER DEFAULT 0,"AbilVal-12" INTEGER DEFAULT 0,"Abil-13" INTEGER DEFAULT 0,"AbilVal-13" INTEGER DEFAULT 0,"Abil-14" INTEGER DEFAULT 0,"AbilVal-14" INTEGER DEFAULT 0,"Abil-15" INTEGER DEFAULT 0,"AbilVal-15" INTEGER DEFAULT 0,"Abil-16" INTEGER DEFAULT 0,"AbilVal-16" INTEGER DEFAULT 0,"Abil-17" INTEGER DEFAULT 0,"AbilVal-17" INTEGER DEFAULT 0,"Abil-18" INTEGER DEFAULT 0,"AbilVal-18" INTEGER DEFAULT 0,"Abil-19" INTEGER DEFAULT 0,"AbilVal-19" INTEGER DEFAULT 0);
            CREATE TABLE "Spells" ("Number" INTEGER, "Name" TEXT, "Short" TEXT,
                "ReqLevel" INTEGER, "ManaCost" INTEGER, "MinBase" INTEGER,
                "MaxBase" INTEGER, "Dur" INTEGER, "Magery" INTEGER,
                "MageryLVL" INTEGER, "Learnable" INTEGER DEFAULT 0,
                "Diff" INTEGER DEFAULT 0,
                "AttType" INTEGER DEFAULT 0, "Targets" INTEGER DEFAULT 0,
                "Classes" TEXT DEFAULT '',
                "Abil-0" INTEGER DEFAULT 0,"Abil-1" INTEGER DEFAULT 0,"Abil-2" INTEGER DEFAULT 0,"Abil-3" INTEGER DEFAULT 0,"Abil-4" INTEGER DEFAULT 0,"Abil-5" INTEGER DEFAULT 0,"Abil-6" INTEGER DEFAULT 0,"Abil-7" INTEGER DEFAULT 0,"Abil-8" INTEGER DEFAULT 0,"Abil-9" INTEGER DEFAULT 0);
            INSERT INTO "Monsters" VALUES
                (29,'dark cultist',50,50.0,20,4,0,'7.5',2,5.0,0,1.0,''),
                (30,'cave bear',120,180.0,35,8,0,'14.25',4,8.0,0,1.0,''),
                (1200,'crypt wight',400,2200.0,80,20,40,'33',10,12.0,3,1.5,'');
            INSERT INTO "Items" ("Number","Name","ItemType","Min","Max","Speed",
                "ArmourClass","DamageResist","Accy","StrReq","Encum","Limit") VALUES
                (100,'quarterstaff',1,2,7,15,0,0,5,0,45,0),
                (245,'iron breastplate',3,0,0,0,18,4,0,13,320,0);
            INSERT INTO "Spells" ("Number","Name","Short","ReqLevel","ManaCost",
                "MinBase","MaxBase","Dur","Magery","MageryLVL") VALUES
                (44,'minor heal','mihe',3,4,6,10,0,3,1),
                (210,'flame bolt','flbo',9,12,22,34,0,1,9);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_fixturePath); } catch { /* temp cleanup */ }
    }

    [Fact]
    public void Database_ReadsRows_MoneyTextParsesExactly()
    {
        using var db = MmeDatabase.Open(_fixturePath);
        Assert.True(db.Probe());

        var mons = db.GetMonsterGridRows();
        Assert.Equal(3, mons.Count);
        Assert.Equal("dark cultist", mons[0].Name);
        Assert.Equal(50L, mons[0].Hp);
        Assert.Equal(7.5, mons[0].AvgDmg);   // MONEY-as-TEXT parsed invariantly
        Assert.Equal(14.25, mons[1].AvgDmg);
        Assert.Equal(33.0, mons[2].AvgDmg);

        Assert.Equal(2, db.GetItemGridRows().Count);
        Assert.Equal("flame bolt", db.GetSpellGridRows()[1].Name);
    }

    [Fact]
    public void ViewModel_Open_Status_And_Counts()
    {
        using var vm = new MainViewModel();
        Assert.False(vm.IsLoaded);

        Assert.True(vm.OpenDatabase(_fixturePath));
        Assert.True(vm.IsLoaded);
        Assert.Equal(3, vm.Monsters.Count);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, vm.Spells.Count);
        Assert.Contains("3 monsters", vm.Status);
        Assert.Contains("2 items", vm.Status);
    }

    [Fact]
    public void ViewModel_Filter_NameContains_NumberExact_SpellShort()
    {
        using var vm = new MainViewModel();
        vm.OpenDatabase(_fixturePath);

        vm.FilterText = "cULt";               // case-insensitive contains
        Assert.Single(vm.Monsters);
        Assert.Equal("dark cultist", vm.Monsters[0].Name);

        vm.FilterText = "1200";               // pure number → exact Number match
        Assert.Single(vm.Monsters);
        Assert.Equal(1200L, vm.Monsters[0].Number);

        vm.FilterText = "flbo";               // spell Short matches too
        Assert.Single(vm.Spells);
        Assert.Equal(210L, vm.Spells[0].Number);

        vm.FilterText = "";                   // clear restores all
        Assert.Equal(3, vm.Monsters.Count);
    }

    [Fact]
    public void ViewModel_BadPaths_FailGracefully()
    {
        using var vm = new MainViewModel();
        Assert.False(vm.OpenDatabase("/nonexistent/nope.db"));
        Assert.Contains("not found", vm.Status);

        // a valid SQLite file that is NOT a converted mmud db
        string stray = Path.Combine(Path.GetTempPath(), $"stray-{Guid.NewGuid():N}.db");
        using (var con = new SqliteConnection($"Data Source={stray}"))
        {
            con.Open();
            using var c = con.CreateCommand();
            c.CommandText = "CREATE TABLE x (y INTEGER)";
            c.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        Assert.False(vm.OpenDatabase(stray));
        Assert.Contains("mdb2sqlite", vm.Status);
        Assert.False(vm.IsLoaded);
        try { File.Delete(stray); } catch { }
    }

    [Fact]
    public void RealDatabase_Smoke_WhenPresent()
    {
        // Runs against the converted stock 1.11p database when it exists
        // (dev sandbox); trivially passes elsewhere.
        const string real = "/home/claude/mme/current/mmud-1.11p.db";
        if (!File.Exists(real)) return;

        using var vm = new MainViewModel();
        Assert.True(vm.OpenDatabase(real));
        Assert.Equal(1101, vm.Monsters.Count);
        Assert.Equal(1950, vm.Items.Count);
        Assert.Equal(1379, vm.Spells.Count);

        vm.FilterText = "dark cultist";
        Assert.Contains(vm.Monsters, m => m.Number == 29 && m.Hp == 50);
    }
}
