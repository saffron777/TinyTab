using System.Text.Json;
using StatisticEngine.Domain.Tables;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Tables;

/// <summary>Paridad de tabulaciones, tablas cruzadas y bondad de ajuste.</summary>
public class TablesParityTests
{
    [Fact]
    public void LasTablasCoincidenConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal)
        {
            ["tally"] = goldenCase => CrossTabs.Tally(Cells(goldenCase.Args[0])),

            ["crossTabulation"] = goldenCase => ResultDto.From(
                CrossTabs.CrossTabulation(Cells(goldenCase.Args[0]), Cells(goldenCase.Args[1]))),

            ["goodnessOfFit"] = goldenCase => ResultDto.From(
                CrossTabs.GoodnessOfFit(
                    GoldenReader.Texts(goldenCase.Args[0]),
                    GoldenReader.Numbers(goldenCase.Args[1]),
                    goldenCase.Count > 2 ? GoldenReader.Numbers(goldenCase.Args[2]) : null))
        };

        GoldenSuite.Run("tables", registry, 1e-12);
    }

    private static CellValue[] Cells(JsonElement element)
    {
        var output = new List<CellValue>();
        foreach (var item in element.EnumerateArray())
        {
            output.Add(item.ValueKind == JsonValueKind.String
                ? CellValue.FromText(GoldenReader.Text(item))
                : CellValue.FromNumber(GoldenReader.Number(item)));
        }
        return output.ToArray();
    }
}
