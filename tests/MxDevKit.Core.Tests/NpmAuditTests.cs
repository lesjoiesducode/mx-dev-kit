namespace MxDevKit.Tests;

public class NpmAuditTests
{
    private const string Report = """
        {
          "auditReportVersion": 2,
          "vulnerabilities": {},
          "metadata": {
            "vulnerabilities": { "info": 0, "low": 2, "moderate": 1, "high": 4, "critical": 1, "total": 8 },
            "dependencies": { "total": 120 }
          }
        }
        """;

    [Theory]
    [InlineData("high", 5)]
    [InlineData("critical", 1)]
    [InlineData("moderate", 6)]
    [InlineData("low", 8)]
    [InlineData("medium", 6)]
    [InlineData("nonsense", 5)]
    public void Count_adds_severities_from_the_threshold(string min, int expected) =>
        Assert.Equal(expected, NpmAudit.Count(Report, min));

    [Fact]
    public void Count_of_a_clean_project_is_zero() =>
        Assert.Equal(0, NpmAudit.Count("""{"metadata":{"vulnerabilities":{"info":0,"low":0,"moderate":0,"high":0,"critical":0,"total":0}}}"""));

    [Theory]
    [InlineData("""{"error":{"code":"ENOLOCK","summary":"This command requires an existing lockfile."}}""")]
    [InlineData("npm ERR! network")]
    [InlineData("")]
    public void Count_is_null_without_a_report(string output) => Assert.Null(NpmAudit.Count(output));
}
