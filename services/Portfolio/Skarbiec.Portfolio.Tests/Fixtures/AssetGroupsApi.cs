using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Testing.Auth;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of an asset group endpoint calls it directly.
internal static class AssetGroupsApi
{
    public const string AssetGroupsUri = "/api/portfolio/asset-groups";

    public const string AssignmentUri = AssetGroupsUri + "/assignment";

    public const string GroupLimitReachedErrorCode = "Conflict.GroupLimitReached";

    public const string GroupColorTakenErrorCode = "Conflict.GroupColorTaken";

    public const string InvalidColorErrorCode = "Validation.InvalidColor";

    public const string InvalidTargetErrorCode = "Validation.InvalidTarget";

    public const string AssetGroupNotFoundErrorCode = "NotFound.AssetGroup";

    public const string AssetNotFoundErrorCode = "NotFound.Asset";

    public const string AssetIdsRequiredErrorCode = "Validation.AssetIdsRequired";

    public const int GroupLimit = 12;

    public const string TargetRequiredForCushionErrorCode = "Validation.TargetRequiredForCushion";

    public const string CushionGroupExistsErrorCode = "Conflict.CushionGroupExists";

    public const string InvalidContributionErrorCode = "Validation.InvalidContribution";

    public const string TooManyContributionsErrorCode = "Validation.TooManyContributions";

    public const string AccountNotFoundErrorCode = "NotFound.Account";

    public const string InvalidClassAllocationErrorCode = "Validation.InvalidClassAllocation";

    public const string ClassAllocationSumErrorCode = "Validation.ClassAllocationSum";

    public const string SharesUri = AssetGroupsUri + "/shares";

    public const string SharesSumErrorCode = "Validation.SharesSum";

    public const string SharesGroupMismatchErrorCode = "Validation.SharesGroupMismatch";

    public const int RegularKind = 0;

    public const int CushionKind = 1;

    public sealed record ClassShare(AssetClass AssetClass, decimal Percent);

    public sealed record ContributionBody(decimal AmountPln, Guid? AccountId = null);

    public sealed record AssetGroupView(
        Guid Id,
        string Name,
        string Color,
        decimal? TargetPln,
        int AssetCount,
        int Kind = RegularKind,
        decimal MonthlyContributionPln = 0m,
        List<ClassShare>? ClassAllocation = null,
        decimal? SharePercent = null,
        List<ContributionBody>? Contributions = null);

    public sealed record GroupShareBody(Guid GroupId, decimal SharePercent);

    public static string AssetGroupUri(Guid groupId) => $"{AssetGroupsUri}/{groupId}";

    public static string ColorKey(int index) => AssetGroupColors.Keys[index];

    public static ClassShare[] Shares(params (AssetClass AssetClass, decimal Percent)[] shares) =>
        shares.Select(s => new ClassShare(s.AssetClass, s.Percent)).ToArray();

    public static object NewAssetGroupBody(
        string name = "Poduszka",
        string? color = null,
        decimal? targetPln = null,
        int kind = RegularKind,
        decimal monthlyContributionPln = 0m,
        ClassShare[]? classAllocation = null,
        ContributionBody[]? contributions = null) =>
        new
        {
            name,
            color = color ?? ColorKey(0),
            targetPln,
            kind,
            contributions = contributions ?? (monthlyContributionPln == 0m ? [] : [new ContributionBody(monthlyContributionPln)]),
            classAllocation = classAllocation ?? []
        };

    public static object NewAssignmentBody(IEnumerable<Guid> assetIds, Guid? groupId) =>
        new { assetIds = assetIds.ToArray(), groupId };

    public static async Task<AssetGroupView> CreateAssetGroupAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Poduszka",
        string? color = null,
        decimal? targetPln = null,
        int kind = RegularKind,
        decimal monthlyContributionPln = 0m,
        ClassShare[]? classAllocation = null,
        ContributionBody[]? contributions = null)
    {
        var response = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody(name, color, targetPln, kind, monthlyContributionPln, classAllocation, contributions), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
    }

    public static async Task<List<AssetGroupView>> ListAssetGroupsAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssetGroupsUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<AssetGroupView>>(cancellationToken))!;
    }

    public static async Task AssignAssetsAsync(
        this HttpClient client, Guid? groupId, CancellationToken cancellationToken, params Guid[] assetIds)
    {
        var response = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody(assetIds, groupId), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static object NewSharesBody(params (Guid GroupId, decimal SharePercent)[] shares) =>
        new { shares = shares.Select(s => new GroupShareBody(s.GroupId, s.SharePercent)).ToArray() };

    public static async Task SaveGroupSharesAsync(
        this HttpClient client, CancellationToken cancellationToken, params (Guid GroupId, decimal SharePercent)[] shares)
    {
        var response = await client.PutAsJsonAsync(SharesUri, NewSharesBody(shares), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<AssetGroupView> UpdateAssetGroupAsync(
        this HttpClient client,
        Guid groupId,
        CancellationToken cancellationToken,
        string name = "Poduszka",
        string? color = null,
        decimal? targetPln = null,
        int kind = RegularKind,
        decimal monthlyContributionPln = 0m,
        ClassShare[]? classAllocation = null,
        ContributionBody[]? contributions = null)
    {
        var response = await client.PutAsJsonAsync(
            AssetGroupUri(groupId), NewAssetGroupBody(name, color, targetPln, kind, monthlyContributionPln, classAllocation, contributions), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
    }

    public const string TreeUri = AssetGroupsUri + "/tree";

    public const string AccountCashManagedErrorCode = "Validation.AccountCashManaged";

    public static string MembersUri(Guid groupId) => $"{AssetGroupUri(groupId)}/members";

    public static object NewMembersBody(IEnumerable<Guid>? accountIds = null, IEnumerable<Guid>? assetIds = null) =>
        new { accountIds = (accountIds ?? []).ToArray(), assetIds = (assetIds ?? []).ToArray() };

    public static async Task SetAssetGroupMembersAsync(
        this HttpClient client,
        Guid groupId,
        CancellationToken cancellationToken,
        IEnumerable<Guid>? accountIds = null,
        IEnumerable<Guid>? assetIds = null)
    {
        var response = await client.PutAsJsonAsync(MembersUri(groupId), NewMembersBody(accountIds, assetIds), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<List<JsonElement>> GetAssetGroupTreeAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(TreeUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.ReadJsonAsync(cancellationToken);

        return TreeItems(body);
    }

    public static List<JsonElement> TreeItems(JsonElement body) =>
        (body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items")).EnumerateArray().ToList();

    public static List<JsonElement> TreeChildren(JsonElement item) =>
        item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray().ToList()
            : [];

    public static Guid? OptionalGuid(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetGuid() : null;

    public sealed record GroupedAccount(Guid AccountId, Guid CashAssetId);

    public static async Task<GroupedAccount> CreateGroupedAccountAsync(
        this HttpClient client,
        Guid? groupId,
        CancellationToken cancellationToken,
        string name = "XTB",
        AccountType type = AccountType.Brokerage)
    {
        var response = await client.PostAsJsonAsync(
            AccountsApi.AccountsUri,
            new CreateAccountRequest { Name = name, Type = type, Wrapper = TaxWrapper.Regular, GroupId = groupId },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!;

        return new GroupedAccount(created.Id, created.CashAssetId);
    }

    public sealed record GroupedBonds(Guid AccountId, Guid CashAssetId, Guid FirstBond, Guid SecondBond, Guid? ExceptionBond);

    // Two bonds inheriting the account's group and, when exceptionGroupId is set, a third bond that is an exception to it.
    public static async Task<GroupedBonds> CreateGroupedBondsAccountAsync(
        this HttpClient client,
        Guid groupId,
        CancellationToken cancellationToken,
        Guid? exceptionGroupId = null,
        string name = "Bonds")
    {
        var account = await client.CreateGroupedAccountAsync(groupId, cancellationToken, name, AccountType.Bonds);
        var first = await client.AddBondAsync(account.AccountId, cancellationToken, PortfolioApi.NewBondRequest(name: "Bond 1"));
        var second = await client.AddBondAsync(account.AccountId, cancellationToken, PortfolioApi.NewBondRequest(name: "Bond 2"));
        Guid? exception = null;
        if (exceptionGroupId is not null)
        {
            var third = await client.AddBondAsync(account.AccountId, cancellationToken, PortfolioApi.NewBondRequest(name: "Bond 3"));
            await client.AssignAssetsAsync(exceptionGroupId, cancellationToken, third.AssetId);
            exception = third.AssetId;
        }

        return new GroupedBonds(account.AccountId, account.CashAssetId, first.AssetId, second.AssetId, exception);
    }

    public static async Task<Dictionary<Guid, decimal?>> ReadSharesAsync(
        this HttpClient client, CancellationToken cancellationToken) =>
        (await client.ListAssetGroupsAsync(cancellationToken)).ToDictionary(g => g.Id, g => g.SharePercent);
}
