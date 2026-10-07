namespace Skarbiec.Contracts;

// Fixed ids, so another service can reference a metal's MarketData instrument without looking it up.
public static class MetalInstruments
{
    public static readonly Guid Gold = new("868a6848-2be2-411f-a114-fb83ba9dc0c7");

    public static readonly Guid Silver = new("046ddc32-89d3-4697-8fd2-1c3ec9f55724");

    public static Guid InstrumentIdFor(Metal metal) => metal switch
    {
        Metal.Gold => Gold,
        Metal.Silver => Silver,
        _ => throw new ArgumentOutOfRangeException(nameof(metal), metal, "Unmapped Metal — add it to MetalInstruments."),
    };
}
