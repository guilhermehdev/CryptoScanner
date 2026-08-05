using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public static class ResistanceScanner
{
    private const int MinWindow = 50;
    private const int IdealWindow = 100;
    private const int MaxWindow = 300;
    private const int MinQualifyingZones = 2;
    private const decimal MinQualifyingScore = 20m;
    private const int FractalSide = 2; // candles de cada lado pra confirmar um pivô

    /// <summary>
    /// Busca resistências acima do preço de entrada, com janela adaptativa (50→100→300
    /// candles). Só devolve zonas que passam no piso de qualidade (MinQualifyingScore).
    /// </summary>
    public static List<ResistanceZone> Scan(List<Candle> candles, decimal entryPrice, decimal atr)
    {
        var zones = ScanRaw(candles, entryPrice, atr);
        return zones.Where(z => z.Score >= MinQualifyingScore).ToList();
    }

    /// <summary>
    /// Busca resistências combinando o timeframe operacional com um timeframe superior
    /// (etapa 4.2) — zonas vistas no timeframe maior ganham +15 de pontuação, e zonas
    /// próximas entre os dois timeframes se fundem numa só (confluência), com um bônus
    /// extra por terem sido confirmadas nos dois. O filtro de qualidade só é aplicado no
    /// final, depois do bônus — assim uma zona diária quase-qualificada não é descartada
    /// antes do bônus ter chance de "salvá-la". Se não houver candles do timeframe superior
    /// disponíveis, cai de volta pro comportamento de timeframe único (4.1).
    /// </summary>
    public static List<ResistanceZone> ScanMultiTimeframe(
        List<Candle> operationalCandles, List<Candle>? higherTimeframeCandles, decimal entryPrice, decimal atr)
    {
        var operationalZones = ScanRaw(operationalCandles, entryPrice, atr);

        if (higherTimeframeCandles == null || higherTimeframeCandles.Count < MinWindow)
        {
            return operationalZones
                .Where(z => z.Score >= MinQualifyingScore)
                .OrderBy(z => z.Price)
                .ToList();
        }

        var higherZones = ScanRaw(higherTimeframeCandles, entryPrice, atr)
            .Select(z => new ResistanceZone
            {
                Price = z.Price,
                TouchCount = z.TouchCount,
                HasStrongRejection = z.HasStrongRejection,
                HasVolumeConfirmation = z.HasVolumeConfirmation,
                IsRecent = z.IsRecent,
                Score = Math.Min(z.Score + 15m, 100m), // bônus de timeframe superior
                LastTestTime = z.LastTestTime
            })
            .ToList();

        var combined = operationalZones.Concat(higherZones).ToList();
        var merged = MergeOverlappingZones(combined, atr);

        return merged
            .Where(z => z.Score >= MinQualifyingScore)
            .OrderBy(z => z.Price)
            .ToList();
    }

    /// <summary>
    /// Busca adaptativa (50→100→300 candles) sem filtrar por pontuação — só geometricamente
    /// relevante (acima da entrada). MinQualifyingScore aqui dentro só decide QUANDO PARAR
    /// de expandir a janela de busca, não filtra o que é devolvido — isso é responsabilidade
    /// do chamador (Scan ou ScanMultiTimeframe), aplicado depois de qualquer bônus.
    /// </summary>
    private static List<ResistanceZone> ScanRaw(List<Candle> candles, decimal entryPrice, decimal atr)
    {
        if (candles.Count < MinWindow || atr <= 0)
            return new List<ResistanceZone>();

        var windowSteps = new[] { MinWindow, IdealWindow, MaxWindow };
        List<ResistanceZone> zones = new();

        foreach (int step in windowSteps)
        {
            int actualWindow = Math.Min(step, candles.Count);
            var slice = candles.GetRange(candles.Count - actualWindow, actualWindow);

            zones = FindZones(slice, atr);

            int qualifying = zones.Count(z => z.Score >= MinQualifyingScore && z.Price > entryPrice);
            if (qualifying >= MinQualifyingZones || actualWindow >= candles.Count)
                break;
        }

        return zones.Where(z => z.Price > entryPrice).ToList();
    }

    private static List<ResistanceZone> MergeOverlappingZones(List<ResistanceZone> zones, decimal atr)
    {
        if (zones.Count == 0)
            return zones;

        decimal tolerance = atr * 0.3m;
        var sorted = zones.OrderBy(z => z.Price).ToList();
        var merged = new List<ResistanceZone>();

        foreach (var zone in sorted)
        {
            if (merged.Count > 0 && Math.Abs(zone.Price - merged[^1].Price) <= tolerance)
            {
                var existing = merged[^1];
                var stronger = zone.Score >= existing.Score ? zone : existing;

                merged[^1] = new ResistanceZone
                {
                    Price = stronger.Price,
                    TouchCount = existing.TouchCount + zone.TouchCount,
                    HasStrongRejection = existing.HasStrongRejection || zone.HasStrongRejection,
                    HasVolumeConfirmation = existing.HasVolumeConfirmation || zone.HasVolumeConfirmation,
                    IsRecent = existing.IsRecent || zone.IsRecent,
                    Score = Math.Min(stronger.Score + 10m, 100m),
                    LastTestTime = zone.LastTestTime > existing.LastTestTime ? zone.LastTestTime : existing.LastTestTime
                };
            }
            else
            {
                merged.Add(zone);
            }
        }

        return merged;
    }

    private static List<(Candle Candle, int Index)> FindPivotHighs(List<Candle> candles)
    {
        var pivots = new List<(Candle, int)>();

        for (int i = FractalSide; i < candles.Count - FractalSide; i++)
        {
            bool isPivot = true;
            for (int j = 1; j <= FractalSide; j++)
            {
                if (candles[i].High <= candles[i - j].High || candles[i].High <= candles[i + j].High)
                {
                    isPivot = false;
                    break;
                }
            }

            if (isPivot)
                pivots.Add((candles[i], i));
        }

        return pivots;
    }

    private static List<List<(Candle Candle, int Index)>> GroupPivots(List<(Candle Candle, int Index)> pivots, decimal atr)
    {
        var sorted = pivots.OrderBy(p => p.Candle.High).ToList();
        var groups = new List<List<(Candle, int)>>();
        decimal tolerance = atr * 0.3m;

        foreach (var pivot in sorted)
        {
            var existingGroup = groups.FirstOrDefault(g => Math.Abs(g.Average(p => p.Item1.High) - pivot.Candle.High) <= tolerance);
            if (existingGroup != null)
                existingGroup.Add((pivot.Candle, pivot.Index));
            else
                groups.Add(new List<(Candle, int)> { (pivot.Candle, pivot.Index) });
        }

        return groups;
    }

    private static List<ResistanceZone> FindZones(List<Candle> candles, decimal atr)
    {
        var pivots = FindPivotHighs(candles);
        var groups = GroupPivots(pivots, atr);

        var zones = new List<ResistanceZone>();
        int recentThresholdIndex = (int)(candles.Count * 0.7m);

        foreach (var group in groups)
        {
            decimal price = group.Average(p => p.Candle.High);
            int touchCount = group.Count;

            bool strongRejection = group.Any(p => IsStrongRejection(p.Candle));
            bool volumeConfirmed = group.Any(p => IsVolumeSpike(candles, p.Index));
            bool isRecent = group.Any(p => p.Index >= recentThresholdIndex);
            DateTime lastTest = group.Max(p => p.Candle.OpenTime);

            decimal score = Math.Min(touchCount * 10m, 30m);
            if (strongRejection) score += 20m;
            if (volumeConfirmed) score += 20m;
            if (isRecent) score += 15m;

            zones.Add(new ResistanceZone
            {
                Price = price,
                TouchCount = touchCount,
                HasStrongRejection = strongRejection,
                HasVolumeConfirmation = volumeConfirmed,
                IsRecent = isRecent,
                Score = score,
                LastTestTime = lastTest
            });
        }

        return zones;
    }

    private static bool IsStrongRejection(Candle candle)
    {
        decimal body = Math.Abs(candle.Close - candle.Open);
        decimal upperWick = candle.High - Math.Max(candle.Close, candle.Open);
        return body > 0 && upperWick >= body * 1.5m;
    }

    private static bool IsVolumeSpike(List<Candle> allCandles, int index)
    {
        const int lookback = 20;
        int start = Math.Max(0, index - lookback);
        int length = index - start;

        if (length <= 0)
            return false;

        var window = allCandles.GetRange(start, length);
        decimal avgVolume = window.Average(c => c.Volume);

        return avgVolume > 0 && allCandles[index].Volume >= avgVolume * 1.5m;
    }
}