namespace Skarbiec.Identity.Features.Register;

public sealed class RegisterUserHandler(IPublishEndpoint publishEndpoint)
{
    public async Task HandleAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(new UserRegistered { UserId = userId, Email = email }, cancellationToken);
    }
}
