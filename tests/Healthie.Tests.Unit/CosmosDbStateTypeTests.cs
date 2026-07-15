using Healthie.Abstractions.Models;
using Healthie.StateProviding.CosmosDb;

namespace Healthie.Tests.Unit;

/// <summary>
/// The stored type recorded with each CosmosDB document guards against returning a state as a type
/// it was not written as. It must not, however, reject state written by a different release: the
/// assembly version tracks the release version, a rejected read throws, and a pulse checker turns a
/// throw into a failed health check. Comparing on anything version-sensitive would take every
/// checker in an existing deployment unhealthy the moment the library is upgraded.
/// </summary>
public class CosmosDbStateTypeTests
{
    private const string CheckerName = "Some.Checker";

    private static string FullName => typeof(PulseCheckerState).FullName!;

    /// <summary>Builds the assembly-qualified name a given release would have recorded.</summary>
    private static string LegacyAssemblyQualifiedName(string assemblyVersion) =>
        $"{FullName}, Healthie.Abstractions, Version={assemblyVersion}, Culture=neutral, PublicKeyToken=null";

    [Fact]
    public void EnsureStoredTypeMatches_ForTheTypeItWasWrittenAs_DoesNotThrow()
    {
        CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(CheckerName, FullName);
    }

    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("2.3.0.0")]
    [InlineData("99.0.0.0")]
    public void EnsureStoredTypeMatches_ForStateWrittenByAnotherRelease_DoesNotThrow(string assemblyVersion)
    {
        var storedByOtherRelease = LegacyAssemblyQualifiedName(assemblyVersion);

        CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(CheckerName, storedByOtherRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureStoredTypeMatches_ForDocumentsWrittenBeforeTheTypeWasRecorded_DoesNotThrow(string? storedStateType)
    {
        CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(CheckerName, storedStateType);
    }

    [Fact]
    public void EnsureStoredTypeMatches_ForAnUnrelatedType_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(
                CheckerName,
                "Some.Other.Namespace.CompletelyDifferentState"));

        Assert.Contains(CheckerName, exception.Message);
    }

    // A type whose name merely starts with the expected one is a different type.
    [Fact]
    public void EnsureStoredTypeMatches_ForATypeWhoseNameSharesThePrefix_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(
                CheckerName,
                FullName + "Extended"));
    }

    // The recorded value has to be the one the check accepts, or writing and reading disagree.
    [Fact]
    public void StateType_RecordedOnWrite_IsAcceptedOnRead()
    {
        var recorded = typeof(PulseCheckerState).FullName;

        CosmosDbStateProvider.EnsureStoredTypeMatches<PulseCheckerState>(CheckerName, recorded);
    }

    [Fact]
    public void StateType_RecordedOnWrite_CarriesNoAssemblyVersion()
    {
        var recorded = typeof(PulseCheckerState).FullName!;

        Assert.DoesNotContain("Version=", recorded, StringComparison.Ordinal);
    }
}
