using ProductionSystem.Services;

namespace ProductionSystem.Services
{
    public class StageAutomationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StageAutomationBackgroundService> _logger;
        // Увеличиваем интервал до 60 секунд для снижения нагрузки
        private readonly TimeSpan _period = TimeSpan.FromSeconds(60);

        // Добавляем счетчик для контроля выполнения
        private int _executionCount = 0;
        private const int MAX_EXECUTIONS = 1000; // Максимальное количество выполнений для автоперезапуска

        public StageAutomationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<StageAutomationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StageAutomationBackgroundService запущен");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Проверяем счетчик выполнений для предотвращения утечек памяти
                    if (_executionCount >= MAX_EXECUTIONS)
                    {
                        _logger.LogWarning($"Достигнуто максимальное количество выполнений ({MAX_EXECUTIONS}). Служба будет перезапущена.");
                        break;
                    }

                    _executionCount++;

                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var stageAutomationService = scope.ServiceProvider
                            .GetRequiredService<IStageAutomationService>();

                        await stageAutomationService.ProcessAutomaticStageExecution();

                        _logger.LogDebug($"Автоматическая обработка этапов выполнена успешно (итерация {_executionCount})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка в автоматической обработке этапов");
                    }

                    try
                    {
                        // Рандомизируем интервал проверки, чтобы избежать синхронизации
                        var randomDelay = (int)(_period.TotalMilliseconds * (0.9 + new Random().NextDouble() * 0.2));
                        await Task.Delay(randomDelay, stoppingToken);
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
                _logger.LogInformation("StageAutomationBackgroundService останавливается");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Непредвиденная ошибка в StageAutomationBackgroundService");
            }
            finally
            {
                _logger.LogInformation($"StageAutomationBackgroundService остановлен после {_executionCount} выполнений");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StageAutomationBackgroundService останавливается");
            await base.StopAsync(stoppingToken);
        }
    }
}