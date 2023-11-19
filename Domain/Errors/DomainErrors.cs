using Domain.Common;

namespace Domain.Errors;
public static class DomainErrors
{
    public static class BankAccount
    {
        public static Error BankAccountNumberIsNotUnique => new(
                "BankAccount.AccountNumberExist",
                $"The Bank Account number is not unique");

        public static Error BankAccountCurrencyIsNotExist => new(
                "BankAccount.CurrencyNotFound",
                $"The Currency is not exist in db");

        public static Func<int, Error> BankAccountWithIdIsNotExist = id => new(
                "BankAccount.NotFound",
                $"The BankAccount with Id {id} was not found");

        public static Error BankAccountsNotExist => new(
                "BankAccounts.NotFound",
                "The BankAccounts was not found");

        public static Func<string, Error> BankAccountWithAccountNumberIsNotExist = accountNumber => new(
                "BankAccount.NotFound",
                $"The BankAccount with account number {accountNumber} was not found");

        public static Error MainDashboardError => new(
                "BankAccount.MainDashboardError",
                $"Main Dashboard is null");

        //public static Func<int, Error> CountPersonalWealthHistoryIsNull = id => new(
        //        "BankAccount.CountPersonalWealth",
        //        $"BankAccount with id {id} History is null");
    }

    public static class StatementFile
    {
        public static Func<string, Error> StatementFileIsNotUnique = name => new(
                "UploadFile.AlreadyExist",
                $"The BankStatement with Name {name} already exist");

        public static Error StatementFilePathFromConfigurationIsNullOrEmpty => new(
                "UploadFile.PathIsNull",
                "Path for statement file is null or empty");

        public static Func<Exception, Error> ErrorWhileDeserializationStatementFile = ex => new(
                "UploadFile.DeserializationError",
                ex.Message);

    }

    public static class BankStatement
    {
        public static Func<int, Error> BankStatementWithIdIsNotExist = id => new(
                "BankStatement.NotFound",
                $"The BankStatement with Id {id} was not found.");
    }

    public static class TransactionCode
    {
        public static Func<int, Error> TransactionCodeWithIdIsNotExist = id => new(
                "BankStatement.NotFound",
                $"The Transaction Code with Id {id} was not found");
    }

    public static class Dashboard
    {
        public static Error MainDashboardNotExist => new(
                "MainDashboard.NotFound",
                $"Main Dashboard was not found");

        public static Func<DateTime, DateTime, Error> MainDashboardForTimeRangeAlreadyExist = (firstDayOfMonth, lastDayOfMonth) => new(
            "MainDashboard.Exist",
            $"Main Dashboard with {firstDayOfMonth.ToShortDateString()} - {lastDayOfMonth.ToShortDateString()} time range exist");

        public static Func<int, Error> MainDashboardWithIdNotExist = id => new(
            "MainDashboard.NotFount",
            $"Main Dashboard with id {id} was not found");
    }

    public static class Authentication
    {
        public static Error InvalidEmailOrPassword => new Error(
            "Authentication.InvalidEmailOrPassword",
            "The specified email or password are incorrect.");

        public static Error RegisterError(string code, string errorMessage)
        {
            return new Error(code, errorMessage);
        }
    }
}
