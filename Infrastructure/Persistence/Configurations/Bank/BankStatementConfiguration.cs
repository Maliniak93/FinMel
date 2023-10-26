using Domain.Entities.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
{
    public void Configure(EntityTypeBuilder<BankStatement> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StatementNumber)
                .IsRequired()
                .HasMaxLength(50);

        builder.Property(x => x.StatementFrom)
                .IsRequired();

        builder.Property(x => x.BeginValue)
                .IsRequired();

        builder.Property(x => x.StatementTo)
                .IsRequired();

        builder.Property(x => x.EndValue)
                .IsRequired();

        builder.HasOne(x => x.BankAccount)
            .WithMany(x => x.BankStatements)
            .HasForeignKey(x => x.BankAccountId);

        builder.Property(x => x.StatementFileId)
                .IsRequired();


    }
}
