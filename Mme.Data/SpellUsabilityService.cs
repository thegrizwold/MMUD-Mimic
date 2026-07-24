namespace Mme.Data;

/// <summary>
/// VB6: modMMudFunc.bas :: SpellIsUsable (:2740). Gate order preserved:
/// class 0 → always usable; learnable gate (Learnable=0 AND Learned From
/// shorter than 5 AND not auto-learn Kai) when andLearnable; magery match
/// (class MageryType vs spell Magery, with the NMR≥1.7 learnable-magery-0
/// class-list escape); magery level; non-Kai unlearnable rejection; the
/// NMR≥1.7 "(n)" Classes membership check; ReqLevel; and the alignment
/// ability gates 97/98/112 + 110/111/113. Kai = magery 5.
/// Not ported: bDisableKaiAutolearn (option not yet surfaced) and the
/// GMUD abil-1107 no-auto-learn flag (commented out in VB6 anyway).
/// </summary>
public sealed class SpellUsabilityService(MmeDatabase db, bool greaterMud,
    double nmrVer = 1.83, bool disableKaiAutolearn = false)
{
    public sealed class SpellGate
    {
        public long Number;
        public long Learnable, Magery, MageryLvl, ReqLevel;
        public string LearnedFrom = "", Classes = "", Name = "", Short = "";
        public string CastedBy = "";
        public short[] Abil = new short[10];
    }

    private Dictionary<long, SpellGate>? _spells;
    private Dictionary<long, (int Magery, int MageryLvl)>? _classes;

    private void EnsureLoaded()
    {
        if (_spells is not null) return;
        _spells = [];
        using (var cmd = db.Connection.CreateCommand())
        {
            var sql = new System.Text.StringBuilder(
                "SELECT \"Number\",\"Learnable\",\"Magery\",\"MageryLVL\"," +
                "\"ReqLevel\",\"Learned From\",\"Classes\",\"Name\",\"Short\"," +
                "\"Casted By\"");
            for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\"");
            sql.Append(" FROM \"Spells\"");
            cmd.CommandText = sql.ToString();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var g = new SpellGate
                {
                    Number = Convert.ToInt64(r[0]),
                    Learnable = Convert.ToInt64(r[1]),
                    Magery = Convert.ToInt64(r[2]),
                    MageryLvl = Convert.ToInt64(r[3]),
                    ReqLevel = Convert.ToInt64(r[4]),
                    LearnedFrom = r[5] as string ?? "",
                    Classes = r[6] as string ?? "",
                    Name = (r[7] as string ?? "").Trim(),
                    Short = (r[8] as string ?? "").Trim(),
                    CastedBy = (r[9] as string ?? "").Replace("\0", "").Trim(),
                };
                for (int x = 0; x <= 9; x++)
                    g.Abil[x] = Convert.ToInt16(r[10 + x]);
                _spells[g.Number] = g;
            }
        }
        _classes = [];
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT "Number","MageryType","MageryLVL" FROM "Classes"
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                _classes[Convert.ToInt64(r[0])] =
                    (Convert.ToInt32(r[1]), Convert.ToInt32(r[2]));
        }
    }

    public SpellGate? GetGate(long spell)
    {
        EnsureLoaded();
        return _spells!.TryGetValue(spell, out var g) ? g : null;
    }

    public IEnumerable<SpellGate> AllSpells()
    {
        EnsureLoaded();
        return _spells!.Values.OrderBy(s => s.Number);
    }

    /// <summary>SpellIsInGame (:2712): NOT in game when Learnable=0 AND
    /// LearnedFrom ≤ 1 char AND CastedBy ≤ 1 char AND not a Kai auto-learn
    /// (Magery≠5 or Kai-with-ReqLevel&lt;1) — unless NMR≥1.8 and the spell
    /// has a Classes list. bDisableKaiAutolearn not surfaced (logged).</summary>
    public bool SpellIsInGame(long spell)
    {
        EnsureLoaded();
        if (!_spells!.TryGetValue(spell, out var sp)) return false;
        const int kai = 5;
        if (sp.Learnable == 0 && sp.LearnedFrom.Length <= 1
            && sp.CastedBy.Length <= 1
            && (sp.Magery != kai
                || (sp.Magery == kai && sp.ReqLevel < 1)
                || (sp.Magery == kai && disableKaiAutolearn)))
        {
            if (nmrVer >= 1.8)
            {
                if (sp.Classes.Length <= 1) return false;
            }
            else return false;
        }
        return true;
    }

    public bool SpellIsUsable(long spell, long classNumber, int level = 0,
        int charAlign = 0, bool andLearnable = false,
        bool onlyInGame = false)
    {
        if (spell < 1) return false;
        if (classNumber < 1) return true;
        if (level < 0) level = 0;
        if (charAlign < 0) charAlign = 0;
        EnsureLoaded();
        // VB6: bOnlyInGame swaps the plain seek for the in-game gate
        if (onlyInGame && !SpellIsInGame(spell)) return false;
        if (!_spells!.TryGetValue(spell, out var sp)) return false;

        const int kai = 5;

        if (andLearnable
            && sp.Learnable == 0 && sp.LearnedFrom.Length < 5
            && (sp.Magery != kai
                || (sp.Magery == kai
                    && (disableKaiAutolearn || sp.ReqLevel < 1))))
            return false;

        (int magery, int mageryLvl) = _classes!.TryGetValue(classNumber,
            out var c) ? c : (0, 0);

        if (sp.Magery != 0)
        {
            if (magery == 0) return false;
            if (magery != sp.Magery)
            {
                bool escape = sp.Learnable > 0 && sp.Magery == 0
                    && nmrVer >= 1.7
                    && (sp.Classes == "(*)" || sp.Classes.Contains(
                        $"({classNumber})", StringComparison.OrdinalIgnoreCase));
                if (!escape) return false;
            }
            else
            {
                if (mageryLvl > 0 && mageryLvl < sp.MageryLvl) return false;
                if (magery != kai && sp.Learnable == 0) return false;
                if (magery == kai && disableKaiAutolearn
                    && sp.Learnable == 0) return false;
            }
        }

        if (nmrVer >= 1.7 && classNumber > 0
            && sp.Classes.Length > 2 && sp.Classes != "(*)"
            && !sp.Classes.Contains($"({classNumber})",
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (level > 0 && level < sp.ReqLevel) return false;

        if (charAlign > 0 || (magery == kai && greaterMud))
        {
            for (int x = 0; x <= 9; x++)
            {
                switch (sp.Abil[x])
                {
                    case 97 or 98 or 112:
                        int isAlign = sp.Abil[x];
                        if (charAlign == 1 && isAlign != 97) return false;
                        if (charAlign == 2 && isAlign != 112) return false;
                        if (charAlign == 3 && isAlign != 98) return false;
                        break;
                    case 110 or 111 or 113:
                        int notAlign = sp.Abil[x];
                        if (charAlign == 1 && notAlign == 110) return false;
                        if (charAlign == 2 && notAlign == 113) return false;
                        if (charAlign == 3 && notAlign == 111) return false;
                        break;
                }
            }
        }
        return true;
    }
}
