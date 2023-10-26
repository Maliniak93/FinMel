namespace Domain;

public interface IStatementFileRepository
{
    Task<bool> IsStatementFileUniqueAsync(string fileName, string userId);
}