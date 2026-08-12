using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class MarketStructureAnalyzer
{
    public static MarketStructureResult Calculate(List<Candle> candles)
    {
        MarketStructureResult result = new();

        if (candles.Count < 50)
            return result;

        List<(Candle Candle, int Index)> swingHighs = new();
        List<(Candle Candle, int Index)> swingLows = new();

        for (int i = 2; i < candles.Count - 2; i++)
        {
            bool isHigh =
                candles[i].High > candles[i - 1].High &&
                candles[i].High > candles[i - 2].High &&
                candles[i].High > candles[i + 1].High &&
                candles[i].High > candles[i + 2].High;

            bool isLow =
                candles[i].Low < candles[i - 1].Low &&
                candles[i].Low < candles[i - 2].Low &&
                candles[i].Low < candles[i + 1].Low &&
                candles[i].Low < candles[i + 2].Low;

            if (isHigh)
                swingHighs.Add((candles[i], i));

            if (isLow)
                swingLows.Add((candles[i], i));
        }

        result.SwingHighCount = swingHighs.Count;
        result.SwingLowCount = swingLows.Count;

        if (swingHighs.Count < 2 || swingLows.Count < 2)
        {
            result.Score = 50;
            result.Sideways = true;
            return result;
        }

        Candle lastHigh = swingHighs[^1].Candle;
        Candle prevHigh = swingHighs[^2].Candle;
        result.LastSwingHighIndex = swingHighs[^1].Index;
        result.PrevSwingHighIndex = swingHighs[^2].Index;

        Candle lastLow = swingLows[^1].Candle;
        Candle prevLow = swingLows[^2].Candle;

        result.HigherHigh = lastHigh.High > prevHigh.High;
        result.HigherLow = lastLow.Low > prevLow.Low;

        result.LowerHigh = lastHigh.High < prevHigh.High;
        result.LowerLow = lastLow.Low < prevLow.Low;

        if (result.HigherHigh && result.HigherLow)
        {
            result.Uptrend = true;
            result.Score = 100;
        }
        else if (result.LowerHigh && result.LowerLow)
        {
            result.Downtrend = true;
            result.Score = 0;
        }
        else
        {
            result.Sideways = true;
            result.Score = 50;
        }

        decimal close = candles[^1].Close;

        result.BreakOfStructure =
            close > prevHigh.High;

        result.ChangeOfCharacter =
            result.BreakOfStructure &&
            result.HigherLow;

        // Espelhos pro lado de baixa (Fase A do lado de venda). BearishBreakOfStructure:
        // preço fecha abaixo do fundo anterior — rompimento de suporte. BearishChangeOfCharacter:
        // esse rompimento combinado com um topo mais baixo que o anterior (LowerHigh) — sinaliza
        // reversão de uma tendência que vinha sendo de alta/lateral pra baixa, não só continuação
        // de uma baixa que já estava confirmada.
        result.BearishBreakOfStructure =
            close < prevLow.Low;

        result.BearishChangeOfCharacter =
            result.BearishBreakOfStructure &&
            result.LowerHigh;

        result.StrongUptrend =
            result.Uptrend &&
            result.BreakOfStructure &&
            result.HigherHigh &&
            result.HigherLow;

        result.StrongDowntrend =
            result.Downtrend &&
            result.LowerHigh &&
            result.LowerLow &&
            close < prevLow.Low;

        if (result.StrongUptrend)
            result.Score = 100;
        else if (result.Uptrend)
            result.Score = 85;
        else if (result.Sideways)
            result.Score = 50;
        else if (result.StrongDowntrend)
            // Antes caía no mesmo Score=15 do Downtrend comum, sem distinção — StrongUptrend
            // já tinha esse tratamento (100 vs. 85), StrongDowntrend não. Corrigido pra manter
            // a mesma simetria: baixa forte fica no extremo oposto (0), não empatada com fraca.
            result.Score = 0;
        else if (result.Downtrend)
            result.Score = 15;
        else
            result.Score = 0;

        return result;
    }
}