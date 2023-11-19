using Domain.Entities.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AccountNumber).IsRequired();

        builder.HasIndex(x => x.AccountNumber).IsUnique();

        builder.HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId);

        builder.HasMany(x => x.BankStatements)
            .WithOne(x => x.BankAccount)
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Cascade);


        builder.HasMany(x => x.History)
            .WithOne(x => x.BankAccount)
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
