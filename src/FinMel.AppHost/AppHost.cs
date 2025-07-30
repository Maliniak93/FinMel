var builder = DistributedApplication.CreateBuilder(args);

var webApi = builder.AddProject<Projects.FinMel_Web>("finmel-web")
    .WithExternalHttpEndpoints();

builder.Build().Run();
