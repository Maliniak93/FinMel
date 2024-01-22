using Domain.Common;
// ReSharper disable All

namespace Domain.Entities.Common;
public class Currency : BaseEntity
{
    public string CurrencyName { get; set; }
    public string CurrencyTag { get; set; }
}
