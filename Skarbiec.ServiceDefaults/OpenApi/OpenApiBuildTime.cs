using System.Reflection;

namespace Skarbiec.ServiceDefaults.OpenApi;

/// <summary>
/// True when the current process is the <c>Microsoft.Extensions.ApiDescription.Server</c>
/// build-time document generator (<c>GetDocument.Insider</c>), not the real service host —
/// Microsoft's own documented pattern for gating startup code that would otherwise need a live
/// DB/broker/HTTP dependency the generator never has (T-hygiene, spec-00). Every service's
/// <c>Program.cs</c> checks this before registering anything the doc generator can't satisfy;
/// the doc generator only introspects mapped routes, so endpoints and DI-resolved-per-request
/// handlers stay unguarded.
/// </summary>
public static class OpenApiBuildTime
{
    public static bool IsActive { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
