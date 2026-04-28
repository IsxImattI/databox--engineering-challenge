using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Pipeline;
using DataboxConnector.Host;
using DataboxConnector.Sinks.Databox.DependencyInjection;
using DataboxConnector.Sources.GitHub.DependencyInjection;
using DataboxConnector.Sources.Spotify.DependencyInjection;
using Serilog;

// Bootstrap logger captures startup errors before the host's logger is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting DataboxConnector host");

    var builder = Host.CreateApplicationBuilder(args);

    // Replace default logging with Serilog reading from configuration.
    builder.Services.AddSerilog((services, config) =>
    {
        config
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .WriteTo.Console(
                outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/databox-connector-.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    });

    // === Core pipeline ===
    builder.Services.AddSingleton<IIngestionPipeline, IngestionPipeline>();

    // === Sink (Databox) ===
    builder.Services.AddDataboxSink(builder.Configuration);

    // === Sources ===
    builder.Services.AddGitHubSources(builder.Configuration);
    builder.Services.AddSpotifySources(builder.Configuration);

    // === Worker (background service) ===
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}