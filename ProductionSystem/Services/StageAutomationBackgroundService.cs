using ProductionSystem.Services;

namespace ProductionSystem.Services
{
    public class StageAutomationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StageAutomationBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromSeconds(30); // Проверяем каждые 30 секунд

        public StageAutomationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<StageAutomationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StageAutomationBackgroundService started");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var stageAutomationService = scope.ServiceProvider
                            .GetRequiredService<IStageAutomationService>();

                        await stageAutomationService.ProcessAutomaticStageExecution();

                        _logger.LogDebug("Automatic stage execution processing completed successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in automatic stage execution processing");
                        // Продолжаем работу даже при ошибке
                    }

                    try
                    {
                        await Task.Delay(_period, stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // Игнорируем отмену задержки при остановке сервиса
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("StageAutomationBackgroundService stopping");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in StageAutomationBackgroundService");
            }
            finally
            {
                _logger.LogInformation("StageAutomationBackgroundService stopped");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StageAutomationBackgroundService is stopping");
            await base.StopAsync(stoppingToken);
        }
    }
}