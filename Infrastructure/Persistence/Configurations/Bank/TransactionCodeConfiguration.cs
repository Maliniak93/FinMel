using Domain.Entities.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class TransactionCodeConfiguration : IEntityTypeConfiguration<TransactionCode>
{
    public void Configure(EntityTypeBuilder<TransactionCode> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired();

        builder.HasIndex(x => x.Code);
    }
}
