using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public class VolumeAnalysisResult
    {
        public decimal BuyingVolume { get; set; }

        public decimal SellingVolume { get; set; }

        public decimal VolumeImbalance { get; set; }

        public bool ClimaxVolume { get; set; }

        public bool Absorption { get; set; }

        public bool Distribution { get; set; }

        public bool Exhaustion { get; set; }

        public int Score { get; set; }
        public decimal VolumeSpike { get; set; }

        // Espelho de Score pro lado de baixa (Fase A do lado de venda) — calculado junto,
        // reaproveitando os mesmos sinais (Distribution, Absorption, etc.), não uma segunda
        // passada pelos candles.
        public int BearishScore { get; set; }
    }

    public static class VolumeAnalyzer
    {
        public static VolumeAnalysisResult Calculate(List<Candle> candles)
        {
            VolumeAnalysisResult result = new();

            if (candles.Count < 20)
                return result;

            var recent = candles.TakeLast(20).ToList();

            decimal buyingVolume = 0;
            decimal sellingVolume = 0;
            decimal totalVolume = 0;

            foreach (var candle in recent)
            {
                totalVolume += candle.Volume;

                if (candle.Close >= candle.Open)
                    buyingVolume += candle.Volume;
                else
                    sellingVolume += candle.Volume;
            }

            result.BuyingVolume = buyingVolume;
            result.SellingVolume = sellingVolume;

            if (totalVolume > 0)
                result.VolumeImbalance = (buyingVolume - sellingVolume) / totalVolume;

            decimal avgVolume = recent.Take(19).Average(x => x.Volume);
            decimal lastVolume = recent.Last().Volume;

            result.ClimaxVolume = avgVolume > 0 && lastVolume > avgVolume * 3m;

            Candle last = recent.Last();

            decimal body = Math.Abs(last.Close - last.Open);
            decimal range = last.High - last.Low;

            result.Absorption =
                result.ClimaxVolume &&
                range > 0 &&
                body / range < 0.25m;

            result.Distribution =
                result.VolumeImbalance < -0.30m &&
                last.Close < last.Open;

            result.Exhaustion = DetectExhaustion(recent);

            result.Score = 50;

            // Sem volume médio nos candles anteriores (ativo pouco líquido nessa janela,
            // mais comum em timeframes curtos como o Scalp) — não dá pra calcular um "spike"
            // relativo a uma base zero. 0 aqui é seguro: cai naturalmente abaixo de qualquer
            // piso de Volume Spike configurado, então o ativo já fica desqualificado por
            // esse critério em vez de derrubar o app com uma divisão por zero.
            result.VolumeSpike = avgVolume > 0 ? lastVolume / avgVolume : 0m;

            if (result.VolumeImbalance > 0.50m)
                result.Score += 25;
            else if (result.VolumeImbalance > 0.25m)
                result.Score += 15;

            if (result.ClimaxVolume)
                result.Score += 10;

            if (result.Absorption)
                result.Score += 10;

            if (result.Distribution)
                result.Score -= 20;

            if (result.Exhaustion)
                result.Score -= 15;

            result.Score = Math.Clamp(result.Score, 0, 100);

            // Espelho pro lado de baixa — mesmos sinais, papéis invertidos onde faz sentido.
            // Distribution já É um sinal de venda (predomínio vendedor + candle vermelho) —
            // aqui soma em vez de subtrair. ClimaxVolume e Absorption ficam como bônus nos
            // dois lados (intensidade/absorção de volume é ambígua quanto à direção sem
            // inspecionar mais fundo — tratado como sinal geral, não específico de lado).
            // Exhaustion também penaliza os dois lados pelo mesmo motivo: o sinal não diz
            // se é uma sequência de alta ou de baixa que está perdendo fôlego.
            result.BearishScore = 50;

            if (result.VolumeImbalance < -0.50m)
                result.BearishScore += 25;
            else if (result.VolumeImbalance < -0.25m)
                result.BearishScore += 15;

            if (result.ClimaxVolume)
                result.BearishScore += 10;

            if (result.Absorption)
                result.BearishScore += 10;

            if (result.Distribution)
                result.BearishScore += 20;

            if (result.Exhaustion)
                result.BearishScore -= 15;

            result.BearishScore = Math.Clamp(result.BearishScore, 0, 100);

            return result;
        }

        /// <summary>
        /// Exaustão: movimento direcional consistente (últimos 5 candles todos de alta ou
        /// todos de baixa) mas com volume caindo — fôlego comprador/vendedor diminuindo
        /// mesmo com o preço ainda seguindo na mesma direção.
        /// </summary>
        private static bool DetectExhaustion(List<Candle> recent)
        {
            if (recent.Count < 5)
                return false;

            var lastFive = recent.TakeLast(5).ToList();

            bool allBullish = lastFive.All(c => c.Close >= c.Open);
            bool allBearish = lastFive.All(c => c.Close <= c.Open);

            if (!allBullish && !allBearish)
                return false;

            decimal earlyAvgVolume = lastFive.Take(2).Average(c => c.Volume);
            decimal lateAvgVolume = lastFive.TakeLast(2).Average(c => c.Volume);

            if (earlyAvgVolume == 0)
                return false;

            // Volume caiu pelo menos 30% entre o início e o fim da sequência direcional.
            return lateAvgVolume < earlyAvgVolume * 0.70m;
        }
    }
}