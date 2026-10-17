namespace Skarbiec.Portfolio.Features.Planning.Allocation;

public enum BucketKind
{
    Investments = 0,
    Savings = 1,
    Cushion = 2,
    Ike = 3,
    Ikze = 4,
    IkzeJdg = 5,
}

public enum BucketShareType
{
    Fixed = 0,
    Percent = 1,
}

public static class BucketKinds
{
    public static bool IsLimited(this BucketKind kind) => kind is BucketKind.Ike or BucketKind.Ikze or BucketKind.IkzeJdg;
}
