using Domain.Entities.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class StatementTransactionConfiguration : IEntityTypeConfiguration<StatementTransaction>
{
    public void Configure(EntityTypeBuilder<StatementTransaction> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BankStatementId)
                .IsRequired();

        builder.HasOne(x => x.BankStatement)
                .WithMany(x => x.StatementTransactions)
                .HasForeignKey(x => x.BankStatementId);

        builder.Property(x => x.ExecutionDate)
                .IsRequired();

        builder.Property(x => x.TransactionDate)
                .IsRequired();

        builder.Property(x => x.Value)
                .IsRequired();

        builder.Property(x => x.AccountValue)
                .IsRequired();

        builder.Property(x => x.RealValue)
                .IsRequired();

        builder.HasOne(x => x.TransactionCode)
            .WithMany()
            .HasForeignKey(x => x.TransactionCodeId);
    }
}
