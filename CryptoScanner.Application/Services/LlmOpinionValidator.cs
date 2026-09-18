using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public static class LlmOpinionValidator
{
    private const decimal RelativeTolerance = 0.005m;

    public static LlmOpinionValidationResult Validate(LlmAnalysisSnapshot snapshot, LlmTradeOpinion opinion)
    {
        var errors = new List<string>();
        bool isTrade = opinion.Decisao is "COMPRA" or "VENDA";

        if (!isTrade)
            return new LlmOpinionValidationResult(true, errors);

        string expectedDecision = snapshot.Direction == "SHORT" ? "VENDA" : "COMPRA";
        if (!snapshot.CanRecommendTrade)
            errors.Add("O scanner não autorizou entrada para este snapshot.");
        if (opinion.Decisao != expectedDecision || opinion.Direcao != snapshot.Direction)
            errors.Add("A decisão ou direção diverge da estratégia do scanner.");
        if (opinion.Entrada is null || opinion.Stop is null || opinion.Tp1 is null || opinion.Tp2 is null)
            errors.Add("Uma operação precisa informar entrada, stop, TP1 e TP2.");
        else
        {
            decimal entry = opinion.Entrada.Value;
            decimal stop = opinion.Stop.Value;
            decimal tp1 = opinion.Tp1.Value;
            decimal tp2 = opinion.Tp2.Value;
            bool validGeometry = snapshot.Direction == "SHORT"
                ? stop > entry && tp1 < entry && tp2 < entry
                : stop < entry && tp1 > entry && tp2 > entry;
            if (!validGeometry)
                errors.Add("Os níveis não respeitam a geometria de risco da direção proposta.");

            if (!Matches(snapshot.ExpectedEntry, entry) ||
                !Matches(snapshot.ExpectedStop, stop) ||
                !Matches(snapshot.ExpectedTp1, tp1) ||
                !Matches(snapshot.ExpectedTp2, tp2))
            {
                errors.Add("Os níveis propostos divergem dos níveis oficiais do scanner.");
            }
        }

        return new LlmOpinionValidationResult(errors.Count == 0, errors);
    }

    private static bool Matches(decimal? expected, decimal actual) =>
        expected is decimal value && Math.Abs(actual - value) <= Math.Max(value * RelativeTolerance, 0.00000001m);
}
