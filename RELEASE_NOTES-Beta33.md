# MMUD-Mimic Beta 33 (0.33.0) — patch notes

Changes since Beta 32 (0.32.0). Test suite 938 → 1001, all green.

## House Style Combat Settings

Realms running the wccexcmd addon differ from stock MajorMUD in ways every
calculator assumed were fixed: everyone swings 6 times, Quick & Deadly can
start at a different swing count, and the crit soft-cap can move. Beta 33 adds
an opt-in overlay for those rules.

- New group on the **EQ tab** (below the Martial Arts calculator) and a
  checkable **Options → House Style Combat Settings** item.
- Fields: **Max Combat Swings**, **QnD Starts at [x] swings** (Quick & Deadly
  applies once a swing costs less than 1000 / x energy; stock 5 → under 200),
  **Crit soft-cap** (MMUD Explorer's 40 — crit chance above it counts 1/3) and
  the engine's **QnD number**: on stock the bonus cap (20), on GreaterMUD the
  divisor (50, or 40 with the "data version > 1.85" option). Defaults 5 / 5 /
  40 and 20 · 50 · 40 follow the loaded engine and reproduce it exactly.
- Edit the fields, press **Apply**: the EQ panel, attack line, Martial Arts
  calculator, monster damage tables and lair Exp/Hr all recompute. **Defaults**
  resets the fields. Turning the toggle off restores engine behaviour without
  losing your numbers. Saved in `settings.json`.
- Fix: the Options "GMUD data version > 1.85" flag now reaches the attack
  engine's jumpkick speed table and Quick & Deadly divisor (it previously only
  affected the EQ calculator).

## House martial arts (Palm Strike / Lightning Kick / Deathblow)

When the loaded database grants abilities **196 / 197 / 198** on the Mystic
class (class 15), three new arts appear everywhere Punch / Kick / JumpKick do:

- Martial-arts pickers on the Exp/Hr strip (now a dropdown) and in the Choose
  Attack dialog — **Ps**, **Lk**, **Db**.
- Extra columns on the EQ tab's Martial Arts calculator, plus a new **Round**
  row showing average damage @ swings for every art.
- Speeds 2200 / 2500 / 3500 (slowed 3000 / 3400 / 4500), multipliers
  ×1.90 / ×2.10 / ×4.00, Deathblow −50 accuracy; damage from the jumpkick
  skill arm, as the addon does it. A stock 1.11p database shows the Beta 32 UI
  unchanged.

## House quests

Beside Ice Sorceress / High Druid in Completed Quests:

- **High Sorcery** (Mage Test): +50 max mana, +15 mana regen, +30 SpDmg%.
- **PerStealth** rank 0–3: backstab damage × (100 + level + 125·rank) / 100.
- **Smash swings** 1–6: smashes per round (energy = 1000 / n, per-swing
  1.2× / 5× unchanged).

Saved in the character file; Beta 32 character files load as stock.

## Display and the Rooms tab

- **Options → UI Scale**: Auto / 100% / 110% / 125% / 150%. Every font and
  control scales together; Auto follows the window width (1080p 100%, 1440p
  about 135%, 4K 160%).
- **Map zoom**: the Rooms map now fits the space beside it by default; Zoom
  buttons Fit / 1× / 1.5× / 2×, or Ctrl + mouse wheel over the map.
- **Presets are buttons** instead of a dropdown, with a Save… button;
  right-click a preset you saved to remove it (the ten built-ins stay).
- **Saved locations**: five slots beside the map. Click one to jump;
  right-click to save the room shown on the map into it, or to clear it.
  Kept in `settings.json`.
- The Char tab's house-quest row (High Sorcery / PerStealth / Smash swings)
  wraps instead of clipping in the narrow column.

## Create MegaMUD DATs

- **Kai spells no longer land as "Bard-3".** The Spells.md type byte was read
  from a stock MegaMUD file: Priest 1–3 · Mage 4–6 · Druid 7–9 · Bard 10 ·
  **Mystic 11** (the tool wrote 13).
- From the same file: the **evil-in-combat** flag now follows the stock rule
  exactly (set when the spell has Damage / Damage(-MR) / DrainLife /
  EvilInCombat — 466 of 466 stock records), **Min/Max are written signed**
  (curses were losing their sign), and Targets-6 spells get the right target
  checkbox. A golden test rebuilds the stock realm onto the stock file byte
  for byte.
- **Full realm export support.** The tool reads either an MMUD Explorer export
  or a full realm export — the Nightmare Redux / MugenMUD Editor `.mdb`
  converted with `tools/mdb2sqlite`. The window shows which it has, offers a
  "Full realm export (.db)…" picker and an **export checklist** (Monsters,
  Items, Races, Classes, Spells, Messages). Full exports also supply the Spell
  Type byte.
- **Spells.md and Messages require the full export.** On an MMUD Explorer
  export they are greyed out and the tool says: *"To create Spells and Messages
  it requires a full realm export from Nightmare Redux or MugenMUD Editor —
  Contact your Sysop."* Monsters / Items / Races / Classes still build.
- **messages.md is now built** (it was copied from the donor untouched). One
  MegaMUD Game Message per spell — name, the effect line ("You feel lucky!"),
  the wear-off line ("The effects of bless wear off!") — overlaid on your
  existing file: your records are kept as they are, spells without a record
  are added, and timed spells whose record has no "Ends with" line receive the
  realm's wear-off text. This is the fix for MegaMUD's *"No matching game
  messages are defined to signal the end of this duration spell."*
  `Messages-preview.txt` beside the output lists the realm side for review.
  Generated records carry no effect flags — tune those in MegaMUD.

## Files

`CHANGED_FILES.txt` lists every changed or added file; `beta32-to-beta33.diff`
is the unified diff of the text sources against Beta 32. New files:
`src/Mme.Core/Formulas/HouseArts.cs`, `src/Mme.App.ViewModels/MainViewModel.HouseStyle.cs`,
`src/Mme.Data/MegaMudDataBuilder.Realm.cs`, `src/Mme.App.ViewModels/MainViewModel.MapSlots.cs`,
`tests/Mme.Core.Tests/Beta33Tests.cs`,
`tests/Mme.Core.Tests/Fixtures/` (stock Spells.md, stock messages.md, a 12-spell
NMR-schema sample database).
