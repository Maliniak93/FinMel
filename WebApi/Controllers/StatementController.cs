using Application.Common;
using Application.Core.Bank.Commands.CreateByFileStatement;
using Application.Core.Bank.Commands.UpdateTransactionCode;
using Application.Core.Bank.Commands.UpdateTransactionDashboardDate;
using Application.Core.Bank.Commands.UpdateTransactionType;
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
    public StatementController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateByFile([FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new CreateByFileStatementCommand(file.FileName, file), cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatementById(int id, CancellationToken cancellationToken)
    {
        var query = new GetStatementByIdQuery(id);

        var response = await Mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatements([FromQuery] BankStatementsSpecificationParameters query, CancellationToken cancellationToken)
    {
        var request = new GetStatementsQuery(query);

        var response = await Mediator.Send(request, cancellationToken);

        if (response.IsSuccess)
        {
            Response.AddPaginationheader(new PaginationHeader(response.Value.Page,
                response.Value.PageSize,
                response.Value.TotalCount,
                response.Value.TotalPages));
        }

        return response.IsSuccess ? Ok(response.Value.Items) : HandleFailureNoContent(response);
    }

    [HttpGet("{id}/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatementTransactions(int id, [FromQuery] BankStatementsTransactionsSpecificationParameters request, CancellationToken cancellationToken)
    {
        var query = new GetStatementTransactionQuery(id, request);

        var response = await Mediator.Send(query, cancellationToken);

        if (response.IsSuccess)
        {
            Response.AddPaginationheader(new PaginationHeader(response.Value.Page,
                response.Value.PageSize,
                response.Value.TotalCount,
                response.Value.TotalPages));
        }

        return response.IsSuccess ? Ok(response.Value.Items) : HandleFailureNoContent(response);
    }

    [HttpGet("transactions/codes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionCodes(CancellationToken cancellationToken)
    {
        var query = new GetTransactionCodesQuery();

        var response = await Mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpPut("transactions/date/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTransactionDashboardDate(int id, [FromBody] UpdateTransactionDashboardDateRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateTransactionDashboardDateCommand(id, request.NewDate);

        var response = await Mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

    [HttpPut("transactions/codes/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTransactionCode(int id, [FromBody] UpdateTransactionCodeRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateTransactionCodeCommand(id, request.Code, request.Description, request.Type);

        var response = await Mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }
    [HttpPut("transactions/type/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTransactionType(int id, [FromBody] UpdateTransactionTypeRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateTransactionTypeCommand(id, request.Type);

        var response = await Mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

}
