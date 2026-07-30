using System.Runtime.CompilerServices;

// Every public type this package used to define now lives in Healthie.NET.DependencyInjection.
// See the note in Healthie.Uptime's TypeForwards.cs for why these are forwarded rather than simply
// moved: without the forward, an assembly compiled against this one fails at runtime rather than at
// build time, which is the worst way to find out.
//
// This package is deprecated on nuget.org and exists only so applications that reference it keep
// working. Nothing new should be added here.

[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.ILeaseProvider))]
[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.InMemoryLeaseProvider))]
[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.LeaderElectedPulseScheduler))]
[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.LeaderElectionOptions))]
[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.LeaderElectionService))]
[assembly: TypeForwardedTo(typeof(Healthie.LeaderElection.StartupExtensions))]
