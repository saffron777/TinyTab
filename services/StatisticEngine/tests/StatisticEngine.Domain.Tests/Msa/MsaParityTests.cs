using System.Text.Json;
using StatisticEngine.Domain.Msa;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Msa;

/// <summary>Paridad del módulo de sistemas de medida (<c>msa.ts</c>).</summary>
public class MsaParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["gageRR"] = goldenCase => ResultDto.From(GageRR.GageRRStudy(
            Rows(goldenCase.Args[0]),
            goldenCase.Count > 1 ? GoldenReader.Number(goldenCase.Args[1]) : null)),
        ["gageStatisticsByOperator"] = goldenCase => GageRR.GageStatisticsByOperator(Rows(goldenCase.Args[0]))
    };

    [Fact]
    public void CoincideConElMotorTypescript() => GoldenSuite.Run("msa", Registry, 1e-12);

    private static GageRow[] Rows(JsonElement element) =>
        element.EnumerateArray()
            .Select(row => new GageRow(
                row.GetProperty("part").GetString() ?? string.Empty,
                row.GetProperty("operator").GetString() ?? string.Empty,
                GoldenReader.Number(row.GetProperty("trial")),
                GoldenReader.Number(row.GetProperty("value"))))
            .ToArray();
}
