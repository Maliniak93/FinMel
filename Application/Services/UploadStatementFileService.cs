using Domain.Common;
using Domain.Errors;
using Microsoft.AspNetCore.Http;

namespace Application.Services;
public class UploadStatementFileService : IUploadStatementFileService
{
    public async Task<Result> UploadFile(string filePath, IFormFile file)
    {
        if (!File.Exists(filePath))
        {
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            return Result.Success();
        }
        else
        {
            return Result.Failure(DomainErrors.StatementFile.StatementFileIsNotUnique(file.FileName));
        }
    }
}
