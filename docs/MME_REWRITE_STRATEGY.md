# MME Rewrite Strategy — VB6 → C# / WPF (.NET 8)

**Status:** Phase 0 (this document). Ground-truth plan for the multiphase port of
MMUD Explorer from VB6 to C#/WPF. This document is the standing prompt: every
future session working on the port reads this FIRST, then the phase section it
is executing, and follows the Anti-Hallucination Protocol below.

**Source of truth:** the VB6 repo (`MMUD-Explorer-master/`). Nothing in this
document overrides the actual `.bas`/`.cls`/`.frm` source. When this doc and the
source disagree, the source wins and this doc gets corrected.

---

## 0. Anti-Hallucination Protocol (non-negotiable, every session)

1. **Never write a game formula from memory.** Every formula in `MME.Core` is
   ported by opening the VB6 procedure in the same session and translating it
   line-by-line. If a session cannot see the VB6 body, it does not port it.
2. **Port, don't "improve."** Preserve integer truncation (`\`), `Int()`,
   banker's-rounding differences, off-by-one loop bounds, and magic constants
   exactly. VB6 `Integer` = C# `short`, `Long` = `int`, `Currency` = `decimal`
   (scale 4), `Single` = `float`, `Double` = `double`, `\` = integer division
   with VB6 semantics (operands rounded to integers FIRST — banker's rounding —
   then truncated division), `Mod` on negatives follows VB6 (result sign =
   dividend sign, same as C#). When in doubt write a micro-test.
3. **Every ported procedure gets a `// VB6: modX.bas :: ProcName` header
   comment** so parity audits can diff against the origin.
4. **No formula lands without a parity test** (see §8). A port with zero tests
   is an unfinished port.
5. **VB6 source files are Windows-1252 with CRLF.** When scripting extraction,
   read them as codepage 1252/latin-1, never UTF-8. Never write back to the
   VB6 tree at all — it stays read-only reference.
6. **`frmMain.frm` is ~1.5 MB — never read it whole.** Grep for the procedure
   or control name, then read a targeted line range.
7. **Do not invent database column names.** Column names come from the VB6
   code (`tabItems("Some Col")`) or from opening `data-v1.11p.mdb` itself.
   Many have spaces and must be bracketed in SQL: `[Map Number]`.
8. **Engine-variant honesty.** Every formula port must answer: "does the VB6
   body branch on `bGreaterMUD`, `nNMRVer`, or `nGlobalDatVer`?" If yes, the
   branch is captured in the rules classes (§4), never dropped or merged.
9. **End every session by updating `PORT_LOG.md`** in the new repo: what was
   ported, test counts, parity deltas, next step. Sessions resume from that
   log, not from chat memory.

---

## 1. Measured Source Inventory (facts, not guesses)

Sizes/counts measured 2026-07-01 from the uploaded repo:

| VB6 file | Lines | Procs | Role | Hidden dependencies (measured) |
|---|---|---|---|---|
| `modMMudFunc.bas` | 4,787 | 70 | Core game formulas | `bGreaterMUD` ×66, `tabSpells` ×81, `tabItems` ×49, `nGlobalChar*` ×44, `nNMRVer` ×3 |
| `modSyntaxsFunc.bas` | 1,370 | 37 | String/format/regex/file utils + a few UI helpers | none of the above (0 hits) — but ~8 procs are UI/Win32 (form ownership, clipboard, ListView) |
| `modExpPerHour.bas` | 5,002 | 33 | Exp/hr Models A–D + calibration harness | `bGreaterMUD` ×5 only; `CalcExpPerHour(...)` is FULLY parameterized (25 args → `tExpPerHourInfo`) |
| `modItemParse.bas` | 1,876 | 52 | Game-text inventory parser + Item Manager population | `tabItems` ×41; 3 procs are ListView-coupled (`LV_AddRowByItemNumber`, `PopulateItemManagerFromParsed`, `AddListViewRowsForItem`) |
| `clsMonsterAttackSim.cls` | 1,712 | 125 | Round-by-round monster attack sim | Self-contained; configured entirely via properties incl. `bGreaterMUD`; ×6 internal branches |
| `modMMudDatabase.bas` | 5,777 | 82 | DAO layer, recordsets, `GetLairInfo`, damage arrays | `nNMRVer` ×18, `bGreaterMUD` ×15 |
| `modMain.bas` | 8,658 | 73 | Entry point + **218 Global/Public declarations** (char state) | `nNMRVer` ×44, `bGreaterMUD` ×25 |
| `modListViewExt.bas` | 2,741 | 29 | ListView sort/group/color engine | UI-only; becomes WPF behaviors/converters, not a port |
| `frmMain.frm` | ~35k | — | **1,448 controls**: 865 Labels, 212 Buttons, 128 Menus, 72 Checks, 70 TextBoxes, 53 Combos, 26 ListViews, 30 Frames | `bGreaterMUD` ×64, `nNMRVer` ×49 |

Other forms: `frmMap.frm` (1.6 MB — graphical room explorer), `frmSwingCalc`,
`frmHitCalc`, `frmBSCalc`, `frmExpCalc`, `frmCoinConvert`, `frmMonsterAttackSim`,
`frmSpellBook`, `frmMegaMUDPathing`, `frmResults`, `frmSettings`,
`frmLoadChar`/`frmPasteChar`, `frmMonsterFilters`, dialogs.

**Database (`data-v1.11p.mdb`, Jet 4):** tables opened at load — `Items`,
`Monsters`, `Spells`, `Rooms`, `Shops`, `Classes`, `Races`, `Lairs`, `Info`,
`TBInfo`. Version detection (`nNMRVer`, `nGlobalDatVer`, `bGreaterMUD`) is
derived from the `Info` table at load in `modMMudDatabase.bas` /
`modMMudFunc.bas` — port that detection logic verbatim in Phase 1e.

**Engine variants:** STOCK, GreaterMUD, Paramud. Existing per-engine splits to
seed the strategy pattern: `CalcExpNeeded_STOCK`, `CalcExpNeeded_GMUD`,
`CalcExpNeeded_GMUD_1_8_5`, `GMUD_GetSpDmgMultiplierFromSC`,
`GMUD_DiminishingReturns`, plus constants `STOCK_MOB_HPREGEN_ROUNDS=18` /
`GMUD_MOB_HPREGEN_ROUNDS=6`. Paramud is gated by `nNMRVer`/`nGlobalDatVer`
thresholds (1.8, 1.82, 1.83, 1.85, 1.9+) — see README v2.2.1 notes (exp formula
and jumpkick differ for Paramud DB 1.9+ vs 1.8.5).

**Exp/hr models:** A (legacy closed-form), B (legacy, **overfit — port as-is,
never extend**), C (cycle macro-sim), D (round-by-round sim, recommended).
Authoritative mechanics notes: `docs/exp-per-hour-models.md` — including the
two traps: `nCharHPRegen` arrives in resting-rate form (already ×3), and
recovery is serialized (rest XOR meditate, never in combat, passive always
ticks). **`RunAllSimulations` embeds ~18 real in-game observations
(`SIM_TABLE`) — this is the ready-made golden dataset for Phase 1 tests.**

**Value color semantics (measured from runtime `ForeColor =` assignments):**
bright green `&HFF00&` (#00FF00), dark green `&HC000&` (#00C000), red `&HFF&`
(#FF0000), orange `RGB(255,157,0)`, yellow `RGB(255,255,0)` / `RGB(227,235,14)`,
cyan-ish `&HC0C000&`, plus system grays. Item Manager shows red for items both
carried and equipped. A hand-rolled dark mode already exists as
`DARKMODE.patch` (`modTheme.bas`) — treat it as intent, not implementation.

---

## 2. Target Architecture

```
MmeExplorer.sln
├── src/
│   ├── Mme.Core/                  # Phase 1. Pure logic. NO WPF, NO OleDb refs.
│   │   ├── Engine/                #   IGameEngineRules + Stock/GreaterMud/Paramud
│   │   ├── Formulas/              #   CombatMath, CharacterMath, SpellMath, EncumMath...
│   │   ├── ExpPerHour/            #   Models A–D + ExpPerHourInputs/Result
│   │   ├── Simulation/            #   MonsterAttackSim (from clsMonsterAttackSim)
│   │   ├── Parsing/               #   GameTextInventoryParser (from modItemParse)
│   │   ├── Model/                 #   CharacterSheet, Item, Monster, Spell, Room, Shop,
│   │   │                          #   Race, Class, LairInfo — plain records/classes
│   │   └── Text/                  #   TextUtils (ported from modSyntaxsFunc.bas)
│   ├── Mme.Data/                  # Phase 1e. OleDb/ACE access to the .mdb.
│   │   ├── MdbConnection.cs       #   provider detection, keepalive, x64 note
│   │   ├── Repositories/          #   ItemRepository, MonsterRepository, ...
│   │   └── GameDataVersion.cs     #   Info-table → NmrVersion/DatVersion/EngineKind
│   ├── Mme.App/                   # Phase 2+. WPF (net8.0-windows), WPF-UI, MVVM.
│   │   ├── Shell/                 #   MainWindow, navigation, docking
│   │   ├── Views/  ViewModels/    #   one pair per VB6 "tab"/form
│   │   ├── Theming/               #   Light.xaml / Dark.xaml semantic palette
│   │   └── Converters/            #   SignToBrush, DeltaToBrush, etc.
│   └── Mme.ParityHarness/         # console app: dumps Core outputs to CSV for
│                                  # side-by-side diff vs VB6 dumps
├── tests/
│   └── Mme.Core.Tests/            # xUnit. Golden-file + SIM_TABLE parity tests.
├── PORT_LOG.md                    # session-to-session state (append-only)
└── docs/PARITY_LEDGER.md          # per-procedure: ported? tested? delta?
```

**Dependency rule:** `Mme.Core` references nothing but the BCL.
`Mme.Data` references `Mme.Core`. `Mme.App` references both. Tests reference
Core (+ Data for integration tests tagged `[Trait("db","mdb")]`).

**Packages (pin on first restore; record versions in PORT_LOG.md):**
- `CommunityToolkit.Mvvm` — source-generated `[ObservableProperty]`/`[RelayCommand]`.
- `WPF-UI` (package id `WPF-UI`) — Fluent styling + native dark/light theme
  switching. Fallback if it fights us: `ModernWpfUI`. Decide once, in Phase 2,
  and record the decision; do not mix both.
- `Dirkster.AvalonDock` + its theme pack — docking/floating panels.
  (Deferred to Phase 3 — Phase 2 shell starts with WPF-UI's `NavigationView`;
  see §7 Phase 2 rationale.)
- `System.Data.OleDb` (Microsoft package; Windows-only, that's fine).
- `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.

**Build/publish:** `net8.0-windows`, `<UseWPF>true</UseWPF>`.
Release: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.

### ACE / bitness (decide now, it bites late)
The VB6 app is x86 and uses Jet via DAO. .NET 8 defaults to x64. The Jet 4
provider (`Microsoft.Jet.OLEDB.4.0`) is **x86-only**; the ACE provider
(`Microsoft.ACE.OLEDB.12.0` / `16.0`) exists in x86 and x64 but only one
bitness can be installed unless using the Office "quiet" workaround.
**Decision: target win-x64 and require the x64 ACE redistributable.**
`MdbConnection` probes providers in order `ACE.16.0 → ACE.12.0` and surfaces a
friendly "install the Access Database Engine x64 redistributable" error with
the download link if neither resolves. Connection string:
`Provider=Microsoft.ACE.OLEDB.12.0;Data Source=<path>;Persist Security Info=False;`
Port the v2.2 lesson "added keepalives to prevent errors after idle" as: open
short-lived connections per query (connection pooling makes this cheap) instead
of one eternal connection — that eliminates the keepalive hack entirely.

---

## 3. The Global-State Problem (the real migration risk)

`modMain.bas` declares **218 globals**; the loaded character is ~200 scalar
globals (`nGlobalCharWeaponMaxDmg`, `nGlobalCharAccyAbils`, …) that
`modMMudFunc` reads directly (44 refs). The port replaces this with:

- **`CharacterSheet`** — one class holding every `nGlobalChar*` /
  `nChar*` scalar as a property, grouped into nested records where natural
  (`Weapon`, `Accuracy`, `Regen`, `Magery`, …). Build the field list by
  grepping `^(Global|Public)` in `modMain.bas` and mapping 1:1. Do NOT redesign
  the shape in Phase 1 — a dumb faithful bag of properties is correct; MVVM
  wrapping comes later in `Mme.App`.
- **`GameData`** — replaces the global recordsets. Interfaces in Core
  (`IItemSource`, `ISpellSource`, …) with the minimal lookups the formulas
  actually perform (mostly keyed reads by `Number` and a few filtered scans).
  `Mme.Data` implements them over OleDb with an in-memory cache of the full
  table (these tables are small — caching whole tables at load mirrors the VB6
  open-recordset-at-startup behavior and kills per-call DB chatter).
- **Porting rule for `modMMudFunc` procedures:** every implicit global read
  becomes an explicit parameter (`CharacterSheet ch`, `GameContext ctx`) or a
  constructor-injected source. The VB6 signature is preserved otherwise.

`GameContext` = `{ IGameEngineRules Rules, GameDataVersion Version, GameData Data }`.

---

## 4. Engine-Variant Strategy (`IGameEngineRules`)

Replace scattered `If bGreaterMUD` / `If nNMRVer >= x` with:

```csharp
public interface IGameEngineRules {
    EngineKind Kind { get; }                    // Stock, GreaterMud, Paramud
    decimal ExpNeeded(int level, ...);          // wraps CalcExpNeeded_* family
    int MobHpRegenRounds { get; }               // 18 stock / 6 GMUD
    double SpellDamageMultiplierFromSC(...);    // GMUD_GetSpDmgMultiplierFromSC
    double DiminishingReturns(...);             // GMUD_DiminishingReturns
    // grown incrementally as each ported formula exposes a branch
}
```

**Method-by-discovery, not upfront design:** the interface grows one member at
a time as Phase 1 porting encounters each `bGreaterMUD`/`nNMRVer` branch. Each
new member's PR-note in `PORT_LOG.md` cites the VB6 line that motivated it.
Paramud is **not** a boolean — it is `GreaterMud`-adjacent behavior gated by
DB version thresholds; model it as `ParamudRules : GreaterMudRules` overriding
only what the version gates change (README: exp formula + jumpkick + max-6-swing
QnD differ at DB ≥1.9). Where a branch is *data-version* rather than *engine*
(e.g. `nNMRVer >= 1.83` regex forms in the DB layer), keep it as a
`GameDataVersion` check — don't force it into the rules interface.

**Selection:** `GameDataVersion.Detect(InfoTable)` ports the existing
`Info`-table logic from `modMMudDatabase.bas` and returns
`(NmrVersion, DatVersion, EngineKind)`; a factory maps `EngineKind` →
rules instance. The UI's manual override (frmMain `fraDatVer`) is preserved
in Phase 3.

---

## 5. Data Layer Plan (`Mme.Data`)

- Keep `data-v1.11p.mdb` untouched — same file the NMR/Jackcess pipeline
  round-trips. **Read-only** open (`Mode=Read` in connection string) — MME is a
  viewer; never take a write lock that could collide with the editor pipeline.
- One repository per table; each does `SELECT * FROM [Table]` at load into
  typed rows (`ItemRow`, …) mirroring VB6 field usage. Column-name spaces are
  real (`[Map Number]`, `[Room Number]`, `[Min Damage]`…) — build the field
  map from actual VB6 accessor strings, table by table, as each explorer is
  ported. Do not pre-map all ~1000 columns in Phase 1; map what the ported
  code touches.
- Port `GetLairInfo` (in `modMMudDatabase.bas`) with special care in Phase 1e —
  it is the input assembler for exp/hr (bakes multi-mob ramp-down into
  `nMobDmg` via `avgAlive=(nMaxRegen+1)/(2*nMaxRegen)`, applies the 0.5-round
  rule and surprise credit to RTK). Model D depends on its exact semantics.
- Memo/attachment quirks of Jet via ACE: `TBInfo` (textblocks) contains long
  text — verify round-trip reads of a few known-long records in the Phase 1e
  smoke test.

---

## 6. Theming & Semantic Value Colors

Two `ResourceDictionary` themes (Light/Dark) defining **semantic brush keys**,
never raw colors in views:

| Key | Light | Dark | VB6 origin |
|---|---|---|---|
| `Value.Positive` | `#008000` | `#4EC94E` | `&HC000&` dark green on white |
| `Value.PositiveStrong` | `#00A000` | `#00FF00` | `&HFF00&` bright green |
| `Value.Negative` | `#C00000` | `#FF5555` | `&HFF&` red |
| `Value.Warning` | `#B8860B` | `#FFD44D` | yellows `RGB(255,255,0)`/`(227,235,14)` |
| `Value.Caution` | `#D06A00` | `#FF9D00` | orange `RGB(255,157,0)` |
| `Value.Info` | `#0060C0` | `#59B7FF` | blue accents |
| `Value.Neutral` | theme fg | theme fg | `&H80000008&` window text |
| `Row.CarriedAndEquipped` | `#C00000` | `#FF6B6B` | Item Manager red rule |

Principles: dark-mode variants keep the **same hue family** as light (per your
requirement) but lifted for contrast on dark surfaces (target ≥ 4.5:1 on the
WPF-UI dark background). Converters in `Mme.App/Converters`:
`SignToBrushConverter` (value>0 → Positive, <0 → Negative, 0 → Neutral),
`DeltaToBrushConverter` (for compare views: better/worse relative to baseline —
port the actual better/worse logic from `modListViewExt.bas` per column, since
"lower is better" applies to some columns like speed/encum). WPF-UI's
`ApplicationThemeManager` drives base surfaces; our dictionary swap rides the
same toggle. Theme choice persists to `settings.json` (replaces `settings.ini`;
Phase 2 includes a one-time INI import).

---

## 7. Phase Plan

### Phase 1 — `Mme.Core` + `Mme.Data` (no UI)
Sub-phases, each independently shippable with tests:

- **1a. `Text/TextUtils`** — port the pure string/number procs from
  `modSyntaxsFunc.bas` (`PutCommas`, `FormatWithCommas`, `FormatBigIntWithCommas`,
  `ExtractNumbersFromString`, `ExtractValueFromString`, `RegExpFind*`,
  `SortLetters*`, `RoundUp*`, `Truncate`, `IsAlpha*`, …). SKIP the UI/Win32
  procs (`SetOwner`, `SetTopMostWindow`, `ClearListViewSelections`,
  `UnloadForms`, `FormIsLoaded`, `SetClipboardText`, `HandleError`) — they are
  replaced by framework features, not ported. Tests: direct input/output
  fixtures.
- **1b. `Formulas/*`** — port `modMMudFunc.bas` procedure-by-procedure in
  dependency order (enum getters first: `Get*Enum` ×~12 → caps/min tables:
  `GetHitMin/GetHitCap/GetDodgeCap/GetSpellHitCap` → `CalcExpNeeded_*` family →
  accuracy/dodge/encum/regen/HP/mana/MR → `CalculateSpellCast`/`CalcCombatRounds`
  last, since they pull spells/items). This is where `CharacterSheet`,
  `GameContext`, and `IGameEngineRules` come alive (§3–4). ~70 procs; expect
  3–4 sessions. Every proc: header comment + parity test.
- **1c. `Simulation/MonsterAttackSim`** — port `clsMonsterAttackSim.cls` as one
  class; its property surface maps 1:1 to C# properties. Deterministic seams:
  if it uses `Rnd`, inject `Random`/an `IRng` so tests can seed it and VB6
  parity uses expected-value assertions rather than RNG-exact matches.
- **1d. `ExpPerHour/*`** — port `tExpPerHourInfo`, `CalcExpPerHour`, and Models
  A, C, D faithfully; Model B faithfully but sealed with an
  `[Obsolete("overfit — do not extend")]`-style doc note per
  `docs/exp-per-hour-models.md`. Port `RunAllSimulations`'s `SIM_TABLE` +
  `LoadSimRows` into the test project as the golden dataset (§8).
- **1e. `Mme.Data`** — OleDb connection/provider probing, repositories for the
  10 tables, `GameDataVersion.Detect`, and the `GetLairInfo` port. Integration
  smoke test against the repo's `data-v1.11p.mdb` (counts per table, spot-check
  known rows, one long TBInfo record).
- **1f. `Parsing/GameTextInventoryParser`** — port `modItemParse.bas` minus the
  3 ListView procs; parser returns a structured result
  (`ParsedInventory { Equipped, Carried, Keys, Ground[], Discrepancies }`).
  Item lookups go through `IItemSource`. The ListView population logic is
  recorded in `PARITY_LEDGER.md` as "moves to Phase 3 Item Manager VM".

**Exit criteria:** all parity tests green; `Mme.ParityHarness` can dump
`CalcExpNeeded` tables, hit/dodge/accuracy sweeps, and SIM_TABLE model outputs
to CSV; ledger shows every proc in the 5 source modules as Ported/Skipped(UI)
with a reason.

### Phase 2 — WPF shell + first calculator end-to-end
- Scaffold `Mme.App`: WPF-UI shell, `NavigationView` left rail (mirrors the VB6
  nav-button column), theme toggle wired to §6 dictionaries, settings service
  (JSON + one-time `settings.ini` import), DB-open flow with the CLI args the
  VB6 exe supports (`.mdb` and/or `.mmec` path).
- **Start with `NavigationView`, not AvalonDock.** Rationale: docking shines
  when multiple explorers exist; with one calculator it only adds risk. Phase 3
  re-hosts pages into AvalonDock documents — the ViewModels don't change.
- Port **`frmCoinConvert`** first (18 KB, zero DB deps) to prove the
  MVVM/theming/converter pipeline, then **`frmSwingCalc`** (72 KB) as the first
  real formula consumer: it exercises `CharacterSheet`, weapon lookups, the
  green/red converters (its 20 runtime color assignments), and the
  paste-MegaMUD-stats parser.
- Character load/save: port the `.mmec` format read/write (`frmLoadChar`,
  `SaveChar`/`LoadChar` in `modMain.bas`) into a `CharacterFileService` — most
  later views need a loaded character, so it lands here.

**Exit criteria:** dark/light switch flips the whole app including semantic
value colors; SwingCalc numbers match VB6 for 3 saved characters (goldens).

### Phase 3 — Explorers, form-by-form (VB6 stays the reference)
Recommended order (each = View + VM + repository fields + its slice of
`modListViewExt` behavior as reusable `DataGrid` sort/group/color behaviors):
1. **Items/Weapons/Armour explorer + Compare** (the marquee feature; three
   ListView pairs on frmMain).
2. **Monsters explorer** (+ lair info, filters — `frmMonsterFilters`).
3. **Spells explorer + Compare + `frmSpellBook`.**
4. **Shops, Classes, Races.**
5. **Item Manager** (consumes Phase 1f parser; carried+equipped red rule).
6. **Character/EQ tab** (equip slots, copy-equip-commands).
7. Remaining calculators: `frmHitCalc`, `frmBSCalc`, `frmExpCalc`,
   `frmMonsterAttackSim` (UI over 1c), `frmResults`.
8. Introduce **AvalonDock** here: each explorer/calculator becomes a dockable
   document/tool pane; nav rail becomes "open panel" commands.

### Phase 4 — Map & pathing
`frmMap.frm` (1.6 MB) is its own project-within-the-project: custom-drawn room
explorer. Plan: render rooms on a virtualized `Canvas`/`DrawingVisual` layer,
port movement/adjacency from the VB6 draw code, then `frmMegaMUDPathing` and
the rooms.md star overlay. Do not start until Phase 3 items 1–2 are in daily
use — the map is the biggest single risk and benefits from a matured data layer.

### Phase 5 — Parity sign-off & packaging
Full `PARITY_LEDGER.md` review, self-contained single-file publish, ACE
detection UX, side-by-side week with VB6, then feature-freeze the VB6 tree.
The VB6 `mudexplr.exe` remains buildable/usable throughout Phases 1–4.

---

## 8. Parity & Verification Protocol

1. **SIM_TABLE goldens (free, do first):** port the ~18 embedded real-game
   observation rows from `RunAllSimulations` into xUnit `[MemberData]`. Assert
   C# Model A/C/D outputs match the VB6-computed outputs (generate the VB6
   expected values once by running `RunAllSimulations` in the VB6 IDE and
   capturing its debug print — commit that capture as
   `tests/goldens/simtable_vb6.txt`).
2. **Formula sweep goldens:** add a tiny VB6 dump routine (dev-mode only, in a
   scratch copy) that writes CSVs: `CalcExpNeeded(level 1..255 × engine)`,
   accuracy/dodge/hit-cap sweeps over representative stat grids, encum/regen
   tables. `Mme.ParityHarness` emits the same CSVs; tests diff them.
   Tolerance: integers exact; doubles `1e-9` relative unless the VB6 code
   itself uses `Single` (then `1e-5`).
3. **Character goldens:** 3+ real `.mmec` files (low-level melee, high-level
   caster, Paramud char) checked into `tests/goldens/chars/`; assert derived
   stats (HP, mana, accuracy, swings, exp/hr for 2 known lairs) against values
   read off the running VB6 app.
4. **DB smoke:** row counts + spot rows per table vs known values from
   `data-v1.11p.mdb`.
5. **Ledger discipline:** `docs/PARITY_LEDGER.md` — one row per VB6 proc:
   `Ported → Tested → Delta(if any, with justification)`. "Skipped (UI)" and
   "Skipped (dead code)" are valid states but need a reason.

---

## 9. Ready-to-Paste Session Prompts

**Phase 1a kickoff:**
> Read `MME_REWRITE_STRATEGY.md` §0–§4 and §7 Phase 1a, then `PORT_LOG.md`.
> Create the solution skeleton per §2 (projects, packages, PORT_LOG.md,
> PARITY_LEDGER.md). Port the pure procs of `modSyntaxsFunc.bas` into
> `Mme.Core/Text/TextUtils.cs` per the 1a list, with `// VB6:` headers and
> xUnit fixtures for each. Read each VB6 proc body (latin-1!) before porting.
> Update the ledger and log. Deliver the repo as a zip.

**Phase 1b session template:**
> Read strategy §0, §3, §4, §7-1b + `PORT_LOG.md`. Continue porting
> `modMMudFunc.bas` from the next unported proc in `PARITY_LEDGER.md`,
> dependency order. For each proc: read the VB6 body, port with exact
> arithmetic semantics, add every `bGreaterMUD`/`nNMRVer` branch to the rules
> classes with a source-line citation, write the parity test. Stop after ~15–20
> procs or when a natural seam is reached; update ledger+log; deliver zip.

(Analogous templates apply for 1c–1f and Phase 2; each cites its strategy
section and resumes from the ledger.)

---

## 10. Risk Register / Known Gotchas

| Risk | Mitigation |
|---|---|
| ACE bitness mismatch on user machines | x64-only build + provider probe + friendly install-link error (§2) |
| VB6 `\`, `Int()`, banker's rounding drift | §0 rule 2 + micro-tests on every arithmetic-heavy proc |
| Hidden global reads silently dropped in port | §3 rule: grep each VB6 proc body for `nGlobal`/`tab[A-Z]`/`bGreaterMUD` before signing it off in the ledger |
| Model B "fixed" during port | Sealed + documented as overfit; parity test pins current behavior |
| `HPRegen ×3 resting-rate form` misapplied | Documented here + in `ExpPerHourInputs` XML-doc; SIM_TABLE goldens catch it |
| frmMain logic (64 `bGreaterMUD` refs) lost because it lives in the form | Phase 3 rule: before porting any view, grep its VB6 event handlers for formula fragments and hoist them into Core with tests first |
| Two theming libraries mixed | Single decision in Phase 2, recorded in PORT_LOG.md |
| `.mdb` write-lock collision with NMR/Jackcess pipeline | Read-only connection mode (§5) |
| Session drift / re-derived mistakes | §0 protocol + PORT_LOG.md + PARITY_LEDGER.md are the memory, not chat |

---

*End of strategy. Phase 1a may begin.*
