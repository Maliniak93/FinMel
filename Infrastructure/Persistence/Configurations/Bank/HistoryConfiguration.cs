using Domain.Entities.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class HistoryConfiguration : IEntityTypeConfiguration<History>
{
    public void Configure(EntityTypeBuilder<History> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.BankAccount)
            .WithMany(x => x.History)
            .HasForeignKey(x => x.BankAccountId);
    }
}
