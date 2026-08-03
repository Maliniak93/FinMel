namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Loads a recorded raw vendor response from <c>Fixtures/PriceSources/RecordedResponses/</c> —
/// re-recording this file is how a vendor format change is caught by a test, not in production
/// (T2.2 scope). T2.3-T2.5 add their own recorded responses alongside these.
/// </summary>
public static class RecordedResponse
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine("Fixtures", "PriceSources", "RecordedResponses", fileName));
}
