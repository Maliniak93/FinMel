var builder = WebApplication.CreateBuilder(args);

builder.AddRabbitMqMessaging<WebApplicationBuilder, ReportingDbContext>(
    configureConsumers: x =>
    {
        x.AddConsumer<DailyPricesSyncedConsumer>(typeof(DailyPricesSyncedConsumerDefinition));
        x.AddConsumer<AssetPositionChangedConsumer>(typeof(AssetPositionChangedConsumerDefinition));
        x.AddConsumer<PortfolioArchivedConsumer>(typeof(PortfolioArchivedConsumerDefinition));
        x.AddConsumer<PortfolioHistoryRebuildConsumer>(typeof(PortfolioHistoryRebuildConsumerDefinition));
    });

var app = builder.Build();

app.MapGetSnapshotEndpoint();

app.Run();
