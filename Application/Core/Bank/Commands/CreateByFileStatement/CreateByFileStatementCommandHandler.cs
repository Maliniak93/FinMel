using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using Application.Services;
using AutoMapper;
using Domain.Common;
using Domain.Entities.Bank;
using Domain.Entities.Common;
using Domain.Entities.File;
using Domain.Enums;
using Domain.Errors;
using Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Application.Core.Bank.Commands.CreateByFileStatement;

public record CreateByFileStatementCommand(string FileName,
    IFormFile File
    ) : ICommand;
internal class CreateByFileStatementCommandHandler : ICommandHandler<CreateByFileStatementCommand>
{
    private readonly IBankStatementRepository _statementRepository;
    private readonly IStatementFileRepository _statementFileRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IInvestmentRepository _investmentRepository;
    private readonly ITransactionCodeRepository _transactionCodeRepository;
    private readonly IUser _user;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;
    private readonly IUploadStatementFileService _uploadStatementFileService;
    private readonly IUnitOfWork _unitOfWork;

    public CreateByFileStatementCommandHandler(IBankStatementRepository statementRepository,
        IStatementFileRepository statementFileRepository,
        IBankAccountRepository bankAccountRepository,
        IInvestmentRepository investmentRepository,
        ITransactionCodeRepository transactionCodeRepository,
        IUser user,
        IMapper mapper,
        IConfiguration configuration,
        IUploadStatementFileService uploadStatementFileService,
        IUnitOfWork unitOfWork)
    {
        _statementRepository = statementRepository;
        _statementFileRepository = statementFileRepository;
        _bankAccountRepository = bankAccountRepository;
        _investmentRepository = investmentRepository;
        _transactionCodeRepository = transactionCodeRepository;
        _user = user;
        _mapper = mapper;
        _configuration = configuration;
        _uploadStatementFileService = uploadStatementFileService;
        _unitOfWork = unitOfWork;

    }
    public async Task<Result> Handle(CreateByFileStatementCommand request,
        CancellationToken cancellationToken)
    {
        if (_user.Id != null && !await _statementFileRepository.IsStatementFileUniqueAsync(request.FileName, _user.Id))
            return Result.Failure(DomainErrors.StatementFile.StatementFileIsNotUnique(request.FileName));

        _unitOfWork.BeginTransaction();

        var path = _configuration["BankStatement:Path"];
        if (string.IsNullOrEmpty(path))
        {
            return Result.Failure(DomainErrors.StatementFile.StatementFilePathFromConfigurationIsNullOrEmpty);
        }
        var statementFile = new StatementFile(path, request.FileName);

        var xmlSantanderStatement = new XmlSantanderStatement();

        var xmlSantanderStatementResult = xmlSantanderStatement.XmlSantanderStatementCreate(request.File);

        if (xmlSantanderStatementResult.IsFailure)
        {
            return xmlSantanderStatementResult;
        }

        var bankStatementDto = _mapper.Map<CreateByFileStatementCommandDto>(xmlSantanderStatement.Statement);

        var bankAccount = await _bankAccountRepository.GetByAccountNumberAsync(bankStatementDto.BankAccountNumber,
            _user.Id!,
            false);

        var bankStatement = new BankStatement(
            statementFile,
            bankStatementDto.StatementNumber,
            bankStatementDto.StatementFrom,
            bankStatementDto.BeginValue,
            bankStatementDto.StatementTo,
            bankStatementDto.EndValue,
            bankAccount.Id);

        var codes = await _transactionCodeRepository.GetAllAsync(_user.Id);

        var statementTransactionList = _mapper.Map<List<StatementTransaction>>(bankStatementDto.StatementTransactions, opt => opt.Items["codes"] = codes);

        bankStatement.AddStatementTransactions(statementTransactionList);

        bankAccount.AddBankHistory(new History(bankStatement.StatementTo, bankStatement.EndValue, bankAccount.Id));

        var investments = bankStatement.StatementTransactions.Where(x => x.TransactionCode.Type == TransactionTypes.Investments).ToList();

        if (investments.Any())
        {
            foreach (var investment in investments)
            {
                var newInwestment = new Investment(investment.Value, bankAccount.CurrencyId, investment.TransactionDate);
                _investmentRepository.Insert(newInwestment);
            }
        }

        _statementRepository.Insert(bankStatement);

        var uploadResult = await _uploadStatementFileService.UploadFile(statementFile.FilePath, request.File);
        if (uploadResult.IsFailure)
            return uploadResult;

        _unitOfWork.CommitTransaction();
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
