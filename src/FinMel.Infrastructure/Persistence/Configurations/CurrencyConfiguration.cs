using FinMel.Domain.Entities.Currency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMel.Infrastructure.Persistence.Configurations;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(3);
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(c => c.Symbol)
            .IsRequired()
            .HasMaxLength(8);
    }
}
