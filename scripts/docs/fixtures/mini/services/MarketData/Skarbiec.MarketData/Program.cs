var builder = WebApplication.CreateBuilder(args);

builder.AddRabbitMqMessaging<WebApplicationBuilder, MarketDataDbContext>(
    configureConsumers: x =>
    {
        x.AddConsumer<AssetPositionChangedConsumer>(typeof(AssetPositionChangedConsumerDefinition));
    });

var app = builder.Build();

app.MapGetInstrumentEndpoint();
app.MapGetInstrumentsBatchEndpoint();
app.MapGetLatestPricesEndpoint();

app.Run();
