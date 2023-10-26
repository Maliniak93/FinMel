using Application.Core.Bank.Commands.CreateBankAccount;
using Application.Core.Bank.Commands.DeleteBankAccount;
using Application.Core.Bank.Commands.UpdateBankAccount;
using Application.Core.Bank.Queries.GetBankAccountById;
using Application.Core.Bank.Queries.GetBankAccounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using WebApi.Abstractions;
using WebApi.Contracts.BankAccounts;

namespace WebApi.Controllers;


public class BankController : ApiController
{

    public BankController(IMediator _mediator) : base(_mediator)
    {
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateBankAccountCommand(
            request.AccountNumber,
            request.Balance,
            request.ClientNumber,
            request.ClientName,
            request.AccountName,
            request.CurrencyId,
            request.IntrestRate,
            request.AccountType
            );

        var response = await _mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailure(response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBankAccountById(int id, CancellationToken cancellationToken)
    {
        var query = new GetBankAccountByIdQuery(id);

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBankAccounts(CancellationToken cancellationToken)
    {
        var query = new GetBankAccountsQuery();

        var response = await _mediator.Send(query, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBankAccount(int id, [FromBody] UpdateBankAccountRequest request, CancellationToken cancellationToken)
    {
        var updateBankAccountCommand = new UpdateBankAccountCommand(id,
            request.ClientName,
            request.AccountName,
            request.IntrestRate,
            request.Balance);

        var response = await _mediator.Send(updateBankAccountCommand, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : HandleFailureNoContent(response);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBankAccount(int id, CancellationToken cancellationToken)
    {
        var deleteBankAccountCommand = new DeleteBankAccountCommand(id);

        var response = await _mediator.Send(deleteBankAccountCommand, cancellationToken);

        return response.IsSuccess ? Ok() : HandleFailureNoContent(response);
    }
}
