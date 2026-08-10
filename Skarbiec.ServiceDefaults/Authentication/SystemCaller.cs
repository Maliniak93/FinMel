namespace Skarbiec.ServiceDefaults.Authentication;

/// <summary>
/// Identifies a request as coming from another Skarbiec service acting on its own behalf — a
/// background job/consumer with no caller identity (e.g. Reporting's <c>DailyPricesSynced</c>
/// consumer, T2.11) — rather than from a specific user's forwarded JWT. This is a separate trust
/// boundary from ADR-006's per-user tenancy: <c>RequireAuthorization()</c> alone only proves the
/// bearer token is valid, not that its holder may see every user's data, so endpoints that
/// deliberately bypass the tenancy query filter (<c>IgnoreQueryFilters()</c>) require this policy
/// instead. Minted by <see cref="Http.SystemTokenHandler"/>, signed with the same shared "Jwt" key
/// every service already validates against — no new secret or auth scheme needed.
/// </summary>
public static class SystemCaller
{
    public const string ClaimType = "skarbiec:caller";
    public const string ClaimValue = "system";
    public const string PolicyName = "System";
}
