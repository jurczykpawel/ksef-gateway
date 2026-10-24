using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSeFGateway.Api.Invoice;

[JsonConverter(typeof(VatRateJsonConverter))]
public readonly record struct VatRate(string Value)
{
    public static implicit operator VatRate(int value) => new(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator VatRate(string value) => new(value == "np" ? "np I" : value);
    public decimal Percentage => Value switch { "23" => 23m, "8" => 8m, "5" => 5m, _ => 0m };
    public string? SummarySuffix => Value switch
    {
        "23" => "1", "8" => "2", "5" => "3", "0" => "6_1",
        "zw" => "7", "np I" => "8", "np II" => "9", _ => null
    };
    public string XmlValue => Value == "0" ? "0 KR" : Value;
    public override string ToString() => Value;
}

public sealed class VatRateJsonConverter : JsonConverter<VatRate>
{
    public const string ValidationMessage = "VAT rate must be a number (0, 5, 8, 23) or a string (zw, np, np I, np II).";
    public override VatRate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        VatRate rate;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)) rate = number;
        else if (reader.TokenType == JsonTokenType.String && reader.GetString() is "zw" or "np" or "np I" or "np II") rate = reader.GetString()!;
        else throw new JsonException(ValidationMessage);
        if (rate.SummarySuffix is null) throw new JsonException(ValidationMessage);
        return rate;
    }
    public override void Write(Utf8JsonWriter writer, VatRate value, JsonSerializerOptions options)
    {
        if (value.SummarySuffix is null) throw new JsonException(ValidationMessage);
        if (int.TryParse(value.Value, out var number)) writer.WriteNumberValue(number);
        else writer.WriteStringValue(value.Value);
    }
}
