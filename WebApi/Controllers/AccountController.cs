using Application.Core.Authentication.Commands.Login;
using Application.Core.Authentication.Commands.Register;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Abstractions;

namespace WebApi.Controllers;
[AllowAnonymous]
public class AccountController : ApiController
{

    public AccountController(IMediator _mediator) : base(_mediator)
    {

    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand registerCommand, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(registerCommand, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailure(response);
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand loginCommand, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(loginCommand, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureUnauthorized(response);
    }
}
