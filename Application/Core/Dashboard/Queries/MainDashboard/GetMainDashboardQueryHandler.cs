using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Dashboard.Queries.MainDashboard;

public record GetMainDashboardQuery : IQuery<GetMainDashboardQueryDto>;
internal class GetMainDashboardQueryHandler : IQueryHandler<GetMainDashboardQuery, GetMainDashboardQueryDto>
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IUser _user;

    public GetMainDashboardQueryHandler(IDashboardRepository dashboardRepository,
        IUser user)
    {
        _dashboardRepository = dashboardRepository;
        _user = user;
    }
    public async Task<Result<GetMainDashboardQueryDto>> Handle(GetMainDashboardQuery request, CancellationToken cancellationToken)
    {
        // ReSharper disable once AssignNullToNotNullAttribute
        var userDashboard = await _dashboardRepository.GetUserDashboard(_user.Id);

        if (userDashboard is null)
        {
            return Result.Failure<GetMainDashboardQueryDto>(DomainErrors.Dashboard.MainDashboardNotExist);
        }

        var mainDashboardQueryDto = new GetMainDashboardQueryDto();

        return Result.Success(mainDashboardQueryDto);
    }
}
