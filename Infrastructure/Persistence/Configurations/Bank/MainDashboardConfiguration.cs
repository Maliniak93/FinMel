using Domain.Entities.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Bank;
public class MainDashboardConfiguration : IEntityTypeConfiguration<MainDashboard>
{
    public void Configure(EntityTypeBuilder<MainDashboard> builder)
    {
        builder.Property(x => x.From)
                .IsRequired();

        builder.Property(x => x.To)
                .IsRequired();
    }
}
