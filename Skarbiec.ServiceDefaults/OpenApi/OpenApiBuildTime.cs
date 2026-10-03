using System.Reflection;

namespace Skarbiec.ServiceDefaults.OpenApi;

// True inside the build-time OpenAPI generator, which has no DB, broker or HTTP dependency to start.
public static class OpenApiBuildTime
{
    public static bool IsActive { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
