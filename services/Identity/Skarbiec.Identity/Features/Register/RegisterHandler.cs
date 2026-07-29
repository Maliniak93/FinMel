using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;

namespace Skarbiec.Identity.Features.Register;

public sealed class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext,
    IPublishEndpoint publishEndpoint)
{
    // UserManager<TUser> predates CancellationToken support and has no overload to forward it to.
    public async Task<Result<RegisterResponse>> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        // Npgsql's retry-on-failure execution strategy forbids a bare BeginTransactionAsync — it
        // can't replay a user-initiated transaction, only a whole delegate it controls. Running
        // both CreateAsync's own SaveChanges and our outbox SaveChanges inside this delegate is
        // also what lets them share one transaction, committing atomically (ADR-012).
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<Result<RegisterResponse>>(async attemptCancellationToken =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(attemptCancellationToken);

            var identityResult = await userManager.CreateAsync(user, request.Password);

            if (!identityResult.Succeeded)
            {
                return MapError(identityResult);
            }

            await publishEndpoint.Publish(new UserRegistered
            {
                UserId = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                OccurredAtUtc = DateTimeOffset.UtcNow
            }, attemptCancellationToken);

            await dbContext.SaveChangesAsync(attemptCancellationToken);
            await transaction.CommitAsync(attemptCancellationToken);

            return new RegisterResponse
            {
                UserId = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName
            };
        }, cancellationToken);
    }

    private static Error MapError(IdentityResult identityResult)
    {
        var errors = identityResult.Errors.ToArray();
        var message = string.Join(" ", errors.Select(e => e.Description));

        var isDuplicate = errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");

        return isDuplicate
            ? new Error("Conflict.DuplicateEmail", message)
            : new Error("Validation.Register", message);
    }
}
