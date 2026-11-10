namespace MxDevKit.Tests;

public class OsvScannerTests
{
    // Shaped like `osv-scanner scan source --format json -r .` (trimmed): two lockfiles, aliases grouped.
    private const string Report = """
        {
          "results": [
            {
              "source": { "path": "/work/api/poetry.lock", "type": "lockfile" },
              "packages": [
                {
                  "package": { "name": "jinja2", "version": "3.1.2", "ecosystem": "PyPI" },
                  "vulnerabilities": [
                    { "id": "GHSA-h5c8-rqwp-cp95", "aliases": ["CVE-2024-22195"], "database_specific": { "severity": "MODERATE" } },
                    { "id": "PYSEC-2024-1", "aliases": ["CVE-2024-22195"] }
                  ],
                  "groups": [ { "ids": ["GHSA-h5c8-rqwp-cp95", "PYSEC-2024-1"], "aliases": ["CVE-2024-22195"], "max_severity": "5.4" } ]
                },
                {
                  "package": { "name": "requests", "version": "2.25.0", "ecosystem": "PyPI" },
                  "vulnerabilities": [ { "id": "GHSA-j8r2-6x86-q33q", "database_specific": { "severity": "MODERATE" } } ],
                  "groups": [ { "ids": ["GHSA-j8r2-6x86-q33q"], "max_severity": "" } ]
                }
              ]
            },
            {
              "source": { "path": "/work/api/web/package-lock.json", "type": "lockfile" },
              "packages": [
                {
                  "package": { "name": "lodash", "version": "4.17.20", "ecosystem": "npm" },
                  "vulnerabilities": [],
                  "groups": [
                    { "ids": ["GHSA-35jh-r3h4-6jhm", "CVE-2021-23337"], "max_severity": "7.2" },
                    { "ids": ["GHSA-29mw-wpgm-hmr9"], "max_severity": "9.8" }
                  ]
                },
                {
                  "package": { "name": "left-pad", "version": "1.0.0", "ecosystem": "npm" },
                  "vulnerabilities": [ { "id": "GO-0000-0001" } ],
                  "groups": [ { "ids": ["GO-0000-0001"] } ]
                }
              ]
            },
            {
              "source": { "path": "/work/api/web/copy/package-lock.json", "type": "lockfile" },
              "packages": [
                {
                  "package": { "name": "lodash", "version": "4.17.20", "ecosystem": "npm" },
                  "groups": [ { "ids": ["CVE-2021-23337", "GHSA-35jh-r3h4-6jhm"], "max_severity": "7.2" } ]
                }
              ]
            }
          ]
        }
        """;

    // jinja2 moderate (5.4), requests moderate (word), lodash high (7.2, counted once for two lockfiles),
    // lodash critical (9.8), left-pad unknown.
    [Theory]
    [InlineData("critical", "high", 1)]
    [InlineData("high", "high", 3)]
    [InlineData("high", "low", 2)]
    [InlineData("moderate", "low", 4)]
    [InlineData("low", "low", 5)]
    [InlineData("medium", "critical", 5)]
    public void Count_follows_the_threshold_and_the_unknown_severity(string min, string unknown, int expected) =>
        Assert.Equal(expected, OsvScanner.Count(Report, min, unknown));

    [Theory]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"results":null}""")]
    [InlineData("""{"results":[{"source":{"path":"/w/go.mod","type":"lockfile"},"packages":[]}]}""")]
    public void Count_of_a_clean_project_is_zero(string json) => Assert.Equal(0, OsvScanner.Count(json));

    [Theory]
    [InlineData("No package sources found, --help for usage information.")]
    [InlineData("""{"error":"offline"}""")]
    [InlineData("[]")]
    [InlineData("")]
    public void Count_is_null_without_a_report(string output) => Assert.Null(OsvScanner.Count(output));

    [Theory]
    [InlineData("package-lock.json", true)]
    [InlineData("web/pnpm-lock.yaml", true)]
    [InlineData("poetry.lock", true)]
    [InlineData("requirements.txt", true)]
    [InlineData("requirements-dev.txt", true)]
    [InlineData("composer.lock", true)]
    [InlineData("Cargo.lock", true)]
    [InlineData("go.mod", true)]
    [InlineData("Gemfile.lock", true)]
    [InlineData("src\\App\\packages.lock.json", true)]
    [InlineData("bin/App.deps.json", true)]
    [InlineData("package.json", false)]
    [InlineData("README.md", false)]
    [InlineData("go.sum", false)]
    public void IsLockfile_knows_what_osv_scanner_reads(string path, bool expected) =>
        Assert.Equal(expected, OsvScanner.IsLockfile(path));

    [Fact]
    public void Arguments_for_both_major_versions()
    {
        Assert.Equal(new[] { "scan", "source", "--format", "json", "--recursive", "/w" }, OsvScanner.Arguments("/w", version1: false));
        Assert.Equal(new[] { "--format", "json", "--recursive", "/w" }, OsvScanner.Arguments("/w", version1: true));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(127, false)]
    [InlineData(2, false)]
    public void Understood_exit_codes(int code, bool expected) => Assert.Equal(expected, OsvScanner.Understood(code));

    [Fact]
    public void Audit_tool_and_unknown_severity_in_config()
    {
        var config = DevKitConfig.Parse("""{ "audit": { "tool": "OSV-Scanner", "unknownSeverity": "Medium" } }""");
        Assert.Equal("osv", config.Audit.Tool);
        Assert.Equal("moderate", config.Audit.UnknownSeverity);

        var defaults = DevKitConfig.Parse("{}");
        Assert.Equal("auto", defaults.Audit.Tool);
        Assert.Equal("high", defaults.Audit.UnknownSeverity);
        Assert.Equal("npm", DevKitConfig.Parse("""{ "audit": { "tool": "npm" } }""").Audit.Tool);
        Assert.Equal("auto", DevKitConfig.Parse("""{ "audit": { "tool": "snyk" } }""").Audit.Tool);
    }
}
