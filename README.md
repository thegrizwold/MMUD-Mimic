# MMUD Explorer — C#/.NET 8 rewrite

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

## Test suite

```
dotnet test
```

693 tests. Anchors were derived from independent replicas of the VB6
math (not from the port itself); real-database tests are guarded on the
converted `mmud-1.11p.db` being present.

## Known remaining work

- **Equipment calculator** (`frmMain.CalcCharacterStats`, ~1,240 lines):
  worn-item → derived-stat computation. The routing table
  (GetAbilityStatSlot) is ported and tested; the accumulation loops are
  scoped in the ledger. Until then the character panel takes the derived
  stats as direct entries — the same numbers the VB6 labels display.
- **Detail panels** (PullSpellEQ and friends): display-string builders
  over already-ported math.
- **Party damage tables** (GetPreCalculatedMonsterDamage): the lair path
  currently divides final Exp/Hr by party size, matching frmMain's lair
  behavior.

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

Release (Phase 5): `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
