using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

public class DataOperationsSmokeTests
{
    private static Frame Frame()
    {
        var dates = Enumerable.Range(0, 12).Select(index => (DateTime?)new DateTime(2024, 1, 1).AddMonths(index)).ToArray();
        return new Frame(
        [
            DataColumn.Numeric("x", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]),
            DataColumn.Numeric("y", [2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24]),
            DataColumn.Text("g", ["a", "a", "b", "b", "a", "a", "b", "b", "a", "a", "b", "b"]),
            DataColumn.DateTime("fecha", dates)
        ]);
    }

    private static Result<AnalysisResult> Run(string id, Dictionary<string, object?>? parameters = null, Dictionary<string, string[]>? roles = null, IReadOnlyDictionary<string, Frame>? datasets = null)
    {
        var input = new AnalysisInput(frame: Frame(), parameters: parameters, roles: roles, datasets: datasets);
        return DataService.Run(id, input);
    }

    [Fact]
    public void FiltraCalculaYOrdena()
    {
        Assert.True(Run("filter", new() { ["condition"] = "x > 6" }).IsOk);
        Assert.True(Run("compute", new() { ["name"] = "z", ["expression"] = "x + y" }).IsOk);
        Assert.True(Run("sort", new() { ["descending"] = true }, new() { ["cols"] = ["x"] }).IsOk);
    }

    [Fact]
    public void AgrupaRemodelaYUne()
    {
        Assert.True(Run("aggregate", new() { ["functions"] = new[] { "mean" } }, new() { ["by"] = ["g"], ["values"] = ["x"] }).IsOk);
        Assert.True(Run("melt", new() { ["var_name"] = "variable", ["value_name"] = "valor" }, new() { ["ids"] = ["g"], ["values"] = ["x", "y"] }).IsOk);
        Assert.True(Run("merge", new() { ["other"] = "otro", ["how"] = "left" }, new() { ["keys"] = ["x"], ["other_keys"] = ["x"] }, new Dictionary<string, Frame> { ["otro"] = Frame() }).IsOk);
    }

    [Fact]
    public void ImputaYDetectaAtipicos()
    {
        var missing = Run("missing", new() { ["method"] = "mean" }, new() { ["cols"] = ["x"] });
        Assert.True(missing.IsOk, missing.IsError ? missing.Error.Message : "missing");
        Assert.True(Run("outliers", new() { ["method"] = "iqr", ["action"] = "flag" }, new() { ["cols"] = ["x"] }).IsOk);
        Assert.True(Run("impute-model", new() { ["method"] = "knn", ["k"] = 3 }, new() { ["cols"] = ["x"], ["predictors"] = ["y"] }).IsOk);
    }

    [Fact]
    public void CreaVariablesYFechas()
    {
        Assert.True(Run("bin", new() { ["name"] = "x_bin", ["bins"] = 3 }, new() { ["col"] = ["x"] }).IsOk);
        Assert.True(Run("recode", new() { ["rules"] = "1..6 => 0\n7..12 => 1" }, new() { ["col"] = ["x"] }).IsOk);
        Assert.True(Run("date-parts", new() { ["parts"] = new[] { "year", "month", "weekday" } }, new() { ["col"] = ["fecha"] }).IsOk);
        Assert.True(Run("dummies", new() { ["drop_first"] = true }, new() { ["cols"] = ["g"] }).IsOk);
        Assert.True(Run("math", new() { ["function"] = "log" }, new() { ["cols"] = ["x"] }).IsOk);
        Assert.True(Run("time-ops", new() { ["operation"] = "diff", ["periods"] = 1 }, new() { ["cols"] = ["x"] }).IsOk);
        Assert.True(Run("text-ops", new() { ["operations"] = new[] { "upper" } }, new() { ["cols"] = ["g"] }).IsOk);
    }
}
