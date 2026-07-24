using System.Text;

namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: SaveCharacter/LoadCharacter INI format
/// (:38725–38935) — byte-compatible with existing MME .ini character
/// files so saves round-trip between programs.
///
/// [PlayerInfo]: Class/Race/Level/Alignment (numbers), Name,
/// Strength/Intellect/**Widsom**/Agility/Health/Charm — the "Widsom"
/// key typo is VB6's and is PRESERVED for compatibility — Quest0..11,
/// Quest_2nd/Quest_6th/Quest_Extra1/Quest_Extra2, Bless0..9.
/// [Inventory]: Head, Ears, Neck, Back, Torso, Arms, Wrist, Wrist2,
/// Hands, Finger1, Finger2, Waist, Legs, Feet, Worn, Off-Hand, Weapon,
/// Eyes, Face, Everywhere (cmbEquip slot order 0..19), and IM_CARRIED
/// as "number|qty," pairs (max 50).
/// Keys this port doesn't consume yet are preserved on load and written
/// back on save so VB6-authored files aren't stripped.
/// </summary>
public sealed class CharacterFile
{
    public static readonly string[] SlotKeys =
    [
        "Head", "Ears", "Neck", "Back", "Torso", "Arms", "Wrist", "Wrist2",
        "Hands", "Finger1", "Finger2", "Waist", "Legs", "Feet", "Worn",
        "Off-Hand", "Weapon", "Eyes", "Face", "Everywhere",
    ];

    public string Name = "";
    public long ClassNumber, RaceNumber, Alignment;
    public long Level = 1;
    public long Str, Int, Wis, Agi, Hea, Chm;
    public long[] Equipped = new long[20];
    public long[] Bless = new long[10];
    public long[] LearnedSpells = new long[100]; // LearnedSpell0..99
    public bool[] Quests = new bool[12];
    public int Quest2nd, Quest6th, QuestExtra1, QuestExtra2;
    public List<(long Number, long Qty)> Carried = [];
    /// <summary>Unrecognized keys kept verbatim per section.</summary>
    public Dictionary<string, List<(string Key, string Value)>> Extras = [];

    public static CharacterFile Load(string path)
    {
        var c = new CharacterFile();
        string section = "";
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 1) continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
            if (!Apply(c, section, key, value))
            {
                if (!c.Extras.TryGetValue(section, out var list))
                    c.Extras[section] = list = [];
                list.Add((key, value));
            }
        }
        return c;
    }

    private static bool Apply(CharacterFile c, string section, string key, string value)
    {
        long V() => long.TryParse(value, out var v) ? v : 0;
        if (section.Equals("PlayerInfo", StringComparison.OrdinalIgnoreCase))
        {
            switch (key)
            {
                case "Name": c.Name = value; return true;
                case "Class": c.ClassNumber = V(); return true;
                case "Race": c.RaceNumber = V(); return true;
                case "Level": c.Level = V(); return true;
                case "Alignment": c.Alignment = V(); return true;
                case "Strength": c.Str = V(); return true;
                case "Intellect": c.Int = V(); return true;
                case "Widsom": c.Wis = V(); return true; // VB6 typo, canonical
                case "Wisdom": c.Wis = V(); return true; // tolerate the fix
                case "Agility": c.Agi = V(); return true;
                case "Health": c.Hea = V(); return true;
                case "Charm": c.Chm = V(); return true;
                case "Quest_2nd": c.Quest2nd = (int)V(); return true;
                case "Quest_6th": c.Quest6th = (int)V(); return true;
                case "Quest_Extra1": c.QuestExtra1 = (int)V(); return true;
                case "Quest_Extra2": c.QuestExtra2 = (int)V(); return true;
            }
            if (key.StartsWith("Quest", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(key[5..], out int q) && q is >= 0 and <= 11)
            { c.Quests[q] = V() == 1; return true; }
            if (key.StartsWith("Bless", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(key[5..], out int b) && b is >= 0 and <= 9)
            { c.Bless[b] = V(); return true; }
            if (key.StartsWith("LearnedSpell", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key["LearnedSpell".Length..], out int ls)
                && ls is >= 0 and <= 99)
            { c.LearnedSpells[ls] = V(); return true; }
            return false;
        }
        if (section.Equals("Inventory", StringComparison.OrdinalIgnoreCase))
        {
            int slot = Array.FindIndex(SlotKeys,
                k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (slot >= 0) { c.Equipped[slot] = V(); return true; }
            if (key.Equals("IM_CARRIED", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in value.Split(',',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('|');
                    if (parts.Length >= 1 && long.TryParse(parts[0], out long n) && n > 0)
                        c.Carried.Add((n,
                            parts.Length >= 2 && long.TryParse(parts[1], out long q) && q > 0
                                ? q : 1));
                }
                return true;
            }
            return false;
        }
        return false;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[PlayerInfo]");
        void W(string k, object v) => sb.AppendLine($"{k}={v}");
        W("Name", Name);
        W("Class", ClassNumber); W("Race", RaceNumber);
        W("Level", Level); W("Alignment", Alignment);
        W("Strength", Str); W("Intellect", Int);
        W("Widsom", Wis); // VB6 key, verbatim
        W("Agility", Agi); W("Health", Hea); W("Charm", Chm);
        for (int x = 0; x <= 11; x++) W("Quest" + x, Quests[x] ? 1 : 0);
        W("Quest_2nd", Quest2nd); W("Quest_6th", Quest6th);
        W("Quest_Extra1", QuestExtra1); W("Quest_Extra2", QuestExtra2);
        for (int x = 0; x <= 9; x++) W("Bless" + x, Bless[x]);
        for (int x = 0; x <= 99; x++) W("LearnedSpell" + x, LearnedSpells[x]);
        WriteExtras(sb, "PlayerInfo");
        sb.AppendLine("[Inventory]");
        for (int i = 0; i < SlotKeys.Length; i++) W(SlotKeys[i], Equipped[i]);
        W("IM_CARRIED", string.Join("",
            Carried.Take(50).Select(cq => $"{cq.Number}|{cq.Qty},")));
        WriteExtras(sb, "Inventory");
        foreach (var (section, list) in Extras)
        {
            if (section is "PlayerInfo" or "Inventory") continue;
            sb.AppendLine($"[{section}]");
            foreach (var (k, v) in list) sb.AppendLine($"{k}={v}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void WriteExtras(StringBuilder sb, string section)
    {
        if (!Extras.TryGetValue(section, out var list)) return;
        foreach (var (k, v) in list) sb.AppendLine($"{k}={v}");
    }
}
