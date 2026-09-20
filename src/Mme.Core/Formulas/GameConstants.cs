namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modMMudFunc.bas :: module-level Public Const declarations.
/// Engine-variant pairs (STOCK_*/GMUD_*) are consumed via IGameEngineRules;
/// they are exposed here so parity tests can reference the raw values.
/// </summary>
public static class GameConstants
{
    public const double MaxSwings = 5.0;              // VB6: MAX_SWINGS (stock base; engine variance — GMUD 6 when DatVersion > 1.85 — lives on IGameEngineRules.MaxSwings)
    public const int RoundSecs = 5;                   // VB6: ROUND_SECS
    public const int SpellRoundSecs = 3;              // VB6: SPELL_ROUND_SECS
    public const double MoveSecsBase = 1.1;           // VB6: MOVE_SECS_BASE

    public const int StockHitMin = 8;                 // VB6: STOCK_HIT_MIN
    public const int GmudHitMin = 2;                  // VB6: GMUD_HIT_MIN
    public const int StockHitCap = 99;                // VB6: STOCK_HIT_CAP
    public const int GmudHitCap = 100;                // VB6: GMUD_HIT_CAP
    public const int StockSpellHitCap = 98;           // VB6: STOCK_SPELL_HIT_CAP
    public const int GmudSpellHitCap = 100;           // VB6: GMUD_SPELL_HIT_CAP
    public const int StockDodgeCap = 95;              // VB6: STOCK_DODGE_CAP
    public const int GmudDodgeSoftCap = 55;           // VB6: GMUD_DODGE_SOFTCAP
    public const int GmudDodgeCap = 98;               // VB6: GMUD_DODGE_CAP

    public const int StockMobHpRegenRounds = 18;      // VB6: STOCK_MOB_HPREGEN_ROUNDS
    public const int GmudMobHpRegenRounds = 6;        // VB6: GMUD_MOB_HPREGEN_ROUNDS

    public const double GmudGhouseShopMarkup = 200.0; // VB6: GMUD_GHOUSE_SHOP_MARKUP

    internal const double I64Max = 9.22337203685478E+18; // VB6: I64_MAX (Private Const, 2^63-1 as the same Double literal)
}
