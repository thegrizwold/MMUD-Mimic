# FEATURE CENSUS — VB6 MMUD Explorer → MMUD-Mimic (C# port)
Status legend: DONE / PARTIAL / CODE-ONLY / MISSING / SKIP.
CODE-ONLY = engine/VM support exists but there is NO UX for it.
This file is the single source of truth for parity gaps. Updated from a
full control-tree audit of frmMain.frm (Session 44). Nothing hides here.

## SIM WAVE: WIRED (S44 Wave C — beta 9)
The Phase-1c MonsterAttackSim engine is now connected end-to-end:
MonsterSimLoader (PopulateMonsterDataToAttackSim :5419 + item bonuses +
SpellHasAbility), ConfigureSim (SetupMonsterAttackSimWithCharStats
:8110 from calculator slots 2/3/8/24/20/25-29), CalcMonsterDamage /
CalcAllMonsterDamage (:8016/:7931 incl. the mixed anti-magic party
split), the MonsterDamageService tier dispatcher, Options menu items,
and the lair mitigation provider (GetLairInfo :713 substitution with
VB6-exact per-step banker's accumulation).

## Global chrome / cross-cutting
- [DONE] Tab button row (13 tabs incl. Exp/Hr split), F-key tips absent
- [DONE] Tab hotkeys F1–F12 (S44; F3 yields to the map find-next box)
- [DONE] Global Filter strip: Use Character, Level, Class, Race, Align,
  Min lvl, GMUD toggle (live-applies; VB6 needed "Filter All" click —
  acceptable divergence, logged)
- [PARTIAL] **Right-click context menus** (S44 Wave A core shipped):
  - [DONE] Items grids (Weapons/Armour/Sundry/Item Manager):
    Equip/Unequip Item, Add to Compare, Copy Name(s), Copy Details,
    Set Combat Backstab Weapon, Add to Item Manager (getable gate)
  - [DONE] Spells grid: Mark/Unmark Spell as Learned (✓ Lrn column),
    Set as Combat Attack/Heal Spell, Set as Bless Spell, Copy
    Name(s)/Details; Options → Clear Learned Spells
  - [DONE] Monsters grid: Copy Name(s), Copy Details (Beta 27: now
    copies the FULL flattened dossier — attack rows + Damage vs Mob +
    Scripting + Lair Stats — via MonsterDetailText/FlattenDossier;
    the S44 row-summary divergence is closed)
  - [DONE Beta 11] Items: Calc Swings / Calc Backstab ctx
  - [DONE S44 Wave H] Spells: What Casts This? → resolved-reference
    results window (lean GetLocations: Monster/Item/Spell/Shop #,
    Room map/room, lair group indexes → room names. DIVERGENCE:
    percent columns, shop item values, textblock/NPC refs unported)
  - [DONE S44 Wave H] Monsters: Where/How is this Monster Summoned? →
    the same resolver over "Summoned By"
  - [DONE S44 Wave H] Sundries: View Chest Contents + Copy Chest —
    full GetChestItems port (abil 43 → spell → abil 148 → textblock;
    cumulative-difference percents; giveitem accumulation; random
    recursion, nest cap 5; compound-failure merge). The sundry grid
    now carries its own ctx menu.
  - [MISSING] Items: Add to Save List (needs compare lists)
  - [MISSING] Spells: Find counters (negates/dispells)
  - [MISSING] Monsters/Shops: Add Items to Save List, Add to Monster
    List (compare), Set Weapon/Spell Combat Calc. to Mob, Set Row(s)
    to This Shop
- [MISSING] Menus — File: Recent Databases (5), Revert to Saved, Save
  Character As, Close File. Options: Calc Monster Dmg vs Char / vs
  Party / Clear Calculated (sim wiring), Clear Learned Spells, Import/
  Export Character from/to NMR, Reload MME, Remove All Filters,
  Settings dialog, "Jump to Equip/Lists on Add" toggle. Tools: External
  Map only (BS/Hit/Swing Calculators = Beta 11, Attack Simulator =
  Beta 12, Exp Calculator / Coin Converter / Notepad = S44 Wave H —
  Coin Converter incl. the charm markup/discount button; Notepad is
  session-persistent w/ Save-As, DIVERGENCE: no INI persistence;
  Spell Book = DONE).
  Help: About/Release Notes (SKIP: Donate, Check Updates, Contribute).
- [SKIP] DEBUG button, Builder button (dev-only tools)

## Engine / math
- [DONE] Damage output services, EQ stats calc (47 slots), Exp/Hr lair
  engine, item values (charm buy/sell), spell gates, Find Best/Next
  Best, char derived stats, CP system, race stats
- [DONE] MonsterAttackSim fully wired (S44 Wave C): loader, char/party
  setup, vs-Char/vs-Party tables, tier dispatcher, lair mitigation.
  DIVERGENCES: no progress dialog/cancel (synchronous, seconds); MR
  input = CharMrOverride else computed slot 24 (OG txtCharMR shows the
  same computed value); rounds fixed at 500 pending Settings; legacy
  pre-NMR-1.8 loader branches unported (can't trigger on a 1.8+ DB)
- [MISSING] Hit Calculator math surface (frmHitCalc)

## Monsters — verbose detail (S45)
- [DONE Beta 18] The PullMonsterDetail attack sections: Dmg/Round
  (red, AVG + sim Max), vs-char/vs-party lines, Between Rounds
  (running-percent quirk + spell coloring), per-attack rows
  (True%, Min-Max, Accuracy, Energy Max-x/round, Hit Spells), spell
  attacks (inline EQ, area Target, Success %), Greet Commands,
  Spawns via (resolved rooms, cap 30). Lazy build on selection.
- [MISSING] Damage vs Mob + Scripting Estimate sections (calc-columns
  wave), EndCast recursion in the inline spell text, spell-jump
  navigation, HPs regen-timing text, Type row.

## EQ panel — combat plumbing (S45 beta 19)
- [DONE] Weapon cast-proc term in Attk/damage-out (castDescription +
  the sCasts regex parse, SpellDmg%% double-apply quirk preserved).
- [DONE] LoadedCharState wired (panel Q&D + per-weapon stat arrays;
  crit subtract/re-add cycle live). uiAccuracyFallback wired.
- [DONE] Use Additional Weight toggle + paste auto-fill from the
  Encumbrance line; live-panel encum now feeds the profile.
- [MISSING] MME-export paste keys (CurrentENC + coin-weight keys);
  EndCast recursion in cast text (shared with monster-detail gap).

## Spell "Learned From" sources (S47 beta 29)
- [DONE] Spells pane lists where a spell is learned: items carrying
  ability 42 (LearnSp) — scanned directly, so it finds teachers the
  denormalized "Learned From" field misses — plus NPC refs, plus for
  textblock-taught spells the traced NPC (Called From -> room -> NPC),
  the quest command, the required checkitem, and the class/level gate.
- [DONE] All of it is double-click jumpable: Item: lines -> the item's
  tab (via DoEquipJump), Monster: lines -> Monsters, [TB n] -> the
  textblock viewer. Closes the "Item: line jumps unported" divergence.
- [MISSING] the rest of PullSpellEQ's OG quartet/nest presentation.

## Monster combat dossier (S46 beta 26)
- [DONE] Damage vs Mob (weapon desc + swings/crit/hit detail + RTK/RTD
  + immune:MagicLVL), Damage vs Lair, Scripting Estimate (exp/hr + the
  %-breakdown details), full Lair Stats block incl. jumpable Other Lair
  Mobs. Dossier refreshes on UseCharacter/AttackMode change.
- [MISSING] 500-round attack sim line (clsMonsterAttackSim wave);
  OOM-rounds line (heal-cost global, menus wave); NMR Possy spawn-chance
  variant (data absent).

## Grid icons v2 (S46 beta 25)
- [DONE] Icons are hand-drawn vector geometry (no emoji font dependency,
  no collisions): sword vs hammer, real helmet, distinct belt, per-slot
  shapes, spell elements. Columns named (Edge/Slot/Element) + sortable
  via SortMemberPath.
- [MISSING] Poison as a distinct element (no distinct AttType in data).

## Grid icons (user enhancement, S45 beta 24 — superseded by v2)
- [DONE] Spell element icon column (Cold/Fire/Stone/Lightning/Normal/
  Water + Heal sparkles), weapon sharp/blunt icon, armour worn-slot
  icon. Empirical AttType->element map; emoji glyphs (Segoe UI Emoji).
- [MISSING] Poison as a distinct element (no distinct AttType in data);
  custom per-realm element overrides.

## Filter perf + textblock modal (S45 beta 23)
- [DONE] Search debounce: only the equip-catalog rebuild is deferred;
  grids + browse decoration stay live per keystroke (fixes type/
  backspace lag). Clear Filters button (resets More Filters + search).
- [DONE] Textblock modal: linked blocks (LinkTo, random/goto/word:N)
  are clickable to open the linked block.

## Monster spawns + textblocks (S45 beta 22)
- [DONE] Spawns-via group/lair parse fixed (real map/room, mob count,
  spawn %); npc/shop-sell/shop-nogen/textblock-rndm tokens.
- [DONE] Textblock refs resolve to container item name + chance,
  clickable to a textblock detail window (roll->effect + Called From).
- [MISSING] Full Lair Stats block (Total Lairs, AVG DMG/mob, Effective
  MagicLVL/SpellImmu, Other Lair Mobs), Damage vs Mob/Lair, Scripting
  Estimate — deferred to a dedicated session.

## Compare lists (Wave I, S45 beta 21)
- [DONE] Four compare grids (Weapons/Armour/Spells/Monsters) with
  Add/Add-All/Clear/Refresh; context-menu + toolbar; weapon/armour
  double-click jumps back to browse.
- [MISSING] Sundry compare grid (none in OG); saved-list persistence
  (Index>=500); monster compare location sub-panel; live row binding.

## Rooms/Map + navigation (S45 beta 20)
- [DONE] Split Map/Room jump boxes; black map background; OG default
  presets; working Find/Next; Find Rooms with Exits (no progress
  bar/cancel divergence).
- [DONE] Jump navigation: item sources pane, monster Spawns-via,
  result windows -> Rooms/Monsters tabs.
- [MISSING] Item:/Spell:/Shop: line jumps; spell-jump nav from cast
  text; map right-click room menu extras.

## Char tab
- [DONE Beta 15] Paste/vitals audit: paste auto-pulls the Combat/
  Equipment Entries (incl. Stealth, previously never pulled); the
  HP/Regen/Spellcasting-Mana panel boxes auto-fill from the computed
  character on every recalc (the VB6 Tag values, computed directly);
  Derived rest/mana read slots 16/17 as bonus inputs (conflation fix).
  SpDmg% manual on stock (GMUD-only); attack-damage strip = Choose
  Attack flow.
- [DONE] Stats/class/race/level/align, steppers, paste, save/load,
  Reload/Reset/Max/CP/Reset Fields, derived panel, bless pickers,
  quests, 2nd/6th align + GMUD combos, worn-EQ link, Mana Regen Needed
- [DONE] **Base loading** (S44): DB open defaults class/race to first
  entries, level 1, stats to race minimums; race change shows min-max
  ranges beside each stat and raises below-min stats (VB6 :21444)
- [DONE] Anti-Magic checkbox + MR Override box (S44, by the derived
  MR line — feeds the sim; spell vs-AM filter still pending)
- [MISSING] Open Hit Calculator button (window missing)
- [MISSING] Bless per-slot ">" jump + Apply Filter / Unfilter / Reload
  Save / Clear buttons
- [MISSING] Stat button hold-to-repeat (logged skip candidate)
- [MISSING] "Stats"/"CP"/"Dodge"/"MR" quick-toggle buttons (cmdCharButtons
  view switchers — verify OG behavior before porting)

## EQ tab
- [DONE] 20 slots single column, Hold checkboxes, computed panel,
  click-to-adjust, Choose Attack, Find Best/Next Best, No-Limiteds,
  carried items, per-slot ">" (Goto + Add to Compare), Copy EQ/Stats
- [MISSING] Hold mass-toggles: **All / None** buttons + **Empty**
  (clear all slots) + **Reset**
- [MISSING] Strength override box + +/- (txtInvenStrength) — Find Best
  weight budget vs char STR
- [MISSING] Use Additional Weight + Calc. Item Weight
- [MISSING] "Unequip on Paste" checkbox
- [MISSING] "Disable Character Specific Stats" checkbox
- [MISSING] Clear Manual Stat Adjustments (one button, all slots)
- [MISSING] Apply Global Filter / Remove Filter buttons on-tab
- [MISSING] Add All to Compare, Copy cmds to EQ these items
- [MISSING] Weapon-slot ">" extras: Calc Swings / Calc BS (windows)
- [SKIP] Stat/Label font pickers (theme system supersedes — confirmed?)

## Weapons tab
- [DONE] Grid incl. Calc.Combat columns (Dmg/Spd, #Swings, xSwings,
  Dmg/Rnd), live Find, detail dossier + locations
- [DONE] Filter panel core (S44): 1H/2H Blunt/Sharp, Non-Magic (abil
  28), BS-able (abil 116), Limiteds, Speed<=, STR<= — live-apply
  (DIVERGENCE: 0/empty disables a gate; Limiteds defaults to SHOW)
- [MISSING] Filter panel rest: Speed@85 column toggle, DMG>= vs AC/DR +
  Dodge, MagicLVL combo, class combo, Ability/Negate-Spell filter
- [PARTIAL] Find/Next = live filter (OG Find moves selection without
  filtering; divergence logged, keep)

## Armour tab
- [DONE] Grid, live Find, detail + locations
- [DONE] Worn-On combo, 7 armour type checkboxes, Non-Magic, No Limit
  (S44, live-apply)
- [MISSING] Ability filter (combo+op+val), **Next Slot** button

## Spells tab
- [DONE] Grid, live Find, detail dossier
- [MISSING] Filter panel: Magery + Magery Level combos, Target combo,
  Attack Type combo, Contains Ability combo, Learnable Only, vs
  Anti-Magic, Apply/Remove
- [MISSING] Calc. Combat vs MR (damage columns at a given MR)
- [DONE] Learned spells (S44): ✓ Lrn grid column, ctx Mark/Unmark,
  Options → Clear Learned Spells

## Classes / Races tab
- [DONE] Both grids with abilities columns
- [DONE S44 Wave G] **View Spellbook** ctx on the classes grid — class
  view at level 999 (frmMain :22034 seeding), read-only book per class
- [MISSING] Detail text panes (class/race dossier), **Equip Selected**
  (apply race/class to character)

## Sundry tab
- [DONE] Grid, live Find, detail + locations
- [MISSING] Ability filter (combo+op+val) + Apply/Remove
- [DONE S44 Wave H] **View Chest Contents** + Copy Chest

## Monsters tab
- [DONE] Grid (Rgn/Exp/HP/AC-DR/Dodge/MR/Damage/LairExp/Mag/Undead),
  live Find, rich dossier (abilities, drops, attacks, dmg/round)
- [DONE] Filter row (S44 Wave D): Regen op combo, HP<=, DMG<= (uses the
  calculated vs-Char/vs-Party tier when computed), EXP>=, Mag<=.
  STILL MISSING: 1-Shot-All, party phys/mag damage-out boxes
- [DONE S44 Wave G] By Lair / By Mob mode radios: lair-average HP and
  Damage columns with the "*" marker (GetLairAveragesFromLocs ported —
  regen-weighted Exp/HP, divisor = total matches, 51% accy majority,
  mode-ties-to-higher magic levels), the Exp/Hr column (lair-average
  CalcExpPerHour or the RegenTime/"Room" per-monster GetDamageOutput
  branch, ÷party), the Recovery% column, the lair filter gates (HP vs
  lair avg :25404, EXP vs exp/hr :25522, DMG By-Mob-only :25601), plus
  the By-Mob Exp/(Dmg+HP) column (:5998) as a bonus.
  DIVERGENCES: Acc (Maj/Mx) column unported (both modes — see census);
  pre-1.83 possy/lair-percent branch dead for our data
- [PARTIAL] Party frame: AC/DR/MR/Dodge/#AM boxes DONE on Exp/Hr (S44,
  feed the vs-Party sim); MaxHP/XMag/RestHP/Acc/Swing/paste MISSING
- [DONE S44 Wave G] More Filters window (frmMonsterFilters :833/:996 +
  the frmMain :25370–25530 gates): cash denomination ladder,
  AC/DR/MR/BSDef/Dodge/GameLimit <=, AvgLairExp >=, #Lairs >=,
  #Mobs <=/>=, undead, non-hostile align whitelists, attack-summary
  gates (no poison/confusion/fear via SpellHasAbility abils 19/71/60,
  acc majority/max — GetMonsterAttackSummary ported incl. the MidSpell
  alternating-difference nPercent quirk and the majority-within-2
  max collapse), the 3 ability filters with the absent-passes-<= rule,
  and Show All grey (RGB 192) instead of skip.
  DIVERGENCES: nMonsterPossy array unported (≥1.83 lair-average checks
  are the live path); spell-attack-type letter strings unported; the
  name Find still hides rows under Show All (OG find selects)
- [DONE Beta 12] Attack Simulator window
- [MISSING] Choose Attack / Damage Out / HP<= helper buttons (Choose
  Attack dialog EXISTS — needs entry point here)

## Shops tab
- [DONE] Shop list, inventory pane (Max/Regen/Cost), info text
- [CODE-ONLY] **Charm-adjusted buy/sell prices** — engine math done,
  NO charm input on tab (VB6: Charm box + +/- buttons)
- [MISSING] Show Buying / Show Selling checkboxes
- [MISSING] Show Trainers
- [MISSING] Jump-to-item cross-links (click inventory row → item tab)

## Lists tab
- [DONE] Item Manager: 11-col grid, paste import, Add, Remove Selected,
  Clear Non-Flagged, detail/locations
- [MISSING] IM flag action buttons: Stash, Sell/Buy, Pickup/Use,
  Drop/Hide, Carry, Clear Flag, +, -, Invert, Copy Text
- [MISSING] **Compare LISTS** — Weapons/Armour/Spells/Monsters list
  grids w/ per-list X clear, Clear All, Refresh, nav buttons; feeds
  from "Add to Save List" ctx menu + EQ Add All
- [MISSING] Attack Simulator button (Lists-side entry)
- [MISSING] Row sequence tags (LV_AssignRowSeqIfMissing)

## <-> Compare
- [DONE] A/B compare with optimizer deltas
- [MISSING] OG compare list semantics (see Lists)

## Rooms (map)
- [DONE] Chart engine, glyphs, tooltips, travel, keyboard walking,
  history, presets (10 + save), find-by-name, Leads Here (exit phase),
  Back
- [MISSING] Map options panel: Follow Map Changes, Don't Follow Hidden/
  Restricted, Don't Mark Lairs/NPCs/Commands, No Tips, Show All Exits
  in Tooltip, Allow Overwrite, Allow Dupes, **Also Mark: None/Shops/
  Spells**, B (blocks toggle)
- [MISSING] Find Rooms with Exits (FindRoomWithDirections)
- [MISSING] frmMap zoom/external window, Help/Legend window
- [MISSING] MegaMUD: Find MegaMud Room/Group, Pathing window
- [MISSING] LeadsHere spell-teleport / monster / textblock phases
- [DONE] vs-Char/vs-Party dmg tiers live behind the map dmg line
  (Options → Calc Monster Dmg fills the tables the line reads)
- [MISSING] Map right-click: Follow Up/Down and Redraw, Redraw From
  Here

## Exp/Hr tab (C#-only split of VB6 lair features)
- [DONE] Lair grid, manual combat entries, party size, exp multi,
  A-D presets, Recalculate, worn-EQ pull
- [DONE] Lair mitigation vs char/party (S44 Wave C: live provider;
  seam widened to Currency-faithful Func<long,int,double>)

## App chrome
- [DONE] MUD/Classic themes, dark retemplates, align colors, resizable
  panes, min window size
- [PARTIAL] Carried-item picker dropdown (text entry today)
