using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

public sealed class BacktestTradeResult
{
    public required string Symbol { get; init; }
    public required DateTime EntryTime { get; init; }
    public required decimal EntryPrice { get; init; }
    public required DateTime ExitTime { get; init; }
    public required decimal ExitPrice { get; init; }
    public required decimal OutcomePercent { get; init; }
    public required string ExitReason { get; init; }
    public required string Signal { get; init; }
    public required decimal Score { get; init; }

    // Fase 1 do lado de venda — não-obrigatório de propósito (default = Long), pra não
    // quebrar nenhuma construção existente de BacktestTradeResult espalhada pelo app.
    public TradeDirection Direction { get; init; } = TradeDirection.Long;

    // Contexto de risco no momento da entrada — para investigar se SLs
    // batidos rápido têm relação com a distância até suporte/resistência.
    public required decimal ResistanceDistancePercent { get; init; }
    public required decimal SupportDistancePercent { get; init; }
    public required decimal RiskRewardAtEntry { get; init; }

    // Instrumentação — Fase A do lado de venda. Esses 2 campos já eram calculados em
    // TrendAnalysis (IsBearishMomentumConfirmed/IsBearishRsiDivergence) mas nunca tinham
    // sido capturados no resultado do backtest. Não-obrigatórios de propósito (default
    // false), mesmo padrão de Direction acima — só exposição de dado pra análise manual,
    // NÃO influenciam Score nem elegibilidade ainda. Sempre false pra trades Long (o
    // conceito não existe desse lado).
    public bool HadBearishMomentumConfirmed { get; init; } = false;
    public bool HadBearishRsiDivergence { get; init; } = false;

    // Diagnóstico — investigação de 12/2026 sobre os 2 campos acima virem sempre false no
    // Bollinger Reversal (63/63 trades desmarcados). True quando MarketStructureAnalyzer
    // conseguiu identificar 2 swing highs válidos na janela (pré-requisito pra Momentum/
    // Divergência sequer serem calculados) — separa "dado indisponível" (estrutura sem
    // topos suficientes) de "dado disponível mas o sinal genuinamente não ocorreu".
    public bool HadSwingHighDataAvailable { get; init; } = false;
}