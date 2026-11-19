using Xunit;
using Xunit.Abstractions;

namespace Payments.Tests;

/// <summary>Which release works in which schema state. This table is the migration plan.</summary>
public class CompatibilityTests(ITestOutputHelper output)
{
    // true: every behaviour check passes. false: at least one fails.
    private static readonly Dictionary<(int Release, Schema State), bool> Expected = new()
    {
        [(1, Schema.Legacy)] = true,  [(1, Schema.Expanded)] = true,  [(1, Schema.Constrained)] = false, [(1, Schema.Relaxed)] = false, [(1, Schema.Contracted)] = false,
        [(2, Schema.Legacy)] = false, [(2, Schema.Expanded)] = true,  [(2, Schema.Constrained)] = true,  [(2, Schema.Relaxed)] = true,  [(2, Schema.Contracted)] = false,
        [(3, Schema.Legacy)] = false, [(3, Schema.Expanded)] = false, [(3, Schema.Constrained)] = true,  [(3, Schema.Relaxed)] = true,  [(3, Schema.Contracted)] = false,
        [(4, Schema.Legacy)] = false, [(4, Schema.Expanded)] = false, [(4, Schema.Constrained)] = false, [(4, Schema.Relaxed)] = true,  [(4, Schema.Contracted)] = true,
    };

    public static IEnumerable<object[]> Combinations() => Expected.Keys.Select(k => new object[] { k.Release, k.State });

    [Theory, MemberData(nameof(Combinations))]
    public async Task Release_in_schema_state(int release, Schema state)
    {
        var failures = await Behaviour.FailuresAsync(release, state);

        output.WriteLine($"MATRIX release {release} | {state} | {Behaviour.Checks.Length - failures.Count}/{Behaviour.Checks.Length}");
        foreach (var failure in failures) output.WriteLine("  " + failure);

        Assert.Equal(Expected[(release, state)], failures.Count == 0);
    }
}
