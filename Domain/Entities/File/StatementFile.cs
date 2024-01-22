using Domain.Common;

namespace Domain.Entities.File;
public class StatementFile : BaseAuditableEntity
{
    // ReSharper disable once NotNullOrRequiredMemberIsNotInitialized
    public StatementFile()
    {

    }
    public StatementFile(string path, string fileName)
    {
        FilePath = Path.Combine(path, fileName);
        FileName = fileName;
    }
    public string FilePath { get; private set; }
    public string FileName { get; private set; }

}
