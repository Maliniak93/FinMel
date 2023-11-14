//using Application.Core.Bank.Commands.CreateBankAccount;
//using Domain.Common;
//using Domain.Enums;
//using Domain.Errors;
//using Domain.Repositories;
//using FluentAssertions;
//using Moq;

//namespace Application.Tests.BankAccountTests;

//public class CreateBankAccountCommandHandlerTests
//{
//    private readonly Mock<IBankAccountRepository> _contextMock;
//    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
//    private readonly CreateBankAccountCommand command = new CreateBankAccountCommand(
//            "123",
//            0,
//            "321",
//            "Tom",
//            "Tom Acc",
//            1,
//            0,
//            AccountType.Account);

//    public CreateBankAccountCommandHandlerTests()
//    {
//        _contextMock = new();
//        _unitOfWorkMock = new();
//    }

//    [Fact]
//    public async Task CreateBankAccountCommandHandler_UniqueAccountNumberAndValidCurrency_ReturnSuccess()
//    {
//        //Arrange
//        _contextMock.Setup(
//            x => x.IsAcountNumberUniqueAsync(
//                It.IsAny<string>()))
//            .ReturnsAsync(true);

//        _contextMock.Setup(
//            x => x.IsCurrencyExistAsync(
//                It.IsAny<int>()))
//            .ReturnsAsync(true);

//        var handler = new CreateBankAccountCommandHandler(_contextMock.Object, _unitOfWorkMock.Object);

//        //Act
//        Result result = await handler.Handle(command, default);

//        //Assert
//        result.IsSuccess.Should().BeTrue();
//    }

//    [Fact]
//    public async Task IsUniqueAccountNumber_DuplicateAccount_ReturnFail()
//    {
//        //Arrange
//        _contextMock.Setup(
//            x => x.IsAcountNumberUniqueAsync(
//                It.IsAny<string>()))
//            .ReturnsAsync(false);

//        var handler = new CreateBankAccountCommandHandler(_contextMock.Object, _unitOfWorkMock.Object);

//        //Act
//        Result result = await handler.Handle(command, default);

//        //Assert
//        result.IsFailure.Should().BeTrue();
//        result.Error.Should().Be(DomainErrors.BankAccount.BankAccountNumberIsNotUnique);
//    }

//    [Fact]
//    public async Task IsCurrencyExistAsync_CurrencyExist_ReturnSuccess()
//    {
//        //Arrange
//        _contextMock.Setup(
//            x => x.IsAcountNumberUniqueAsync(
//                It.IsAny<string>()))
//            .ReturnsAsync(true);

//        _contextMock.Setup(
//            x => x.IsCurrencyExistAsync(
//                It.IsAny<int>()))
//            .ReturnsAsync(false);

//        var handler = new CreateBankAccountCommandHandler(_contextMock.Object, _unitOfWorkMock.Object);

//        //Act
//        Result result = await handler.Handle(command, default);

//        //Assert
//        result.IsFailure.Should().BeTrue();
//        result.Error.Should().Be(DomainErrors.BankAccount.BankAccountCurrencyIsNotExist);
//    }
//}