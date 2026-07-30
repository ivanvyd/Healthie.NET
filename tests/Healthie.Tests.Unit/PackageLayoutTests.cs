using Healthie.Abstractions.Insights;
using Healthie.DependencyInjection;
using Healthie.LeaderElection;
using Healthie.Uptime;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Uptime and leader election moved into the core package, and the packages they used to live in
/// became type forwards.
/// </summary>
/// <remarks>
/// <para>
/// The reason this is a test and not a note: moving a type between assemblies is a binary break that
/// the compiler cannot see. Source that says <c>using Healthie.Uptime;</c> keeps building either
/// way, and an application compiled against the old assembly only discovers the difference when it
/// throws <c>TypeLoadException</c> at the first call. Nothing else in the suite would catch a
/// forward that was dropped or misspelled.
/// </para>
/// <para>
/// So these assert against assembly-qualified names, the way the runtime resolves them, rather than
/// against the C# types the compiler has already bound for us.
/// </para>
/// </remarks>
public class PackageLayoutTests
{
    /// <summary>Every public type the two deprecated packages ever exposed.</summary>
    public static TheoryData<string, string> ForwardedTypes() => new()
    {
        { "Healthie.Uptime.IUptimeStore", "Healthie.Uptime" },
        { "Healthie.Uptime.InMemoryUptimeStore", "Healthie.Uptime" },
        { "Healthie.Uptime.StartupExtensions", "Healthie.Uptime" },
        { "Healthie.Uptime.UptimeCalculator", "Healthie.Uptime" },
        { "Healthie.Uptime.UptimeRecorder", "Healthie.Uptime" },
        { "Healthie.Uptime.UptimeReport", "Healthie.Uptime" },
        { "Healthie.Uptime.UptimeSegment", "Healthie.Uptime" },
        { "Healthie.LeaderElection.ILeaseProvider", "Healthie.LeaderElection" },
        { "Healthie.LeaderElection.InMemoryLeaseProvider", "Healthie.LeaderElection" },
        { "Healthie.LeaderElection.LeaderElectedPulseScheduler", "Healthie.LeaderElection" },
        { "Healthie.LeaderElection.LeaderElectionOptions", "Healthie.LeaderElection" },
        { "Healthie.LeaderElection.LeaderElectionService", "Healthie.LeaderElection" },
        { "Healthie.LeaderElection.StartupExtensions", "Healthie.LeaderElection" },
    };

    /// <summary>
    /// Asking the old assembly for a type it no longer defines must still hand back the type, now
    /// living in the core assembly. This is what an application compiled against 4.0.0 does.
    /// </summary>
    [Theory]
    [MemberData(nameof(ForwardedTypes))]
    public void ATypeAskedForByItsOldAssembly_ResolvesToTheCoreAssembly(string typeName, string oldAssembly)
    {
        var resolved = Type.GetType($"{typeName}, {oldAssembly}", throwOnError: false);

        Assert.True(resolved is not null, $"'{typeName}, {oldAssembly}' did not resolve -- the type forward is missing.");
        Assert.Equal("Healthie.DependencyInjection", resolved!.Assembly.GetName().Name);
    }

    /// <summary>
    /// The point of the move: the core package registers both without a second package installed.
    /// </summary>
    [Fact]
    public void TheCorePackage_RegistersUptimeAndLeaderElection()
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(PackageLayoutTests).Assembly);
        services.AddHealthieUptime();
        services.AddHealthieLeaderElection();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IUptimeStore>());
        Assert.NotNull(provider.GetService<IUptimeInsights>());
        Assert.NotNull(provider.GetService<ILeaseProvider>());
        Assert.NotNull(provider.GetService<ILeadershipInsights>());
    }

    /// <summary>
    /// The meta-package must stay a pointer, not a bundle.
    /// </summary>
    /// <remarks>
    /// One stray <c>ProjectReference</c> in Healthie.NET.Package.csproj turns <c>dotnet add package
    /// Healthie.NET</c> into an install that drags a database driver, a scheduler and a UI framework
    /// onto machines that asked for none of them. Nothing else would catch that: it builds, it packs,
    /// and the damage is only visible in the restore graph of whoever installed it.
    /// </remarks>
    [Fact]
    public void TheMetaPackage_DependsOnTheCorePackageAndNothingElse()
    {
        var csproj = FindRepositoryFile("src/Healthie.NET.Package/Healthie.NET.Package.csproj");

        var referenced = System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(csproj), @"<ProjectReference\s+Include=""[^""]*[\\/]([A-Za-z.]+)\.csproj""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.Equal(["Healthie.DependencyInjection"], referenced);
    }

    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relativePath)))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, $"Could not find '{relativePath}' above {AppContext.BaseDirectory}.");

        return Path.Combine(directory!.FullName, relativePath);
    }

    /// <summary>
    /// Still opt-in. Folding them into core was about how many packages you install, not about
    /// starting services nobody asked for.
    /// </summary>
    [Fact]
    public void TheCorePackage_StartsNeitherOfThemUnlessAsked()
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(PackageLayoutTests).Assembly);

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IUptimeStore>());
        Assert.Null(provider.GetService<ILeaseProvider>());
        Assert.Null(provider.GetService<IUptimeInsights>());
        Assert.Null(provider.GetService<ILeadershipInsights>());
    }
}
