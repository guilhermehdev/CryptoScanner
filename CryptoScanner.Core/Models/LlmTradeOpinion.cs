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
    public string[] Motivos { get; init; } = [];
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

        throw new JsonException($"O valor '{text}' não é um nível decimal válido.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
