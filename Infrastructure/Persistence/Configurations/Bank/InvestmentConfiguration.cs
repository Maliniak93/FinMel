using Domain.Entities.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class InvestmentConfiguration : IEntityTypeConfiguration<Investment>
{
    public void Configure(EntityTypeBuilder<Investment> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Amount)
                .IsRequired();

        builder.Property(i => i.CurrencyId)
                .IsRequired();

        builder.Property(i => i.StartInvestment)
                .IsRequired();

        builder.Property(i => i.IsFinished)
                .IsRequired();

        builder.HasOne(i => i.Currency)
                .WithMany()
                .HasForeignKey(i => i.CurrencyId);
    }
}
