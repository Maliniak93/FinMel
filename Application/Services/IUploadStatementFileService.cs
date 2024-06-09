using Domain.Common;
using Microsoft.AspNetCore.Http;

namespace Application.Services;
public interface IUploadStatementFileService
{
    Task<Result> UploadFile(string filePath, IFormFile file);
}
