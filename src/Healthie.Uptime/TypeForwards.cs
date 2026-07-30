using System.Runtime.CompilerServices;

// Every public type this package used to define now lives in Healthie.NET.DependencyInjection.
//
// Forwarded rather than moved outright, because moving a type between assemblies is a binary break
// even when the source is identical: an assembly compiled against Healthie.Uptime asks the runtime
// for Healthie.Uptime!Healthie.Uptime.IUptimeStore by name, and without a forward it gets a
// TypeLoadException at the first call. With one, the runtime follows the pointer and the caller
// never notices. That is what makes folding these into core a minor release rather than a major.
//
// This package is deprecated on nuget.org and exists only so applications that reference it keep
// working. Nothing new should be added here.

[assembly: TypeForwardedTo(typeof(Healthie.Uptime.IUptimeStore))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.InMemoryUptimeStore))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.StartupExtensions))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.UptimeCalculator))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.UptimeRecorder))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.UptimeReport))]
[assembly: TypeForwardedTo(typeof(Healthie.Uptime.UptimeSegment))]
