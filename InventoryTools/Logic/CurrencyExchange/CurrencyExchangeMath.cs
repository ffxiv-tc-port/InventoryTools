using System;
using System.Collections.Generic;

namespace InventoryTools.Logic.CurrencyExchange;

// 本檔只做純計算、不碰遊戲或插件服務（離線探針直接編這一份）。
// 契約：拿不到價格一律回「不知道」的狀態，絕不把不知道當成 0 算進每單位價值。

/// <summary>一筆兌換裡的一項成本或獎勵。</summary>
public readonly record struct ExchangePart(uint ItemId, uint Count, bool IsHq);

public enum QuoteState
{
    Ok,
    NotMarketable,
    NotChecked,
    Pending,
    NoData,
}

/// <summary>某件物品在某世界的報價；<see cref="UnitPrice"/> 只在 <see cref="QuoteState.Ok"/> 時有意義。</summary>
public readonly record struct MarketQuote(QuoteState State, double UnitPrice, double? DailySales, DateTime? UpdatedAt);

/// <summary>由好到壞的判定；數值越大越不確定（排序時排在越後面）。</summary>
public enum ExchangeValueState
{
    Ok,
    NoData,
    Pending,
    NotChecked,
    CostUntradable,
    Untradable,
    NoCurrencyCost,
}

public sealed record ExchangeValuation(
    ExchangeValueState State,
    uint CurrencyCount,
    double? RewardValue,
    double? OtherCost,
    double? ValuePerCurrency,
    double? DailySales,
    DateTime? OldestUpdate);

public static class CurrencyExchangeMath
{
    public const uint GilItemId = 1;

    /// <summary>
    /// 每 1 單位 <paramref name="currencyId"/> 換到的市值＝（Σ獎勵單價×數量 − Σ其他成本）÷ 貨幣用量。
    /// 其他成本是 Gil 就按面額；是物品就用它的市價，物品不能上市場＝<see cref="ExchangeValueState.CostUntradable"/>。
    /// 狀態取所有參與項目裡最不確定的那一個；不是 Ok 時 <see cref="ExchangeValuation.ValuePerCurrency"/> 為 null。
    /// </summary>
    public static ExchangeValuation Evaluate(
        IReadOnlyList<ExchangePart> costs,
        IReadOnlyList<ExchangePart> rewards,
        uint currencyId,
        Func<ExchangePart, MarketQuote> quote)
    {
        uint currencyCount = 0;
        foreach (var c in costs)
        {
            if (c.ItemId == currencyId)
            {
                currencyCount += c.Count;
            }
        }

        if (currencyCount == 0 || rewards.Count == 0)
        {
            return new ExchangeValuation(ExchangeValueState.NoCurrencyCost, currencyCount, null, null, null, null, null);
        }

        var state = ExchangeValueState.Ok;
        double rewardValue = 0;
        double otherCost = 0;
        double? dailySales = null;
        DateTime? oldest = null;

        foreach (var r in rewards)
        {
            var q = quote(r);
            state = Worse(state, FromQuote(q.State, false));
            rewardValue += q.UnitPrice * r.Count;
            if (q.DailySales is { } d)
            {
                dailySales = dailySales is { } prev ? Math.Min(prev, d) : d;
            }

            oldest = Older(oldest, q.UpdatedAt);
        }

        foreach (var c in costs)
        {
            if (c.ItemId == currencyId)
            {
                continue;
            }

            if (c.ItemId == GilItemId)
            {
                otherCost += c.Count;
                continue;
            }

            var q = quote(c);
            state = Worse(state, FromQuote(q.State, true));
            otherCost += q.UnitPrice * c.Count;
            oldest = Older(oldest, q.UpdatedAt);
        }

        if (state != ExchangeValueState.Ok)
        {
            return new ExchangeValuation(state, currencyCount, null, null, null, dailySales, oldest);
        }

        return new ExchangeValuation(
            state,
            currencyCount,
            rewardValue,
            otherCost,
            (rewardValue - otherCost) / currencyCount,
            dailySales,
            oldest);
    }

    /// <summary>保守單價：均價與目前最低掛單取較低者；只有一邊 &gt; 0 就用那邊；兩邊都沒有回 null。</summary>
    public static double? ConservativeUnitPrice(double averagePrice, double minListing)
    {
        if (averagePrice > 0 && minListing > 0)
        {
            return Math.Min(averagePrice, minListing);
        }

        if (averagePrice > 0)
        {
            return averagePrice;
        }

        return minListing > 0 ? minListing : null;
    }

    /// <summary>每日成交筆數；完全沒有成交紀錄（筆數 0 且沒有最後成交日）回 null＝不知道，不是 0。</summary>
    public static double? DailySales(int sellCount, DateTime? lastSellDate, int windowDays)
    {
        if (sellCount <= 0 && lastSellDate == null)
        {
            return null;
        }

        return (double)Math.Max(0, sellCount) / Math.Max(1, windowDays);
    }

    public static bool IsStale(DateTime? updatedAt, DateTime now, int maxAgeHours)
    {
        return updatedAt == null || (now - updatedAt.Value).TotalHours > Math.Max(1, maxAgeHours);
    }

    /// <summary>
    /// 依每單位價值排序用的比較：可估的永遠排在不可估的前面（不論升降冪），
    /// 不可估之間依 <see cref="ExchangeValueState"/> 由確定到不確定。
    /// </summary>
    public static int CompareByValue(ExchangeValuation a, ExchangeValuation b, bool descending)
    {
        var aOk = a.ValuePerCurrency.HasValue;
        var bOk = b.ValuePerCurrency.HasValue;
        if (aOk != bOk)
        {
            return aOk ? -1 : 1;
        }

        if (!aOk)
        {
            return a.State.CompareTo(b.State);
        }

        var cmp = a.ValuePerCurrency!.Value.CompareTo(b.ValuePerCurrency!.Value);
        return descending ? -cmp : cmp;
    }

    /// <summary>手上這批能換幾次（無條件捨去）；用量 0 回 0。</summary>
    public static long ExchangeTimes(long held, uint currencyCount)
    {
        return currencyCount == 0 ? 0 : Math.Max(0, held) / currencyCount;
    }

    /// <summary>可換數量＝能換幾次 × 每次拿到的件數；一次都換不起時按一次算（只看單次的流動性）。</summary>
    public static double ExchangeableUnits(long held, uint currencyCount, uint unitsPerExchange)
    {
        return Math.Max(1, ExchangeTimes(held, currencyCount)) * (double)Math.Max(1u, unitsPerExchange);
    }

    /// <summary>
    /// 穩健值＝每單位價值 × min(1, 日銷 ÷ 可換數量)。價值或日銷不知道回 null（不參與穩健值排序）；
    /// 價值 ≤ 0 原值回傳，不打折（打折會讓虧損看起來變小）。
    /// </summary>
    public static double? RobustValue(double? valuePerCurrency, double? dailySales, double exchangeableUnits)
    {
        if (valuePerCurrency is not { } v || dailySales is not { } s)
        {
            return null;
        }

        if (v <= 0)
        {
            return v;
        }

        return v * Math.Min(1.0, Math.Max(0, s) / Math.Max(1.0, exchangeableUnits));
    }

    /// <summary>
    /// 「賣得完優先」的比較：有穩健值的在前；有價值但日銷不知道的其次（之間依價值）；不可估的最後（依確定度）。
    /// 三組的先後不隨升降冪改變。
    /// </summary>
    public static int CompareByRobust(ExchangeValuation a, double? robustA, ExchangeValuation b, double? robustB, bool descending)
    {
        var ga = RobustGroup(a, robustA);
        var gb = RobustGroup(b, robustB);
        if (ga != gb)
        {
            return ga.CompareTo(gb);
        }

        if (ga == 2)
        {
            return a.State.CompareTo(b.State);
        }

        var cmp = ga == 0 ? robustA!.Value.CompareTo(robustB!.Value) : a.ValuePerCurrency!.Value.CompareTo(b.ValuePerCurrency!.Value);
        return descending ? -cmp : cmp;
    }

    private static int RobustGroup(ExchangeValuation v, double? robust) =>
        robust.HasValue ? 0 : v.ValuePerCurrency.HasValue ? 1 : 2;

    /// <summary>可估的排前面、null 一律排最後；用在日銷量、資料時間這類可能不知道的欄。</summary>
    public static int CompareNullableLast(double? a, double? b, bool descending)
    {
        if (a.HasValue != b.HasValue)
        {
            return a.HasValue ? -1 : 1;
        }

        if (!a.HasValue)
        {
            return 0;
        }

        var cmp = a.Value.CompareTo(b!.Value);
        return descending ? -cmp : cmp;
    }

    /// <summary>上限占比（0～1）；上限未知或 0 回 null。</summary>
    public static double? FillRatio(long amount, long? cap)
    {
        if (cap is not > 0)
        {
            return null;
        }

        return Math.Min(1.0, (double)Math.Max(0, amount) / cap.Value);
    }

    private static ExchangeValueState FromQuote(QuoteState s, bool isCost) => s switch
    {
        QuoteState.Ok => ExchangeValueState.Ok,
        QuoteState.NotMarketable => isCost ? ExchangeValueState.CostUntradable : ExchangeValueState.Untradable,
        QuoteState.NotChecked => ExchangeValueState.NotChecked,
        QuoteState.Pending => ExchangeValueState.Pending,
        _ => ExchangeValueState.NoData,
    };

    private static ExchangeValueState Worse(ExchangeValueState a, ExchangeValueState b) => a >= b ? a : b;

    private static DateTime? Older(DateTime? a, DateTime? b)
    {
        if (a == null)
        {
            return b;
        }

        return b == null || a <= b ? a : b;
    }
}
