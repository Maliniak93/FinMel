using Application.Common;
using Application.Core.Bank.Commands.CreateByFileStatement;
using Application.Core.Bank.Commands.UpdateTransactionCode;
using Application.Core.Bank.Queries.GetStatementById;
using Application.Core.Bank.Queries.GetStatements;
using Application.Core.Bank.Queries.GetStatementTransaction;
using Application.Core.Bank.Queries.GetTransactionCodes;
using Application.Extensions;
using Domain.Specifications.StatementSpecification;
using Domain.Specifications.TransactionSpecification;
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
    public async Task<IActionResult> GetStatementById(int id, CancellationToken cancellationToken)
    {
        var query = new GetStatementByIdQuery(id);

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatements([FromQuery] BankStatementsSpecificationParameters query, CancellationToken cancellationToken)
    {
        var request = new GetStatementsQuery(query);

        var response = await _mediator.Send(request, cancellationToken);

        Response.AddPaginationheader(new PaginationHeader(response.Value.Page,
            response.Value.PageSize,
            response.Value.TotalCount,
            response.Value.TotalPages));

        return response.IsSuccess ? Ok(response.Value.Items) : HandleFailureNoContent(response);
    }

    [HttpGet("{id}/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatementTransactions(int id, [FromQuery] BankStatementsTransactionsSpecificationParameters request, CancellationToken cancellationToken)
    {
        var query = new GetStatementTransactionQuery(id, request);

        var response = await _mediator.Send(query, cancellationToken);

        Response.AddPaginationheader(new PaginationHeader(response.Value.Page,
            response.Value.PageSize,
            response.Value.TotalCount,
            response.Value.TotalPages));

        return response.IsSuccess ? Ok(response.Value.Items) : HandleFailureNoContent(response);
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
