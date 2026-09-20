using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6 v2.3.4: frmPasteChar.frm :: ParsePasteParty (:2089–2720) +
/// CalculateAverageParty (:2722–2951) + the cmdContinue_Click apply block
/// (:1991–2085). The Exp/Hr "Paste Party" flow: up to six characters' stat
/// + inventory outputs pasted one after another → per-member AC/DR/MR/HP,
/// derived dodge / resting rates / accuracy / anti-magic, optional per-member
/// attack estimates (weapon, MA or spell), and the party averages that feed
/// the Exp/Hr party boxes.
///
/// Includes the two 2.3.4 FIXES: (1) the Name/Race/Class regex alternative
/// stops at the next "Label:" so a class like "Missionary" / "Witchunter"
/// followed by "Level:" on the same line is recognised; (2) every stat keeps
/// its own slot counter which is synced up to the character index on each
/// "Name:", and inventory blocks are keyed off the "Name:" count, so a member
/// missing Mana/Spellcasting/Encumbrance lines (or no inventory) no longer
/// shifts later members' values into the wrong slot.
///
/// The VB6 form's textboxes are modelled as nullable numbers on
/// <see cref="PartyMember"/> (null ⇔ Len(Trim(txt)) = 0) so the averaging
/// rules are identical. UI-only pieces (InputBox per member, MsgBoxes,
/// txtText handling) are the window's job.
/// </summary>
public sealed class PartyPasteService(MmeDatabase db, IGameEngineRules rules,
    double nmrVer, bool onlyInGame = false, bool use2ndWrist = true)
{
    public const int MaxMembers = 6;

    /// <summary>One column of the party screen (1-based Index like VB6).</summary>
    public sealed class PartyMember
    {
        public int Index;                       // 1..6
        public string Name = "";
        public string ClassName = "";
        public string RaceName = "";
        public long ClassNumber, RaceNumber;
        public short Level, Str, Int, Wil, Agi, Hea, Cha, Spellcasting, MaxMana;
        public long Hp, EncCur, EncMax;
        public short? Ac, Dr, Mr;               // txtPastePartyAC/DR/MR
        public long? HitPoints;                 // txtPastePartyHitpoints
        public long? Dodge, RegenHp, RestHp;    // txtPastePartyDodge/RegenHP/RestHP
        public long? Heals;                     // txtPastePartyHeals (user entry)
        public long? Accuracy;                  // txtPastePartyACCY
        public long? Damage, SpellDamage;       // txtPastePartyDMG / SpellDMG
        public double? Swings;                  // txtPastePartySwings
        public bool AntiMagic;                  // chkPastePartyAM
        public bool AttackedLast;               // optPastyPartyAtkLast

        // equipment resolution (sEquipLoc / sWorn + the item scan)
        public string[] EquipNames = new string[20];
        public string[] WornNames = new string[2];
        public long WeaponNumber, WeaponSpeed, WeaponStr;
        public double AccyWorn;
        public short AccyAbil, PlusDodge, PlusRegen;
        public CharacterProfile Profile = new();

        /// <summary>User's attack code for this member ("a", "aa", "bs",
        /// "jk", a 4-letter spell short…); empty = skip.</summary>
        public string AttackText = "";
        /// <summary>Set by <see cref="PartyPasteService.CalculateAttack"/>.</summary>
        public string AttackNote = "";

        /// <summary>CalculateAverageParty's party-size test: any box filled.</summary>
        public bool HasAnyData => AntiMagic || Ac is not null || Dr is not null
            || Mr is not null || Dodge is not null || HitPoints is not null
            || RestHp is not null || RegenHp is not null || Heals is not null
            || Swings is not null || Accuracy is not null
            || SpellDamage is not null || Damage is not null;

        /// <summary>"Name - 85 Warrior" (the InputBox caption).</summary>
        public string Caption(MmeDatabase db)
        {
            string s = Name.Trim().Length > 0 ? Name.Trim() : $"Character {Index}";
            if (Profile.Level > 0) s += $" - {Profile.Level}";
            if (Profile.Class > 0) s += $" {db.GetClassName(Profile.Class)}";
            return s;
        }

        /// <summary>Wants an attack prompt (VB6: weapon found or spellcasting &gt; 0).</summary>
        public bool CanAttack => WeaponNumber > 0 || Profile.Spellcasting > 0;
    }

    /// <summary>The averages row (index 0 of every VB6 control array).</summary>
    public sealed class PartyAverages
    {
        public long? Ac, Dr, Mr, Dodge, HitPoints, RegenHp, RestHp, Heals, Damage,
            Accuracy, SpellDamage;
        public double? Swings;
        public int AntiMagicCount;
        public int PartySize;
    }

    public sealed class ParseResult
    {
        public List<PartyMember> Members { get; } = [];
        public bool AnyData => Members.Count > 0;
    }

    // ------------------------------------------------------------------
    // ParsePasteParty
    // ------------------------------------------------------------------

    /// <summary>The 2.3.4 regex (frmPasteChar :2174).</summary>
    public const string StatRegex =
        @"(?:(Armour Class|Hits|Mana|Encumbrance):\s*\*?\s*(-?\d+)\/(\d+)|(MagicRes|Level|Spellcasting|Strength|Agility|Willpower|Charm|Intellect|Health):\s*\*?\s*(\d+)|(Name|Race|Class):\s*([^\s:]+(?:\s(?![^\s:]*:)[^\s:]+)?))";

    public ParseResult Parse(string pastedText)
    {
        var res = new ParseResult();
        if (pastedText is null || pastedText.Length < 10) return res;

        var matches = RegexUtils.RegexFindV2(pastedText, StatRegex,
            matchCase: false, multiLine: true, allowEmptySubMatches: false);
        if (matches.Length == 1 && matches[0].FullMatch.Length == 0) return res;

        // one slot array per field, index 0 = count (VB6 nXxx(0) counters)
        var name = new string[7]; var cls = new string[7]; var race = new string[7];
        int cName = 0, cClass = 0, cRace = 0, cMr = 0, cLevel = 0, cAgi = 0, cStr = 0,
            cInt = 0, cHea = 0, cCha = 0, cWil = 0, cSc = 0, cHp = 0, cMana = 0,
            cAc = 0, cDr = 0, cEncCur = 0, cEncMax = 0;
        var mr = new short[7]; var level = new short[7]; var agi = new short[7];
        var str = new short[7]; var intel = new short[7]; var hea = new short[7];
        var cha = new short[7]; var wil = new short[7]; var sc = new short[7];
        var hp = new long[7]; var mana = new short[7]; var ac = new short[7];
        var dr = new short[7]; var encCur = new long[7]; var encMax = new long[7];

        static short S16(string s) => VbRuntime.CInt(VbRuntime.Val(s));
        static long L64(string s) => VbRuntime.CLng(VbRuntime.Val(s));

        foreach (var m in matches)
        {
            if (m.SubMatches.Length == 0 || m.SubMatches.Length == 1 && m.SubMatches[0].Length == 0)
                continue; // UBound = 0 → skip_match
            string label = m.SubMatches[0];
            string v1 = m.SubMatches.Length > 1 ? m.SubMatches[1].Trim() : "";
            string v2 = m.SubMatches.Length > 2 ? m.SubMatches[2].Trim() : "";
            switch (label)
            {
                case "Name":
                    if (cName >= 6) break;
                    name[++cName] = v1;
                    // 2.3.4: bring every counter up to the current character index
                    int sync = cName - 1;
                    if (cClass < sync) cClass = sync;
                    if (cRace < sync) cRace = sync;
                    if (cMr < sync) cMr = sync;
                    if (cLevel < sync) cLevel = sync;
                    if (cAgi < sync) cAgi = sync;
                    if (cStr < sync) cStr = sync;
                    if (cInt < sync) cInt = sync;
                    if (cHea < sync) cHea = sync;
                    if (cCha < sync) cCha = sync;
                    if (cWil < sync) cWil = sync;
                    if (cSc < sync) cSc = sync;
                    if (cHp < sync) cHp = sync;
                    if (cMana < sync) cMana = sync;
                    if (cAc < sync) cAc = sync;
                    if (cDr < sync) cDr = sync;
                    if (cEncCur < sync) cEncCur = sync;
                    if (cEncMax < sync) cEncMax = sync;
                    break;
                case "Class": if (cClass < 6) cls[++cClass] = v1; break;
                case "Race": if (cRace < 6) race[++cRace] = v1; break;
                case "MagicRes": if (cMr < 6) mr[++cMr] = S16(v1); break;
                case "Level": if (cLevel < 6) level[++cLevel] = S16(v1); break;
                case "Agility": if (cAgi < 6) agi[++cAgi] = S16(v1); break;
                case "Strength": if (cStr < 6) str[++cStr] = S16(v1); break;
                case "Intellect": if (cInt < 6) intel[++cInt] = S16(v1); break;
                case "Health": if (cHea < 6) hea[++cHea] = S16(v1); break;
                case "Charm": if (cCha < 6) cha[++cCha] = S16(v1); break;
                case "Willpower": if (cWil < 6) wil[++cWil] = S16(v1); break;
                case "Spellcasting": if (cSc < 6) sc[++cSc] = S16(v1); break;
                case "Hits": if (cHp < 6) hp[++cHp] = L64(v2); break;            // max hits
                case "Mana": if (cMana < 6) mana[++cMana] = S16(v2); break;      // max mana
                case "Armour Class":
                    if (cAc >= 6 || cDr >= 6) break;
                    ac[++cAc] = S16(v1); dr[++cDr] = S16(v2);
                    break;
                case "Encumbrance":
                    if (cEncCur >= 6 || cEncMax >= 6) break;
                    encCur[++cEncCur] = L64(v1); encMax[++cEncMax] = L64(v2);
                    break;
            }
        }

        // ---- equipment scanner (adapted from frmMain PasteCharacter) --------
        var equipLoc = new string[7, 20];
        var worn = new string[7, 2];
        var text = new System.Text.StringBuilder();
        int openIdx = -1, iChar = 0, nameCount = 0;
        void Clear() { text.Clear(); openIdx = -1; }
        foreach (char raw in pastedText)
        {
            if (!TestPasteChar(raw)) continue;
            if (raw != ' ') text.Append(raw);
            string t = text.ToString();
            if (t.Contains("name:", StringComparison.OrdinalIgnoreCase))
            {
                // 2.3.4: a new stat block starts here — the inventory that
                // follows belongs to this character (block index, not a count
                // of inventory blocks)
                nameCount++;
                Clear(); continue;
            }
            if (t.Contains("equippedwith:", StringComparison.OrdinalIgnoreCase)
                || t.Contains("arecarrying", StringComparison.OrdinalIgnoreCase))
            {
                iChar = nameCount > 0 ? nameCount : iChar + 1;
                Clear(); continue;
            }
            switch (raw)
            {
                case ',': Clear(); break;
                case '(': openIdx = text.Length; break;
                case ')':
                    if (openIdx == -1) { Clear(); break; }
                    if (iChar >= 1 && iChar <= 6)
                    {
                        string keyword = text.ToString(openIdx, text.Length - openIdx - 1)
                            .ToUpperInvariant();
                        string item = text.ToString(0, openIdx - 1);
                        switch (keyword)
                        {
                            case "HEAD": equipLoc[iChar, 0] = item; break;
                            case "EARS": equipLoc[iChar, 1] = item; break;
                            case "EYES": equipLoc[iChar, 17] = item; break;
                            case "FACE": equipLoc[iChar, 18] = item; break;
                            case "NECK": equipLoc[iChar, 2] = item; break;
                            case "BACK": equipLoc[iChar, 3] = item; break;
                            case "TORSO": equipLoc[iChar, 4] = item; break;
                            case "ARMS": equipLoc[iChar, 5] = item; break;
                            case "WRIST":
                                if (!string.IsNullOrEmpty(equipLoc[iChar, 6]))
                                { if (string.IsNullOrEmpty(equipLoc[iChar, 7])) equipLoc[iChar, 7] = item; }
                                else equipLoc[iChar, 6] = item;
                                break;
                            case "WAIST": equipLoc[iChar, 11] = item; break;
                            case "FINGER":
                                if (!string.IsNullOrEmpty(equipLoc[iChar, 9]))
                                { if (string.IsNullOrEmpty(equipLoc[iChar, 10])) equipLoc[iChar, 10] = item; }
                                else equipLoc[iChar, 9] = item;
                                break;
                            case "HANDS": equipLoc[iChar, 8] = item; break;
                            case "LEGS": equipLoc[iChar, 12] = item; break;
                            case "FEET": equipLoc[iChar, 13] = item; break;
                            case "WORN":
                                if (!string.IsNullOrEmpty(worn[iChar, 0]))
                                { if (string.IsNullOrEmpty(worn[iChar, 1])) worn[iChar, 1] = item; }
                                else worn[iChar, 0] = item;
                                break;
                            case "OFF-HAND": equipLoc[iChar, 15] = item; break;
                            case "WEAPONHAND": case "TWOHANDED": equipLoc[iChar, 16] = item; break;
                        }
                    }
                    Clear();
                    break;
            }
        }

        // ---- members --------------------------------------------------------
        // VB6 calcit runs every slot 1..6; a slot that only saw a Race/MR/…
        // still gets its class/race anti-magic and counts toward party size
        int count = new[] { cName, cClass, cRace, cLevel, cAc, cDr, cMr, cHp, cMana,
            cSc, cAgi, cStr, cInt, cHea, cCha, cWil, cEncCur, cEncMax }.Max();
        if (count > 6) count = 6;
        for (int i = 1; i <= count; i++)
        {
            var pm = new PartyMember { Index = i };
            if (cName >= i) pm.Name = name[i] ?? "";
            if (cClass >= i) pm.ClassName = cls[i] ?? "";
            if (cRace >= i) pm.RaceName = race[i] ?? "";
            if (cAc >= i) pm.Ac = ac[i];
            if (cDr >= i) pm.Dr = dr[i];
            if (cMr >= i) pm.Mr = mr[i];
            if (cHp >= i) { pm.HitPoints = hp[i]; pm.Hp = hp[i]; }
            pm.Level = level[i]; pm.Agi = agi[i]; pm.Str = str[i]; pm.Int = intel[i];
            pm.Hea = hea[i]; pm.Cha = cha[i]; pm.Wil = wil[i]; pm.Spellcasting = sc[i];
            pm.MaxMana = mana[i]; pm.EncCur = encCur[i]; pm.EncMax = encMax[i];
            for (int x = 0; x < 20; x++) pm.EquipNames[x] = equipLoc[i, x] ?? "";
            pm.WornNames[0] = worn[i, 0] ?? ""; pm.WornNames[1] = worn[i, 1] ?? "";
            res.Members.Add(pm);
        }

        ResolveEquipment(res.Members);
        foreach (var pm in res.Members) Derive(pm);
        return res;
    }

    // TestPasteChar (modMMudFunc :3219) — the full accepted set incl. ' " . ` and
    // digits, so "cat's-eye pendant (Neck)" still matches the DB name
    private static bool TestPasteChar(char c) => MudParse.TestPasteChar(c.ToString());

    private sealed record ItemRow(long Number, string Stripped, long Worn, long InGame,
        long Speed, long StrReq, long Accy, short[] Abil, long[] AbilVal);

    private List<ItemRow>? _items;
    private List<ItemRow> Items => _items ??= LoadItems();

    private List<ItemRow> LoadItems()
    {
        var rows = new List<ItemRow>();
        using var cmd = db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"Worn\",\"In Game\",\"Speed\",\"StrReq\",\"Accy\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\"");
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var abil = new short[20]; var val = new long[20];
            for (int x = 0; x <= 19; x++)
            {
                abil[x] = Convert.ToInt16(r[7 + x * 2]);
                val[x] = Convert.ToInt64(r[8 + x * 2]);
            }
            rows.Add(new ItemRow(Convert.ToInt64(r[0]),
                (r[1] as string ?? "").Replace(" ", ""),
                Convert.ToInt64(r[2]), r[3] is DBNull ? 1 : Convert.ToInt64(r[3]),
                Convert.ToInt64(r[4]), Convert.ToInt64(r[5]), Convert.ToInt64(r[6]),
                abil, val));
        }
        return rows;
    }

    /// <summary>The tabItems scan (:2434–2493): resolve each member's worn
    /// item names, pick up the weapon, sum worn Accy, and accumulate the
    /// per-ability profile fields.
    /// QUIRK PIN (VB6 :2464–2478): the ability Select Case uses the
    /// lblInvenCharStat SLOT numbers 34–42 as if they were ability numbers
    /// (7 crit, 11 maxdmg, 13/14/15 BS, 37/40/34 punch skill/accy/dmg,
    /// 38/41/35 kick, 39/42/36 jumpkick, 19 stealth) — so real ability 34
    /// (Dodge) lands in punch damage and the later "Case 34: nPlusDodge"
    /// is unreachable, 37 (Picklocks) lands in punch skill, etc. Kept
    /// verbatim for parity; flagged upstream.</summary>
    private void ResolveEquipment(List<PartyMember> members)
    {
        bool any = members.Any(m => m.WornNames.Any(w => w.Length > 0)
            || m.EquipNames.Any(e => e.Length > 0));
        if (!any) return;

        foreach (var it in Items)
        {
            if (onlyInGame && it.InGame == 0) continue;
            if (it.Stripped.Trim().Length == 0) continue;
            foreach (var pm in members)
            {
                for (int x = 0; x <= 19; x++)
                {
                    if ((x == 14 || x == 19)
                        && (Eq(it.Stripped, pm.WornNames[0]) || Eq(it.Stripped, pm.WornNames[1])))
                    {
                        if (it.Worn == 1) pm.EquipNames[19] = it.Stripped;
                        else if (it.Worn == 16) pm.EquipNames[14] = it.Stripped;
                    }
                    if (!Eq(it.Stripped, pm.EquipNames[x])) continue;
                    if (x == 7 && !use2ndWrist) goto nextItem; // VB6 GoTo skip: (next item)
                    if (x == 16)
                    {
                        pm.WeaponNumber = it.Number;
                        pm.WeaponSpeed = it.Speed;
                        pm.WeaponStr = it.StrReq;
                    }
                    pm.AccyWorn += it.Accy;
                    var p = pm.Profile;
                    for (int y = 0; y <= 19; y++)
                    {
                        if (it.Abil[y] <= 0 || it.AbilVal[y] == 0) continue;
                        short v = checked((short)it.AbilVal[y]);
                        switch (it.Abil[y])
                        {
                            case 7: p.Crit += v; break;
                            case 11: p.PlusMaxDamage += v; break;
                            case 13: p.PlusBsAccy += v; break;
                            case 14: p.PlusBsMinDmg += v; break;
                            case 15: p.PlusBsMaxDmg += v; break;
                            case 37: p.MaPlusSkill[1] += v; break;
                            case 40: p.MaPlusAccy[1] += v; break;
                            case 34: p.MaPlusDmg[1] += v; break; // PIN: shadows the dodge case
                            case 38: p.MaPlusSkill[2] += v; break;
                            case 41: p.MaPlusAccy[2] += v; break;
                            case 35: p.MaPlusDmg[2] += v; break;
                            case 39: p.MaPlusSkill[3] += v; break;
                            case 42: p.MaPlusAccy[3] += v; break;
                            case 36: p.MaPlusDmg[3] += v; break;
                            case 19: p.Stealth += v; break;
                            case 145: p.ManaRegen += v; break;
                            case 123: pm.PlusRegen += v; break;
                            case 22: if (v > pm.AccyAbil) pm.AccyAbil = v; break;
                        }
                    }
                }
            }
        nextItem:;
        }
    }

    private static bool Eq(string a, string b) =>
        b.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The "calcit" block (:2496–2568): class/race resolution,
    /// class/race anti-magic + dodge + regen abilities, CalcDodge,
    /// CalcRestingRate ×2, EncumPCT, CalculateAccuracy, profile stats.</summary>
    private void Derive(PartyMember pm)
    {
        var p = pm.Profile;
        if (pm.ClassName.Length > 0)
        {
            var c = db.GetClassList().FirstOrDefault(x => x.Name == pm.ClassName);
            if (c is not null) { pm.ClassNumber = c.Number; p.Class = c.Number; }
        }
        if (pm.RaceName.Length > 0)
        {
            var r = db.GetRaceList().FirstOrDefault(x => x.Name == pm.RaceName);
            if (r is not null) { pm.RaceNumber = r.Number; p.Race = r.Number; }
        }
        if (pm.ClassNumber > 0)
        {
            if (db.GetClassAbilityValue(pm.ClassNumber, 51) >= 0) pm.AntiMagic = true;
            int t = db.GetClassAbilityValue(pm.ClassNumber, 34);
            if (t != MmeDatabase.AbilityNotFound) pm.PlusDodge += checked((short)t);
            t = db.GetClassAbilityValue(pm.ClassNumber, 123);
            if (t != MmeDatabase.AbilityNotFound) pm.PlusRegen += checked((short)t);
        }
        if (pm.RaceNumber > 0)
        {
            if (db.GetRaceAbilityValue(pm.RaceNumber, 51) >= 0) pm.AntiMagic = true;
            int t = db.GetRaceAbilityValue(pm.RaceNumber, 34);
            if (t != MmeDatabase.AbilityNotFound) pm.PlusDodge += checked((short)t);
            t = db.GetRaceAbilityValue(pm.RaceNumber, 123);
            if (t != MmeDatabase.AbilityNotFound) pm.PlusRegen += checked((short)t);
        }
        if (pm.Level > 0 && pm.Agi > 0 && pm.Cha > 0)
        {
            pm.Dodge = CharacterMath.CalcDodge(pm.Level, pm.Agi, pm.Cha, pm.PlusDodge,
                pm.EncCur, pm.EncMax);
            p.Dodge = checked((short)pm.Dodge.Value);
        }
        if (pm.Level > 0 && pm.Hea > 0)
        {
            pm.RegenHp = CharacterMath.CalcRestingRate(rules, pm.Level, pm.Hea, pm.PlusRegen, false);
            pm.RestHp = CharacterMath.CalcRestingRate(rules, pm.Level, pm.Hea, pm.PlusRegen, true);
            p.HpRegen = pm.RegenHp.Value;
        }
        if (pm.EncMax > 0)
            p.EncumPct = VbRuntime.CInt((double)pm.EncCur / pm.EncMax * 100);
        if (p.EncumPct > 100) p.EncumPct = 100;
        if (pm.ClassNumber > 0 && pm.Level > 0)
        {
            pm.Accuracy = CombatMath.CalculateAccuracy(rules, checked((short)pm.ClassNumber),
                pm.Level, pm.Str, pm.Agi, pm.Int, pm.Cha, VbRuntime.CInt(pm.AccyWorn),
                pm.AccyAbil, p.EncumPct, AttackTypeMud.None, db.GetClassCombat(pm.ClassNumber));
            p.Accuracy = pm.Accuracy.Value;
        }
        p.Hp = pm.Hp;
        p.Level = pm.Level; p.Str = pm.Str; p.Int = pm.Int; p.Agi = pm.Agi;
        p.Cha = pm.Cha; p.Wis = pm.Wil; p.Hea = pm.Hea;
        p.Spellcasting = pm.Spellcasting; p.MaxMana = pm.MaxMana;
    }

    // ------------------------------------------------------------------
    // per-member attack (the InputBox loop :2595–2711)
    // ------------------------------------------------------------------

    /// <summary>Map the InputBox text to an attack: physical
    /// (a/aa/aaa/bs/pu/kick/jk) or a 4-letter spell short. Returns false for
    /// "skip" inputs (VB6 GoTo next_char).</summary>
    public static bool ParseAttackText(string text, out AttackTypeMud attack, out string spellShort)
    {
        attack = AttackTypeMud.None; spellShort = "";
        // NOTE: VB6 compares Binary ("A" → skip, "BASH" → 4-letter spell short);
        // the port accepts the attack codes case-insensitively on purpose
        string s = (text ?? "").Trim();
        switch (s.ToLowerInvariant())
        {
            case "": case "0": return false;
            case "a": case "at": case "att": case "attack": attack = AttackTypeMud.Normal; return true;
            case "aa": case "ba": case "bash": attack = AttackTypeMud.Bash; return true;
            case "aaa": case "sm": case "smash": attack = AttackTypeMud.Smash; return true;
            case "p": case "pu": case "punch": attack = AttackTypeMud.Punch; return true;
            case "k": case "ki": case "kick": attack = AttackTypeMud.Kick; return true;
            case "j": case "jk": case "jumpk": case "jumpkick": attack = AttackTypeMud.Jumpkick; return true;
            case "bs": case "backstab": attack = AttackTypeMud.Surprise; return true;
            default:
                if (s.Length == 4) { spellShort = s; return true; }
                return false;
        }
    }

    /// <summary>Runs the member's attack estimate and fills Damage/Swings
    /// or SpellDamage. Returns a user-facing note ("No weapon detected." …)
    /// or "" when the attack was computed.</summary>
    public string CalculateAttack(PartyMember pm, SpellUsabilityService spells)
    {
        pm.AttackNote = "";
        if (!ParseAttackText(pm.AttackText, out var attack, out string spellShort))
            return "";
        var p = pm.Profile;

        if ((attack is AttackTypeMud.Bash or AttackTypeMud.Smash) && pm.WeaponNumber == 0)
            return pm.AttackNote = "No weapon detected.";
        if ((attack == AttackTypeMud.Punch && p.MaPlusSkill[1] == 0)
            || (attack == AttackTypeMud.Kick && p.MaPlusSkill[2] == 0)
            || (attack == AttackTypeMud.Jumpkick && p.MaPlusSkill[3] == 0))
            return pm.AttackNote = "Proper MA skill not detected.";

        long spellNum = 0;
        if (spellShort.Length == 4)
        {
            if (p.Spellcasting == 0) return pm.AttackNote = "Spellcast rating not detected.";
            spellNum = GetSpellByShort(spellShort, pm.ClassNumber, spells);
            if (spellNum <= 0) return pm.AttackNote = $"No learnable spell with short \"{spellShort}\".";
        }

        if (attack == AttackTypeMud.None && spellNum > 0)
        {
            if (p.Class > 0)
            {
                var (_, _, magery, mageryLvl) = db.GetClassHitDice(p.Class);
                p.ManaRegen = (double)CharacterMath.CalcManaRegen(rules, p.Level, p.Int, p.Wis,
                    p.Cha, mageryLvl, (MagicType)magery, (long)p.ManaRegen);
            }
            var spell = db.GetSpellRecord(spellNum);
            var cast = SpellMath.CalculateSpellCast(rules, p, spell, p.Level);
            if (cast.AvgRoundDmg > 0) pm.SpellDamage = cast.AvgRoundDmg;
        }
        else if (attack != AttackTypeMud.None)
        {
            // (VB6 nCombat is procedure-scoped and only set when nClass > 0, so a
            // class-less member inherited the previous member's combat — the port
            // starts each member at 0, a deliberate fix)
            short combat = 0;
            if (p.Class > 0) { combat = db.GetClassCombat(p.Class); p.Combat = combat; }

            decimal energy;
            if (attack is AttackTypeMud.Surprise or AttackTypeMud.Smash) energy = 1000m;
            else
                energy = CombatMath.CalcEnergyUsed(combat, pm.Level, pm.WeaponSpeed, pm.Agi,
                    pm.Str, p.EncumPct, pm.WeaponStr, 0m,
                    isBackstab: attack == AttackTypeMud.Surprise);
            p.Crit = checked((short)(p.Crit + rules.QuickAndDeadlyBonus(pm.Agi, energy, p.EncumPct)));

            if (attack is AttackTypeMud.Bash or AttackTypeMud.Smash)
            {
                pm.Accuracy = CombatMath.CalculateAccuracy(rules, checked((short)p.Class),
                    checked((short)p.Level), p.Str, p.Agi, p.Int, p.Cha,
                    VbRuntime.CInt(pm.AccyWorn), pm.AccyAbil, p.EncumPct, attack, combat);
                p.Accuracy = pm.Accuracy.Value;
            }

            var weapon = pm.WeaponNumber > 0 ? db.GetWeaponRecord(pm.WeaponNumber) : null;
            var dmg = AttackMath.CalculateAttack(rules, p, attack, pm.WeaponNumber, weapon,
                classCombat: combat,
                classStealthFromClass: db.GetClassStealth(p.Class),
                raceStealthFromRace: db.GetRaceStealth(p.Race));
            if (dmg.RoundTotal > 0)
            {
                pm.Damage = dmg.RoundTotal;
                pm.Swings = dmg.Swings;
            }
        }
        return "";
    }

    /// <summary>modMMudDatabase GetSpellByShort: first spell whose Short
    /// matches, usable by the class (SpellIsUsable(…, True) when a class is
    /// known).</summary>
    public long GetSpellByShort(string shortName, long classNumber, SpellUsabilityService spells)
    {
        shortName = shortName.Trim();
        if (shortName.Length == 0) return 0;
        foreach (var (number, shortCode) in db.GetSpellShorts())
        {
            if (!shortCode.Trim().Equals(shortName, StringComparison.Ordinal)) continue;
            if (classNumber > 0 && !spells.SpellIsUsable(number, classNumber,
                    andLearnable: true, onlyInGame: onlyInGame))
                continue;
            return number;
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // CalculateAverageParty (:2722)
    // ------------------------------------------------------------------

    /// <summary>The averages row. Members whose box is empty (null) are
    /// skipped per field; the "attacked last" member counts twice for
    /// AC/DR/MR/Dodge/RegenHP (its value is added again and the divisor is
    /// +1); heals SUM; swings default to 1 for a member with damage but no
    /// swings; VB6 Round = banker's.</summary>
    public static PartyAverages Average(IReadOnlyList<PartyMember> members)
    {
        var a = new PartyAverages();
        bool atkLast = members.Any(m => m.AttackedLast);
        int extra = atkLast ? 1 : 0;

        long? Avg(Func<PartyMember, long?> f, bool doubleLast)
        {
            long total = 0; int count = 0;
            foreach (var m in members)
            {
                long? v = f(m);
                if (v is null) continue;
                total += v.Value + (doubleLast && m.AttackedLast ? v.Value : 0);
                count++;
            }
            if (count == 0) return null;
            return (long)VbRuntime.Round((double)total / (count + (doubleLast ? extra : 0)));
        }

        a.Ac = Avg(m => m.Ac, true);
        a.Dr = Avg(m => m.Dr, true);
        a.Mr = Avg(m => m.Mr, true);
        a.Dodge = Avg(m => m.Dodge, true);
        a.HitPoints = Avg(m => m.HitPoints, false);
        a.RegenHp = Avg(m => m.RegenHp, true);
        a.RestHp = Avg(m => m.RestHp, false);
        long heals = members.Where(m => m.Heals is not null).Sum(m => m.Heals!.Value);
        if (heals > 0) a.Heals = heals;
        a.Damage = Avg(m => m.Damage, false);
        a.Accuracy = Avg(m => m.Accuracy, false);
        a.AntiMagicCount = members.Count(m => m.AntiMagic);
        a.SpellDamage = Avg(m => m.SpellDamage, false);

        // PIN: VB6 nTotal is a Long — every `nTotal = nTotal + val(swings)`
        // banker's-rounds the running sum (1.6667 + 1.6667 → 2 → 4), then
        // Round(nTotal / nCount, 1)
        long swTotal = 0; int swCount = 0;
        foreach (var m in members)
        {
            if (m.Swings is not null && m.Damage is not null)
            { swTotal = (long)VbRuntime.Round(swTotal + m.Swings.Value); swCount++; }
            else if (m.Damage is not null) { swTotal += 1; swCount++; }
        }
        if (swCount > 0) a.Swings = VbRuntime.Round((double)swTotal / swCount, 1);

        a.PartySize = members.Count(m => m.HasAnyData);
        return a;
    }

    // ------------------------------------------------------------------
    // cmdContinue_Click apply (:2003–2076)
    // ------------------------------------------------------------------

    /// <summary>What the Continue button writes to the Exp/Hr tab. Null =
    /// field left untouched (VB6 only writes non-empty boxes).</summary>
    public sealed class ApplyPlan
    {
        public int PartySize;
        public long? Ac, Dr, Mr, Dodge, HitPoints, AntiMagicCount, RestHp, Accuracy;
        public double? Swings;
        public long? DamageOut, SpellDamageOut;
        public bool ZeroSpellDamageOut, ZeroDamageOut;
        /// <summary>Round(regen/6)+heals (NMR ≥ 1.83) or Round(regen/2)+Round(rest/3).</summary>
        public long Healing;
        /// <summary>Healing was computed from regen OR heals but not both — the
        /// OG asks before overwriting the HEALS/[DMG&lt;=] box.</summary>
        public bool HealingNeedsConfirm;
        public bool HasHealing => Healing > 0 && (RegenGiven || HealsGiven);
        public bool RegenGiven, HealsGiven;
        /// <summary>frmMain.txtMonsterDamage at plan time (for the prompt).</summary>
        public double CurrentThreshold;
    }

    public ApplyPlan BuildApplyPlan(PartyAverages a, double currentMonsterDamage)
    {
        var plan = new ApplyPlan { PartySize = a.PartySize, CurrentThreshold = currentMonsterDamage };
        plan.RegenGiven = a.RegenHp is not null;
        plan.HealsGiven = a.Heals is not null;
        if (plan.RegenGiven || plan.HealsGiven)
        {
            long regen = a.RegenHp ?? 0, rest = a.RestHp ?? 0, heals = a.Heals ?? 0;
            if (nmrVer < 1.83 && !plan.HealsGiven)
                plan.Healing = (long)VbRuntime.Round(regen / 2.0) + (long)VbRuntime.Round(rest / 3.0);
            else
                plan.Healing = (long)VbRuntime.Round(regen / 6.0) + heals;
            plan.HealingNeedsConfirm = plan.Healing > 0 && currentMonsterDamage != 99999
                && (plan.RegenGiven != plan.HealsGiven) && currentMonsterDamage != plan.Healing;
        }
        plan.Ac = a.Ac; plan.Dr = a.Dr; plan.Mr = a.Mr; plan.Dodge = a.Dodge;
        plan.HitPoints = a.HitPoints; plan.AntiMagicCount = a.AntiMagicCount;
        plan.RestHp = a.RestHp; plan.Accuracy = a.Accuracy; plan.Swings = a.Swings;
        if (a.Damage is not null)
        {
            plan.DamageOut = a.Damage;
            plan.ZeroSpellDamageOut = a.SpellDamage is null; // when the box holds ≥ 9999
        }
        if (a.SpellDamage is not null)
        {
            plan.SpellDamageOut = a.SpellDamage;
            plan.ZeroDamageOut = a.Damage is null;
        }
        return plan;
    }
}
