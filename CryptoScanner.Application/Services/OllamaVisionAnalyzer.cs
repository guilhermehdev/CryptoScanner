using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public sealed class OllamaVisionAnalyzer(HttpClient httpClient)
{
    private const string Model = "gemma3:4b";
    private const string Endpoint = "http://localhost:11434/api/chat";

    public async Task<LlmTradeOpinion> AnalyzeAsync(
        string imagePath,
        object indicators,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Imagem do gráfico não encontrada.", imagePath);

        var image = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cancellationToken));
        var prompt = "Você é um analista auxiliar de um scanner de criptomoedas.\n" +
            "Analise Long e Short, mas não invente valores.\n" +
            "A decisão deve ser exclusivamente COMPRA, VENDA, AGUARDAR ou IGNORAR.\n" +
            "Use os indicadores estruturados como fonte principal e a imagem apenas como contexto visual.\n" +
            "Se um valor não estiver disponível, use null. Confiança é um inteiro de 0 a 100.\n" +
            "Responda somente JSON no contrato decisao, direcao, confianca, tendencia, entrada, stop, tp1, tp2, motivos e riscos.\n" +
            "Indicadores do scanner:\n" + JsonSerializer.Serialize(indicators);

        var request = new
        {
            model = Model,
            stream = false,
            format = "json",
            messages = new[] { new { role = "user", content = prompt, images = new[] { image } } }
        };

        using var response = await httpClient.PostAsJsonAsync(Endpoint, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<OllamaEnvelope>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama retornou uma resposta vazia.");

        var parsedOpinion = JsonSerializer.Deserialize<LlmTradeOpinion>(envelope.Message.Content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Ollama retornou JSON inválido.");

        var opinion = Normalize(parsedOpinion);
        Validate(opinion);
        return opinion;
    }

    private static LlmTradeOpinion Normalize(LlmTradeOpinion opinion)
    {
        return new LlmTradeOpinion
        {
            Decisao = NormalizeDecision(opinion.Decisao),
            Direcao = NormalizeDirection(opinion.Direcao),
            Confianca = opinion.Confianca,
            Tendencia = opinion.Tendencia,
            Entrada = opinion.Entrada,
            Stop = opinion.Stop,
            Tp1 = opinion.Tp1,
            Tp2 = opinion.Tp2,
            Motivos = opinion.Motivos,
            Riscos = opinion.Riscos
        };
    }

    private static string NormalizeDecision(string? value)
    {
        var token = NormalizeToken(value);
        var exact = token switch
        {
            "COMPRAR" or "COMPRA" or "LONG" => "COMPRA",
            "VENDER" or "VENDA" or "SHORT" => "VENDA",
            "ESPERAR" or "AGUARDAR" or "WAIT" => "AGUARDAR",
            "IGNORAR" or "IGNORE" => "IGNORAR",
            _ => ""
        };
        if (exact.Length > 0)
            return exact;
        if (token.Contains("VENDA") || token.Contains("VENDER") || token.Contains("SHORT"))
            return "VENDA";
        if (token.Contains("COMPRA") || token.Contains("COMPRAR") || token.Contains("LONG"))
            return "COMPRA";
        if (token.Contains("AGUARD") || token.Contains("ESPER") || token.Contains("WAIT"))
            return "AGUARDAR";
        if (token.Contains("IGNOR"))
            return "IGNORAR";
        return "AGUARDAR";
    }

    private static string NormalizeDirection(string? value)
    {
        var token = NormalizeToken(value);
        var exact = token switch
        {
            "COMPRA" or "COMPRAR" or "LONG" or "ALTA" => "LONG",
            "VENDA" or "VENDER" or "SHORT" or "BAIXA" => "SHORT",
            "NEUTRA" or "NEUTRO" or "NONE" or "NENHUMA" or "INDEFINIDA" => "NEUTRA",
            _ => ""
        };
        if (exact.Length > 0)
            return exact;
        if (token.Contains("SHORT") || token.Contains("VENDA") || token.Contains("VENDER") || token.Contains("BAIXA"))
            return "SHORT";
        if (token.Contains("LONG") || token.Contains("COMPRA") || token.Contains("COMPRAR") || token.Contains("ALTA"))
            return "LONG";
        return "NEUTRA";
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void Validate(LlmTradeOpinion opinion)
    {
        var decisions = new[] { "COMPRA", "VENDA", "AGUARDAR", "IGNORAR" };
        var directions = new[] { "LONG", "SHORT", "NEUTRA" };
        if (!decisions.Contains(opinion.Decisao, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Decisão da LLM fora do contrato permitido.");
        if (!directions.Contains(opinion.Direcao, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Direção da LLM fora do contrato permitido.");
        if (opinion.Confianca is < 0 or > 100)
            throw new InvalidOperationException("Confiança da LLM fora do intervalo 0-100.");
    }

    private sealed class OllamaEnvelope
    {
        public OllamaMessage Message { get; init; } = new();
    }

    private sealed class OllamaMessage
    {
        public string Content { get; init; } = "";
    }
}
