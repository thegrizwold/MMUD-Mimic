# PORT LOG (append-only; newest session at top)

## Session 46 — 2026-07-07 — BETA 25: icon columns to vector geometry + headers + sorting

(Workspace rebuilt from the beta 24 source zip after the weekly reset;
.NET 8 SDK reinstalled; DB restored to /home/claude/mme/current/ — the
tests expect it one level ABOVE the repo dir, not inside it. Baseline
re-verified 853/853 before changes.)

USER REPORTS (beta 24 follow-up):
1. Weapon "sharp" glyph looked like a spear and collided with spears.
2. Head + waist shared an icon (helmet ~= belt); belt wasn't a belt.
3. The icon columns needed names (headers) and to be sortable.

FIX — replaced the emoji-glyph icon system with hand-authored vector
Geometry paths (IconConverters.cs). Root cause of the collisions was
the Segoe UI Emoji font falling back to look-alikes for the niche
pictographs (military helmet, belt) and rendering the dagger as a
spear-ish blade. Vector paths remove the font dependency entirely,
inherit the theme colour, and stay crisp at any DPI.
- New geometries (0..24 box, Stretch=Uniform): a clear one-edged SWORD
  (blade+crossguard+grip+pommel) for sharp, HAMMER for blunt; per-slot
  shapes incl. a real domed HELMET and a distinct buckled BELT, plus
  ring/boot/shield/cloak/shirt/glove/bracer/amulet/ear/arm/legs/glasses/
  mask/aura/dot; spell elements flame/snowflake/bolt/rock/droplet/
  arcane-star/sparkles. Audited: no two slots share a shape. All 26
  paths structurally validated (command/arg-count parse check).
- Converters now return Geometry (SpellKindGeometryConverter,
  WeaponKindGeometryConverter, SlotGeometryConverter) + the existing
  colour converter. Columns render a <Path Fill/Stroke=ThGridFg ...>
  (spell element keeps its per-element tint via SpellKindColorConverter).

HEADERS + SORTING:
- The three template columns got names — "Edge" (weapons), "Slot"
  (armour), "Element" (spells) — and SortMemberPath so the header is
  clickable-sortable: WeaponKind / Worn / DamageKind respectively. The
  sort groups sensibly (all Fire together, all Head together) since it
  sorts on the classification string that drives the icon.

Files: src/Mme.App/IconConverters.cs (rewritten), src/Mme.App/
MainWindow.xaml (converter registrations + three columns). Icon fill
uses the real grid-foreground brush key ThGridFg (ThFg didn't exist —
would have rendered invisible).

Suite: 853/853 (unchanged; classification logic already pinned by
Session45IconClassifyTests, which also backs the sort keys).
Shipped as BETA 25.

## Session 45 (cont.) — 2026-07-05 — BETA 24: element/weapon/slot icon columns (user enhancement)

USER ENHANCEMENT (beyond the OG — not in the VB6 either): little icons
in the browse grids for spell damage element, weapon sharp/blunt, and
armour worn-slot.

SPELLS — DamageKind icon column:
- SpellGridRow.DamageKind: heal (any Abil 18) -> "Heal"; else if the
  spell deals damage (Abil 1/8/17) the element from AttType. AttType
  mapping was NOT the filter-dropdown labels (those are offset/wrong);
  derived empirically from named stock spells: 0 Cold (frost jet, ice
  storm), 1 Fire (fireball, flame), 2 Stone (stonestrike, earthquake),
  3 Lightning (lightning bolt, shock), 4 Normal-magic (magic missile,
  harm), 5 Water (acid jet, black water, drowning). Non-damage/non-heal
  -> "None" (blank).
- Glyphs (Segoe UI Emoji): fire/snowflake/voltage/rock/droplet/arcane
  star; Heal -> sparkles. Element-tinted via SpellKindColorConverter.

WEAPONS — sharp/blunt + handedness icon:
- WeaponBrowseRow.WeaponKind from WeaponType (chkHanded :4474): 0
  1H-Blunt, 1 2H-Blunt, 2 1H-Sharp, 3 2H-Sharp; IsSharp/IsTwoHanded.
  Blade glyph for sharp, hammer for blunt; tooltip carries the full
  "1H Sharp" etc.

ARMOUR — worn-slot icon:
- ArmourBrowseRow.SlotKey (lowercased Worn slot name) -> a per-slot
  glyph (helmet/ring/boot/shield/gloves/...); tooltip = the slot name.

Implementation: 4 IValueConverters (IconConverters.cs) mapping the
model strings to glyph + color; DataGridTemplateColumn icon columns
inserted after "#" on the Weapons/Armour/Spells grids. No image assets
(crisp, themeable, zero-dependency).

- DIVERGENCE: emoji glyphs depend on the Segoe UI Emoji font (present
  on Win10+); the AttType->element map is empirical (the filter combo's
  own labels disagree with the data and were not trusted). Poison has
  no distinct AttType in this data (cure poison = AttType 4).

Suite: 853/853 (+3). Shipped as BETA 24.

## Session 45 (cont.) — 2026-07-05 — BETA 23: modal TB-jumps, search-lag debounce, Clear Filters

TEXTBLOCK MODAL TB->TB JUMP (user: double-click inside the TB window
doesn't open linked blocks):
- GetTextblockDetail now appends "[TB n]" tails to (a) the header's
  LinkTo, and (b) any action line that links another block — "random
  N" / "goto N" / "block N" / the "word:N" form (e.g. "Dhelvanen:229").
  The LookupResultsWindow JumpHandler already routes [TB n] through
  NavigateFromLine -> RequestTextblock, so those lines now open the
  linked block in a new TB window. Called-From monster/room lines were
  already jumpable.
- PINNED: TextblockDetail_LinkedBlocks_GetClickableTails.

SEARCH LAG ON TYPE/BACKSPACE (user: every keystroke re-filters live and
lags; VB6 is instant):
- ROOT CAUSE: FilterText's setter ran ApplyFilter() every keystroke,
  which called ReloadEquipLists() — a full equipment-catalog rebuild +
  ItemUsabilityService scan — on EACH character. (ApplyBrowseFilter was
  already stamp-cached from a prior session, so it wasn't the cost.)
- FIX: split the setter. The cheap in-memory grid filter
  (ApplyListFilterOnly) AND the stamp-cached ApplyBrowseFilter run
  immediately per keystroke (grids stay live, Jump/selection stay
  correct); only ReloadEquipLists is debounced via a
  System.Threading.Timer that Posts back through the captured UI
  SynchronizationContext. FilterDebounceMs (default 180; 0 =
  synchronous for tests / no-UI-thread). ApplyFilter() (load / panel /
  character / By-Lair changes) still does the full synchronous pass.
- PINNED: ClearAllFilters_ResetsSearchAndFields,
  ListFilter_RunsImmediately_EvenWithDebounceOn. Regression caught +
  fixed: JumpToItem depends on live WeaponRows, so ApplyBrowseFilter
  had to stay on the immediate path (not debounced).

CLEAR FILTERS BUTTON (user request, next to More Filters — VB6
ResetFilterOptions :38419): MainViewModel.ClearAllFilters resets the
More-Filters fields (spell magery/level/target/attacktype/ability,
monster regen/hp/dmg/exp/magic, weapon/armour/sundry ability triples)
to defaults AND clears the global search, firing a single refilter.
New "Clear Filters" toolbar button.

Suite: 850/850 (+3). Shipped as BETA 23.

## Session 45 (cont.) — 2026-07-05 — BETA 22: spawn-parse fix + user-friendly textblocks

SPAWNS-VIA GARBAGE (user: monster spawn rooms show "(29/1)" and
"(not found)" instead of real map/room):
- ROOT CAUSE in ResolveLocationRefs' group/lair branch: it matched
  "[6-0-5][2]Group(lair): 1/552" and read the "6-0-5" SPAWN-CADENCE
  triple as map/room (-> "(6/0)"/garbage), and let plain
  "Group: 7/1289" fall through unresolved.
- FIX (matches VB6 sLairRegex + GetLocations :6560): the map/room is
  AFTER "Group(lair):" / "Group:"; the bracket triple is cadence, the
  "[N]" is the mob count. Now:
    "Group: 1/547"                 -> "Spawn: <room> (1/547)"
    "[6-0-5][2]Group(lair): 1/552" -> "Lair (2 mobs): <room> (1/552)"
  Added ParseMapRoom (regex, junk-tolerant) shared by room + group.
  Also added npc #, textblock(rndm) #, shop(sell)/shop(nogen) token
  handling and a trailing "(NN%)" chance capture kept for display.
- PINNED: GroupAndLair_ResolveToRealRooms_NotGarbage.

USER-FRIENDLY TEXTBLOCKS (user: items show "Textblock 1000 (5%)";
should show the CONTAINER item name + chance, and be clickable to a
textblock context window we were missing):
- ResolveTextblockRef + FindTextblockContainer: walk the TB's
  "Called From" upward (bounded depth 8) until an "Item #N" appears
  (the container whose open-spell reveals the loot); show ITS name +
  the chance, e.g. "Locked Box (1%)". Falls back to "Textblock N".
- Every TB ref carries a "[TB n]" tail (item sources + monster Greet
  Commands). MmeDatabase.GetTextblockDetail renders the roll->effect
  action lines (giveitem/summon numbers resolved to names) + the
  resolved Called-From chain.
- NavigateFromLine gains a "[TB n]" branch raising RequestTextblock;
  MainWindow opens a LookupResultsWindow with the TB detail, itself
  jumpable (items/monsters/rooms in the block).
- PINNED: Textblock_ResolvesToContainerName.
- DIVERGENCE: the OG scrapped recursive TB expansion (2025.11.09) and
  shows raw "Textblock N"; this is the friendlier behavior the user
  requested, layered on top. Item#-in-Obtained-From entries (the OG's
  already-resolved containers) still render as "Item: <name>".

DEFERRED (next dedicated session, agreed w/ user): the full missing
monster-stats block — Total Lairs, AVG DMG/mob, AVG Exp/HP/AC/DR/MR,
Effective MagicLVL, Effective SpellImmu, Other Lair Mobs, Damage vs
Mob, Damage vs Lair, Scripting Estimate. These need the LairInfo
averages aggregation + per-mob combat sim + scripting model wired
into PullMonsterDetail; too large to do faithfully here.

Suite: 847/847 (+2). Shipped as BETA 22.

## Session 45 (cont.) — 2026-07-05 — BETA 21: nav-swap guard, preset display, new icon, Wave I compare lists

MAP/ROOM JUMP SWAP (user: jumps populate room into the map field and
vice-versa):
- Traced every path — NavigateFromLine regex (group1=map, group2=room),
  NavigateToRoom -> ShowMap(map, room), the split-box sync, and the
  XAML bindings — all already map-first and correct; a two-digit-map
  guard test (17/2269) confirms no swap.
- The one genuine latent swap: the orphaned legacy MapJump() still
  parsed via ExtractMapRoom (the EXIT-string parser, room-first).
  Hardened to a clean "map/room" split. The split boxes already used
  MapGoSplit, so this only affected the vestigial combined box.
- Pinned: NavigateFromLine_RoomJump_PutsMapAndRoomInRightFields.

PRESET SELECTION NOT DISPLAYING (user): cmdMapPreset reset
SelectedIndex = -1 right after jumping, wiping the shown text. Now
keeps the selection; a DropDownOpened handler clears it on open so
re-picking the same preset still re-jumps.

APP ICON: the AI-render read as mush at taskbar sizes. Replaced with
a crisp vector-drawn gold-on-navy shield + centered sword, rendered
at 16-256 (multi-size .ico + png).

WAVE I — COMPARE LISTS (frmMain lvWeaponCompare/lvArmourCompare/
lvSpellCompare/lvMonsterCompare :4106+): the Compare tab is now four
grids (Weapons/Armour/Spells/Monsters) mirroring each browse list's
columns. MainViewModel.CompareLists.cs holds four Observable
collections with Add-single ("Add to Compare"), Add-All ("Add All to
Compare", menu :539, routed by grid row type), Clear (cmdCompareClear
:22083), and Refresh (re-pulls held rows from the current browse
lists, keeping filtered-out rows stale rather than dropping them).
Context menus on Weapons/Armour/Spells/Monsters grids gained both
add variants; Weapons/Armour compare rows double-click back to their
browse grid + tab. Replaces the old 2-item A/B compare panel.
- DIVERGENCES: Sundry has no OG compare grid (excluded); the OG's
  saved-list persistence (Index >= 500 "saved" variants) and the
  compare location sub-panel (lvMonsterCompareLoc) are unported;
  compare rows are snapshots (Refresh re-pulls, no live binding).

Suite: 845/845 (+5). Shipped as BETA 21.

## Session 45 (cont.) — 2026-07-05 — BETA 20: backstab fix, app icon, rooms overhaul, jump navigation

BACKSTAB (user: EQ-tab BS hugely different from the BS Calculator,
which matches the OG/game source ±1):
- ROOT CAUSE: three MORE externalized CalculateAttack inputs no caller
  supplied. (1) classStealthFromClass / raceStealthFromRace — VB6
  calls GetClassStealth/GetRaceStealth inside CalculateAttack
  (:1246-:1250); the port externalized them and DamageOutputService
  passed neither, so a Ninja was treated as a NON-stealth class and
  ate the ×75% surprise-damage penalty (:1614 branch). (2) The
  nGlobalCharAccyAbils/Other/Items composition consumed by
  CalculateBackstabAccuracy — LoadedCharState had the fields, the
  equip recalc never exported them.
- FIXES: MmeDatabase.GetRaceStealth (race Abil 102; class is Abil
  103); EquipmentStatsService exports the accy trio into
  res.Loaded; both stealth flags now passed at all five
  CalculateAttack sites; BsCalcVm seeds its Class Stealth checkbox
  from the character's class like frmBSCalc.
- PINNED: EQ surprise path == BS Calculator exactly (307 avg / 199
  min on the ninja fixture) — EqSurprise_Agrees_WithBsCalculator.

APP ICON: user-supplied art (blue shield/sword chosen), white
background flood-filled to transparency, fringe faded, multi-size
app-icon.ico (16-256) as ApplicationIcon + window icon.

ROOMS TAB (user batch):
- Map/Room jump split into two boxes (Tab between numbers; Enter or
  Go runs the jump). The old combined "23/5001" box removed.
- Toolbar rebuilt as a WrapPanel — the old single-row StackPanel
  overflowed 1080p, which visually clipped the Find controls (the
  root cause of "Find doesn't work at all": headless tests prove the
  find logic worked; the controls were unreachable).
- Map background black (OG) — MapCanvas.OnRender.
- App-wide TextBox VerticalContentAlignment=Center (the off-kilter
  numbers).
- The OG's ten built-in presets (frmMain :30719-:30728: Newhaven
  1/2140 … Rhudar 2/2523) now lead the preset list, saved presets
  dedup-appended after.
- Find Rooms with Exits ported (FindRoomWithDirections :22862): name
  contains/exact + exact exit-direction mask; hidden/text/remote/
  timed exit types don't count, map-changes do; 100-hit cap with the
  OG's message. New RoomFindWindow; results jumpable.

JUMP NAVIGATION (user batch):
- Navigation spine: MainViewModel.RequestTab event + NavigateToRoom +
  NavigateFromLine ("(map/room)" tail → Rooms tab jump; "Monster:
  name (N)" → Monsters tab + grid selection, clearing the Find box
  when it hides the row).
- Item detail bottom pane now appends "Obtained From" + "References"
  resolved via ResolveLocationRefs (PullItemDetail :765-:767) —
  monsters, shops, rooms, chest textblocks.
- Double-click jumps: Weapons/Armour/Sundry detail lines, monster
  dossier lines (Spawns-via → Rooms tab), What Casts This / Where
  Summoned / Chest Contents result windows, Find-with-Exits results.
- DIVERGENCES: Item:/Spell:/Shop: line jumps unported (rooms +
  monsters only); the exits search runs without the OG progress
  bar/cancel; RoomFindWindow is modeless vs the OG's modal popup.

SIZING (user screenshots): "No limited items" moved to its own line
(clipped in the Find Best row); the slot:value and Attk helper texts
wrap at 400px; the old duplicate "Additional Weight" manual-adjust
row removed (superseded by the OG toggle; slot 0 stays reachable via
"0:x" in Manual Adjustments); More Filters window widened 560→640
(the Speed<=/STR<=/Ability row clipped).

Suite: 840/840 (+2). Shipped as BETA 20.

## Session 45 (cont.) — 2026-07-05 — BETA 19: the Attk 511-vs-501 fix + Use Additional Weight

USER BUG: OG shows "Attk: 511 @ 5", port shows "501 @ 5" with matching
panel stats; every weapon differs. Task: verify the combat formulas
against the original source.

ROOT CAUSE (found by line-reading CalculateAttack modMMudFunc
:1167–1934 + GetDamageOutput modMain :4823):
  nRoundTotal = nRoundPhysical + Round(nExtraAvgSwing × swings × hit%)
The second term is WEAPON CAST-PROC DAMAGE: the OG builds sCasts from
the weapon's Abil 43 (casts spell) + 114 (%chance), then regex-parses
its own display text for the damage numbers and proc percent. The C#
AttackMath had the entire path ported (regexes verbatim, quirk pins)
— gated on a castDescription delegate that NO CALLER EVER SUPPLIED.
Same for LoadedCharState (panel Q&D + equipped-weapon stat arrays for
the crit subtract/re-add cycle) and uiAccuracyFallback (the
lblInvenCharStat(10).Tag accuracy fallback): the types existed,
nothing wired them. Proc-carrying weapons therefore lost their entire
proc contribution — the user's 10-point delta (≈2/swing × 5 swings).

FIXES:
- EquipmentStatsService captures the nGlobalChar* session state during
  every recalc (frmMain :27425–27546): weapon Number/Accy/Encum per
  hand, per-abil Crit/MaxDmg/BS-accy/BS-min/BS-max/MA-trios/Stealth/
  STR/AGI contributions, and the panel Q&D (:27936, set only when >0)
  — exported as EquipmentStatsResult.Loaded (Core LoadedCharState).
- AttackConfig.LoadedState carries it; BuildAttackConfig wires it.
- MmeDatabase.PullSpellEqForCasts: the sCasts segment builder in the
  exact shape the parser regex needs ("Damage/Damage(-MR)/DrainLife
  X to Y ... for N rounds"), with the OG QUIRK PRESERVED: a SpellDmg%
  bonus scales the printed numbers (modMMudDatabase :249, bGetsBonus
  per :74–84 — abils 1/17 stock, 8/18 GMUD) and the parser scales the
  matched numbers AGAIN (modMMudFunc :1835) — a faithful double-apply.
  Abil 144 rewrites "Damage(-MR)" → "Damage" (:339).
- All five CalculateAttack sites in DamageOutputService now pass
  loadedState + uiAccuracyFallback, and the backstab + weapon paths
  pass castDescription (the sCasts build is weapon-gated internally).
- PINNED: hellblade(325) casts sunsword(408) [Damage 6 to 30] @ 25% —
  hand-computed VB6: extraAvgHit 18, extraAvgSwing Round(4.5)=4
  (banker's), total = physical + Round(4 × swings). Exact match,
  end-to-end through GetDamageOutput. Plus LoadedState capture pins
  and a regex-shape pin on the composed cast segment.

USE ADDITIONAL WEIGHT (user request — the OG encum was 1159 vs the
port's 1064; encumbrance slows swings, so the OG lets the pasted
character's carried weight feed the swing calc):
- chkInvenAddWeight/txtInvenAddWeight ported: VM UseAddWeight +
  AddWeight, checkbox + box under the black EQ panel.
- EquipmentStatsService.AdditionalWeight applied at the OG's exact
  spot (:27251, after the item loop, before the encum% math) with the
  "Additional Items (N)" StatTips(0) line.
- Paste auto-fill (frmMain :35784–:35790): the parser's existing
  LeftoverWeight (pasted Encumbrance − resolved equipment, coins ride
  inside the pasted total) now sets AddWeight + turns the checkbox ON
  (even at leftover 0, like the OG). Replaces the old _manualAdj[0]
  stopgap. DIVERGENCE: the OG's MME-clipboard-export path (CurrentENC
  + per-denomination coin keys, Round(qty/3) each) is unported — the
  C# has its own save format.

FIDELITY FIX FOUND BY THE NEW TEST: PopulateCharacterProfile (:22–23)
reads the LIVE PANEL encum (lblInvenCharStat 0/1) — the C# sheet was
feeding the profile from the pulled character-strip values, which go
stale when the combat-entries pull is off (wrong encum% → wrong
swings/Q&D/walk-speed). BuildSheet now sources the live panel slots
with a strip fallback when no recalc has run.

Suite: 838/838 (+5). Shipped as BETA 19.

## Session 45 (cont.) — 2026-07-05 — BETA 18: verbose monster attacks + the lag fix

Two user reports: (1) the verbose monster attack information from the
OG's detail pane was missing entirely; (2) the lists lag hard vs the
VB6. (Also asked: is memory capped? No — no cap exists; the lag was
algorithmic.)

PERF (the lag):
- ROOT CAUSE: ApplyBrowseFilter ran ItemUsabilityService
  .GetUsableItemNumbers — a full-DB usability scan over every item —
  on EVERY refilter, i.e. every Find-box KEYSTROKE, and rebuilt every
  tab's list each time. The OG computes usability during population.
  FIX: stamp-cached on (level, class, align, min-lvl, in-game, GMUD).
- DecorateMonsterRows (lair-average regex + CalcExpPerHour + damage
  tiers per monster) also ran per keystroke — now memoized on the
  attack ConfigKey + a _charRev counter bumped by every equipment
  recalc; Find keystrokes reuse the decorated list.
- DataGrid style gained EnableRowVirtualization +
  VirtualizingPanel.VirtualizationMode=Recycling (cheaper rebinds).

VERBOSE MONSTER DETAIL (PullMonsterDetail, modMain :2534–3020 + :3874,
read line-by-line) — built LAZILY on selection as typed DossierLines
rendered by an ItemsControl with the OG's colors:
- Dmg/Round * (RGB(204,0,0)): AVG from Monsters.AvgDmg (NMR ≥ 1.8
  path) + Max from the sim's theoretical GetMaxDamage
  (CalculateMonsterAvgDmg(mon, 0) equivalent via MonsterSimLoader),
  with the "before character defenses, calculated when DB created"
  suffix.
- Dmg/Round vs char (RGB(144,4,214)) / vs party (&H40C0) lines when
  the calc tables hold values (TryGetVsChar/TryGetVsParty added to
  MonsterDamageService), with the "{N} round sim" suffix.
- Between Rounds: MidSpell rows with the running-nPercent difference
  quirk, spell name + number, and the inline effect text; colored by
  spell ability — fear/slay (60/95) red bold, poison (19) green bold,
  confusion (71) orange bold RGB(255,128,0), illusion (13 ≤ −999)
  bold.
- Attacks: (Round(AttTrue%)|cum-diff)% + AttName ("Attack N"
  fallback); melee/rob rows Min-Max / Accuracy / Energy with the
  Fix(monsterEnergy / attEnergy) "Max Fx/round"; Hit Spell rows with
  inline EQ + ability coloring; spell attacks (AttType 2): "Spell:
  [name(N), EQ]", "Target: <enum>" when Targets ∈ {9,11,12}
  (SpellIsAreaAttack :4783), "Success %: AttMin", Energy.
- Greet Commands (Textblock N, green) and "Spawns via ..." resolved
  location lines (cap 30, count overflow noted).
- NEW DB surface: GetMonsterAttackRecord (incl. AttTrue%),
  SpellAreaInfo, PullSpellEqInline — a lean PullSpellEQ
  (modMMudDatabase :4064): damage/effect range at cast level via the
  ported SpellMath + SpellDoesDamage, the ability list with values
  (damage abils 1/17 folded into the range), duration tail.
- Palette: the OG's four runtime colors added VERBATIM to both themes
  (ThDmgRed #CC0000, ThDmgPurple #9004D6, ThDmgParty #C04000,
  ThConfusionOrange #FF8000) + contrast-gate pairs at the
  OG-faithful floor.
DIVERGENCES (logged): Damage vs Mob + Scripting Estimate sections
ride the deferred calc-columns wave; PullSpellEqInline omits the
EndCast recursion and percent-column variant; row spell-jump
navigation unported; Greet/Type placement differs from the OG's
fixed-row order; HPs regen-timing text ("every 90 seconds
[18 rounds]") still pending.

Tests: +2 (Session45MonsterVerbose): the rat's melee rows (exact
Min-Max/Accuracy/Energy Max-x shapes), the red Dmg/Round suffix,
Spawns-via presence; dark cleric Between Rounds + spell-attack row +
Success % + non-empty inline EQ. Suite: 833/833.
Shipped as BETA 18.

## Session 45 (cont.) — 2026-07-05 — BETA 17: UI verification gates + the render-blindness fixes

Root problem named by the user: WPF cannot run in the Linux dev
environment, so render-time bugs (bindings, template visuals,
contrast) shipped blind. This beta adds permanent verification in two
layers, and fixes everything the new gates + the user's reports caught.

REPORTED FIXES:
- ExpCalcWindow rebuilt on a record (ExpChoice): the combos bound
  ValueTuples, whose element names are not reflection-visible
  properties — DisplayMemberPath resolved to nothing and every
  dropdown row rendered blank. Window widened to 540.
- GMUD data version > 1.85 (Q&D /40) now DEFAULTS OFF — vanilla /30
  is what everyone runs; the /40 behavior is a GMUD realm setting,
  opt-in only (user call).
- RadioButton had NO style at all → black text on the dark panel
  (the unreadable By Mob / By Lair report). Implicit style added.
- MenuItem fully RE-TEMPLATED: property triggers alone lose to
  Aero2's template-internal highlight (the hardcoded light-blue wash
  that made dark menu hover unreadable — and why the beta-16 style
  trigger didn't take). One template covers all four roles with
  themed check glyphs, submenu arrows, gestures, and ThSel highlight.
  ContextMenu style added.
- STRUCTURAL: the implicit control styles lived in
  MainWindow.Resources — every tool window rendered UNTHEMED (the
  white exp-calc screenshot). All 19 implicit styles moved to
  App.xaml (application scope); MainWindow keeps only its keyed
  styles and ctx menus.

STATIC GATES (Linux-runnable, now in the suite — Session45UiGateTests):
1. Palette contrast: the palette now lives as data in
   Mme.Core/Theme/ThemePalette.cs (single source; the WPF
   ThemeManager consumes it). WCAG floors per text pair (body 4.5,
   chrome 3.0, OG-faithful semantics 2.2, warn/modified tested vs the
   black EQ panel where the OG used them).
2. Interaction-state visibility: RGB distance ≥ 25 between changed
   and resting surfaces (luminance-only WCAG calls Classic's
   light-blue hover invisible when it isn't; the retired terminal
   skin's black-on-black dropdown highlight scores 0 here).
3. DynamicResource key existence: every {DynamicResource ThX} in the
   XAML must exist in the palette.
4. Tuple ban: window code-behind may not build tuple collections
   (the exp-calc bug class). GetChestContents moved to a ChestEntry
   record to keep the rule exception-free.
5. Binding-path audit: every {Binding X} (skipping RelativeSource /
   ElementName) must resolve against reflected VM/Data properties or
   the window's own code-behind members (textual harvest incl.
   record positional params).

GATE CATCHES ON FIRST RUN (both real, both shipped blank until now):
- The spellbook "Diff" column bound a property that didn't exist —
  SpellGridRow + query gained Diff (VB6 :5447 raw Spells.Diff; the
  Cast% calc variant remains with the deferred calc-columns wave).
- The Exp/Hr "Attack:" combo bound AttackModes, which didn't exist —
  the combo has been EMPTY in every beta. AttackModes list added
  (a0..a7 per MmeAttackType).

RENDER SMOKE (the eyes — Windows CI):
- tests/Mme.App.RenderSmoke (net8.0-windows): renders MainWindow +
  every tool window in BOTH themes offscreen via RenderTargetBitmap,
  saves PNGs, and FAILS on any WPF binding error via a
  PresentationTraceSources trap. A DisplayMemberPath typo is now a
  red build, not a desktop discovery.
- .github/workflows/render-smoke.yml: ubuntu job runs the logic
  suite; windows-latest job runs the render smoke and uploads the
  screenshot gallery as an artifact per push.
- SHIP RECIPE CHANGE: local tests run via explicit path
  `dotnet test tests/Mme.Core.Tests` (the smoke project is CI-only).

Test-infra side fixes: the Wave12 fixture Spells table gained Diff
(SQLite's double-quote fallback had returned the literal string); the
StaticResource audit now resolves keys through App.xaml scope, as WPF
itself does.

Suite: 831/831 (+5 gates). Shipped as BETA 17.

## Session 45 — 2026-07-05 — BETA 16: theme overhaul (Dark replaces the terminal skin) + PII scrub

User audit + my audit of the MUD terminal theme found structural
contrast failures: the ComboBox dropdown highlight (ThSel) was
#000000 on a #000000 input — invisible selection; MenuItem hover was
#003300 on #0C0C0C (~1.3:1); disabled text #446644 on black (~2:1).
Root cause: the ANSI palette is all pure-saturation corner colors
with no mid-tones, so every WPF interaction state (hover / press /
selection / disabled) lacked a neighbor shade. Decision (user call,
seconded): retire the terminal skin.

CHANGES:
- ThemeManager rewritten: "Classic" (unchanged OG light chrome) is
  now the DEFAULT; "Dark" replaces "MUD". Saved theme.json values of
  "MUD" migrate to Dark on load.
- Dark palette rules: chrome is neutral Windows-dark (#1E1E1E /
  #252526 / #3F3F46 / #094771 selection / #3E3E42 hover / #6D6D6D
  disabled — structural, every state has a defined neighbor). ALL
  semantic/text colors are the OG's own runtime colors verbatim:
  red &HFF #FF0000, usable/learned green &HC000 #00C000, modified
  yellow RGB(255,255,0), warn orange RGB(255,157,0), ShowAll grey
  RGB(192,192,192) (works on both themes as-is), neutral #909090.
  The only two adjustments dark requires: black body text → #F0F0F0
  (inversion) and the section-header blue lifted in luminance only
  (#5C85E0) — flagged, not flair.
- Same font in both themes (the Consolas switch is gone — the skin
  no longer changes layout metrics). EQ stat panels keep the OG
  green-on-black inventory look in both themes (faithful).
- New semantic keys: ThAccentBlue, ThWarnOrange, ThModifiedYellow,
  ThUsableGreen, ThUnusableRed. Four hardcoded XAML Foregrounds
  (#0000C0 dossier ×2, #C00000 evil, #FF9D00 warn) converted to
  DynamicResource so they follow the theme. Map legend swatches and
  the map palette stay literal (the map's own colors).
- Menu: "Classic Theme" / "Dark Theme"; sync logic updated.
- PII scrub (standing rule): a first-name reference in
  ThemeManager.cs and one in Lairs.cs removed; PORT_LOG.md scrubbed
  (24 name references → "the user"; one alias reference → "the OG
  author") — mechanical redaction only, entry substance untouched;
  the beta-6 nickname replaced with "CLASSIC THEME" per the naming
  decision. Repo-wide sweep now returns zero hits.

Suite: 826/826 (no behavior changes — theme + docs only).
Shipped as BETA 16.

## Session 44 (cont. 8) — 2026-07-05 — BETA 15: paste/vitals audit (user bug report)

Report: pasting a MegaMUD character left the HP / HP Regen boxes at 0,
mana boxes likewise, and the Combat/Equipment Entries required a
manual "Pull now".

VB6 archaeology (read line-by-line): PasteCharacter (:36654) never
fills the HP/mana boxes — in the OG, txtCharHPRegen/txtCharManaRegen
are equipment-slot boxes auto-OVERWRITTEN from slots 16/17 on every
recalc (:27977), and HP / Mana / Regen / Meditate are COMPUTED labels:
lblCharMaxHP.Tag = Round((sMin+sMax)/2)+slot5 (:38243),
lblCharRestRate.Tag = CalcRestingRate(lvl, effHea, slot16, resting)
(:38226), lblCharMaxMana.Tag = maxMana+slot6 (:38336),
lblCharManaRate.Tag = Fix(manaRegen) (:38315). The port had made all
six panel boxes dead manual inputs feeding CalcExpPerHour, and
BuildSheet → Populate echoed them circularly (prof.Hp = ui.CharMaxHp).

FIXES:
- ApplyGameTextPaste now calls PullCombatEntriesFromEq()
  unconditionally after the recalc (the OG's EQ→strip dataflow).
- PullCombatEntriesFromEq gained CharStealth = slot 19 (was never
  pulled) + its notify entry.
- NEW AutoFillVitalsFromCharacter, run from NotifyEquipPanel on every
  recalc: computes the VB6 Tag values DIRECTLY (not via Populate) —
  CharHp = Round((sMin+sMax)/2)+slot5 via CalcMaxHp min/max,
  CharHpRegen = CalcRestingRate(lvl, EffHea, slot16, resting:true),
  CharMaxMana = CalcMaxMana(lvl, mageryLvl)+slot6 (0 for no-magery
  classes), CharManaRegen = Fix(CalcManaRegen(..., slot17)),
  CharMeditateRate = Fix(CalcManaRegen(..., slot17, meditating)).
  Boxes stay editable; the next recalc re-fills (the OG's overwrite
  behavior for the regen boxes, extended to the computed labels since
  our boxes ARE the exp-engine inputs). Gated on class+level.
- Derived.cs conflation fixes: CharDerivedRest and CharDerivedMana
  previously used the CharHpRegen/CharManaRegen BOXES as bonus inputs;
  with the boxes now holding totals that would feed back — both now
  read slots 16/17 (the VB6 truth).
- NOTE (not a bug): the boxes fill with the CALCULATOR's values, not
  the pasted "Hits: 246/246" — the OG never reads Hits: either; the
  in-game total includes CP-bought HP the calculator can't know.
  SpDmg% stays manual on stock (GMUD-only, from SC :28079).
  CharDamage/FirstRound/Surprise strip = the Choose Attack flow,
  legitimately manual.
- RecalcEquipmentForTests() public seam.

Tests: +2 (Session44PasteAudit): the user's paste shape (Human/Ninja
L20 + ninjato) → level/stats/class landed, CharHp/CharHpRegen > 0,
CharMaxMana == 0 for the manaless ninja, CharStealth/CharEncumMax
pulled without Pull-now; direct-set Mage L20 → mana + regen > 0 and
HP grows on a level-40 recalc (refresh-on-recalc pin).
Suite: 826/826. Shipped as BETA 15.

## Session 44 (cont. 7) — 2026-07-04 — BETA 14: Wave H — small tools + lookup ctx + chest contents

Read line-by-line: frmCoinConvert (ConvertCoin/CalcCoin/charm button),
frmExpCalc (CalcExp + cmdCalcExp_Click clamps), frmNotepad,
frmMain LookUpSpellCast (:31324), cmdSundryChests_Click (:23878),
GetChestItems (modMMudDatabase :5224), GetLocations (modMain :6539 —
scoped subset).

NEW:
- MmeDatabase: GetChestContents + ChestDig — the full chest parser:
  ItemType-8 gate with the OG's message shapes, abil 43 → spell →
  abil 148 → textblock (AbilVal or MinBase/MaxBase fallback), root
  "random N" recursion, QUIRK PINS: cumulative-difference percents
  ((per1−per2)/100 running), duplicate-item compound-failure merge
  (pct += fail·p·mod; fail ×= 1−p), nest cap 5, display Round(·,1)%.
  ResolveLocationRefs — lean GetLocations for the lookup ctx items
  (Monster/Item/Spell/Shop #, Room map/room, lair group indexes →
  GetRoomName). GetSpellCastedBy, GetExpTableList.
  DIVERGENCE: GetLocations percent columns / shop values / textblock
  and NPC refs unported.
- Windows: CoinConvertWindow (copper-routed ConvertCoin, cap
  9,999,999,999, Round(·,8), Fix(c/3) weights, charm modifier
  1−((Fix(CHM/5)−10)/100) markup/discount button gated on the char
  filter and CHM ∉ {0,50}); ExpCalcWindow (class ExpTable+100 + race
  ExpTable, clamps 2–500/10–500, cumulative + Needed via
  rules.ExpNeeded); NotepadWindow (session text, selection-aware
  copy, Save-As; DIVERGENCE: no INI persistence);
  LookupResultsWindow (lean frmResults).
- Ctx: Spells "What Casts This?", Monsters "Where/How is this Monster
  Summoned?", Sundries "View Chest Contents" + "Copy Chest to
  Clipboard" (sundry grid gained its own ctx menu). Tools menu +3.

Tests: +4 (Session44WaveH): magic-missile Casted-By resolution
(Monster #379), rat Summoned-By lair-room lines (>50), chest scan
(non-container refusal + percent (0,100] sanity across the real
chests), exp-table monotonicity. Suite: 824/824.
Shipped as BETA 14.

## Session 44 (cont. 6) — 2026-07-04 — BETA 13: Wave G — By-Lair mode, More Filters, class spellbook

Read line-by-line: AddMonster2LV lair columns (modMain :5690–6110),
FilterMonsters lair + extras gates (frmMain :25360–25610),
GetLairAveragesFromLocs (modMMudDatabase :161–360), frmMonsterFilters
(cmdExec :833, Form_Load :996), GetMonsterAttackSummary
(modMMudDatabase :2598–2790), SpellHasAbility (:4874), the class
spellbook launch (frmMain :22034).

NEW:
- LairInfoService.GetLairAveragesFromLocs: regex per-lair extraction,
  QUIRK PINS — divisor = TOTAL matches incl. non-contributors, Exp/HP
  regen-weighted, nPossSpawns = InstrCount("Group:") + nLairs, magic
  level MODE with ties-to-higher, accy majority ≥ 51%, NMobs left
  unrounded, MaxRegen floor 1. DIVERGENCE: DF_Flags rollup unported.
- MmeDatabase.SpellHasAbility + GetMonsterAttackSummary (special-
  attacks mode): unique-accuracy percentage buckets on AttTrue%,
  dominant ≥ 51% majority with the |max−dom| ≤ 2 collapse, special
  scans of DeathSpell/MidSpells/spell-attacks/hit-spells for abils
  19/71/60, and the MidSpell running-nPercent alternating-difference
  quirk preserved verbatim. DIVERGENCE: attack-type letter strings
  (bGetSpellAttackTypes) unported.
- MonsterBrowseRow: SummonedBy/HpRegen/raw extras fields/abils list/
  lair display decorations + text getters; By-Mob Exp/(Dmg+HP) column
  formula (:5998) implemented as a bonus; DamageResolved tier value.
- MainViewModel.MonsterLairMode: the by-lair decoration pass — the
  RecalculateLairsCore setup replicated per pass (as the OG refilters),
  lair-avg HP/Damage with "*", the two CalcExpPerHour branches
  (lair averages vs RegenTime>0/"Room" GetDamageOutput), generic-HP
  fallback (dmg·2 / 5%), ÷party, Recovery %; per-pass average cache
  keyed on Summoned By (config identity = the bundle key). By-Mob
  passes also resolve tier damage now; extras NumLairs/NumMobs gates
  get lair averages on demand in By-Mob (VB6 computes unconditionally;
  identical results, deferred cost).
- MainViewModel.MonsterExtras + MonsterFiltersWindow: the full
  frmMonsterFilters state (reset defaults + save clamps :842/:892),
  the :25370–:25530 gate ports (cash ladder, align whitelists,
  ability absent-passes-<= rule), ShowAll → DoesNotMatchFilter grey
  RGB(192,192,192) row style. Filter gates updated: HP vs lair avg,
  EXP vs exp/hr in lair mode, DMG By-Mob only. DIVERGENCES:
  nMonsterPossy array unported; name Find hides under ShowAll.
- Class spellbook viewer: BuildSpellBook(forClass, level),
  SpellBookWindow class mode (level 999), ClassesCtxMenu.

Tests: +5 (Session44WaveG): rat attack summary (10/10, no specials),
lair averages (lair count, weighted HP, the possSpawns identity),
by-lair decoration (asterisks + Recovery %), extras gates (undead
drop, ShowAll grey, cash ladder, ability absent-pass/fail pair),
class spellbook (crane 838 in Mystic at 999, absent for Warrior).
Suite: 820/820. Shipped as BETA 13.

## Session 44 (cont. 5) — 2026-07-04 — BETA 12: Wave F attack simulator window

The 2,113-line frmMonsterAttackSim, read line-by-line (cmdRunSim
:1808, cmdResetUserDefs :1744, Form_Load :1930, ResetFields :1963,
LoadMonsters :1989, GotoMonster :2099, control-tree defaults: rounds
"2000", Dynamic CHECKED). As predicted, a shell: the engine and
loader were already ported, so the window is pure orchestration.

NEW:
- MonsterSimVm.cs: fresh sim per run, rounds cap 500,000, dynamic
  diff 0.0001 (the batch vs-char calc uses 0.001 — the OG genuinely
  uses two thresholds), caps WITHOUT class ('add class here at some
  point?' comment preserved as behavior), >0-gated defense inputs
  (MR default 50), elemental resists Col/Fir/Sto/Lit/Wat (OG index 4
  unused), Always-Dodge → DodgeBeforeAc, Hide-Energy, Log-Max-Round-
  Only. Reset[Zero] / Reset[From Char] with the party branch (PARTY
  caption, Exp/Hr boxes, resists+prot zeroed, AM when count>1).
  Results: AvgDmg/Rnd Round(·,1), Max/Seen, Phys/Spell breakdown,
  per-attack rows with Round(·,3)·100 percentages, the DmgResist
  100%-when-zero-damage special, ResistDodge spell-vs-phys branch,
  combat log. RunSim(randomSource?) seam for deterministic tests.
- MonsterSimWindow.xaml(.cs) + InverseBoolConverter (rounds box greys
  under Dynamic, as the OG). Ctx: Monsters → Attack Simulator (with
  GotoMonster seeding, frmMain :20075). Tools menu entry.

DIVERGENCES: no progress bar (synchronous, fast); no window-position
persistence.

Tests: +3 (Session44WaveF): config pins (class-less HitMin 8, the
0.0001 threshold, DodgeBeforeAc, rounds), rat run result/format pins
(TrueCast 100 single-attack, header shapes self-consistent with sim
totals), reset paths (zero + party branch). Suite: 815/815.
Shipped as BETA 12 — all four context-menu windows complete.

## Session 44 (cont. 4) — 2026-07-04 — BETA 11: Wave E calculator windows

Read line-by-line first: frmSwingCalc CalcSwings (:1917) + Form_Load
seeding (:1819) + CalcTrueAverage (modMMudFunc :4446); frmBSCalc
CalcBS (:1004); frmHitCalc DoHitCalc (:951) + SetHitCalcVals (:1112)
+ GetMonsterData (:1597); the ctx seeding sites (frmMain :34340
Swing/BS GotoWeapon, :34155 Hit-Calc attacker/defender prompt).

All three windows are UI shells over ALREADY-PORTED Core math
(CalcEnergyUsed, AdjustSpeedForSlowness, CalcEncumbrancePercent,
QuickAndDeadlyBonus, CalcBsDamage, CalculateAttackDefense,
BackstabAccuracy, CalculateAttack) — zero new formula ports needed.

NEW:
- ToolCalcs.cs: SwingCalcVm / BsCalcVm / HitCalcVm. QUIRK PIN: the
  swing rotation's `\` and Mod banker's-round the Currency energy to
  Long first, so the table uses ROUNDED energy while "Raw swing" is
  exact 1000/energy. BS: abil-116 gate, Fix((STR−100)/10)×2-stock str
  bonus, abil→equip-slot adds (11/14/15/19), equipped-weapon (+1H
  offhand) dedup subtraction. Hit: attacker/defender seeding matrix,
  mob accy = max melee AttAcc (types 1/3, Att%>0), dodge abil 34,
  see-hidden 57, evil align {1,2,5,6}; vsMob or BS-on-stock disables
  prot-evil; overall = Round(hit − hit·dodge%).
- MainViewModel.ToolAccess.cs: Db/RulesPublic/StatValue/SlotValue/
  EquippedItem/WeaponChoices/MonsterChoices/BuildProfileForTools/
  CharClassHasStealth.
- MmeDatabase: GetWeaponPickList / GetMonsterPickList /
  GetHitCalcMonster.
- Windows: SwingCalcWindow / BsCalcWindow / HitCalcWindow. Ctx:
  Calc Swings + Calc Backstab (item tabs), Hit Calc vs Mob with the
  OG's attacker/defender prompt. New Tools menu hosts all three.

DIVERGENCES (census): no window-position persistence; BS-attacker
accuracy seeding omits the BS-weapon-swap dedup (editable box); no
MegaMUD clipboard-paste button on the TrueAVG strip.

Tests: +4 (Session44WaveE): CalcTrueAverage algebra + clamps, swing
orchestration vs direct Core energy math incl. the rounded-rotation
quirk, dagger BS vs direct CalcBsDamage, manual hit calc vs direct
CalculateAttackDefense + rat seeding pins (AC 0, accy 10, evil).
Suite: 812/812. Shipped as BETA 11.

## Session 44 (cont. 3) — 2026-07-04 — BETA 10: Wave D filter panels

Read first: FilterSpells (frmMain :24923 — magery carve-out at :24960
which ALSO bypasses the MageryLevel/Learnable gates via
skip_magery_check; the five target sets; Kai autolearn exemption),
FilterMonsters (:25203 — main row gates; grey-vs-skip belongs to the
extras window's Show All), FilterWeapons ability block (:25829 — op 0
"<=" / 1 ">="; PRESENCE required, any slot may satisfy; negate-spell
ItemData -1 mode deferred), AddMonster2LV (modMain :5682 — columns
only, no gates; the filter decision is caller-side),
cmdEquipButtons (:19597 — cases 0/1 mass Hold).

NEW: MainViewModel.Filters.cs — SpellPassesPanel (incl. carve-out +
SpellIsUsable via SpellUsabilityService), MonsterPassesPanel (By-Mob
semantics; DMG gate reads the vs-Char/vs-Party tier tables from Wave
C), ItemPassesAbility (shared by the three item tabs), AbilityChoices
(distinct DB abilities named via GetAbilityName forceAll). Rows
extended: SpellGridRow += Learnable/AttType/Targets/Classes/Abil[10];
Weapon/Armour/Sundry browse rows += Abils pairs (sundry query gained
the abil columns). XAML panels on all five tabs; EQ Hold All/None.

FIXTURE NOTE: the Wave-12 synthetic DB lacked the new Spells/Items
columns — schema extended with DEFAULT-0 columns and named-column
INSERTs; its pinned counts unchanged.

DIVERGENCES (census too): monster row filters By-Mob only until the
By-Lair wave; spells vs-Anti-Magic + Calc-vs-MR pending; item ability
negate-spell mode (-1) pending.

Tests: +6 (Session44WaveD/Hold): spell target/atktype/magery gates,
the carve-out (form of the crane #838, Mystic vs Warrior, ReqLevel 40
still gates), monster HP/EXP/Regen/DMG-tier gates vs giant rat, pure
ability-op semantics on dagger #68, browse-flow BS-able integration,
mass Hold. Suite: 808/808. Shipped as BETA 10.

## Session 44 (cont. 2) — 2026-07-04 — BETA 9: Wave C sim wiring COMPLETE

The "big wave" that S43 budgeted 2+ porting sessions for, finished as
wiring. Everything below was read line-by-line before writing:
PopulateMonsterDataToAttackSim (modMMudDatabase :5419),
CalculateMonsterItemBonuses, SpellHasAbility, GetHitMin (modMMudFunc),
SetupMonsterAttackSimWithCharStats (modMain :8110),
CalculateMonsterDamageVsChar/ALL (:8016/:7931), LoadMonsters table init
(frmMain :30543), GetLairInfo mitigation block (modMMudDatabase :713).

NEW CODE:
- Mme.Data/MonsterSimLoader.cs: monster row → sim config. Attack slots
  (physical + spell paths incl. the area-spell-in-attack-slot zeroing
  guard, abil 1→17→8 cascade with MR-flag overrides, AtkSuccess=AttMin
  spell quirk, hit-spells), MidSpell between-round block, weapon+drop
  item bonuses (Limit-0 gate, drop%-scaled, per-step banker's on the
  Integer accumulator), duplicate-name suffixing, has-attack gate.
  SpellRecord gains Targets (+ GetSpellRecord column & offset).
- MainViewModel.MonsterDamage.cs: ConfigureSim (caps via rules incl.
  GetClassArmourType→HitMin; char inputs from slots 2/3/8/24/20/25-29;
  party inputs from the new Exp/Hr boxes), CalcMonsterDamage (single +
  mixed anti-magic weighted split, banker's Round(·,1) into the
  tables), CalcAllMonsterDamage, ClearCalculatedMonsterDamage.
  New props: CharAntiMagic, CharMrOverride, PartyAc/Dr/Mr/Dodge/
  PartyAntiMagicCount, MonsterSimRounds.
- Lairs: options.PartyDamage ← MonsterDamageService.Get (the :2898/:713
  tier chain). SEAM CORRECTION: Func<long,int,long> →
  Func<long,int,double> with per-step banker's into the Long
  accumulator — VB6 :718 adds Currency into a Long field, rounding on
  EVERY step; the old integer-return seam quietly rounded per VALUE.
- UX: Options → Calc Monster Dmg vs Char / vs Party / Clear; Char tab
  Anti-Magic + MR Override; Exp/Hr AC/DR/MR/Dodge/#AM party boxes.

THE FAILING-TEST SAGA (logged in full because it's instructive):
UseCharacter_OverridesExpKnobs went red after the provider landed.
Instrumentation showed lair 10-10-15: generic pass had ALWAYS been the
-1 death sentinel (fallback HP 42 vs 57.8/round); the char pass had
only ever passed because the B4.5 stub ZEROED mob damage when
UseCharacter was on with no provider. With real mitigation live, the
test's unarmed level-1 (then unarmed level-60) character died exactly
as the OG would compute. Test now runs a maxed L60 Warrior in Oneshot
mode — asserting its actual intent (profile overrides the knobs), not
the stub's mobs-hit-for-zero world. Also fixed en route:
CalcMonsterDamage now self-initializes MonsterDamageService (it
previously relied on the lair path having created it — caught when
bisecting with the wiring disabled).

DIVERGENCES (new, also in census): no progress bar/cancel; MR source =
override-else-slot-24; rounds pinned at 500 (Settings dialog pending);
pre-NMR-1.8 loader branches unported (DB is 1.83 — unreachable).

Tests: +4 (Session44WaveCTests): giant-rat loader field pins, caps
config, tier fill + clear restore, mixed-AM party weighting. Suite:
802/802. Shipped as BETA 9.

## Session 44 (cont.) — 2026-07-03 — BETA 8: Wave A + Wave B core

Continuation of the S44 audit session; the user picked Wave A plus the char
base-loading bug ("panel doesn't auto-populate race starting stats").

CHAR BASE LOADING (the reported bug):
- CharRaceNumber setter now ports cmbGlobalRace_Click (frmMain :21444):
  publishes per-stat min-max ranges (StatRanges, shown beside each
  stepper) and raises any stat below the race minimum to it.
- OpenDatabase defaults class/race to the first list entries, level to
  1, and lands stats on race minimums — same outcome as VB6 startup
  (combos land on entry 0, click handler raises 0→min). Implemented at
  open rather than via a synthetic click; faithful-equivalent.
- SteppersResetCopyWeight test updated: it pinned zero-stats-after-open,
  which was the bug.

WAVE A — grid context menus (mnuItemsPopUp :34847, mnuSpellsPopUp
:36197, mnuAuxPopUp subset; InvenEquipItem :28614; LearnOrUnlearnSpell/
LearnSpell/UnLearnSpell modMain :701+; EquipBlessSpell :21805;
ItemIsGetable modMMudDatabase :3310 — all read line-by-line):
- MainViewModel.GridActions.cs (new): EquipOrUnequipItem (worn-slot
  routing incl. finger pairing, InvenAddEquip missing-entry insertion,
  Nowhere-Worn message), ToggleLearnedSpell/ClearLearnedSpells/
  IsSpellLearned, SetBlessSpell (first-open-slot path), ItemIsGetable
  (Gettable flag OR "NPC #" OR "Textblock #" sans "Room "),
  ImAddFromGrid. MmeDatabase gains GetItemGettable.
- SpellGridRow gains mutable Learned + "✓" Lrn cell; grid re-notified
  on toggle (RefreshLearnedSpellColors equivalent).
- ContextMenus: ItemsCtxMenu on GridWeapons/GridArmour/GridSundry/
  GridIm (Equip/Unequip, Add to Compare, Copy Name(s)/Details, Set
  Combat Backstab Weapon, Add to Item Manager); SpellsCtxMenu on
  GridSpells (Mark Learned, Set Combat Attack/Heal Spell — seeds
  AttackSpellNumber then opens ChooseAttackWindow, mirroring
  PopUpChooseCombatGUI(0, spell) —, Set as Bless, copies);
  MonstersCtxMenu (copies). Options menu: Clear Learned Spells.
- F1–F12 tab hotkeys (cmdNav tips); F3 yields to the map find box.

DIVERGENCES (new):
- Wrist second-slot: bInvenUse2ndWrist setting not surfaced (no
  Settings dialog); treated as always-on so wrist mirrors finger
  pairing.
- Monsters Copy Details copies the row summary, not the full dossier.
- Choose-Attack seeding sets VM state before opening the dialog rather
  than passing GotoBackstab/GotoSpell params.

WAVE B — Weapons/Armour filter panels (weapons gate block :25850+,
armour :24611, read first):
- WeaponBrowseRow += Magical (abil 28, last-wins) + WeaponTypeNum;
  ArmourBrowseRow += Magical.
- Weapons: 1H/2H Blunt/Sharp, Non-Magic, BS-able, Limiteds, Speed<=,
  STR<=. Armour: Worn-On combo, 7 type checkboxes, Non-Magic, No Limit.
  All compose with the global usability filter + live Find.
- DIVERGENCE: filters live-apply; numeric boxes at 0/empty DISABLE that
  gate (VB6 val()=0 would hide everything); Limiteds defaults to SHOW
  (VB6 default-unchecked hides limiteds until Apply).

Tests: +7 (Session44WaveATests ×5, Session44WaveBTests ×2) pinning race
baselines on open, race-change raise-to-min, dagger-68 equip/unequip
slot-16 routing, corselet-1212 Torso routing, learned toggle/clear +
grid flag, getable gate, handed/limit/speed filters, worn-on/type
filters. Suite: 798/798.

Census: Wave A/B items flipped to DONE with remainders itemized;
divergences noted inline. Shipped as BETA 8.

## Session 44 — 2026-07-03 — FULL UX PARITY AUDIT (no code shipped)

the user's call: the skeleton exists but core usability isn't beta-ready;
go tab-by-tab, VB6 vs C#, and find what's blatantly missing — including
things assumed wired that aren't. Method: parsed the complete
frmMain.frm control tree (1,354 controls, 11 framNav panes + chrome +
all 7 popup menus) and cross-checked every interactive control against
MainWindow.xaml + ViewModels by grep, not memory.

HEADLINE: **clsMonsterAttackSim was already ported** — Mme.Core/Sim/
MonsterAttackSim.cs, 1,260 lines, Phase 1c, Wave5SimTests green, quirk
pins documented (dead >100 clamp, nResist_Reduction leak) — and it is
referenced NOWHERE in App/ViewModels. GetHitMin/GetHitCap/GetSpellHitCap/
GetDodgeCap: also already in Mme.Core (GameEngineRules, CombatMath).
The S43 handoff declared a 2-session porting wave for a class that was
already sitting in the tree. §0.7 (grep before porting) saved it this
time; the census, the handoff, AND the port log all had it wrong — the
census now carries a CODE-ONLY status precisely for this failure mode.
The remaining sim work is WIRING: SetupMonsterAttackSimWithCharStats
(modMain :8282), nMonsterDamageVsChar/Party tables +
GetPreCalculatedMonsterDamage (:8238), menu items, and the UX surfaces.

Other CODE-ONLY finds (engine done, zero UX):
- Shops charm-adjusted pricing (charm math shipped in item values wave;
  no charm input on the Shops tab)
- Learned-spell state (paste + LearnedSpellPickList exist; no grid
  marking, no mark/unmark action, no Clear Learned)
- Anti-Magic exists in Core (sim/SpellMath) but NO char-level checkbox
  anywhere in the app

Biggest pure-UX gaps (full detail in rewritten FEATURE_CENSUS.md):
1. Right-click context menus on all browse grids — Equip/Unequip from
   grid, Mark Learned, Copy Name/Details, Add to IM/Save List, Set
   Combat Calc to Mob, What Casts This, Where Summoned, chest copy.
   The OG is DRIVEN from these menus; without them the browse tabs are
   read-only reports.
2. Browse filter panels: Weapons (handed/non-magic/BS-able/limiteds/
   speed/STR/DMG-at-AC/ability+negate-spell), Armour (Worn-On! + type
   checkboxes + ability), Spells (magery/target/attack-type/contains-
   ability/learnable/vs-AM), Sundry ability filter, Monsters filter
   row + By Lair/By Mob + party frame + More Filters window.
3. EQ tab: All/None/Empty/Reset hold+slot mass actions, strength
   override, additional weight, unequip-on-paste, clear-all manual
   adjustments.
4. Lists: IM flag action buttons (Stash/Sell/Pickup/Drop/Carry/Invert/
   Copy Text) and the four compare LISTS.
5. Menus/Tools: Recent DBs, Revert/Save-As/Close, NMR import/export,
   Remove All Filters, Settings, and the Tools windows (Hit/Swing/BS/
   Exp calcs, Coin Converter, Notepad, External Map).
6. Rooms: map options checkboxes (follow/marks/tips/Also-Mark), map
   right-click (Follow Up/Down, Redraw From Here), rooms-with-exits.

Census corrections both directions: "Browse tabs: All grids, filters,
dossiers DONE" was FALSE (no filter panels exist); sim MISSING was
FALSE (CODE-ONLY). FEATURE_CENSUS.md fully rewritten from the control
tree with per-tab sections and a SKIP lane (DEBUG/Builder/fonts/donate).

No code, no ship this session. Suite untouched: 791/791. Proposed wave
order logged in census header discussion with the user pending.

## Session 43 — 2026-07-03 — BETA 7: census burn-down (single run)

Answering "how many can you do in a single run" — the honest constraint
is context, not effort; here's one maximal pass. First finding: the
census itself was STALE — steppers, Copy Char, Reset Fields, Additional
Weight (VM), and the ENTIRE CP system (CharDerivedCps: RefreshCPs tail,
Level Required, EXP Req via class+race ExpTable) already existed from
prior waves. My own duplicates (CalcCpCost, GetClassExpTable, CP
summary, stat steppers) written before checking were deleted; the
census file is corrected and the lesson stands both ways — gaps must be
named, and DONEs must be too.

New this run:
- MmeDatabase.GetRaceStats (Races m*/x* stat mins/maxes in calculator
  order, BaseCP, ExpTable).
- MainViewModel.CharActions.cs: StatsMax / StatsResetToRaceMin /
  SnapshotStats + StatsReload (snapshot auto-taken after game-text
  paste and .mmec load — Reload restores the last import), CP-string
  clipboard builder ("s100, i50, ... (N CP remaining)"),
  ManaRegenNeeded (Ceiling of bless mana/round).
- Char tab: button row (Reload / Reset / Max / CP / Reset Fields),
  CharDerivedCps + Mana Regen Needed bound into the computed panel.
- EQ per-slot ">" buttons — VB6 cmdEquipGoto (:22492) +
  mnuEquipGotoItem (:34393) + GotoItem (:26343) read line-by-line and
  ported: type routing (ItemType 1→Weapons; 0+Worn≠0→Armour; else
  Sundry), select + ScrollIntoView in the target grid, and the OG
  "Item N was not found in the current X list. Remove filter and try
  again?" Yes/No flow (clears name filter + Use Character, retries).
  "Add to Compare" fills A then B then rotates B — DIVERGENCE logged:
  OG appends to a compare LIST (still a census gap). Weapon-slot Calc
  Swings / Calc BS entries deferred with their windows.
- GetPreCalculatedMonsterDamage (modMain :8238) READ and traced: it is
  a lookup into tables populated by clsMonsterAttackSim (1712-line
  N-round monster attack simulator with per-class hit/dodge caps and
  char AC/DR/MR/resist inputs). Declared its own dedicated wave rather
  than half-porting it here.

15 new tests: CalcCpCost curve anchor, Dwarf StatsMax=110/Reset=50,
snapshot reload, CP clipboard format, jump routing (dagger→Weapons,
corselet→Armour), filtered-out→unfiltered retry, compare fill,
mana-regen label. **Suite: 791/791.** Ships as MmeExplorer-beta7.

## Session 42 — 2026-07-03 — BETA 6.2: EQ restructure, Choose Attack, census

the user's audit (with OG v2.2 screenshots): menu hover unreadable; EQ tab
clipped at narrow widths; slots should be ONE column (OG); blesses +
quests belong on the CHAR tab; the Attk click needs the real Choose
Attack dialog, not a bare mode menu; and a standing demand — stop
discovering gaps via screenshots.

- MenuItem IsHighlighted trigger → ThBtnHover bg + ThTabSelFg text.
- EQ slots: UniformGrid(2×10) → straight-down StackPanel (OG order via
  EquipSlotCatalog). Blesses + Quests CUT from EQ right column and
  moved to Char tab (bless combos single-column, MaxDropDownHeight 360;
  quests under "Completed Quests"). Fixes the width clipping. Duplicate
  bless UI (Char group from beta 6 + old EQ group) consolidated to one.
- ChooseAttackWindow (src/Mme.App/ChooseAttackWindow.xaml[.cs]) — the
  PopUpChooseCombatGUI dialog: oneshot / weapon (+bash/smash/+backstab)
  / MA picker / manual phys+spell / learned spell / any spell @ level /
  meditate; seeds from and writes back AttackMode, AttackMartialArts,
  CharDamage/CharSpellDamage, AttackSpellNumber/Level, AttackBackstab,
  AttackUseMeditate; RecalcEquipment on Continue. VM gained
  AttackSpellPickList / LearnedSpellPickList / RefreshAttackDisplay.
- FEATURE_CENSUS.md created (repo root + shipped in zip): the full
  DONE/PARTIAL/MISSING ledger — engine (GetPreCalculatedMonsterDamage,
  CP system, Hit Calculator), Char buttons/steppers, EQ jump buttons/
  additional-weight/compare-all, map zoom/MegaMUD/LeadsHere phases,
  compare list, shops cross-links, carried picker. Updated every wave;
  gaps get named instead of discovered.

**Suite: 776/776.** Ships as MmeExplorer-beta6.2.

## Session 41b — 2026-07-03 — BETA 6.1: theme refinement + column collapse fix

**ComboBox fully retemplated** (the 6.0 eyebleed): the stock template
ignores Background on the closed face — replaced with a custom
ControlTemplate (dark ToggleButton face w/ arrow Path, ContentPresenter
over SelectionBoxItem, dark Popup w/ themed border, hover/disabled
triggers). **CheckBox retemplated** (dark box + ThBright check path).
**ScrollBar retemplated** (slim rounded thumb, transparent page-click
RepeatButtons, horizontal trigger).

**COLUMN COLLAPSE (the user's screenshots):** the beta-6 ScrollViewer wrap
around the tab-content host gave DataGrids an INFINITE measure width —
star-sized columns cannot distribute infinite space and collapse to
their 20px minimums (every grid rendered as slivers). Wrap removed;
under-min behavior is now just Window MinWidth/MinHeight 1024x620 (the
OG's fixed-minimum form behavior). LESSON: never put grid-bearing
layouts inside an infinite-measure ScrollViewer.

**Suite: 776/776.** Ships as MmeExplorer-beta6.1.

## Session 41 — 2026-07-03 — BETA 6 "CLASSIC THEME": themes, resize, bless, EQ link

**Theme system:** all chrome converted to DynamicResource Th* keys;
ThemeManager (src/Mme.App/ThemeManager.cs) applies MUD (ANSI terminal)
or Classic (OG grey) palettes into Application.Current.Resources at
runtime — fonts too (Consolas 12 vs Microsoft Sans Serif 11); persisted
to theme.json beside the exe; Options-menu toggle. EQ stat panels
(EqLbl/EqVal, black + Courier) intentionally identical in both themes —
the OG kept black stat panels on the grey app.

**Dropdown fix (the user's screenshot):** implicit ComboBoxItem template —
dark popup, highlight = ThSel (BLACK in MUD per spec, so item colors
survive selection); MenuItem/Separator dark too. Alignment combo items
colored: Evil=Red, Neutral=Gray, Good=White (classic gets readable
equivalents; keys ThAlign*).

**Resizing:** Window MinWidth/MinHeight 1024x620; entire tab-content
host wrapped in an Auto ScrollViewer (undersized windows scroll instead
of truncating); GridSplitters added — Monsters grid↔dossier (dossier
MinWidth 220) and Shops list↔inventory (list MinWidth 220), with
column-index shifts for the moved children.

**Bless slot pickers (cmbCharBless 0..9):** the VM's BlessSlots (built
in the icebox wave, never rendered) now UI — 10 combos, 2-column
UniformGrid, "(none)"-headed pick list from BlessService; selections
feed RecalcEquipment as before.

**Worn-equipment calculator link:** UseEqForCombatEntries +
PullCombatEntriesFromEq on the VM — copies computed slots into the Char
tab Combat/Equipment Entries (accy=10+AccuracyAttackAdj, hitmagic=12 +
non-weapon, +min/max=30/11, BS=13/14/15, encum=0/1, quickness=31,
spellcast=9, dodge=8, crits=EffectiveCrits, MA=34..42); auto-refreshes
on every recalc while checked (the VB6 inven-calc → char dataflow;
logged functional equivalent). Checkbox + "Pull now" button replace the
later-wave stub text.

Also: redundant disabled "Rooms" placeholder tab removed (leftover from
the alpha tab census; the real map tab sits after Lists per the VB6
order). StatAdjust/PresetName dialogs converted to DynamicResource so
they follow the theme.

Anchors: pull copies slot values (+15 accy adj → CharAccuracy≥15,
quickness 4/7 exact, dodge>0); auto-link refreshes on recalc and stops
when unchecked; 10 bless slots each with a "(none)"-headed list.

**Suite: 776/776.** Ships as MmeExplorer-beta6.

## Session 40b — 2026-07-03 — BETA 5.1 hotfix: launch crash

Beta 5 failed at startup: XamlParseException "Item has already been
added: DataGridRow" — the pre-theme LIGHT DataGridRow/DataGridColumnHeader
implicit styles survived below the injected terminal versions. Duplicate
implicit styles in one ResourceDictionary COMPILE clean and only throw at
runtime BAML load, which Linux-side builds can't exercise. Removed the
stale pair; DetailBox darkened; StatAdjustWindow/PresetNameWindow given
their own dark inline styles (they'd have rendered light).

Permanent guard added: XamlResourceAuditTests — (1) every window audited
for duplicate keyless Style TargetTypes, (2) every {StaticResource X}
must resolve to an x:Key in the same file (dangling refs also crash at
load).

**Suite: 773/773.** Ships as MmeExplorer-beta5.1.

## Session 40 — 2026-07-03 — BETA 5 "TERMINAL": theme + Char calc + EQ adjust

**Terminal theme (the user's screenshots as spec):** app-wide ANSI palette —
#0C0C0C window, green labels (#00A800), yellow values (#FFFF55), cyan
headers/tabs (#55FFFF), Consolas base font; implicit dark styles for
GroupBox/TextBox/Button/ComboBox/ListBox/Menu/StatusBar/ToolTip/DataGrid
(+ header/row/cell selection styles); TabBtn restyled (cyan selected on
#003838); helper greys → ANSI #557755; monster dossier pane darkened.

**Char tab derived stats (txtCharStats_Change :40810 family):** new
MainViewModel.Derived.cs — CharDerivedHp (RefreshHitPoints :38345:
min=CalcMaxHP(max−min,·), max=CalcMaxHP((max−min)·L,·), avg=
Round((min+max)/2)+slot-5 gear bonus, "~avg (min-max)+N" format),
CharDerivedRest (CalcRestingRate normal+resting), CharDerivedMana
(RefreshMagic :38427: CalcMaxMana + slot-6 bonus "(base+bonus)", regen
Fix(CalcManaRegen), medi ticks), CharDerivedPicklocks (RefreshPicklocks
:38500: new CharacterMath.CalcPicklocks — stock L·2/L>15 halving into
Fix(((b·5)+(AGI+INT))·2/7), GMUD (INT+AGI+CHA·2+eff·28)/7 — + slot-22
bonus), CharDerivedMr, CharDerivedDodge (engine slot 8/24 totals).
Effective stats from EquipmentStatsResult.EffectiveStats (VB6 .Tag =
base + equip bonus). NotifyDerived rides NotifyEquipPanel. New
MmeDatabase.GetClassHitDice (GetClassMinHP/MaxHP :2072/:2093 — Min=
MinHits, Max=MinHits+MaxHits — + MageryType/LVL). RefreshCPs deferred
(needs race base-stat baselines; logged).

**EQ click-to-adjust (CharStatAdjustmentPrompt :29392):** every EqVal
TextBlock tagged with its calculator slot; MouseUp → StatAdjustWindow
("Enter STAT Adjustment … (will be added to computed value)", seeded
with current, slot-10 stock accuracy note verbatim); writes via new
SetManualAdjustment/GetManualAdjustment on the VM — VB6 clamps
(>9999→9999, <−9999→−999), AC/DR display units ÷10 store ×10; slot 46
(swings/Attack line) opens the attack-type picker (PopUpChooseCombatGUI
≡ context menu over MmeAttackType).

FINDING (test-pinned): dodge (8) and MR (24) manual adjustments feed
the engine plus-pools consumed by CalcDodge/CalcMr — the final slots
are recomputed TOTALS, so the derived MR reads slot 24 directly (a
naive CalcMr+slot re-add double-counts; caught by test, fixed).

Anchors: CalcPicklocks(80, 90agi, 90int)=185 — the user's LIVE level-80
thief screenshot value; Warrior hit dice (6, 6+4); HP string matches
hand-computed CalcMaxHP min/max/avg; Mage 12 mana line CalcMaxMana(20,3)
+ Regen + Medi, Warrior "Max Mana: 0"; adjustment write path 8:25 /
AC 5→"2:50" / clamps; +200 HP slot-5 adj changes the HP line.

**Suite: 771/771.** Ships as MmeExplorer-beta5.

## Session 39 — 2026-07-03 — BETA 4.5: parity march

**Keys-section separation:** ParseInventorySections now returns
(Name, Qty, IsKey) with key names tracked through consolidation;
PasteResult gains KeyItems (subset of Carried resolved via the keys
blob). Item Manager import uses it: key rows get Worn="Key" and
importKeys=false skips exactly the key rows (closes the beta-4
divergence).

**Flag ParseActionAndQty (modListViewExt :659):** both the spaced
" ... x#" and unspaced "...x#" forms; base uppercased; qty ≥ 1;
normalized on Flag set ("carried x3" → "CARRIED x3").

**bDisableKaiAutolearn:** SpellUsabilityService ctor flag wired into all
three gates it touches in VB6 — the andLearnable gate, the Kai
Learnable=0 rejection, and SpellIsInGame — surfaced as Options > Disable
Kai auto-learn; toggling rebuilds the usability service.

**Map presets (cmdMapPreset :23046):** named bookmarks; registry slots
become map-presets.json beside the exe (logged divergence); Preset
dropdown + Save with a name dialog on the Rooms tab.

**Lair "Dmg vs Char: N/clear" line (MapMapExits :33423):** BuildMap now
accepts LairQueryOptions; the VM builds the same ManualAttackOptions
character bundle the Lairs tab uses and passes it per map build; the
tooltip appends the line when NAvgDmgLair ≠ 0. IMPORTANT FINDING
(test-pinned): with UseCharacter on and no party-damage tables, the
mitigation math (options.PartyDamage null, PartyDamageUpperBound −1)
zeroes NAvgDmg — so the line stays absent until
GetPreCalculatedMonsterDamage is ported. The seam is proven by a
synthetic PartyDamage provider test that renders the line end-to-end.
GetPreCalculatedMonsterDamage is now the top ledger item.

Anchors: brass key tagged in KeyItems while dagger isn't; IM key rows
Worn="Key" and importKeys=false skips them; flag normalization all four
shapes; Kai gates flip for way-of-the-owl under the option while magic
missile is untouched; preset save/go round-trip; synthetic-provider dmg
line renders with "Lair Exp:" + "/clear".

**Suite: 765/765.** Ships as MmeExplorer-beta4.5.
REMAINING LEDGER: GetPreCalculatedMonsterDamage (top), frmMap zoom,
MegaMUD marking/scans, LeadsHere spell/monster/textblock phases,
FindRoomWithDirections, row sequence tags, vs-Party dmg label.

## Session 38 — 2026-07-03 — BETA 4: the Item Manager (Lists tab)

**Ported from modItemParse.bas** (the OG author's documented module):
PopulateItemManagerFromParsed (:956), AddSectionItems/
AddListViewRowsForItem (:1198), AddOneRow (:1340), plus
GetShopRoomNames (modMMudDatabase :1641).

- Column set verbatim: Number, Name, Flag, QTY, Source, Enc, Type
  (GetItemTypeEnum), Worn (Key / GetWornTypeEnum / GetWeaponTypeEnum /
  "Nowhere" by ItemType), Usable (ItemIsUsableByChar with
  bIgnoreMinItemLVL=True ≡ our usability call without the min-level
  strip), Value (EvaluateBestPriceForHit "buy / sell" or "(sell) X"),
  Shop (GetShopRoomNames: "Assigned To" Room tokens → room names,
  ", "-joined, fallback shopname(n); "+N more" suffix). Value column
  carries the copper SELL price as its numeric sort key (the VB6
  ListSubItems(9).Tag), wired via SortMemberPath.
- Import flow: the three VB6 MsgBox prompts kept as prompts (Import
  Equipped? / Import Keys? / Clear NON-FLAGGED first?); sections feed
  from the existing GameTextPasteService — Equipped slots, Carried,
  and GroundItems resolved via new MmeDatabase.FindItemByExactName
  (case-insensitive exact, lowest Number, simple plural fallback).
- Flags: user-editable Flag column; Clear Non-Flagged removes only
  empty-Flag rows (the VB6 protected-row semantics).
- Detail + Locations panes (ProcessListViewClick equivalent):
  GetItemDetailText dossier + "Obtained From" tokens as location rows,
  shop tokens resolved to room names with the (sell) marker.
- New MmeDatabase helpers: GetItemBasics, FindItemByExactName,
  GetShopRoomNames.

DIVERGENCES (logged): the paste port folds KEYS into Carried, so
importKeys currently keeps everything and no rows get the "Key" Worn
cell from imports (manual BuildImRow(isKey:=true) supports it);
modListViewExt's ParseActionAndQty Flag "xN" suffix not ported; row
sequence tags (LV_AssignRowSeqIfMissing) not ported. Test-confirmed
faithful quirk: "ropes and grapples" does NOT resolve (SingularizeSimple
only strips the final word's plural, and "rope and grapple" ends
singular) — matches VB6.

Anchors: shop 5 → "(1/355)" room name; dagger row (Enc 35 / Weapon /
Usable Yes / "buy / sell" / Sword Shop room "+1 more" / copper sort tag);
corselet Worn "Torso"; key flag forces "Key"; full paste import hits
Equipped/Inventory/Ground with "2 daggers" qty 2; CARRIED flag survives
Clear Non-Flagged; hellblade locations show "Shop (sell):" + Monster
source; add-by-number happy/sad paths.

**Suite: 759/759.** Ships as MmeExplorer-beta4.

## Session 37 — 2026-07-03 — BETA 3.5: refinement (map nav + spell In Game)

**Map Find Text (cmdMapFindText :22809):** MapBuilderService.
FindRoomByName — case-insensitive Name contains, rooms iterated in
(map,room) order, findNext resumes after the stored last hit
(nMapLastFind semantics), miss resets state and reports "Name not
found." (VB6 MsgBox text). UI: Find box + Next button, Enter/F3 keys.
The Index=2 "FindRoomWithDirections" variant is deferred (logged).

**Leads Here (cmdMapLeadsHere :20284), room-exit phase:** every room
with any of its ten exits resolving (via ExtractMapRoom) to the current
room, Action exits skipped, listed as "Room: name (m/r)" in a dialog
with double-click travel. DEFERRED (logged, and stated in the dialog
itself): the spell-teleport phase (Abil 140/141 + GetCurrentSpellMinMax),
the monster phase, and TextBlockHasTeleport.

**Keyboard walking (MapGoDirection :33249 + txtMapMove :40973):**
GoDirection takes the named exit (Action/empty → no move; missing
target → no move). Keys: the original numpad scheme (8/2/6/4 N/S/E/W,
9/7/3/1 diagonals, 0 Up, Decimal Down) plus arrows and N/S/E/W/U/D
letters as a logged modern addition. Canvas takes focus on click/tab.

**Spell In Game gate (SpellIsInGame :2712):** not-in-game when
Learnable=0 AND LearnedFrom ≤1 char AND CastedBy ≤1 char AND not a Kai
auto-learn (Magery≠5, or Kai with ReqLevel<1), with the NMR≥1.8
Classes-list escape. Threaded as onlyInGame into SpellIsUsable (VB6
swaps the plain seek for the gate), the spellbook list, paste spell
matching, and .mmec load validation via the existing Options toggle.
bDisableKaiAutolearn still not surfaced (logged).

Anchors: dispel magic (65) not in game / way of the owl (37) Kai
auto-learn in game / red potion (50) CastedBy in game; Lucky Strike
Find → 1/2 then Next → 1/10; LeadsHere(1/1) finds 1/3, 1/100, 1/1381;
GoDirection walks N/S/E incl. Door exits and refuses missing U; VM
find/walk/leads round-trips; spellbook shrinks-but-not-empty under
OnlyInGame.

**Suite: 753/753.** Ships as MmeExplorer-beta3.5.

## Session 36 — 2026-07-03 — BETA 3: THE ROOMS MAP

**Feature census first** (per the user): read MapStartMapping (:33763),
MapMapExits (:33271), MapActivateCell (:32586), MapDrawOnRoom (:32785),
ExtractMapRoom (modMMudFunc :2368), GetTextblockCMDS (:4987), and pulled
every chkMapOptions/optAlsoMark caption from the form header.

**Mme.Data/MapBuilderService (+ .Chart partial):** pure grid model.
- Geometry: 30×23 = 690 cells, center 345; adjacency N −30, S +30
  (the VB6 comment says +20 — the code says +30; code wins), E +1, W −1,
  NE −29, NW −31, SE +31, SW +29; edge exits emit grey stubs and never
  activate; U/D/remote never activate.
- Flood fill with duplicate suppression, the Allow-Dupes delayed-dedupe
  phase, and Allow-Overwrite ALT-grid promotion passes (black pre-mark +
  bright-yellow post-mark + "OVERWRITTEN ROOM - WAS:" tooltip tail).
- Exit classifier: the full 5-char prefix table ((Key:2, (Item 3,
  (Toll 4, (Hidd 6, (Door 7, (Trap 9, (Text 10, (Gate 11, Actio 12,
  (Clas 13, (Race 14, (Leve 15, (Time 16, (Tick 17, (Max  18, (Bloc 19,
  (Alig 20, (Dela 21, (Cast 22, (Abil 23, (Spel 24), map-change 8 wins.
- Line QBColors verbatim, including the preserved dead row: the color
  table lists Case 30 for alignment but the classifier makes 20, so
  alignment exits get the default grey — exactly like the original.
- Block colors: silver no-U/D, green up, yellow down, cyan both,
  black pending-activation.
- Glyphs: red not-found square, green command square (4px room / 6px
  action-exit), red NPC open circle, magenta lair circle, cyan
  shop/spell stars, grey edge stubs.
- Tooltip in the 9/27/2025 order: name, Also Here (lair GetLairInfo
  averages via the existing LairInfoService/LairLoader + NPC), light
  description + light detail with the character-illumination ±150 math,
  Shop/Placed/Room Spell, per-exit details (Key/Item item names, Class/
  Race OK-NO names, Cast pre/post spell names, Spell Trap names, Toll
  with the 5/12/2026 coin reduction ≥10000→runic ≥100→platinum 0.##),
  Show-All-Exits "DIR > roomname" mode, remote Actions, Room commands
  via new MmeDatabase.GetTextblockCmds (first token per ':' line, '*'
  stripped, '|' → " OR "), Max Regen "@ N mins" (GMUD "(N−1)m 30s"),
  Lair Exp/HP = averages × max regen.
- ExtractMapRoom: digit-scan-backward port; the VB6 Mid(s,0) error path
  on digitless input maps to the zeroed result (equivalent outcome).
- New MmeDatabase resolvers: GetSpellName/GetMonsterName/GetShopName/
  GetRoomName/GetTextblockCmds.

**UI:** new Rooms tab. Custom MapCanvas (DrawingContext) draws blocks,
half-cell exit stubs into the corridor gaps, glyph overlays, the QBColor
palette, and a soft radial OUTER GLOW behind marked rooms keyed to the
mark color (the user's ask — a modern reading of the original's overlays;
logged as an addition, not a VB6 behavior). Hit-tested hover tooltips
per cell; click travels to the room; map/room jump box; 20-deep go-back
history (nMapLastMap/Room semantics incl. the mid-goback no-push rule);
all ten map option checkboxes + Shops/Spells also-mark picker; legend
strip. Database switch resets builder caches.

**Deferred (logged):** frmMap zoom window, MegaMUD room-hash marking &
scans, map presets, on-map find-text, cmdMapLeadsHere reverse lookup,
lair "Dmg vs char /clear" tooltip line (needs
GetPreCalculatedMonsterDamage seam), bDisableKaiAutolearn.

Anchors (real db): Town Gates 1/1 floods N chain 1/3→1/4, S 1/100, Door
east stub blue 9; Lucky Strike Casino 1/2 lair ring magenta + command
square green + "Lair Exp:"/"Max Regen: 1" tooltip; toll 1/1381 "E:
(Toll: 5 gold)" + green stub; hidden 3/36 dark-magenta stub + Not
Hidden suppression; cell math all eight directions + edge stubs; room
not found; VM history/jump/click-travel.

**Suite: 747/747.** Ships as MmeExplorer-beta3.

## Session 35 — 2026-07-03 — BETA 2.5: the icebox defrost (everything before Rooms)

**ItemValueService (GetItemValue :3469 + GetShopMarkup :1700 +
modItemParse EvaluateBestPriceForHit :1242):** currency multipliers,
shop markup Fix(cost×markup/100), stock charm sell math
((Fix(charm/2)+25)×price with the 4294967295 overflow-wrap bug
PRESERVED, then Fix(/100)), GMUD sell (÷2 + Fix((charm−50)/5)%), buy mod
1−((Fix(charm/5)−10)/100) with wrap, Friendly coin reduction (Runic ≥1e7
÷1e6 … Silver ≥100 ÷10, Round(,2) banker's). Shop tokens from "Obtained
From" (comma tokens starting "shop", first digits = number, "(sell)" →
no-buy). Best price: cheapest BUY tie→lowest shop#, "buy / sell" format;
else lowest-# sell shop as "(sell) X". Carried grid gained
Enc/Usable/Value/Shop columns via CarriedRowInfo.

**SpellUsabilityService (SpellIsUsable :2740):** learnable gate
(Learnable=0 AND LearnedFrom<5 chars AND not Kai auto-learn), magery
match with the NMR≥1.7 escape preserved-as-dead-code, MageryLVL,
non-Kai Learnable=0 rejection, "(n)" Classes membership, ReqLevel, align
abils 97/98/112 + 110/111/113 (Kai+GMUD forces the loop). NOT ported
(logged): bDisableKaiAutolearn option; GMUD abil-1107 (commented out in
VB6 anyway). No spell "In Game" gate yet (bOnlyInGame path) — logged for
a future wave.

**Wired everywhere:** paste rejects spells not usable by class
("+ (not usable by class)" in the summary); LoadCharacter drops learned
spells the class can't use (VB6 :29918-:29923) with a status report;
SpellBookWindow (File menu) lists class-learnable spells with learned
checkboxes through LearnOrUnlearnSpell first-free-slot semantics
(modMain :703).

**Ground items (ConsolidateGroundByRoom subset):** "You notice … here."
spans (wrapped-line collection), room keyed by nearest preceding
non-boundary line, movement verbs reset the room, MAX per item per room
then SUM across rooms; reported in the paste summary (not imported as
carried). Approximations logged: room key omits the VB6 exits component;
movement-verb set is the common list; leading articles stripped (notice
lines say "a bench" where inventory lines don't — caught by test).

**StatConfirmWindow:** one dialog for all '*'-flagged stats (VB6 uses
serial InputBoxes :36995+ — divergence noted); suggested base = pasted −
(current effective − base); Apply writes confirmed bases, cancel keeps
pasted. Also merged the duplicate Options menus (pre-existing wart).

**Suite: 738/738.** Ships as MmeExplorer-beta2.5. NEXT: beta 3 = Rooms.

## Session 34 — 2026-07-03 — BETA 2: Find Best / Next Best + <-> compare tab

**Ported InvenFindBest (:28767) as Mme.Data/EquipOptimizerService:**
- Criterion tables verbatim (:28791–28886): Armour (AC+DR sum / AC /
  DR / dodge 34 / prot-evil 24 / prot-good 25), Attack (accy 22/105/106
  + Accy field, BS 116/117/118, crits 58, dmg-shield 72, maxdmg 4),
  Resist (36/3/5/66/65/147), Stat (96/88/123/13+14/69/145/37+180/70/
  27/39/40+41+179), Mystics (91/94/90/93/89/92).
- Scoring: AC+DR sum for Armour/0; else FIRST matching ability in item
  slot order among up to three ability codes, falling back to the DB
  field. Ties broken by Get_Enc_Ratio (:4552 — total/enc Round(,4)×100,
  enc<1 → total, banker's rounding preserved).
- InvenFindBestDupeFail (:29152): wrists/fingers refuse an item already
  chosen or equipped in the paired slot (the VB6 "after" version using
  nEquippedItem).
- Two-handed conflict (:29094): all three hold-combination branches
  ported, including the recheck-from-slot-16 goto with the no-2H flag.
- Next Best: exclusion set accumulates equipped items; candidates must
  score ≤ the current item's score and not be excluded; best survivor
  wins. Fresh criterion resets the chain. "Nothing found." message kept.
- Options: No-limited-items (chkInvenNoLimited), holds skip slots.

UI: EQ tab Find Best row (criterion combo + Best/Next Best + no-limited
checkbox); results land in the status bar.

**<-> compare tab enabled:** two full-item pickers, side-by-side
dossiers via GetItemDetailText with "=== name (number) ===" headers, and
a numeric delta line (AC/DR/Accy/Encum/Limit, A vs B). Logged as a
functional equivalent, not a line-pinned port of the VB6 tab layout.

Anchors: AC+DR Best picks corselet 1212 on Torso; Next Best steps to
stormmetal 1835 (380+160); a held Torso freezes; MR find-best never
duplicates rings across fingers; EncRatio matches VB6 rounding shapes;
compare 1212 vs 1835 shows "AC: 400 vs 380 (+20)" / "DR: 200 vs 160 (+40)".

**Suite: 727/727.** Ships as MmeExplorer-beta2. Beta 3 = the Rooms map.

## Session 33 — 2026-07-03 — BETA 1: spell picker, equip holds, sorted tooltips, Copy EQ/Stats

Declared beta: every calculator input/output of the original is ported,
line-pinned, and tested (722). Remaining majors are explorer features
(compare tab, find-best, full Item Manager, Rooms, spellbook) — the RC
roadmap, listed in the README.

**Attack spell picker:** MmeDatabase.GetAttackSpellList ("name (short)"
where Short non-empty, ordered by Name); AttackSpellOptions filters to
the learned set when AttackMode == SpellLearned (the VB6 combo
behavior) — LearnedSpells finally has its consumer. AttackMode became a
notifying property to refresh the picker; paste/load/clear notify too.

**Equip-hold (chkEquipHold):** Hold flag per EquipSlotVm + checkbox
beside each slot combo. Paste skips held slots in both directions
(VB6 :36925 unequip loop + :36974 equip skip).

**SortInvenToolTips (:28321) ported:** per-slot tooltip lines sort
descending by the value in the LAST parenthetical; slots 2/3 split
"a/d" pairs and take their own component. Divergences noted in-code:
unparseable values score 0 instead of aborting the sort (VB6's CDbl
error path kills the whole sub), and ties keep accumulation order.

**Copy EQ/Stats (InvenCopytoClipboard :28499, non-command path):**
Class/Race/Level/effective-Strength header (EffectiveStats[6] now on
EquipmentStatsResult), "Armour Class: a/d", the VB6 "Encumberance"
typo preserved, equipped list padded to 31 chars with slot captions,
"Stats:" comma list with MA Punch/Kick/Jumpkick × DMG/Skill/Accy labels.

Anchors: corselet (40) sorts above Ice Sorceress (1) in the AC tip and
Level above Intellect in crits; spell 1 lists as "magic missile (mmis)";
a held Torso survives a paste that re-equips the weapon slot; the
clipboard text contains the exact VB6 header/typo/padding shapes.

**Suite: 722/722.** Ships as MmeExplorer-beta1.

## Session 32 — 2026-07-03 — Alpha 10: Char-tab paste button, PasteSpells, Options menu

**Paste button relocated per the user:** Character groupbox header now hosts
the Paste Character button (File menu entry retained) plus the new
CharName box.

**Ported PasteSpells (frmMain :37246):** line-oriented parse of the
`spells` table — squeeze-spaces then split; field 0 numeric level > 0,
field 1 mana ("0" accepted), field 2 short name, remainder = full spell
name (VB6 blanks the first three and rejoins); Len > 17 gate kept.
"you have no spells/powers" clears the learned set. Resolution via new
MmeDatabase.ResolveSpellNames — lowest Number wins with Short non-empty,
case-sensitive compare like VB6's table scan. SpellIsUsable class
filter NOT yet ported (logged); unmatched names reported in the paste
summary. nLearnedSpells persists as LearnedSpell0..99 in .mmec
(VB6 :29918 read / :38929 write) via CharacterFileService round-trip.
No engine consumer yet — the learned-spell auto-attack mode is a
future wave (logged).

**Options menu (first cut):**
- Only In Game: Items."In Game" gate threaded through
  GetUsableItemNumbers (equipped-exempt, like min-level).
- GMUD data version > 1.85: DatVer property on EquipmentStatsService
  drives the Quick&Deadly divisor 40/50 (previously hardcoded 40 with
  a ledger note). Test proves divergence requires an equipped weapon —
  GMUD QnD is weapon-gated (StrReq ≤ effective STR), naked characters
  tie at any divisor.
- Auto-save character on paste → CurrentCharacterFile.

**Suite: 718/718.** Ships as MmeExplorer-alpha10.

## Session 31 — 2026-07-03 — Alpha 9: Paste Character + .mmec compatibility

**.mmec answered:** VB6's character file IS a renamed INI (ReadINI
"PlayerInfo" — :29538/:29603). Our CharacterFileService already wrote
the byte-compatible layout, so compatibility = dialog filters/default
name/extension-append on *.mmec (legacy .ini still loads) + the
DataFile/DataFileVer keys VB6 stores (written via Extras).

**Ported PasteCharacter core (frmMain :36654):**
Mme.Data/GameTextPasteService.cs —
- TestPasteChar scanner (modMMudFunc :3219 charset) with space-stripped
  accumulation; "equippedwith:"/"arecarrying" context clears; '('/')'
  slot-keyword capture with WRIST/FINGER pairing, WORN 2-bucket capture
  disambiguated by Items.Worn (1→slot 19, 16→slot 14),
  OFF-HAND/WEAPONHAND/TWOHANDED mapping.
- Race/Class/Name windowed extraction (:36783 — stop tokens Exp:/
  Level:/Lives/CP: with 20/15/35 windows and newline guards).
- ExtractValueFromString port (modSyntaxsFunc :286 — leading space/'*'
  skip, digit scan). Level/Encumbrance/6 stats; VB6's exact modified-
  marker spellings ("Strength: *", "Intellect:*", "Willpower:*",
  "Agility:*", "Health: *", "Charm:  *") flag ModifiedStats — the
  InputBox confirm flow is replaced by a summary warning (logged).
- Inventory/keys subset of modItemParse.bas ParseGameTextInventory
  (:157): blob collection with section boundaries + inline header
  splits, comma tokenization, bracket/cash/equipped filters,
  ParseCountAndName (trailing "(N)" groups + leading count), list
  consolidation. Ground/notice sections recognized as boundaries only;
  shop/value enrichment and PasteSpells deferred (logged).
- Resolution: space-stripped exact name match per slot; carried by
  case-insensitive exact name with plural→singular fallback; leftover
  encum → manual adjustment slot 0 (VB6 Additional Weight), clamped ≥0.

VM ApplyGameTextPaste applies with unequip-on-paste (hold checkboxes
not yet ported), returns an honest summary (equipped/carried counts,
modified-stat warning, unmatched names). New CharName property persists
through .mmec save/load. UI: File → Paste Character... dialog with the
VB6 instruction template when the clipboard is empty.

Port bugs caught by tests: blob boundary off-by-one (VB6 i=j-1) ate the
keys section; a fixture invented "name (2)" carried syntax that VB6
classifies as equipped (IsEquippedItem) — fixture corrected to game-
authentic forms; leftover-weight expectation went negative where VB6
clamps to 0.

**Suite: 714/714.** Ships as MmeExplorer-alpha9.

## Session 30 — 2026-07-03 — Alpha 8: launch crash fixed (alpha-5 sed overreach), binding guard added

Flight recorder verdict from the user's mme-log.txt: InvalidOperationException
"TwoWay binding cannot work on read-only property EqMaSkillPunch"
(+Kick/Jk) thrown during Window.Show. In alpha 5/6 this same exception
was unhandled and aborted window creation — process alive, no window:
the reported "hang". Alpha 7's handler surfaced it in one flight.

**Root cause (mea culpa, logged):** the alpha-5 MA-matrix rebind did a
blanket Text="{Binding MaSkillPunch}"→EqMaSkillPunch replace that hit
BOTH the EQ-panel TextBlocks (intended) and the Char tab's INPUT
TextBoxes (not intended). TextBox.Text defaults to TwoWay; the EqMa*
properties are read-only computed → throw at binding activation.

**Fix:** the nine Char-tab TextBoxes reverted to the read-write entry
properties (MaSkillPunch/Kick/Jumpkick, MaDmgPunch/Kick/Jumpkick,
MaAccyPunch/Kick/Jumpkick); the EQ-panel TextBlocks keep the computed
EqMa* slots 34–42.

**Regression guard:** XamlBindingSanityTests scans MainWindow.xaml for
every TextBox Text binding and reflects the target property on
MainViewModel — any TwoWay binding on a setter-less property fails the
suite. Guard's first run also flagged the five detail-text areas,
which turned out to already carry explicit Mode=OneWay (safe); the
guard now respects explicit one-way modes.

**Suite: 711/711.** Ships as MmeExplorer-alpha8.

## Session 29 — 2026-07-03 — Alpha 7: startup-hang diagnostics + binding-storm hardening

the user hit a launch hang (process alive, window never appears, survives
task-manager kill of the visible entry). Not reproducible off-Windows,
so this build is a flight recorder plus fixes for the two genuine
hazards found in audit:

**Hazard 1 — EqAttack was a heavy binding getter:** the full damage
engine (bundle + profile + GetDamageOutput) ran every time WPF
evaluated the property. Now computed once per RecalcEquipment into a
cached field.

**Hazard 2 — NotifyEquipPanel used OnChanged(string.Empty):** the
refresh-everything form re-evaluated every binding on the window
(including, previously, the heavy getter) on every recompute. Restored
an explicit property list.

**Flight recorder:** App.OnStartup writes mme-log.txt beside the exe
(previous run preserved as mme-log.prev.txt) with breadcrumbs —
startup → window ctor → xaml parsed → datacontext bound → loaded →
first frame rendered — plus Dispatcher/AppDomain/Task unhandled
exception logging with a user-visible dialog instead of silent death.

**Software-render fallback:** --software-render argument or a
software-render.txt marker file forces
RenderOptions.ProcessRenderMode=SoftwareOnly — the standard mitigation
for the WPF GPU-driver startup hang class (process runs, no window
paints), which matches the user's symptom profile.

Recovery guidance shipped in README (kill all Mme.App.exe in the
Details tab; clear %TEMP%\.net\Mme.App extraction cache).

**Suite: 710/710.** Ships as MmeExplorer-alpha7 (diagnostic build).

## Session 28 — 2026-07-03 — Alpha 6: Exp/Hr shows monster names (GetMultiMonsterNames)

the user's display report: the Exp/Hr list showed raw GroupIndex strings
where VB6 shows monster names. Root: LairDisplayRow surfaced
lt.GroupIndex; VB6 renders GetMultiMonsterNames(MobList).

**Ported GetMultiMonsterNames (modMMudDatabase :2571):** comma-split
number string → "name(id), name(id)" (suffix dropped with hideNumber);
empty → "None"; unknown ids skipped; failure returns the input.
Tolerates a missing trailing comma (VB6 callers append one).

Lairs rows now carry Group = resolved names (falling back to the index
when resolution yields nothing) plus a GroupIndex field for reference;
the grid gained a wide Monsters column with the raw index beside it.
Three lair tests updated to select rows by GroupIndex (their previous
Group=="10-10-15" selector was asserting the exact display defect being
fixed). Anchors: id 1 → "name(1)" shape, pair join, hideNumber, and a
real-db check that most lair rows resolve to lettered names.

**Suite: 710/710.** Ships as MmeExplorer-alpha6.

## Session 27 — 2026-07-03 — Alpha 5: StatTips + InvenColorCodeStats + computed MA matrix

**Ported InvenColorCodeStats (:28421):** value coloring — negative red
(&HFF), positive yellow for slots 10/12/19/24 (accy/hitmagic/stealth/
MR), else white with "+" prefix; zero keeps panel green. Label-
brightening for manual adjustments deferred (labels are static here).

**StatTips (the VB6 tooltip breakdown) threaded through the engine:**
EquipmentStatsResult gains Tips[47]; sources appended at every
accumulation site — class/race abils ("Class: Name (v)" — race HP,
DR ÷10 values), item encum/abils/AC-DR pairs ("name (a/d)", carried
tagged), bless (BlessService.Sources merged), quests, manual
adjustments, STR damage, crit terms (Level/Agility/Intellect/Charm/
Combat/Quick&Deadly), dodge terms (Encumbrance/Agility/Level/Charm per
:27975), MR Int/Wis components, and the accuracy + stealth ref-string
outputs from the ported math captured verbatim. Panel binds
ToolTip per stat. Tooltip sorting (SortInvenToolTips) not ported —
sources appear in accumulation order; noted.

**MA matrix computed:** Punch/Kick/JmpKck grid now binds slots 34–42
(DMG 34/35/36, Skill 37/38/39, Accy 40/41/42) from the engine instead
of mirroring Char-tab typed values.

Anchor test: corselet + Ice Sorceress + magic armour + Half-Ogre lvl 99
produces tips containing the item name, quest line, bless line, race HP
(99), crit terms Level(9)/Intellect(5), STR dmg (5), MR Int(25)/Wis(75),
dodge Level(19), and the encum source.

**Suite: 708/708.** Ships as MmeExplorer-alpha5.

## Session 26 — 2026-07-03 — Alpha 4: Attk line, manual adjustments, carried grid, min-lvl

**Attk line:** black panel now runs the ported damage pipeline exactly
as VB6's trailing GetDamageOutput(0,0,0,50,0,…,bForceCharacter:=True)
call — current attack mode vs a 50-MR target, "Round(avg)+bs @
Truncate(swings,2)" / "one-shot" formatting. The EQ weapon slot
overrides the sheet/config weapon so equipping feeds it live.

**Manual stat adjustments (char_StatAdjustments, :27204 + :27320):**
engine input added — encum slot 4 applies before CalcEncum; the rest
route with AC/DR ÷10 (values are tenths), accy → accyOther, dodge/MR →
pools, hitmagic GMUD max-wins. UI: compact "slot:value" pairs box (the
VB6 per-stat dialog is a later polish).

**Carried items grid:** ObservableCollection rows (number × qty with
live name lookup) syncing CarriedItems → engine; add/remove buttons;
character-file load repopulates the grid. Item picker dropdown deferred.

**Min lvl strip box:** txtGlobalMinLVL ported — gates browse tabs and
equip lists through ItemIsUsableByChar's minItemLevel with the equipped
exemption verified by test.

Anchors: adj 2:10/3:25/7:3/8:5/24:7 land as +1.0 AC/+2.5 DR/+3 crits/
+5 dodge/+7 MR; min-lvl 40 strictly shrinks the usable set and an
equipped victim survives via the exemption callback.

**Suite: 707/707.** Ships as MmeExplorer-alpha4.

## Session 25 — 2026-07-03 — Alpha 3: bless spells, quest UI, carried items, VB6-compatible character files

The auto-calculation wave, per the user: maximum calculator before model
downgrade.

**Ported: RefreshCharBless (frmMain :38129–38268) + bless combo
population (:21700–21810)** as Mme.Data/BlessService. PINS: spell level
clamped [ReqLevel..Cap>0]; nAvgCast = CLng((min+max)/2) banker's;
AbilVal 0 → cast average drives the stat; abil 7 pre-rounds /10 1dp;
stock accy highest-bless-WINS (assignment) vs GMUD accumulate; bless
BLUR reuses item divisors but VB6 resets the worn-armour tracker before
this runs, so stock bless BLUR ALWAYS lands in Fix(/2) — preserved by
passing wornArmourType=0; mana upkeep Σ Round(Mana/(dur·3),3) ×5×6
Round 2 (SPELL_ROUND_SECS=3, ROUND_SECS=5). List filter: targets
1/2/5/13 + real duration for learnable/learned/Kai spells; SpellIsUsable
class-filtering of the LIST not ported yet (matches filter-off VB6) —
honest gap.

**EquipmentStatsService extended:** blessSpells/carried/alignmentFilter
inputs. Bless application per :27258–27390 (enc → re-CalcEncum → str →
re-CalcEncum → encumPct AFTER; slot loop with accy/dodge/MR/hitmagic
special cases; shadow 100; stat-bonus 102–124 mapping). Carried items:
encum × qty; stats gated on (ItemType 10 or armour Worn 0) +
ItemIsUsableByChar; STOCK carried applies ABILITIES ONLY while GMUD also
treats AC/DR/Accy fields as abilities (eq_abils_only vs
gmud_ability_equivs :27430) — pinned.

**Character files (SaveCharacter :38725–38935)** as
Mme.Data/CharacterFile — byte-compatible INI: [PlayerInfo] with the
verbatim "Widsom" key typo (tolerates "Wisdom" on read, writes
"Widsom"), Quest0..11 + option combos, Bless0..9; [Inventory] slot keys
Head..Everywhere + IM_CARRIED "n|qty," pairs (cap 50); unknown keys
preserved round-trip so VB6 files aren't stripped. File→Load/Save
Character menu items; load suspends recalc, restores everything, then
refilters once.

**UI:** ten bless combos + quest checkbox strip (GMUD set
visibility-bound) + Bless Mana panel line. Honest not-yet note updated
(manual adjustments, Attk line, Item Manager grid).

Anchors: magic armour (52) adds exactly +5.0 AC flat and
Round(avgCast/10,1) DR at level 15, mana upkeep > 0; Ice Sorceress/ARD
stock quest deltas; GMUD-only quests inert under stock; bench ×2 = +1000
enc with zero stat bleed; VB6-format INI fixture round-trips including
Widsom, Off-Hand, IM_CARRIED, and unknown-key preservation.

**Suite: 705/705.** Ships as MmeExplorer-alpha3.

## Session 24 — 2026-07-03 — Alpha 2: .mdb "file in use" fixed + no-database state made honest

the user hit "The process cannot access the file because it is being used
by another process" opening an .mdb, plus blank class/race combos.
One root cause: the import failed, so no database ever loaded — the
combo bindings (NamedEntry) were verified correct; empty lists just
LOOK identical to the RC4 tuple bug.

**The lock was us.** Microsoft.Data.Sqlite pools connections: disposing
the tmp-writer connection kept the OS file handle alive, so
File.Move(tmp → cache) failed on Windows. (Linux moves open files
happily, which is why the suite couldn't catch it.) Fixes:
Pooling=False on the importer's tmp connection; ClearAllPools() before
replacing the cache; AceMdbTableReader opens with Mode=Read so an open
realm/NMR/Access can't contend; the app closes its own database first
when reconverting over the currently-open cache (new VM.CloseDatabase);
import-failed dialog now explains the close-NMR / delete-stale-.ldb
recovery. Regression test: Import → open+dispose cache → touch mdb →
Import again must replace cleanly with no leftover .tmp.

**UX honesty:** filter-strip class/race combos and the EQ slot panel
now grey out until a database loads — an empty enabled dropdown reads
as "broken", a disabled one reads as "not ready".

**Suite: 701/701.** Ships as MmeExplorer-alpha2.

## Session 23 — 2026-07-03 — ALPHA 1: the EQ tab becomes the equipment builder + native .mdb open

the user's stage call accepted: this is an ALPHA, not an RC — the equipment
builder heart was missing. Both gaps closed this session.

**Ported: CalcCharacterStats (frmMain :26986–28230)** as
Mme.Data/EquipmentStatsService — full line-by-line read + helpers
InvenCalcEncum (:26935), AdjMainStatBonus (:26947), GetRaceHPBonus
(modMMudDatabase :2051), GetQuickAndDeadlyBonus/CalcQuickAndDeadlyBonus
(modMMudFunc :4485/:4527). Consumes previously-ported CalculateAccuracy,
CalcDodge, CalcMr, CalcSpellCasting, CalculateStealth, CalcEncum,
CalcEnergyUsedWithEncum, GmudGetSpDmgMultiplierFromSc, MovementSpeed.
Externalized default-neutral: carried items, bless, manual adjustments,
quest checkboxes (EquipQuests input exists, UI later),
chkInvenHideCharStats; display-only code dropped with notes. LOUD PIN:
the worn-armour-type tracker (:27417) reads ItemType, not ArmourType —
suspected VB6 bug feeding the stock BLUR divisor, preserved verbatim.
Other pins in the class doc: encum-first ordering, stock accy
highest-wins vs GMUD accumulate, hitmagic GMUD max / stock add + weapon
fold, class/race DR ÷10 but AC raw, BLUR divisors, STR dmg rules, quest
table, stock >40 crit diminishing is DISPLAY-ONLY (EffectiveCrits).

**EQ slot layer:** nEquippedItem is 0 To 19 — twenty slots.
InvenAddEquip (:26857) Worn→slot routing ported as EquipSlotCatalog
(fingers Worn 4+13 and wrists Worn 14 populate BOTH paired slots;
weapons = slot 16; population gated by ItemIsUsableByChar with
ignoreMinItemLVL like the VB6 dofilter loop).

**UI:** EQ tab rebuilt — 20 slot combos (two columns) driving the black
panel via per-slot VMs; panel now binds COMPUTED Eq* values (was a Char
mirror), red negative dodge, effective-crits annotation, honest
not-yet-included note. Char stat boxes + race trigger live recompute.
Added CharWil/CharHea.

**Native .mdb open (Option A, approved):** MdbImportService (seam:
IMdbTableReader) imports Access tables into a sibling .db cache with
Jackcess-converter parity — verbatim column names ("Markup%", "Att%-0"),
MONEY → exact invariant decimal TEXT, booleans → −1/0, dates invariant
TEXT, NULL passthrough; atomic tmp-then-move; staleness = cache older
than mdb. AceMdbTableReader (System.Data.OleDb, [SupportedOSPlatform])
tries ACE 16 → ACE 12 → Jet 4 and surfaces AceNotInstalledException;
File→Open filter is now "NMR database (*.mdb;*.db)" with a graceful
ACE-download dialog. Conversion logic is fully tested off-Windows via
the seam; the OLEDB adapter itself needs a Windows smoke test (the user's
box) — flagged honestly.

Anchors: naked lvl-99 Warrior/Human all-100s crits 17 / MR 100 / STR
maxdmg 5; petrified stone corselet 1212 adds exactly +40.0/+20.0 AC/DR;
hellblade 325 hitmagic fold = 5 (non-weapon 0); Half-Ogre vs Human at 99
= +99 HP; slot catalog routes corselet→Torso, hellblade→Weapon, tower
shield→Off-Hand, rings into both fingers. Import parity: MONEY
"100000000" exact, True→−1, NULL passes, cache staleness both ways.

**Suite: 700/700.** Ships as MmeExplorer-alpha1.

## Session 22 — 2026-07-03 — Blank combos fixed + ItemIsUsableByChar ported

**Bug (the user's screenshot):** Class/Race dropdowns opened tall-but-blank.
Root cause: GetClassList/GetRaceList returned ValueTuples — WPF
DisplayMemberPath binds PROPERTIES, and tuple members are fields, so
every row rendered empty. Fixed with a NamedEntry record. Lesson filed:
the fixture-defensive try/catch made the failure silent; binding-layer
types now use records only.

**Ported: ItemIsUsableByChar (frmMain :40382–40566)** as
Mme.Data/ItemUsabilityService — the global-filter equip gate. PINS:
fast-path (lvl≥999 + class Any + align Any); abil 135/136 level window;
abil 59 ClassOk (val==class) BYPASSES armour/weapon type checks; abil 28
magical rejected only by classes with class-abil 51 (anti-magic);
required-align 97/112/98 and not-align 110/113/111 vs the align filter;
ClassRest slots (any nonzero restriction with no match → fail); armour
needs classArmourType ≥ item type, stock-only shield (Worn 12) denial
for 1H classes (0/2/4/9); weapon-type switch with Any groupings, 8=all,
Staff(9) hardcodes dagger 68 + quarterstaff 100. Faithful quirk:
RaceRest is NEVER consulted — VB6 filters by class only. Externalized:
the four global-filter UI reads and the equipped-item exemption
(callback; no equip slots yet).

**Wired:** Use Character + Level/Class/Align in the strip now filter
Weapons/Armour/Sundry live (properties upgraded to notify + refilter —
they were inert auto-props). Align combo added to the strip
(Any/Good/Neutral/Evil ↔ cmbGlobalAlignment index). Min-lvl box still
pending.

Real-db anchors: Mage denied plate + 2H sharp, granted 68/100; Priest
any-blunt granted quarterstaff, denied greatsword; Witchunter (abil 51)
denied magical hellblade 325 that Warrior gets; lvl-1 set strictly
smaller than lvl-999 (abil-135 gating).

**Suite: 696/696.**

## Session 21 — 2026-07-03 — Polish wave: screenshot-driven fixes, all formula-sourced

the user's side-by-side screenshots surfaced wrong-data defects; every fix
below came from a fresh VB6 read, with pins in code comments:

- **Sorting:** all browse queries now COLLATE NOCASE (SQLite binary
  collation had sorted "AngelCrusher" before "abyssal scimitar").
- **Weapons (AddWeapon2LV :4674–4800):** Acc = last-wins abil 22/105/106
  THEN + Accy field; BS defaults "No", abil 116 last-wins; Crits 58
  last-wins; LVL = abil 135; AC column = RoundUp(AC/10)/"/"/DR/10
  (ceiling on AC only, RoundUp read from modSyntaxsFunc :631). Combat
  columns (Dmg/Spd, #Swings, xSwings, Dmg/Rnd) now computed per row by
  the PORTED CalculateAttack with a profile from PopulateCharacterProfile
  (generic branch when Use Character is off) — Dmg/Spd =
  Round(total/swings/speed, 4)·1000 banker's, #Swings truncated 2 dp.
- **Armour (AddArmour2LV :4568 + Get_Enc_Ratio :4552):** Acc ACCUMULATES
  (unlike weapons); AC/Enc = (AC+DR raw)/enc banker's-rounded 4 dp ×100,
  enc<1 → raw total. Verified against the user's screenshot value 30.95.
- **Classes (AddClass2LV modMain :6163–6203):** Exp% = ExpTable + 100;
  Cmbt = CombatLVL − 2; HP = MinHits to (MinHits+MaxHits); ability 59
  (ClassOk) skipped. Anchored on Bard (210% / 3 / 4-7 in the 2025 db).
- **Shops (PullShopDetail :4104 + GetItemValue :3469–3660):** regen text
  gains the real Amount and d/h/m decomposition; cost = currency
  multiplier → Fix-markup → "#,# Copper (reduced Coin)" with coin tiers
  and exact-".00"-only trim. Anchor reproduces the screenshot string
  "30,000 Copper (300 Gold)" verbatim. Test-authoring catch: I expected
  "15.5 Gold"; VB6 keeps "15.50" (trims only exact ".00") — port was
  right, test corrected.
- **Spell detail:** the "Damage" wording is now gated on the ported
  SpellDoesDamage; non-damage spells (illuminate) say "Effect value".
- **XAML:** Char-tab star-sized rows → Auto (skyscraper textboxes fixed);
  Weapons gains LVL + combat columns; Armour gains Level.

Different-vintage note: the user's VB6 runs a 7/1/2026 realm export; the
bundled conversion is 9/28/2025 — raw values (Bard 80 vs 110 ExpTable)
legitimately differ between snapshots; formulas now match.

**Suite: 695/695** (2 new formula-anchor tests).

## Session 20 — 2026-07-03 — Visual parity wave: the VB6 face

the user compared the shipped shell against the original side-by-side —
verdict: engine right, face wrong (generic 4-tab gray vs the dense
12-tab cockpit). This wave rebuilt the WPF UI in the VB6 image. No VB6
code is used anywhere; the .frm/.bas sources and screenshots served
purely as the visual/behavioral spec for native XAML/C#.

**Name tables (display layer):** EnumNames (Mme.Core) is the single
source for ability/enum names — GetAbilityName was mechanically
extracted from modMMudFunc :3405–3723 (227 stock names; GMUD overrides
15/16/50/188/189, QuestFlag ranges, hidden-unless-forced message abils
101/115/120/137/144/155) with the GetXxxEnum functions (:2929–3195)
hand-read: armour/weapon/class-weapon/worn/item types, monster
alignment, magery, spell targets. A duplicate generated module from a
mid-session restart was deleted in favor of it.

**Browse layer (Mme.Data/BrowseQueries.cs):** display-only projections
for Weapons (BS = abil-116 sum, Crits = abil-58 sum, AC shown /10),
Armour (AC/DR /10, AC-per-Enc), Sundry, Monsters (grid + full dossier:
alignment, cash by coin, abilities, numbered item drops with %, energy,
avg dmg), Shops (inventory slots with "N% for 1 per Mm" regen text and
copper(gold) cost), Classes, Races. Item/spell detail text builders
resolve spell names for learn/remove/give-temp abils and class
restrictions; spell detail = the PORTED GetCurrentSpellMinMax with the
"(@lvl N): Damage X to Y / Min: base+(inc*lvl)" formula lines.

**MainWindow.xaml rewritten:** menu bar; green "Global Filter + Use
Character" strip (level/class/race/GMUD); bold DB banner from the Info
table; 13 flat button-tabs (Char, <->, EQ, Lists, Weapons, Armour,
Spells, Class/Race, Sundry, Monsters, Shops, Rooms, Exp/Hr — <->,
Lists, Rooms disabled pending their waves); MS Sans Serif 11px density,
17px grid rows, classic blue row selection; Monsters = grid + right
dossier with VB6 color language (evil red/orange, good green, abilities
blue, dmg red); EQ = the black terminal stat panel (Courier, cyan
labels / green values / yellow accuracy) + the Punch/Kick/JmpKck black
matrix, honestly labeled as mirroring the Char entries until
CalcCharacterStats lands; Char = VB6-style grouped stat boxes; Exp/Hr
keeps the attack strip + lair grid.

**Honest gaps, stated in-UI where relevant:** equipment calculator
(CalcCharacterStats — the flagged wave), PullSpellEQ exact prose,
weapon-detail combat prose (current detail box is a stats+abilities
summary, not frmMain's CalculateAttack sentence), references panels,
Rooms map renderer, Lists manager, <-> compare.

**Suite: 693/693.** Fresh win-x64 self-contained build published.

## Session 19 — 2026-07-03 — Release candidate: routing table + packaging

**Ported** GetAbilityStatSlot (modMain :8395–8548) — the ability →
stat-slot routing table behind the equipment calculator and spell-EQ —
with anchors for the dead cases (9/38/39/72/87 → −1), the triple
accuracy route (22/105/106), the value-variant traps/picks (179/180),
and the deliberately-unrouted 28.

**Scoped, honestly NOT ported:** CalcCharacterStats (frmMain :26986–
28230, 1,244 lines). Head + item-accumulation loop read and the
structure captured in the ledger (AC/DR ÷10 accumulation, weapon-slot
28/142 hitmagic capture, BLUR armor-type divisors, stock
highest-accuracy-wins vs GMUD accumulate, GMUD-only +stat items). A
rushed half-read port would be exactly the unverifiable slop this
project exists to avoid — it is the one flagged wave remaining, with
enough ledger notes for any session (or Claude Code) to pick up cold.
The character panel's direct-entry fields cover the gap functionally.

**Packaging:** README rewritten as the release-facing document (Windows
build/publish instructions, converter usage, ported-surface summary,
known-remaining list) with the session-bootstrap notes retained for
contributors.

**Suite: 693/693 green.** The app cross-compiles clean; `dotnet run`
in src/Mme.App on a Windows box is the compilation moment.

## Session 18 — 2026-07-03 — Character & Attack panel: cohesion wave

No new VB6 reads — this wave assembles ported parts into the app.

**Character sheet panel** (Lairs tab expander): level, class/race combos
loaded from the DB (GetClassList/GetRaceList), alignment, STR/INT/AGI/
CHA, stealth/dodge/crit, accuracy, hit-magic pair, ±damage and BS trio,
encumbrance, quickness, bless, spellcasting, spell-damage bonus, and the
nine MA pluses — every box maps to a named CharacterSheetState field
(and through it to the exact frmMain control). "Use Character" is
chkGlobalFilter.

**Attack strip**: mode selector (Manual / One-shot / Weapon / Spell @
level / Spell learned / Martial arts / Bash / Smash = the full
nGlobalAttackTypeMME range), weapon #, spell # + cast level, MA type,
Backstab + BS weapon #, Meditate toggle.

**Wiring**: ManualAttackOptions.CreateBundle gained a full overload
(sheet + AttackConfig); the 5-arg manual overload delegates to it and
is behaviorally unchanged (tested). The VM builds sheet + config from
the panel; with Use Character on, the CalcExpPerHour character args
(HP/regen/threshold/spell cost/overhead/mana/MP regen/meditate/walk)
come from a Normal-mode Populate instead of the strip — frmMain :25572.
Strip surprise values now defer to the engine when Backstab mode is on.
One design pin kept: the lair-path engine runs at party 1 with the VM
dividing final Exp/Hr (the party>1 GetDamageOutput branch is for the
per-monster tables, GetPreCalculatedMonsterDamage territory) — an
initial regression where PartySize leaked into AttackConfig.Party was
caught by the wave-13 party test and fixed.

**Suite: 692/692 green.** New tests: weapon-mode and spell-mode
end-to-end equality (VM bundle == hand-built service, undead
restriction respected through the lair provider), Use-Character knob
override changes Exp/Hr, legacy manual bundle unchanged.

**Remaining for full cohesion:** the equipment calculator (worn-item →
derived stats, replacing hand-entry), spell/monster/item detail panels
(PullSpellEQ), GetPreCalculatedMonsterDamage party tables, §8.2 goldens
on Windows.

## Session 17 — 2026-07-03 — PopulateCharacterProfile: the character model is real

**Ported** PopulateCharacterProfile (modMain :5178–5380) as
CharacterProfileService.Populate over a CharacterSheetState DTO that
maps every frmMain control and session global it read (field-by-field
comments name the exact VB6 source). ByRef mutation semantics kept:
unset fields retain caller values. Five thin lookups landed with it —
GetSpellManaCost (PIN: ×Fix(1000/EnergyCost) multi-cast multiplier when
0 < E ≤ 500), GetClassCombat (CombatLVL − 2, dead trailing assignment
noted), GetClassStealth (abil 103; UI fallback externalized),
GetItemStrReq, IsTwoHandedWeapon (ItemType 1 + WeaponType 1/3).

Highlight pins: the surprise-weapon ElseIf chain and its
accuracy-adjustment block (two-handed off-hand subtraction, main-hand
contribution removal, tChar.nPlusBSaccy MUTATED before
BackstabAccuracy); the party branch WRITES 1 back into the HP textbox;
UI Double→Integer/Long coercions are banker's (VbRuntime), not
truncation; GMUD alone folds AccyItems into backstab accuracy.

**WIRED:** ProfileRequest gained ForceCharacter and GetDamageOutput now
passes bForceCharacter at all four profile calls; the VM's manual strip
runs the REAL generic-maximum branch (Level = GetMaxLevel, HP 10000,
accy 999) exactly as frmMain manual mode does. One test-authoring
mistake caught in-session: a mined anchor misread item 342's Abil
layout (AbilVal-0 = 50 belongs to abil 0, not 116) — test corrected to
compute relatively; the port was right.

**Suite: 688/688 green.**

**Remaining:** the character-sheet UI panel (class/race/level/stat
entry feeding CharacterSheetState — the frmMain equipment-label
machinery), PullSpellEQ + detail panels, thin lookups on demand,
§8.2 goldens.

## Session 16 — 2026-07-03 — Spell quartet: the caster math lands

**Ported** GetSpellMinDamage / GetSpellMaxDamage / GetSpellDuration /
SpellDoesDamage / GetMaxLevel (modMMudDatabase :4527–4790, :155) as
Mme.Core/Formulas/SpellDamageMath.cs — statics over a
Func<long, SpellRecord?> resolver so the abil-151 chain-cast recursion
is DB-free and testable. Headline pins: drain (abil 8) counts as damage
AND as heals; fixed AbilVal overrides skip clamp + scaling with the
last slot winning (so chains recurse at the UNCLAMPED level); the
energy multi-cast needs both sides ≥ 143 with the ≤ 500 gate on the
no-chain multiplier; recursion passes the mutated castLevel and
decremented energyRem while DROPPING bHealsInstead and bNotDuration;
the quartet's un-ByVal'd params are implicit ByRef — every call site
audited inert, ported by value.

**Process catch:** GetCurrentSpellMinMax was re-read and re-derived
this session before the ledger check revealed it was already ported in
wave 3 (SpellMath). The duplicate implementation was deleted; the
freshly derived expectations were kept as 5 cross-check tests against
the wave-3 port — all pass, mutually validating both readings.
PullSpellEQ (~460 lines) scoped and deferred to the spell detail panel
wave: it is a display-string builder over this math.

**Anchors** from an independent Python replica against the real 1.11p
DB: turn undead (clamp/cap/monster-skip), meteor swarm (energy
multiplier at two energies), poison bolt (fired chain adds 0 — target
1366 has no damage abils), dragonfire (EnergyCost 0 blocks its chain
forever), vampiric touch (drain dual-count), constriction (fixed
override). Synthetic resolvers prove the two recursion-drop pins no
stock spell exercises.

**Suite: 681/681 green** (16 new, first-run after the dedup).

**Remaining:** PopulateCharacterProfile + equipment sheet,
PullSpellEQ + detail panels, thin lookups on demand, §8.2 goldens.

## Session 15 — 2026-07-03 — GetDamageOutput: the manual strip goes real

**GetDamageOutput is ported** (modMain :4825–5176) as
DamageOutputService over the already-ported CalculateAttack /
CalculateSpellCast / CalculateResistDamage, with seams for
PopulateCharacterProfile (AttackConfig + profile source) and the
per-monster damage cache (DamageVsMonsterCache ⇔ nChar*VsMonster).
ItemHasAbility (specific-ability mode) ported into MmeDatabase with the
−31337 sentinel. AttackRestrictions + MmeAttackType enums defined from
the VB6 source.

**App impact — fidelity upgrade:** the character strip no longer feeds
flat constants. Manual damage now routes through the REAL a5_Manual
path: CalculateAttack(specifyDamage) vs each lair's actual AC/DR/Dodge
and CalculateResistDamage vs MR (new M.Dmg strip field for magical).
The Monsters-tab single-monster path now calls
GetDamageOutput(monsterNumber) exactly like frmMain — the service loads
the monster's real defenses/abilities and the damage cache does its
job. Strip backstab fields override the surprise trio until
PopulateCharacterProfile lands (documented). ManualAttackOptions
factory shared between the VM and the parity tests so both compute
through the identical chain. Monster-stats map cached per service
(a 49 s suite regression back to 1 s).

**Suite: 665/665 green.** Wave 15 first-run: all 8 GetDamageOutput
path tests including cache semantics via profile-source call counting
and the undead-restriction ElseIf chain against real spell 18.

**Remaining:** PopulateCharacterProfile + the equipment character sheet
(replaces the strip; enables UseCharacter + party damage tables via
GetPreCalculatedMonsterDamage), PullSpellEQ + spell quartet for the
caster panel, detail panels + thin lookups on demand, §8.2 goldens.

## Session 14 — 2026-07-03 — GetLairAveragesFromLocs + Monsters-tab Exp/Hr

**The Monsters grid now shows Exp/Hr** — the classic MME view. Per
monster: GetLairAveragesFromLocs("Summoned By") aggregates every lair
the monster spawns in (cached per string like frmMain's guard), then the
frmMain lair path runs; monsters with RegenTime > 0 or "Room" summons
take the single-monster path (mobs 1, lairs −1, own AvgDmg/HP/HPRegen,
walk 0). ExpMulti toggle added (nExp = EXP·ExpMulti). Party divide as
before.

**Ported (read line-by-line per §0):**
- GetLairAveragesFromLocs → LairInfoService.Averages.cs. Headline pins:
  PossSpawns-before-version-gate; skipped lairs still divide AND leave
  outlier-participating zeros in the walk array; nMobs unrounded while
  everything else banker's-rounds; the 51% accuracy
  majority-of-majorities threshold is LIVE here (decorative in
  LoadLairInfo); immune check uses a reduced-arg provider call and only
  the −9998 sentinel zeroes damage.
- InstrCount (modMain) → TextUtils; CalcAverageNonZero (modMain) →
  StatsMath. Both trivial, both read.

**Suite: 657/657 green.** Wave 14 first-run: synthetic two-lair
averaging hand-trace, skipped-lair pin, live-threshold anchor, immune
sentinel (request shape asserted), shade #15 end-to-end equality, and
single-monster-path parity.

**Remaining:** GetDamageOutput + character/equipment model (the manual
strip's replacement — biggest remaining lift), PullSpellEQ + spell
quartet, detail panels + thin lookups as the panels need them, §8.2
VB6-runtime CSV goldens on Windows.

## Session 13 — 2026-07-03 — One-shot: lair loading + live Exp/Hr in the app

**Milestone: the Exp/Hr column is ALIVE.** The application now runs the
complete MME calculation spine end-to-end: mdb2sqlite → MmeDatabase →
LairLoader (LoadLairInfo port) → LairInfoService (GetLairInfo) →
ExpHourModels.CalcExpPerHour (Models A–D dispatcher) → Lairs tab.

**Ported (read line-by-line per §0):**
- StatsMath (Mme.Core/Formulas): RemoveOutliers / GetMedian /
  GetMedianAbsDev / GetStdDev / QuickSort. All-outliers-untouched pin,
  MAD→sample-stddev fallback, Hoare middle-pivot sort.
- LairLoader (Mme.Data): LoadLairInfo + the three DICT helpers over the
  SQLite gateway (GetLairRows / GetMonsterLairStats / GetInfoNmrVersion
  added to MmeDatabase). NMR version via the Phase 1a
  ExtractNumbersFromString port ("v1.8.3" → 1.83 → AttTrue% path).
  Integration anchors from an independent Python replica against the
  real converted 1.11p (421 lairs; two lairs' full aggregate sets,
  including a truncation-signed −4 average fire resist).

**App wiring (Phase 2):**
- MainViewModel.Lairs partial: character stat strip (damage sextet, HP,
  regen, threshold, spell cost/overhead, mana, MP regen, meditate, walk
  speed, party, GMUD, model A–D toggles) → per-lair Exp/Hr with the
  frmMain :25572 argument mapping, generic-HP fallback (avgDmg·2 / 5%),
  and the frmMain party divide (engine at party 1 — the per-monster
  party damage tables are a later wave).
- Lairs / Exp per Hour tab in MainWindow: strip + sortable grid (Group,
  Mobs, Lairs, AvgExp, Delay, Walk, Exp/Hr, RTC, Recovery, Move).
  Auto-populates on database open; Recalculate re-loads (so the GMUD
  toggle correctly re-applies the −0.5 delay at load time).
- RecalculateLairs is defensive: a database without Lairs/Info tables
  degrades to browse-only with a status message.

**Suite: 649/649 green** (incl. end-to-end VM assert equal to a direct
engine-chain computation with a fresh service, party-divide parity, and
GMUD reload).

**Honest remaining scope (the "rest of the rest"):**
- GetDamageOutput + the equipment/character model → real character
  sheets instead of the manual stat strip; enables UseCharacter and the
  per-monster party damage tables (GetPreCalculatedMonsterDamage).
- PullSpellEQ + GetSpellMin/MaxDamage/GetSpellDuration → caster numbers.
- GetLairAveragesFromLocs (:164–392, uses StatsMath — next natural wave)
  → MME's by-monster lair averaging view; single-monster/boss exp path.
- Detail panels (monster/item/spell drill-down), remaining ~60 thin
  lookup wrappers as needed by those panels.
- §8.2 VB6-runtime CSV goldens on the Windows box.

## Session 12 — 2026-07-03 — Phase 2 opened: converter + SQLite gateway + app shell

**Milestone: the application exists.** Mme.App (WPF, net8.0-windows,
EnableWindowsTargeting — cross-compiles green on Linux) with File→Open
Database, live name/number filter, and Monsters/Items/Spells grids.
MainViewModel lives in Mme.App.ViewModels (net8.0) so shell logic is
unit-tested in the sandbox; XAML stays a thin binding layer.

**Converter shipped:** tools/mdb2sqlite (Java/Jackcess fat jar,
self-contained). Converted the stock data-v1.11p.mdb that ships in the
MME repo: 10 tables, 34,078 rows, 1.9 s. MONEY (Currency) columns land
as exact decimal TEXT — the C# readers parse invariantly, so no float
drift enters the data path. VB6 True → -1 preserved. Number indexes
created to mirror the Jet Seek indexes.

**Mme.Data:** MmeDatabase SQLite gateway (read-only open, Probe = the
VB6 OpenTables TOP-1 sanity check, grid row queries over verbatim Access
column names).

**Tests:** Wave12AppShellTests — fixture DB mirroring the converter
schema (incl. MONEY-as-TEXT parsing anchors) + a guarded smoke test
against the real converted 1.11p (1101/1950/1379 rows, filter checks).
Suite 640/640 green.

**Next:**
- Phase 1e wave 2: stats stack (RemoveOutliers/median/MAD/stddev/
  QuickSort) + GetLairAveragesFromLocs + LoadLairInfo over the SQLite
  gateway → lair data feeding the ported GetLairInfo.
- Then wire the Exp/Hr column: LoadLairInfo → GetLairInfo → the
  CalcExpPerHour dispatcher, surfaced in the Monsters grid.
- PullSpellEQ + spell quartet; GetDamageOutput once the character
  model lands; then the character panel in the app.

## Session 11 — 2026-07-02 — Phase 1e opened: storage decision + GetLairInfo

**Decisions:**
- Storage: app reads SQLite natively (Microsoft.Data.Sqlite); OleDb
  package removed. MDB → SQLite via a one-shot Jackcess converter jar
  (same toolchain as the realm tooling). Recorded in the ledger header.
- GetDamageOutput (modMain) deliberately NOT pulled into this wave — it
  is the bridge between character state (Phase 2 domain) and the ported
  CalculateAttack; it gets its own wave. GetLairInfo seams it at the
  exact VB6 call boundary (LairDamageRequest → DamageOutput).

**Done (read line-by-line per §0):**
- Mme.Data goes live: Model/LairInfo.cs (full LairInfoType, with the
  nMaxRegen-is-actually-mob-count naming pin), LairInfoService.cs
  (GetLairInfoIndex/SetLairInfo/GetLairInfo with LairQueryOptions
  seams for chkGlobalFilter / party filter / sGlobalAttackConfig /
  bStartup / GetPreCalculatedMonsterDamage).
- Mme.Core/Model/DamageTypes.cs: tDamageOutput + eDefenseFlags (values
  verbatim from modMain).
- Wave11LairInfoTests.cs: 8 tests — banker's Currency→Long coercions,
  flag thresholds (antiMag mobs/2 vs 0.9 ratio), sentinel path,
  write-back gating, mitigation double-round (41/3 → 13.7 → 14),
  avgAlive-regardless-of-RTK pin (30/(4/6) = 45.0), SetLairInfo gates.
  FIRST-RUN green. Suite 635/635.

**Next (Phase 1e wave 2):**
- GetLairAveragesFromLocs + RemoveOutliers/GetMedian/GetMedianAbsDev/
  GetStdDev/QuickSort (the stats stack feeding LoadLairInfo).
- Then LoadLairInfo + the SQLite schema (extract table/column usage
  from the DB accessors) + the Jackcess converter.
- Then PullSpellEQ (:4067, ~460 lines) and the spell min/max/duration
  quartet. GetDamageOutput wave after the character model lands.

## Session 10 — 2026-07-02 — Phase 1d waves 4+5 (Models C, D, dispatcher) — modExpPerHour COMPLETE

**Done (all read line-by-line per §0):**
- Formulas/ExpHourModels.ModelCD.cs: CephCBuildCombatProfile (expected
  RTK/RTC with surprise-chance mixing, min-damage tails, long-fight mob
  regen), CephCBuildCycleProfile (200-lair macro-cycle threshold sim,
  75%/25% rest starts, 90% targets), CephModelC (slack overhead, spawn
  supply cap, pressure-based recovery split), CephModelD (round-by-round
  ramp-down sim, serialized rest→meditate, per-kill overhead + heavy-hit
  rest relief). Public CephCCombatProfile/CephCCycleProfile types.
- Formulas/ExpHourModels.Dispatcher.cs: CalcExpPerHour + the
  ExpHourModelSelection seam for the bGlobal_ceph* flags. Negative-flag
  decode, model averaging, ShowAll K/M string assembly, text thresholds,
  cluster/backstab suffixes.
- Wave9ModelCDTests.cs (13 tests) + Wave10DispatcherTests.cs (6 tests),
  all from independent VB6-text Python replicas. FIRST-RUN match for the
  third and fourth consecutive waves. Suite 627/627 green.
- PARITY_LEDGER: modExpPerHour marked COMPLETE 28/28 — runtime surface
  fully ported; the VB6 calibration harness + debug printers dropped with
  note (superseded by the parity suite).

**Headline pins (full list in ledger):**
- Dispatcher RTC DOUBLE-ACCUMULATE BUG: nRTC added twice per model and
  divided by nCount twice — single-model runs report DOUBLED RTC in the
  live VB6 app (anchored 16.0 vs Model B's 8).
- Model C one-shot overkill bug preserved (OverkillFrac = 1.0 exactly).
- Killability gate coerces (mobHP − surpriseDMG) via banker's CLng.
- Model C supply has no +0.25; Model D's does. C's walk-speed default is
  1; the dispatcher's 1.25 flows into it in practice.
- Model D ignores nMobHPRegen entirely; unlimited mode still pays the
  1.5 s/mob kill overhead.

**Next: Phase 1e — Mme.Data (modMMudDatabase.bas).**
- GetLairInfo is the priority: it produces every dispatcher input,
  including the nMobDmg transform Model D undoes and the canonical RTK.
- PullSpellEQ feeds CalculateAttack's cast path.
- MDB access strategy undecided (strategy doc silent): likely a one-shot
  MDB→SQLite importer (Jackcess precedent) or MDBTools; decide + document
  before porting. Then Phase 2: Mme.App WPF.

## Session 9 — 2026-07-02 — Phase 1d wave 3 (ceph_ModelB)

**Done (full 1,015-line body read line-by-line per §0, plus re-read of the
cephB_* utility bodies to author the reference replica):**
- Formulas/ExpHourModels.ModelB.cs: CephModelB full port — effective-RTK
  smoothing (caster 0.78/0.74-floor, melee near-2 taper, RoundUp), surprise
  opener v2025.08.27 (signed delta, one-shot gate, pack fade, discrete
  floor), overkill caps with one-shot/low-RTK/group tapers, chain cut,
  mid-band trim, MB4 micro kill trim, travel loop + MB1 micro-route taper +
  wCX easing + dense inflate, HP rest (tick/min/rate boosts, bruiser lift,
  rest pulse), mana model (in-combat MP fraction shaping, MB2 damp, MB5
  cost bump, MB3 pool damp, relabel patch), instant 22 s micro-floor +
  respawn gate via CephBApplySlackWindow, meditation display overlap,
  fraction pack with bBackstabLess flag.
- Wave8ModelBTests.cs: 9 test methods / 10 scenarios from an INDEPENDENT
  Python replica (/tmp/modelB_ref.py) written directly from the VB6 text —
  including full cephB_CalcTravelLoopSecs band logic. C# matched all
  anchors to 13+ decimals on the FIRST run.
- Suite 608/608 green. Ledger 19/28 for modExpPerHour.

**Pins worth remembering (full list in ledger):**
- Boss shortcut precedes the zero bail-out (reverse of Model A) and skips
  the XP knob; EPH is NOT rounded (fractional 8181.81… anchored).
- Caster effRTK 0.74 floor; slowdown output is a effRTK/nRTK ratio.
- One-shot surprise saturates savings via pOneShot Lerp, so regen
  attenuation only matters for PARTIAL surprise (stock/GMUD anchor pair).
- Dead hpWalkEq_B path dropped with note; function-scoped mana vars stay 0
  for slack-window calls when the mana block is skipped.

**Next:**
- Wave 4: Model C (cephC_BuildCombatProfile/BuildCycleProfile/ceph_ModelC
  + tCephC_* types) and Model D (~340-line round sim). Then the
  CalcExpPerHour dispatcher. Then Phase 1e Mme.Data → Phase 2 WPF.

## Session 8 — 2026-07-02 — Phase 1d wave 2 (ceph_ModelA)

**Done (full 1,076-line body read line-by-line per §0):**
- Model/ExpHourTypes.cs: ExpHourKnobs DTO added (externalized
  nGlobal_ceph*_Knob globals; VB6 leaves them 0.0 until UI init and several
  multiplies are UNGUARDED — defaults here are the UI-initialized 1.0).
- Formulas/ExpHourModels.ModelA.cs: CephModelA full port — bail-outs,
  cluster detection, surprise opener (logistic one-shot smoothing, regen
  attenuation, pack fade), boss shortcut, kill/overshoot, HP recovery with
  qRatio bands + sustained gate, mana pool model, movement model
  (density/route/spawn-based), walk-credit recompute, loop-level HP
  fallback, walk/rest overlap credits (2025-11-10 revision), hard zero-rest
  gate, hpScale, spawn gating, fractions, negative-flag encodings.
- Wave7ModelATests.cs: 9 anchors whose expecteds come from an INDEPENDENT
  Python replica written directly from the VB6 text (not the C#), covering
  boss shortcut, basic-damage spawn-gated melee, HP-recovery melee, caster
  pool+walk-credit, surprise-better, surprise-worse (negative attack flag),
  cluster limit (negative move flag + stale-frac divergence), and a
  stock-vs-GMUD regen-attenuation split.
- Suite 599/599 green — C# matched the replica on the FIRST run.

**Pins worth remembering (full list in ledger):**
- Dead tuner block at top; boss shortcut skips the XP knob; UNCLAMPED
  regen-attenuation smoothstep (extreme regen amplifies surprise savings);
  restAsManaEq credit path dead since the 2025-11-10 patch; spawn gating
  mixes the stale pre-overlap demand FRACTION with the overwritten
  post-overlap demand TIME (visible as TimeRecovering ≠ HP fraction in the
  cluster anchor); roomsPerPool floor only on the second computation.

**Next:**
- Wave 3: ceph_ModelB (~1,015 lines). Then Model C+D, dispatcher.

## Session 7 — 2026-07-02 — Phase 1d wave 1 (modExpPerHour foundation)

**Done (all bodies read line-by-line per §0):**
- Module survey: 5,002 lines, 28 procs, pure Double math; ONLY external dep
  is bGreaterMUD. Debug plumbing dropped (never affects math).
- Model/ExpHourTypes.cs: ExpPerHourInfo (tExpPerHourInfo verbatim).
- Formulas/ExpHourMath.cs: all module constants; MinDbl/MaxDbl/ClampDbl/
  SafeDiv; cephC_Ceil + cephC_EstimateMoveSecs; cephB smoothing suite
  (Saturate/SmoothStep/Lerp/MulBlend/BandWeight) + CalcOverkill +
  CalcDensity + CalcTravelLoopSecs + ApplySlackWindow (ref params, tick
  lengths kept as parameters per the VB6 signature); cephD_OverkillFrac;
  cephA_InCombatMPFrac + cephA_CalcHPRecoveryRounds (q-elasticity);
  IsMobKillable pulled forward from modMain.bas onto IGameEngineRules.
- Wave6ExpHourTests.cs: 20 hand-traced anchors (numerically verified during
  authoring), incl. the travel-loop scarcity-overwrite pin in both regimes
  and the IsMobKillable stock-vs-GMUD regen-stall divergence.
- Suite 590/590 green.

**Findings / pins:**
- cephB_CalcTravelLoopSecs: the dens ≥ 5 reduced scarcity coefficient
  (0.15 − 0.03) is silently DISCARDED by the else-branch recompute — it only
  survives in the 12–16-lair discrete band. Genuine quirk, pinned.
- IsMobKillable: nMobTotalHP is a dead store (computed with CLng, never
  read) — dropped with a note; the nCharTotalHP regen credit IS live and
  banker's-rounds (anchored at the 133.33 → 133 boundary).
- Mob regen scales by rtk/window when the fight is shorter than the regen
  window; stock 18 vs GMUD 6 rounds produces killable-vs-unkillable flips.

**Next (Phase 1d continues):**
- Wave 2: ceph_ModelA (~1,076 lines). Wave 3: ceph_ModelB (~1,015).
  Wave 4: Model C (profiles + model) + Model D. Wave 5: CalcExpPerHour
  dispatcher (needs Format()-style strings). Harness procs → Phase 3/drop.

## Session 6 — 2026-07-02 — Phase 1c (clsMonsterAttackSim) COMPLETE via audit

**Situation:** src/Mme.Core/Sim/MonsterAttackSim.cs (1251 lines) pre-existed
with no PORT_LOG entry, no ledger rows, and no tests — provenance unknown.
Per §0 it was treated as unverified.

**Done:**
- Read clsMonsterAttackSim.cls in full (1712 lines): declarations,
  RandomNumber, AddToCombatLog, GetMaxDamage, RunSim (478–1148),
  ResetActiveAtkSpell/BetweenSpell, IsSpellResisted, CalcResistedDamage,
  ProgressBar subs, ResetValues, ResetActiveSpells, private
  Apply_GMUD_DiminishingReturns (:1692 — byte-identical duplicate of
  modMMudFunc :2504; the duplication is faithful to source).
- Full audit of the existing C# against that read. VERDICT: faithful. All
  quirk pins verified present: dead colon-clamp (>100 clamp nested inside
  the <0 branch), nResist_Reduction never reset, stale-x "attack 6" log
  label, next_attempt checks nLastAttackType=1 despite the 'spell comment,
  first between-round slot only (GoTo), duration-spell hit-fail consumes no
  energy vs half on resist/fail, N+1 average divide, MobIsEvil never read,
  ResetValues omits four fields.
- Documented deviations: ctor pre-applies defaults (header pin ADDED this
  session — the one source change), decimal-vs-Double→CCur 1e-14 noise in
  elemental resist and dynamic-calc division, ProgressBar/DoEvents/
  privHandleError dropped. RandomSource seam: Func<double> [0,1).
- Wave5SimTests.cs: 25 anchors using ScriptedRandom (throws on exhaustion →
  every anchor validates exact draw counts). RandomNumber bounds,
  CalcResistedDamage theory (incl. MR<50 boost 100@40→110), IsSpellResisted
  gates/cap-196, GetMaxDamage variants, RunSim scripted scenarios (N+1 avg,
  DR glance, dodge-before-AC, FAIL half-energy, MR reduction stats, RESIST,
  3-round duration trace, between-round first-slot, dead-clamp readback,
  ResetValues omission pins, dynamic-calc early stop).
- Two anchor fixes were mine, not port bugs: next_attempt energy check fires
  after successful attacks too — attempt 3 never draws (Consumed 8→7, 6→5).
- Suite 570/570 green. Ledger section added, marked COMPLETE 13/13.

**Next:**
- Phase 1d: modExpPerHour.bas. Then 1e modMMudDatabase/Mme.Data (incl.
  PullSpellEQ to feed CalculateAttack's castDescription), 1f modItemParse.
  §8.2 VB6-runtime CSV goldens still outstanding (needs Windows box).

## Session 5 — 2026-07-02 — Phase 1b wave 4 (CalculateAttack) — modMMudFunc COMPLETE

**Done (779-line body read line-by-line per §0):**
- Model/AttackTypes.cs: WeaponRecord (tabItems fields + Abil/AbilVal ×20),
  WeaponEquipStats + LoadedCharState (externalized nGlobalChar* session
  globals), AttackDamage (tAttackDamage verbatim).
- AttackMath.GetAbilityEquipSlot — the pure nEquip half of modMain's
  GetAbilityStatSlot (sText half stays Phase 3).
- AttackMath.CalculateAttack — full port: manual/proxy/bare-hand/MA/weapon/
  backstab/bash/smash paths, loaded-character weapon-swap block, ability
  equip loop, encum recalc, energy/swings/QnD/crit chain, defense + stock
  negative-dodge blend, casts build (Abil 43/114/1114 behind a
  castDescription delegate) + the three verbatim regex parse patterns, all
  detail strings.
- Wave4Tests.cs: 49 hand-traced anchors (equip-slot theory, manual, MA
  stock/GMUD, kick DR-ordering, proxies, backstab full trace + stealth
  variants, bash/smash, defense wiring via wave-2 oracles, casts
  build/parse incl. the SpellDmgBonus integer-divide and duration-tick
  paths, loaded-state swap, crit diminishing returns).
- Suite 545/545 green (wave-3 baseline 496 preserved).
- PARITY_LEDGER.md: modMMudFunc marked COMPLETE (70/70).

**Fix during anchoring:** unlisted negative weaponNumbers (e.g. −1) must
return the empty result even when a record is supplied — the VB6 Seek always
NoMatches on them. One-line guard added; no other port defects surfaced.

**Findings / pins worth remembering:**
- The %spell percent variable is SHARED with the stock negative-dodge blend
  fraction — a weapon whose Abil 43 has no preceding Abil 114 can emit the
  leftover blend value as its cast percent.
- Stock applies kick/jk 1.33/1.66 PRE-roll (before DR) while GMUD applies
  them POST-DR with accuracy penalties instead; stock DR subtracts before
  the bash/smash multiplier, GMUD after — big comparative-damage implications.
- GMUD MA damage formulas banker's-round (Long = Double); stock uses Fix
  chains — off-by-one at .25/.75 boundaries.
- Proxy weapons ignore bAbil68Slow and never set sAttackDesc.
- Multi-cast weapons: nExtraAvgHit/Swing are per-match overwrites; the final
  divide averages only the LAST match across the match count.

**Next:**
- Phase 1b is DONE. Phase 1c: clsMonsterAttackSim.cls; then 1d modExpPerHour,
  1e modMMudDatabase/Mme.Data (incl. PullSpellEQ to feed castDescription),
  1f modItemParse. §8.2 VB6-runtime CSV goldens still outstanding.

## Session 4 — 2026-07-02 — Phase 1b wave 3 (spellcast aggregator + combat rounds + parse/ability helpers)

**Done (all bodies read line-by-line in-session per §0):**
- Module types: tCharacterProfile → Model/CharacterProfile.cs; tCombatRoundInfo +
  RoomExitType → Model/CombatTypes.cs; tSpellCastValues + SpellMinMaxDur +
  SpellRecord DTO → Model/SpellTypes.cs.
- CombatMath.CalcCombatRounds + ccr_Saturate/SmoothStep/Lerp/SafeDiv/Max
  (ccr_Min skipped — dead, zero call sites).
- MudParse.cs (new): ExtractTextCommand, ExtractMapRoom, TestPasteChar,
  TestAlphaChar.
- EnumNames: GetAbilityName (full 187-case + GMUD block), GetAbilityList,
  AbilityEffectsCharStats.
- SpellMath: SpellIsInGame, SpellIsUsable, GetCurrentSpellMinMax (pulled
  forward from modMMudDatabase.bas — pure given the record), CalcRoundsToOom
  (pulled forward from modMain.bas), and the big one — CalculateSpellCast.
- VbRuntime: Fix/CStr/CLng/CInt decimal overloads.
- Wave3Tests.cs: 96 hand-traced anchors. Suite 496/496 green (wave-2
  baseline 400 preserved).
- PARITY_LEDGER.md: 18 new rows; modMMudFunc 69/70 accounted.

**Decisions:**
- CalculateAttack DEFERRED to wave 4. Dep survey complete (tabItems ×9 fields,
  PullSpellEQ, frmMain.lblInvenCharStat UI read, bGreaterMUD ×13,
  nGlobalDatVer, bHideRecordNumbers); body ~780 lines NOT read line-by-line
  yet, and porting it properly needs a WeaponRecord DTO + externalized
  PullSpellEQ results. Rushing it at the tail of a big wave is how §0
  violations happen — logged honestly instead.
- GetAbilityStats stays Phase 3 (ListView-bound); CalculateSpellCast consumes
  it only via the verified nValue=0 collapse to GetAbilityName.
- C# signature divergence: GetCurrentSpellMinMax ref params (useLevel,
  noHeader) moved ahead of the optionals (C# requirement); documented in the
  doc header, discard overload provided.

**Findings / pins worth remembering:**
- CalculateSpellCast: abils 8/18/176 with AbilVal ≠ 0 ASSIGN (overwrite)
  the damage/heal accumulators; abil 17 AbilVal ≠ 0 reuses the max resist
  temp for the min add; DamageResisted is points during the loop then
  OVERWRITTEN with signed percent at the end (a fixed-value drain can read as
  −233% resisted); elemental resist hits damage only for abil 1 but both for
  abil 17 — MinRoundDmg can legitimately exceed AvgRoundDmg.
- CalcRoundsToOOM: the aura fail-refund also resets the recast counter,
  doubling recast rate; precheck / round>200-full-mana / rounds=999 are three
  behaviorally identical never-OOM exits (all return the unassigned 0).
- CalcCombatRounds: per-mob HP is CLng banker's in the main path but the
  surprise block recomputes pure-Double; the 1.5 bump fires only when
  RTK == 1 exactly.
- GetAbilityName GMUD: duplicate Case 1101 ("MeetsReqToHit" wins), Case 1102
  missing entirely.
- MR < 50 non-antimagic BOOSTS damage by (50−mr)% in CalculateResistDamage —
  relevant to spell-balance intuition, not just parity.

**Next (wave 4):**
- Read CalculateAttack (1171–1949, /tmp/calcattack.bas) line-by-line; define
  WeaponRecord DTO; externalize PullSpellEQ outputs + the UI read; port +
  anchors. That completes modMMudFunc (70/70) and closes Phase 1b.
- Then Phase 1c: clsMonsterAttackSim.cls.

## Session 3 — 2026-07-02 — Phase 1b wave 2 (dodge/accuracy/stats/energy/resist)

**Done (62/70 modMMudFunc procs now accounted — see ledger):**
- `Model/GameEnums.cs` — AttackTypeMud, EvilPoints (VB6 Double literals
  banker's-rounded on enum coercion; "Villian" typo kept), MagicType
  (enmMagicEnum lives in modMMudDatabase.bas but is pure — pulled forward).
- `Engine/GameEngineRules.cs` — 9 new members, each cited to its VB6 origin:
  MaxSwings (GMUD 6 when DatVersion > 1.85), RestingRateDivisor (750/500),
  DodgeVsAccuracy, DodgeMaxAccuracyForPercent (stock closed-form + nudges /
  GMUD binary search), BackstabAccuracy, MovementSpeed, Picklocks,
  QuickAndDeadlyBonus (GMUD divisor 40/50 version gate), ManaRegenBonus.
- `Formulas/CharacterMath.cs` — CalcDodge, CalcEncum (UI read externalized →
  isV111iData), CalcEncumbrancePercent, GetEncumPercents (faithful float-loop
  drift), CalcRestingRate (overflow → −1 pin), CalcManaRegen, CalcMr,
  CalcMaxHp, CalcMaxMana, CalcSpellCasting, CalcCpLevel,
  CalcMoneyRequiredToTrain, CalculateStealth (ref-text + discard overload).
- `Formulas/CombatMath.cs` — CalculateAccuracy (GetClassCombat externalized →
  classCombat param), CalculateAttackDefense (dead-lower-clamp pin),
  CalcBsDamage, CalcTrueAverage, CalcEnergyUsed family (Currency semantics),
  AdjustSpeedForSlowness.
- `Formulas/SpellMath.cs` — GetSpellCastChance (tabSpells path externalized via
  fromSpellLookup flag), ResistPctSignedOfBase, NegResistPctShareOfTotal,
  CalculateResistDamage (full Currency/Double mutation chain).
- `Formulas/MudMath.cs` — added GmudGetSpDmgMultiplierFromSc.
- `Text/VbRuntime.cs` — **CORRECTION**: CStr(double) changed from
  shortest-round-trip to "G15" (VB6 caps CStr at 15 significant digits — the
  1a version would print 0.30000000000000004 where VB6 prints 0.3; regression
  test added). New: CLng, CInt, Round(double/decimal, digits), CCur.
- `tests/Wave2Tests.cs` — 129 new hand-traced anchor tests (float-drift-
  sensitive anchors verified against an IEEE-double replication of the exact
  VB6 op order). **400 tests, all passing** (baseline was 271).

**Decisions this session:**
- Interleaved single-gate `bGreaterMUD` checks inside otherwise-shared bodies
  (CalculateAccuracy, CalculateStealth, CalculateAttackDefense) stay inline via
  `rules.Kind` with the VB6 line cited, rather than duplicating whole ~100-line
  bodies per engine. Whole-formula divergences remain interface members.
- Currency arithmetic rule pinned: VB6 `/` on Currency → Double; assignment
  back to a Currency variable banker's-rounds to 4 dp (VbRuntime.CCur).
- GetSpellCastChance Integer-overflow error path (error 6 → 0) not replicated;
  inputs bounded far below ±32767. Noted in the doc comment per strategy §0.

**Findings this session (feed forward):**
- CalcCPLevel is declared as plain `Function` (no Public/Private) — grep for
  `^Function` too when inventorying modules, or procs get missed.
- CalculateAttackDefense: all lower clamps are colon-chained onto single-line
  upper-clamp Ifs → DEAD CODE in VB6. Only upper clamps are live. Do not "fix".
- GMUD CalcPicklocks and the VB6 source's own C# reference comment disagree
  (round vs truncate); the VB6 code rounds — code wins over comments, always.
- CalculateStealth GMUD labels print stock Fix values while the total uses a
  rounded float sum — UI in Phase 3 must replicate the mismatch, not the sum.
- GetEncumPercents depends on For-loop Double drift for its exact output
  ("80% @ 3840" not 3841 at totalEncum 4800) — never convert to integer steps.

**Next step (Phase 1b wave 3 — final ~8 procs):**
- CalcCombatRounds + `ccr_*` privates (modMMudFunc.bas 165–330) — pure once
  tCombatRoundInfo is defined; port the type with it.
- ExtractTextCommand/ExtractMapRoom, TestPasteChar/TestAlphaChar,
  GetAbilityName/GetAbilityList/AbilityEffectsCharStats (pure Select Case).
- The two giants last, with tabSpells/tabClasses/tabItems contracts
  externalized the same way as this session: CalculateSpellCast (745–1133,
  tabSpells ×81 reads → design a SpellRecord DTO first) and CalculateAttack
  (1171–1949, ~780 lines). GetAbilityStats + GetQuickAndDeadlyBonus remain
  Phase 3 VM. tCharacterProfile/tSpellCastValues/tAttackDamage types come in
  with these consumers.

## Session 2 — 2026-07-02 — Phase 1a delivery + Phase 1b wave 1

**Done:**
- Finished Phase 1a deliverables: `Mme.ParityHarness` now emits CSVs
  (val/putcommas/formatcommas/extractnumbers/rounding + expneeded sweep),
  README.md, .gitignore, strategy doc copied into `docs/`.
- Phase 1b wave 1 (27/70 modMMudFunc procs accounted — see ledger):
  - `Formulas/GameConstants.cs` — module constants incl. STOCK_/GMUD_ pairs.
  - `Formulas/EnumNames.cs` — all 13 Get*Enum mappers (dependency-audited CLEAN).
  - `Formulas/ExpTables.cs` — CalcExpNeeded_STOCK (Currency→decimal, uint32
    rollover emulation), _GMUD (taper cliff 34), _GMUD_1_8_5, private
    modifiers/IDiv/CanI64Mul.
  - `Formulas/MudMath.cs` — RoundUpToNearest5, GMUD_DiminishingReturns.
  - `Engine/GameEngineRules.cs` — **first IGameEngineRules members born from
    real branches** (strategy §4, method-by-discovery): HitMin(classArmourType?),
    HitCap, SpellHitCap, DodgeCap(soft), MobHpRegenRounds, ExpNeeded.
    StockRules + GreaterMudRules(datVersion); NO ParamudRules subclass yet —
    the only version gate so far (exp ≤1.85) lives inside GreaterMudRules with
    the VB6 line cited. Promote to subclass when Paramud-only branches multiply.
- **271 tests, all passing** (98 new).

**Design decisions recorded:**
- `IGameEngineRules.ExpNeeded` returns double (the VB6 dispatcher's type);
  `ExpTables.CalcExpNeededStock` returns decimal (the inner Currency type).
- GetHitMin's tabClasses lookup externalized: interface takes the RESOLVED
  class ArmourType (null ⇔ nClass=0). VB6 lookup-miss semantics = pass 0.
- GMUD_DiminishingReturns stays a named static (not a rules member): VB6 call
  sites call the GMUD-prefixed function explicitly; polymorphism would invent
  a stock behavior that doesn't exist.
- Exp anchors are hand-traced pins; §8.2 VB6-runtime goldens still owed —
  harness emits `expneeded.csv` (tables 100/200/290/400/600 × levels 1–255)
  ready to diff against a VB6-side dump.

**Next step (Phase 1b, session 3):**
- Continue modMMudFunc.bas: `DodgeMaxAccForPercent` (101 lines, bGreaterMUD ×1),
  `CalcDodge`/`CalcDodgeVSAccuracy`, `CalculateAccuracy`/`CalculateAttack`
  family — audit deps first; expect first CharacterSheet fields (nGlobalChar*
  weapon/accuracy globals) to surface here → create Model/CharacterSheet with
  the faithful 1:1 property bag (strategy §3) when the first global appears.

## Session 1 — 2026-07-02 — Phase 1a COMPLETE

**Done:**
- Solution scaffolded per strategy §2: `Mme.Core` (net8.0, BCL-only),
  `Mme.Data` (net8.0 + System.Data.OleDb 9.0.x, empty for now),
  `Mme.ParityHarness` (console), `tests/Mme.Core.Tests` (xUnit).
  `Mme.App` (WPF) deliberately NOT created yet — WPF projects require the
  Windows targeting pack; create it in Phase 2 on a Windows machine.
- Ported `modSyntaxsFunc.bas` pure procs → `Mme.Core/Text/`:
  - `VbRuntime.cs` — VB6 primitives: `Val` (whitespace-skipping, E/D exponents,
    &H/&O with 16/32-bit signed reinterpret, correctly-rounded via canonical
    string + double.Parse), `Trim` (SPACES ONLY — VB6 Trim$ parity),
    `CStr(double)`, `Int` (floor), `Fix` (truncate), module constants.
  - `TextUtils.cs` — 23 ported procedures (see ledger).
  - `RegexUtils.cs` — `RegexFindV2` + `EscapeRegex`, `RegexMatchV2` type.
- **173 tests, all passing** (built + run with .NET 8.0.422 SDK).
- Ledger fully populated for modSyntaxsFunc.bas: 23 Ported, 13 Skipped
  (UI/BCL), 1 Skipped (dead code: RegExpFind v1 — zero live call sites).

**Findings this session (feed forward):**
- `RegExpFind` (v1) is dead code — never call sites; only v2 is live (7 sites).
- PINNED VB6 BUG in `PutCrLF`: exactly one char after the final LF is dropped.
  Faithfully ported + tested. If a Phase 3 view ever "loses" a trailing char in
  converted text, this is why — decide THEN whether to fix app-side.
- `GetFirstWord` returns "" for ANY input with a leading space (space search
  runs on the untrimmed string). Callers in later phases may rely on this.
- VB6 `Trim$` strips spaces only. `VbRuntime.Trim` everywhere in ported code;
  never use .NET `string.Trim()` when porting.
- VB6 `Format$` midpoint behavior pinned as banker's (0.5→"0", 1.5→"2") in
  `FormatWithCommas`. If a VB6-side golden ever disagrees, revisit with real
  VB6 output and update BOTH code and this note.
- `FindStringIndex` error path returns 0 (not -1) — latent VB6 bug, kept.

**Environment notes:**
- .NET SDK installed at /home/claude/dotnet in the Linux work container;
  on Windows use any 8.0.4xx SDK. No solution-level pinning yet.
- System.Data.OleDb added to Mme.Data (restores fine on Linux; runtime is
  Windows-only, which is fine — it's not exercised until Phase 1e).

**Next step (Phase 1b, session 2):**
- Begin `modMMudFunc.bas` in dependency order per strategy §7-1b:
  start with the ~12 `Get*Enum` procedures (no dependencies), then
  `GetHitMin`/`GetHitCap`/`GetDodgeCap`/`GetSpellHitCap`, then the
  `CalcExpNeeded_STOCK/_GMUD/_GMUD_1_8_5` family — which is the first
  `IGameEngineRules` material (strategy §4).
- Before porting each proc: grep its body for `nGlobal`, `tab[A-Z]`,
  `bGreaterMUD`, `nNMRVer` and record hidden deps in the ledger row.
- Create `Engine/` (IGameEngineRules + StockRules/GreaterMudRules/ParamudRules
  skeletons) and `Model/CharacterSheet` scaffolding as the first branch/global
  is encountered — not before.
