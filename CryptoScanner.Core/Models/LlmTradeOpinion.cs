using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoScanner.Core.Models;

public sealed class LlmTradeOpinion
{
    public string Decisao { get; init; } = "AGUARDAR";
    public string Direcao { get; init; } = "NEUTRA";
    public int Confianca { get; init; }
    public string Tendencia { get; init; } = "INDEFINIDA";
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? Entrada { get; init; }
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? Stop { get; init; }
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? Tp1 { get; init; }
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? Tp2 { get; init; }
    [JsonConverter(typeof(FlexibleStringArrayConverter))]
    public string[] Motivos { get; init; } = [];
    [JsonConverter(typeof(FlexibleStringArrayConverter))]
    public string[] Riscos { get; init; } = [];
}

/// <summary>
/// Aceita níveis que a LLM devolve como número, texto com ponto/vírgula ou marcador nulo.
/// </summary>
public sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number))
            return number;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Esperado número, texto ou null; recebido {reader.TokenType}.");

        var text = reader.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text is "—" or "-" or "N/A" or "null")
            return null;

        // A LLM pode devolver uma faixa (ex.: "1.8065 - 1.7850").
        // O contrato aceita um único nível; não escolha silenciosamente uma das pontas.
        if (text.Contains(" - ", StringComparison.Ordinal) || text.Contains("–", StringComparison.Ordinal) || text.Contains("—", StringComparison.Ordinal))
            return null;

        if (text.Contains(','))
        {
            // A vírgula é o separador decimal usual no retorno em português;
            // remova pontos de milhar quando os dois separadores aparecerem.
            var normalized = text.Contains('.')
                ? text.Replace(".", string.Empty).Replace(',', '.')
                : text.Replace(',', '.');
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var brazilian))
                return brazilian;
        }
        else if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        // Níveis malformados não invalidam a análise inteira; ficam indisponíveis para a UI.
        return null;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

/// <summary>
/// Aceita uma lista de textos ou uma única frase no retorno da LLM.
/// </summary>
public sealed class FlexibleStringArrayConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Esperada lista ou texto; recebido " + reader.TokenType + ".");

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value ?? [], options);
}
