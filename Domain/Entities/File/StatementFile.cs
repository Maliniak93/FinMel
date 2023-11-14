using Domain.Common;

namespace Domain.Entities.Files;
public class StatementFile : BaseAuditableEntity
{
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
