using System;
using System.Collections.Generic;
using System.Text;

using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

public static class TrendScorer
{
    public static int Calculate(decimal close, decimal ema21, decimal ema50, decimal ema200)
    {
        int score = 0;

        if (close > ema200)
            score += 40;

        if (ema21 > ema50)
            score += 30;

        if (ema50 > ema200)
            score += 30;

        return score;
    }

    // Espelho pro lado de baixa (Fase A do lado de venda) — mesma lógica, invertida.
    public static int CalculateBearish(decimal close, decimal ema21, decimal ema50, decimal ema200)
    {
        int score = 0;

        if (close < ema200)
            score += 40;

        if (ema21 < ema50)
            score += 30;

        if (ema50 < ema200)
            score += 30;

        return score;
    }
}