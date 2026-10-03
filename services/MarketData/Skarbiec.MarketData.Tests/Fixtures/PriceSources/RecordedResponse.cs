namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// Re-recording a file is how a vendor format change is caught by a test, not in production.
public static class RecordedResponse
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine("Fixtures", "PriceSources", "RecordedResponses", fileName));
}
