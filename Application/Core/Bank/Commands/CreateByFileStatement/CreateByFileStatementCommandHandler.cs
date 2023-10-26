using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using Application.Services;
using AutoMapper;
using Domain;
using Domain.Common;
using Domain.Entities.Bank;
using Domain.Entities.Common;
using Domain.Entities.Files;
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
    private readonly ITransactionCodeRepository _transactionCodeRepository;
    private readonly IUser _user;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;
    private readonly IUploadStatementFileService _uploadStatementFileService;
    private readonly IUnitOfWork _unitOfWork;

    public CreateByFileStatementCommandHandler(IBankStatementRepository statementRepository,
        IStatementFileRepository statementFileRepository,
        IBankAccountRepository bankAccountRepository,
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
        if (!await _statementFileRepository.IsStatementFileUniqueAsync(request.FileName, _user.Id))
            return Result.Failure(DomainErrors.StatementFile.StatementFileIsNotUnique(request.FileName));

        _unitOfWork.BeginTransaction();

        var path = _configuration["BankStatement:Path"];
        if (string.IsNullOrEmpty(path))
        {
            return Result.Failure(DomainErrors.StatementFile.StatementFilePathFromConfigurationIsNullOrEmpty);
        }
        var statementFile = new StatementFile(path, request.FileName);

        var xmlSantanderStatement = new XmlSantanderStatement();

        using (var stream = request.File.OpenReadStream())
        {
            var xmlSantanderStatementDeserialization = xmlSantanderStatement.DeserializeFile(stream);
            if (xmlSantanderStatementDeserialization.IsFailure)
                return xmlSantanderStatementDeserialization;
        }

        var bankStatementDto = _mapper.Map<CreateByFileStatementCommandDto>(xmlSantanderStatement.Statement);

        var bankAccount = await _bankAccountRepository.GetByAccountNumberAsync(bankStatementDto.BankAccountNumber,
            _user.Id,
            false);

        if (bankAccount is null)
        {
            return Result.Failure(DomainErrors.BankAccount.BankAccountWithAccountNumberIsNotExist(bankStatementDto.BankAccountNumber));
        }

        var bankStatement = new BankStatement(
            statementFile,
            bankStatementDto.StatementNumber,
            bankStatementDto.StatementFrom,
            bankStatementDto.BeginValue,
            bankStatementDto.StatementTo,
            bankStatementDto.EndValue,
            bankAccount.Id);

        var codes = await _transactionCodeRepository.GetAllAsync(_user.Id);

        foreach (var statementTransactionDto in bankStatementDto.StatementTransactions)
        {
            var statementTransaction = _mapper.Map<StatementTransaction>(statementTransactionDto);

            if (!codes.Select(x => x.Code).Contains(statementTransactionDto.TransactionCode))
            {
                var newCode = new TransactionCode(statementTransactionDto.TransactionCode, statementTransactionDto.DescriptionBase);
                codes.Add(newCode);

                statementTransaction.AddTransactionCode(newCode);
            }
            else
            {
                var code = codes.Where(x => x.Code == statementTransactionDto.TransactionCode).SingleOrDefault();
                statementTransaction.AddTransactionCode(code);
            }
            bankStatement.AddStatementTransaction(statementTransaction);
        }

        bankAccount.AddBankHistory(new History(bankStatement.StatementTo, bankStatement.EndValue, bankAccount.Id));

        _statementRepository.Insert(bankStatement);

        var uploadResult = await _uploadStatementFileService.UploadFile(statementFile.FilePath, request.File);
        if (uploadResult.IsFailure)
            return uploadResult;

        _unitOfWork.CommitTransaction();
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
