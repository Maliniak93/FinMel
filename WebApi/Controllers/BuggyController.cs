using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Abstractions;

namespace WebApi.Controllers;

public class BuggyController : ApiController
{
    public BuggyController(IMediator _mediator) : base(_mediator)
    {
    }

    [HttpGet("auth")]
    public ActionResult<string> GetSecret()
    {
        return "secret";
    }

    [AllowAnonymous]
    [HttpGet("not-found")]
    public IActionResult GetNotFound()
    {
        return NotFound();
    }

    [AllowAnonymous]
    [HttpGet("server-error")]
    public IActionResult GetServerError()
    {
        string nullThing = null;
        nullThing.ToString();

        return Ok();
    }

    [AllowAnonymous]
    [HttpGet("bad-request")]
    public IActionResult GetBadRequest()
    {
        return BadRequest("This is bad request");
    }


}
