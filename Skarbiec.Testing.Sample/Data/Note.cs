using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Testing.Sample.Data;

public sealed class Note : IUserOwned
{
    public Guid Id { get; init; }

    public required string Text { get; set; }

    public Guid UserId { get; set; }
}
