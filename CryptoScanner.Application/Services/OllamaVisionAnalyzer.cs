using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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

        var opinion = JsonSerializer.Deserialize<LlmTradeOpinion>(envelope.Message.Content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Ollama retornou JSON inválido.");

        Validate(opinion);
        return opinion;
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
