using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed partial class SqliteStrategyLabRepository
{
    // One read transaction keeps all exported tables at the same database snapshot.
    public Task ExportAsync(Stream destination, CancellationToken token = default) => Use(async db =>
    {
        using var tx = db.BeginTransaction(deferred: true);
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var counts = new Dictionary<string, long>();
        var queries = new (string File, string Query)[]
        {
            ("configuracao", "SELECT * FROM LabSettings ORDER BY Id"),
            ("variantes", "SELECT * FROM LabVariants ORDER BY Id"),
            ("oportunidades", "SELECT * FROM LabOpportunities ORDER BY Id"),
            ("decisoes", "SELECT * FROM LabDecisions ORDER BY OpportunityId,VariantId"),
            ("trades", "SELECT * FROM LabTrades ORDER BY Id"),
            ("saidas", "SELECT * FROM LabExits ORDER BY Id"),
            ("resumo", """
                SELECT v.Id AS VariantId, v.ParametersJson, v.EngineVersion, v.Cash,
                  v.Cash + COALESCE((SELECT SUM(json_extract(t.StateJson,'$.LiquidationValue'))
                    FROM LabTrades t WHERE t.VariantId=v.Id AND t.Closed=0),0) AS EstimatedEquity,
                  v.PeakEquity, v.MaxDrawdown AS MaxObservedDrawdownPercent,
                  (SELECT COUNT(*) FROM LabTrades t WHERE t.VariantId=v.Id AND t.Closed=0) AS OpenTrades,
                  (SELECT COUNT(*) FROM LabTrades t WHERE t.VariantId=v.Id AND t.Closed=1) AS ClosedTrades,
                  (SELECT COUNT(*) FROM LabTrades t WHERE t.VariantId=v.Id AND t.Closed=1 AND t.NetProfit>0) AS Wins,
                  (SELECT COALESCE(SUM(t.NetProfit),0) FROM LabTrades t WHERE t.VariantId=v.Id AND t.Closed=1) AS RealizedNetProfit,
                  (SELECT COUNT(*) FROM LabTrades t WHERE t.VariantId=v.Id AND json_extract(t.StateJson,'$.HasObservationGap')=1) AS TradesWithGaps,
                  (SELECT COUNT(*) FROM LabDecisions d WHERE d.VariantId=v.Id AND d.Accepted=0) AS RejectedDecisions
                FROM LabVariants v ORDER BY v.Id
                """)
        };
        foreach (var (file, query) in queries)
        {
            token.ThrowIfCancellationRequested();
            await using var command = db.CreateCommand();
            command.Transaction = tx;
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(token);
            await using var writer = new StreamWriter(zip.CreateEntry(file + ".csv").Open(), new UTF8Encoding(true));
            await writer.WriteLineAsync(string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(i => Csv(reader.GetName(i)))).AsMemory(), token);
            long rows = 0;
            while (await reader.ReadAsync(token))
            {
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => Csv(reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!));
                await writer.WriteLineAsync(string.Join(",", values).AsMemory(), token);
                rows++;
            }
            counts[file] = rows;
        }
        await using (var writer = new StreamWriter(zip.CreateEntry("manifesto.json").Open(), Encoding.UTF8))
            await writer.WriteAsync(JsonSerializer.Serialize(new { FormatVersion = 1, ExportedAtUtc = DateTimeOffset.UtcNow, Counts = counts }, new JsonSerializerOptions { WriteIndented = true }).AsMemory(), token);
        await using (var writer = new StreamWriter(zip.CreateEntry("LEIA-ME.txt").Open(), Encoding.UTF8))
            await writer.WriteAsync("""
                RELATÓRIO COMPLETO DO LABORATÓRIO
                Todos os registros persistidos, sem o limite de 200 linhas da tela.
                Os arquivos representam uma única fotografia consistente do banco.
                CSV UTF-8, separador vírgula, decimal ponto, aspas duplicadas para escape.
                Campos nulos ficam vazios. Timestamps *Ms são Unix em milissegundos UTC.
                Textos iniciados por =, +, -, @ recebem apóstrofo para evitar fórmulas em planilhas;
                números negativos permanecem numéricos. Remova o apóstrofo ao analisar esses textos.

                RELAÇÕES
                variantes.Id = decisoes.VariantId = trades.VariantId.
                oportunidades.Id = decisoes.OpportunityId = trades.OpportunityId.
                trades.Id = saidas.TradeId. Os campos *Json preservam indicadores, parâmetros,
                estado completo do trade e eventos de saída, incluindo custos e parciais.

                INTERPRETAÇÃO
                resumo.csv: caixa, patrimônio estimado, lucro realizado, contagens e queda máxima.
                Patrimônio usa a última cotação registrada, que pode estar desatualizada.
                NetProfit na tabela trades só vale para operações fechadas; não some o lucro
                realizado ao caixa: os recebimentos das saídas já estão incluídos no saldo.
                Wins / ClosedTrades é a taxa de vitórias; com zero trades ela é indefinida.
                Operações com HasObservationGap devem ser analisadas separadamente.
                Rejeições não são trades perdedores e não têm resultado contrafactual registrado.
                Não existe histórico completo de cada cotação nem curva contínua de patrimônio.
                Custos e regras da geração inicial estão documentados em docs/strategy-lab.md.
                Somente simulações, sem trades manuais, credenciais ou configurações da conta.
                A coleta inclui perdas, rejeições e posições abertas. Não representa garantia
                de desempenho futuro; novas hipóteses precisam de avaliação em dados posteriores.
                """.AsMemory(), token);
        token.ThrowIfCancellationRequested();
        tx.Commit();
        return 0;
    }, token);

    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) &&
            !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
