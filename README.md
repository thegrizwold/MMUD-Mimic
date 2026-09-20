# MMUD-Mimic — C#/.NET 8 rewrite of MMUD Explorer

A ground-up port of the VB6 MMUD Explorer to C#/.NET 8, built for
**bit-exact parity** with the original engine: every calculation was read
from the VB6 source line-by-line before porting, VB6's arithmetic quirks
(banker's rounding on CLng/CInt, Fix truncation, Currency semantics,
integer-division `\` behavior) are preserved via a small VbRuntime shim,
and every deliberate quirk is pinned in `docs/PARITY_LEDGER.md` with the
original line numbers.

## Building & running (Windows)

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
cd src\Mme.App
dotnet run
```

Or build a standalone executable:

```
dotnet publish src\Mme.App\Mme.App.csproj -c Release -r win-x64 --self-contained false
```

The WPF app targets `net8.0-windows`; everything else (`Mme.Core`,
`Mme.Data`, the tests) is cross-platform.

## Database

The app reads a **SQLite** conversion of the NMR `data-*.mdb` realm
database. Convert once with the bundled Jackcess tool (Java 11+):

```
java -jar tools\mdb2sqlite\mme-mdb2sqlite.jar data-v1.11p.mdb mmud-1.11p.db
```

(Dependency jars go in `tools\mdb2sqlite\lib\` — see
`tools\mdb2sqlite\README.md`.) Then File → Open Database in the app.
MONEY columns convert to exact decimal text; booleans keep VB6's −1/0.

## What's ported (all anchored by tests)

- **Combat engine** — CalculateAttack (weapon/MA/surprise/bash/smash),
  CalculateSpellCast, resist math, accuracy + backstab accuracy, both
  Stock and GreaterMUD rule sets behind `IGameEngineRules`.
- **Spell math** — GetSpellMin/MaxDamage (with the abil-151 chain-cast
  recursion and energy multi-cast rules), durations, SpellDoesDamage,
  GetCurrentSpellMinMax.
- **Damage orchestration** — GetDamageOutput with all eight attack modes,
  the per-monster damage cache, target restriction/immunity gates.
- **Character model** — PopulateCharacterProfile over an externalized
  UI-state DTO (character / party / generic-maximum branches).
- **Exp/Hr** — the full lair pipeline: LoadLairInfo, lair averaging
  (GetLairAveragesFromLocs), the A–D exp/hr models, RTK/RTC, recovery and
  movement text.
- **App** — WPF shell with Monsters/Items/Spells grids + filtering, the
  Lairs/Exp-per-Hour tab, a character sheet panel (the global filter), and
  an attack strip covering the full mode range.

## Beta 31 additions

- **Route Finder** (Rooms tab bar, or Tools → Route Finder, Ctrl+R): shortest
  walk between any two rooms honouring your character (level/class/race/
  alignment gates, keys you carry, picklocks) and traversal options. Each step
  says what to do (open, search, pay toll, use key, say the phrase, text
  command). When impossible it tells you **where and why** — the first
  blocking exit and reason — or, for unreachable areas, the spell/textblock
  teleports that lead in. Exports MegaMUD `.mp` paths.
- **Tools → Create MegaMUD DATs**: syncs `Spells.md` / `Monsters.md` /
  `Items.md` (+ Races/Classes names) to the open realm using your existing
  MegaMUD files as donors, then verifies every file by tree descent.
- **EQ tab → Slot lists: find by ability**: filter every slot to gear carrying
  one ability (SpDmg% / Speed / Quickness quick buttons), Find Best on
  "Spell Dmg %", "Speed", "Quickness", and a worn-gear totals readout.

## Beta 32 additions (MMUD Explorer v2.3.4 fork)

- Every v2.3.4 UP/FIX that has a surface in the port: shops-first reference
  option (Options menu, persisted), NPC greet/teleport commands in map room
  tooltips, AC/DR header click alternating AC-desc / DR-desc, weapon **Extra**
  column scaled by hit %, monster HP no longer lair-combined in lair mode,
  **BS Defense** column (NMR 1.83+), Find Best rewritten to the 2.3.4
  engine (per-item cache, SUM criteria, ring/bracelet hand-over, 2-hander vs
  off-hand) with the new VileWard / Attributes / All Elemental / Perception
  criteria, spell Difficulty shown for learnable Diff-0 spells, Item Manager
  −/+ quantity buttons with the flag " xN" suffix as quantity truth, spell
  immunity vs required level, lair-mode party no longer breaking spell
  filtering, pasted stats reduced by item bonuses, "Copy Name" on reference
  lines, taught spells in item references, 20-slot ability scans, ability
  1102 UseSpell. Full mapping in `PORT_LOG.md` (Session 50).
- **EQ tab quick filters are removable tags**: "+" pins the dropdown's
  ability as a tag, ✕ removes it, click toggles; the worn-gear totals follow
  the tag list. Saved in `settings.json` beside the exe, along with the
  Options-menu toggles.
- **Paste Party** (File → Paste Party, or the button beside the Exp/Hr Party
  box): paste the whole party's stat + inventory outputs; per-member AC/DR/MR/
  HP/dodge/regen/accuracy, optional per-member attack estimates, and the
  averages written to the Exp/Hr party boxes — the OG's frmPasteChar party
  mode with its 2.3.4 fixes. Also new: reference lists sort by % (shop rows
  show their regen %), a **Spell Atk.** column on the Monsters grid.
- **Signed releases**: `build/publish.ps1` and the `release` workflow publish
  one single-file exe (framework-dependent, ~5 MB, needs the .NET 8 Desktop
  Runtime; `-SelfContained` bundles it) and sign it with Azure Artifact Signing (see
  `docs/SIGNING.md` for why SmartScreen flagged the betas and the owner's
  Azure checklist).

## Beta 33 additions (house rules for wccexcmd realms)

- **House Style Combat Settings** (EQ tab group and Options menu): an opt-in
  overlay on the stock/GMUD rules for realms running the wccexcmd addon —
  **Max Combat Swings**, **QnD Starts at [x] swings** (Quick & Deadly applies
  under 1000/x energy; stock 5 → 200), **Crit soft-cap** (MME's 40, above
  which crits count 1/3) and the engine's **QnD number** — stock the bonus
  cap (20), GreaterMUD the divisor (50, or 40 with the data-version > 1.85
  option). Defaults follow the loaded engine and are byte-identical to it;
  edit and press **Apply** to
  recompute the EQ panel, attack line, MA calculator, monster damage and
  lair Exp/Hr. Saved in `settings.json`.
- **House martial arts**: when the loaded realm grants ability 196 / 197 /
  198 on the Mystic (class 15), **Palm Strike (Ps)**, **Lightning Kick (Lk)**
  and **Deathblow (Db)** join Punch/Kick/JumpKick in every martial-arts
  picker and as extra columns on the EQ tab's MA calculator, which also
  gained a **Round** row (average damage @ swings per art). Speeds,
  multipliers and Deathblow's −50 accuracy follow the addon (v69); a stock
  1.11p database shows the Beta 32 UI unchanged.
- **House quests** beside Ice Sorceress / High Druid: **High Sorcery** (Mage
  Test: +50 mana, +15 mana regen, +30 SpDmg%), **PerStealth** rank 0–3
  (backstab × (100 + level + 125·rank)/100) and **Smash swings** 1–6 (the a32
  ladder). Saved in the character file.
- GMUD's "data version > 1.85" option now also reaches the attack engine's
  jumpkick speed table (it previously only fed the EQ calculator).
- **Create MegaMUD DATs**: Kai spells now land as Mystic (the type byte is 11,
  read from a stock MegaMUD Spells.md — the old 13 showed as "Bard-3"); "evil
  in combat", signed Min/Max and the Targets-6 checkbox follow the stock file
  exactly. The tool now reads a **full realm export** (the Nightmare Redux /
  MugenMUD Editor .mdb converted with `tools/mdb2sqlite`) as well as an MMUD
  Explorer export, and has an export checklist. Spells.md and the spell game
  messages need the full export — on an MMUD Explorer export they are greyed
  out ("Contact your Sysop"); Monsters/Items/Races/Classes still build. From a
  full export the tool also builds **messages.md** (MegaMUD's Game Messages,
  a text file): your existing records are kept, spells without one get a
  record (spell name · effect line · wear-off line), and timed spells whose
  record lacks an "Ends with" line receive the realm's wear-off text — the
  fix for "No matching game messages are defined to signal the end of this
  duration spell". `Messages-preview.txt` lists the realm side for review.

## Test suite

```
dotnet test
```

986 tests. Anchors were derived from independent replicas of the VB6
math (not from the port itself); real-database tests are guarded on the
converted `mmud-1.11p.db` being present.

## Known remaining work

- **Party damage tables** (GetPreCalculatedMonsterDamage): the lair path
  currently divides final Exp/Hr by party size, matching frmMain's lair
  behavior.

Completed since this list was first written: the **equipment
calculator** (`frmMain.CalcCharacterStats`, ~1,240 lines) shipped in
session 23 — `Mme.Data/EquipmentStatsService.cs` runs the full
accumulation (class/race ability scans, encumbrance-first ordering,
bless, the equipped + carried item loop, per-slot source tips), with
real-database anchors and the divergence list recorded in
`docs/PARITY_LEDGER.md`. `EquipmentCalc_AccumulatesWornItemIntoDerivedStats`
pins the behaviour directly. The **spell detail panel**
(`PullSpellEQ` :4067–4527 plus `GetAbilityStats`) shipped in session 48 —
`Mme.Data/SpellEqService.cs` renders the full effect string (nested
EndCast recursion, RemovesSpells, DR tenths, the energy-cost
"xN times/round" tail, flag abilities) and returns the teleport /
textblock / summon / spell refs as clickable lines.

## For contributors / future sessions

1. `docs/MME_REWRITE_STRATEGY.md` — the master plan and Anti-Hallucination Protocol (§0).
2. `PORT_LOG.md` — what the last session did and what's next.
3. `docs/PARITY_LEDGER.md` — per-procedure port/test status.

## Layout
- `src/Mme.Core` — pure ported game logic (BCL only, no UI/DB refs)
- `src/Mme.Data` — OleDb/ACE access to `data-v1.11p.mdb` (Phase 1e)
- `src/Mme.ParityHarness` — CSV dumper for side-by-side VB6 output diffs
- `src/Mme.App` — WPF shell (created in Phase 2, requires Windows)
- `tests/Mme.Core.Tests` — xUnit parity tests

## Build & test
```
dotnet test                      # all parity tests
dotnet run --project src/Mme.ParityHarness [outDir]   # emit parity CSVs
```

Release: `.\build\publish.ps1` (publish profile `win-x64-single` + Azure Artifact Signing — see `docs/SIGNING.md`)
