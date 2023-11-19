using Application.Core.Dashboard.Commands.CreateMainDashboard;
using Application.Core.Dashboard.Commands.UpdateMainDashboard;
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

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromQuery] CreateMainDashboardCommand request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromQuery] UpdateMainDashboardCommand request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
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
