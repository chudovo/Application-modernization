using Xunit;
using Xunit.Abstractions;

namespace Crm.Tests;

public class EngineTests(ITestOutputHelper output)
{
    private async Task<List<string>> RunAsync(Engine engine)
    {
        var failures = await Behaviour.FailuresAsync(engine);
        output.WriteLine($"ENGINE {engine}: {Behaviour.Checks.Length - failures.Count} of {Behaviour.Checks.Length} checks pass");
        foreach (var failure in failures) output.WriteLine($"ENGINE {engine} FAILS {failure}");
        return failures;
    }

    [Fact]
    public async Task Sql_server_passes_every_check() => Assert.Empty(await RunAsync(Engine.SqlServer));

    [Fact]
    public async Task Postgresql_passes_every_check() => Assert.Empty(await RunAsync(Engine.Postgres));

    // The two tests below are not goals. They record how far a statement-by-statement translation gets,
    // so that the numbers in the README stay true.

    [Fact]
    public async Task The_mechanical_translation_fails_ten_of_fourteen() =>
        Assert.Equal(10, (await RunAsync(Engine.PostgresMechanical)).Count);

    [Fact]
    public async Task The_mechanical_translation_with_identities_reset_fails_nine_of_fourteen() =>
        Assert.Equal(9, (await RunAsync(Engine.PostgresMechanicalWithSequences)).Count);
}
