using System.Text.Json;
using System.Text.Json.Serialization;
using StatisticEngine.Domain.Data;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>
/// Serializa una columna de dataset como <c>{ "name", "kind", "values" }</c>, con <c>null</c> para
/// los faltantes numéricos y de fecha. El API sólo escribe datasets (nunca los lee de este tipo).
/// </summary>
public sealed class DataColumnJsonConverter : JsonConverter<DataColumn>
{
    public override DataColumn Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Los datasets son sólo de salida en este contrato.");

    public override void Write(Utf8JsonWriter writer, DataColumn column, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", column.Name);
        writer.WriteString("kind", column.Kind switch
        {
            DataColumnKind.Numeric => "numeric",
            DataColumnKind.Text => "text",
            _ => "datetime"
        });
        writer.WritePropertyName("values");
        writer.WriteStartArray();
        for (var index = 0; index < column.Count; index++)
        {
            switch (column.Kind)
            {
                case DataColumnKind.Numeric:
                {
                    var value = column.NumberAt(index);
                    if (double.IsNaN(value)) writer.WriteNullValue();
                    else writer.WriteNumberValue(value);
                    break;
                }
                case DataColumnKind.Text:
                {
                    var value = column.TextAt(index);
                    if (value is null) writer.WriteNullValue();
                    else writer.WriteStringValue(value);
                    break;
                }
                default:
                {
                    var value = column.DateAt(index);
                    if (value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else if (value.Value.TimeOfDay == TimeSpan.Zero)
                    {
                        writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    break;
                }
            }
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
