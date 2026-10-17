using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.TransferPlan.CheckTransferPlanRow;

public sealed class CheckTransferPlanRowHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid rowId, CheckTransferPlanRowRequest request, CancellationToken cancellationToken)
    {
        var row = await dbContext.TransferPlanRows.FirstOrDefaultAsync(r => r.Id == rowId, cancellationToken);
        if (row is null)
        {
            return TransferPlanErrors.RowNotFound(rowId);
        }

        row.CheckedAtUtc = request.Checked ? row.CheckedAtUtc ?? timeProvider.GetUtcNow() : null;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
