using LedSupBot.Application;
using LedSupBot.Infrastructure;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddFlatEnvironmentVariableBridge();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<FoundationHeartbeatWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LedSupBot.Workers");
logger.LogInformation(
    "Workers host starting (Phase 2 foundation). WhatsApp queue processing is NOT implemented.");

host.Run();
