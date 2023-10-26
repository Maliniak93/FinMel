using Application.Core.Bank.Commands.CreateByFileStatement;
using Application.Core.Bank.Commands.UpdateTransactionCode;
using Application.Core.Bank.Queries.GetStatementById;
using Application.Core.Bank.Queries.GetStatements;
using Application.Core.Bank.Queries.GetStatementTransaction;
using Application.Core.Bank.Queries.GetTransactionCodes;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using WebApi.Abstractions;
using WebApi.Contracts.BankStatements;

namespace WebApi.Controllers;

public class StatementController : ApiController
{
    public StatementController(IMediator _mediator) : base(_mediator)
    {
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateByFile([FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new CreateByFileStatementCommand(file.FileName, file), cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatementById(int id, [FromBody] GetStatementByIdRequest request, CancellationToken cancellationToken)
    {
        var query = new GetStatementByIdQuery(id, request.PageNumber, request.PageSize);

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatements([FromBody] GetStatementsQuery query, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet("transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatementTransactions([FromBody] GetStatementTransactionQuery query, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet("transactions/codes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionCodes([FromBody] GetTransactionCodesQuery query, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpPut("transactions/codes/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatementTransactions(int id, [FromBody] UpdateTransactionCodeRequest request, CancellationToken cancellationToken)
    {
        var query = new UpdateTransactionCodeCommand(id, request.Code, request.Description, request.IsExpensionIncome);

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }
}
