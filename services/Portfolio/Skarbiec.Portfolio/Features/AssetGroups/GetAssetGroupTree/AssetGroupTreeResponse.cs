using System.Text.Json.Serialization;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.GetAssetGroupTree;

[JsonConverter(typeof(JsonStringEnumConverter<AssetGroupTreeItemKind>))]
public enum AssetGroupTreeItemKind
{
    Account,
    Asset
}

/// <summary>An account (with its securities as children) or a stand-alone asset; the fields of the other kind are null.</summary>
public sealed record AssetGroupTreeItem(
    AssetGroupTreeItemKind Kind,
    Guid? AccountId,
    Guid? AssetId,
    string Name,
    AccountType? AccountType,
    AssetClass? AssetClass,
    Guid? GroupId,
    Guid? CashAssetId,
    bool IsArchived,
    IReadOnlyList<AssetGroupTreeChild> Children);

/// <summary>A Stock, Etf or Bond in an account; GroupId is its effective group.</summary>
public sealed record AssetGroupTreeChild(
    Guid AssetId,
    string Name,
    AssetClass AssetClass,
    Guid? GroupId,
    bool IsException,
    bool IsArchived);
