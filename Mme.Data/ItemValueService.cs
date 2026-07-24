using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6: modMMudDatabase.bas :: GetItemValue (:3469) + GetShopMarkup (:1700)
/// + modItemParse.bas :: ExtractShopsFromObtainedFrom / EvaluateBestPriceForHit
/// (:1242). Copper pricing with currency multipliers, shop markup, the charm
/// buy/sell modifiers (including the stock 4294967295 overflow-wrap bug,
/// preserved), friendly coin reduction, and best-shop selection: cheapest
/// BUY among buying shops (tie → lowest shop number), else the lowest-number
/// SELL-only shop.
/// </summary>
public sealed class ItemValueService(MmeDatabase db, bool greaterMud)
{
    public sealed record ItemValue(double CopperBuy, double CopperSell,
        string FriendlyBuyShort, string FriendlySellShort);

    private Dictionary<long, (double Price, int Currency, string Obtained)>?
        _items;
    private Dictionary<long, int>? _markups;

    private void EnsureLoaded()
    {
        if (_items is not null) return;
        _items = [];
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT "Number","Price","Currency","Obtained From" FROM "Items"
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                _items[Convert.ToInt64(r[0])] = (Convert.ToDouble(r[1]),
                    Convert.ToInt32(r[2]), r[3] as string ?? "");
        }
        _markups = [];
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT "Number","Markup%" FROM "Shops"
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                _markups[Convert.ToInt64(r[0])] = Convert.ToInt32(r[1]);
        }
    }

    public int GetShopMarkup(long shop)
    {
        EnsureLoaded();
        return _markups!.TryGetValue(shop, out int m) ? m : 0;
    }

    /// <summary>GetItemValue (:3469). Price 0 → "Free"/"(no value)".</summary>
    public ItemValue GetItemValue(long itemNumber, int charm = 0,
        int markup = 0, long shopNumber = 0, bool noBuy = false)
    {
        EnsureLoaded();
        if (!_items!.TryGetValue(itemNumber, out var it))
            return new ItemValue(0, 0, "unknown", "");
        if (it.Price == 0) return new ItemValue(0, 0, "Free", "(no value)");

        double copperBuy = it.Currency switch
        {
            1 => it.Price * 10,       // Silver
            2 => it.Price * 100,      // Gold
            3 => it.Price * 10000,    // Platinum
            4 => it.Price * 1000000,  // Runic
            _ => it.Price,            // Copper
        };
        double copperSell = copperBuy;

        if (!noBuy)
        {
            if (shopNumber > 0) markup = GetShopMarkup(shopNumber);
            if (markup > 0)
                copperBuy += VbRuntime.Fix(copperBuy * (markup / 100.0));
        }

        if (charm > 0)
        {
            if (greaterMud)
            {
                copperSell /= 2;
                double mod = VbRuntime.Fix((charm - 50) / 5.0);
                copperSell += mod * copperSell / 100;
            }
            else
            {
                double mod = VbRuntime.Fix(charm / 2.0) + 25;
                copperSell = mod * copperSell;
                while (copperSell > 4294967295d) // VB6 overflow bug, preserved
                    copperSell -= 4294967295d;
                copperSell = VbRuntime.Fix(copperSell / 100);
            }
            double buyMod = 1 - ((VbRuntime.Fix(charm / 5.0) - 10) / 100);
            if (!noBuy)
            {
                copperBuy = buyMod * copperBuy;
                while (copperBuy > 4294967295d)
                    copperBuy -= 4294967295d;
            }
        }

        if (copperBuy < 0) copperBuy = 0;
        if (copperSell < 0) copperSell = 0;
        return new ItemValue(copperBuy, copperSell,
            noBuy ? "n/a" : Friendly(copperBuy), Friendly(copperSell));
    }

    /// <summary>The coin-reduction (:3565): Runic ≥ 1e7 (÷1e6), Platinum
    /// ≥ 1e5 (÷1e4), Gold ≥ 1e3 (÷100), Silver ≥ 100 (÷10), else Copper;
    /// Round(,2) on reduced values.</summary>
    internal static string Friendly(double copper)
    {
        if (copper >= 100)
        {
            double v; string coin;
            if (copper >= 10000000) { v = copper / 1000000; coin = "Runic"; }
            else if (copper >= 100000) { v = copper / 10000; coin = "Platinum"; }
            else if (copper >= 1000) { v = copper / 100; coin = "Gold"; }
            else { v = copper / 10; coin = "Silver"; }
            return $"{Math.Round(v, 2, MidpointRounding.ToEven):0.##} {coin}";
        }
        return $"{Math.Round(copper, MidpointRounding.ToEven):0} Copper";
    }

    public sealed record ShopToken(long ShopNumber, bool NoBuy);

    /// <summary>ExtractShopsFromObtainedFrom: comma tokens starting "shop",
    /// first number = shop, "(sell)" anywhere → sell-only.</summary>
    public static List<ShopToken> ExtractShops(string obtainedFrom)
    {
        var shops = new List<ShopToken>();
        if (string.IsNullOrWhiteSpace(obtainedFrom)) return shops;
        foreach (var part in obtainedFrom.Split(','))
        {
            string t = part.Trim().ToLowerInvariant();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (!t.StartsWith("shop")) continue;
            bool sellOnly = t.Contains("(sell)");
            long n = FirstNumber(t);
            if (n > 0) shops.Add(new ShopToken(n, sellOnly));
        }
        return shops;
    }

    private static long FirstNumber(string s)
    {
        var digits = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (digits.Length > 0) break;
        }
        return digits.Length > 0 ? long.Parse(digits.ToString()) : 0;
    }

    public sealed record BestPrice(long ShopNumber, string ValueText,
        bool SellOnly, double SortSell, int MoreShops);

    /// <summary>EvaluateBestPriceForHit (:1242): cheapest BUY among buying
    /// shops (tie → lowest shop number) formatted "buy / sell"; else the
    /// lowest-number sell-capable shop as "(sell) X"; no shops → none.</summary>
    public BestPrice EvaluateBestPrice(long itemNumber, int charm,
        string obtainedFrom)
    {
        var shops = ExtractShops(obtainedFrom);
        if (shops.Count == 0) return new BestPrice(0, "", false, 0, 0);

        bool haveBuy = false, haveSell = false;
        double bestBuy = -1, bestSellVal = 0;
        long bestBuyShop = 0, bestSellShop = 0;

        foreach (var s in shops)
        {
            var v = GetItemValue(itemNumber, charm, 0, s.ShopNumber, s.NoBuy);
            if (!s.NoBuy && v.CopperBuy > 0
                && (!haveBuy || v.CopperBuy < bestBuy
                    || (v.CopperBuy == bestBuy && s.ShopNumber < bestBuyShop)))
            { haveBuy = true; bestBuy = v.CopperBuy; bestBuyShop = s.ShopNumber; }
            if (v.CopperSell > 0
                && (!haveSell || s.ShopNumber < bestSellShop))
            { haveSell = true; bestSellShop = s.ShopNumber; bestSellVal = v.CopperSell; }
        }

        if (haveBuy)
        {
            var v = GetItemValue(itemNumber, charm, 0, bestBuyShop, false);
            return new BestPrice(bestBuyShop,
                $"{v.FriendlyBuyShort} / {v.FriendlySellShort}",
                false, v.CopperSell, shops.Count - 1);
        }
        if (haveSell)
        {
            var v = GetItemValue(itemNumber, charm, 0, bestSellShop, true);
            return new BestPrice(bestSellShop,
                $"(sell) {v.FriendlySellShort}", true, bestSellVal,
                shops.Count - 1);
        }
        return new BestPrice(0, "", false, 0, shops.Count - 1);
    }
}
