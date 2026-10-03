namespace Skarbiec.MarketData.Features.GetFxRate;

/// <summary>The {Currency}PLN rate in effect on the asked date, and the date it was published for.</summary>
public sealed record FxRateResponse(string Currency, DateOnly Date, decimal Rate);
