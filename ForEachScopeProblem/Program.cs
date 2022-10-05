using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logConfig =>
    {
        logConfig.ClearProviders();

        logConfig.AddOpenTelemetry(options => 
        { 
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.IncludeScopes = true;

            options.AddFilteredAsyncConsoleExporter();
        });
    })
    .ConfigureServices(service =>
    {
        service.AddHostedService<HostedService>();
    })
    .Build();

host.Run();

public static class Extenstions
{
    public static OpenTelemetryLoggerOptions AddFilteredAsyncConsoleExporter(this OpenTelemetryLoggerOptions options)
    {
        return options.AddProcessor(
            new FilterProcessor(new[] {
                new BatchLogRecordExportProcessor(
                    new ConsoleLogRecordExporter(new ConsoleExporterOptions()),
                    scheduledDelayMilliseconds: 100
                ) }));
    }
}

public class FilterProcessor : CompositeProcessor<LogRecord>
{
    public FilterProcessor(IEnumerable<BaseProcessor<LogRecord>> processors) : base(processors)
    {
    }

    public override void OnEnd(LogRecord data)
    {
        _filter.Value = false;
        //if the log record has been previously used in a call to batchlogrecordexportprocessor
        //then the buffer will not be null and this ForEachScope will not run
        //wich prevents the filter from being performed.
        data.ForEachScope(Filter, data);

        if (!_filter.Value)
            base.OnEnd(data);

        _filter.Value = false;
        //this will always work as the values will be buffered in the call to OnEnd
        data.ForEachScope(Filter, data);
    }

    private static void Filter(LogRecordScope scope, LogRecord data)
    {
        if (_filter.Value) return;

        foreach (KeyValuePair<string, object> scopeItem in scope)
        {
            if (scopeItem.Key == "keep" && (int)scopeItem.Value == 0)
            {
                _filter.Value = true;
                return;
            }
        }
    }

    private static ThreadLocal<bool> _filter = new ThreadLocal<bool>(false);
}

public class HostedService : BackgroundService
{
    private readonly ILogger<HostedService> _logger;

    public HostedService(ILogger<HostedService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int loop=0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _logger.BeginScope("Scope {keep}", loop % 2);

            _logger.LogInformation("Loop {loop}", loop++);

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}