using Application.Core.Dashboard.Queries.MainDashboard;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using WebApi.Abstractions;

namespace WebApi.Controllers;

public class DashboardController : ApiController
{
    public DashboardController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMainDashboard(CancellationToken cancellationToken)
    {
        var query = new GetMainDashboardQuery();

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }
}
