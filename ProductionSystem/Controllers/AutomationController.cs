using Microsoft.AspNetCore.Mvc;
using ProductionSystem.Services;

namespace ProductionSystem.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AutomationController : ControllerBase
    {
        private readonly IStageAutomationService _automationService;
        private readonly ILogger<AutomationController> _logger;

        // Добавляем блокировку для предотвращения одновременных вызовов
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static DateTime _lastProcessingTime = DateTime.MinValue;
        private const int MIN_INTERVAL_SECONDS = 5;

        public AutomationController(IStageAutomationService automationService, ILogger<AutomationController> logger)
        {
            _automationService = automationService;
            _logger = logger;
        }

        /// <summary>
        /// Ручной запуск процесса автоматического управления этапами
        /// </summary>
        [HttpPost("process")]
        public async Task<IActionResult> ProcessAutomaticStageExecution()
        {
            // Проверяем, не слишком ли часто вызывается метод
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastProcessingTime).TotalSeconds;

            if (elapsed < MIN_INTERVAL_SECONDS)
            {
                _logger.LogWarning($"Слишком частый вызов автоматики: {elapsed:F1} сек. с предыдущего запуска");
                return Ok(new
                {
                    success = true,
                    message = $"Автоматическая обработка уже выполнялась недавно. Пожалуйста, подождите {MIN_INTERVAL_SECONDS - (int)elapsed} сек."
                });
            }

            // Пытаемся получить блокировку
            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                _logger.LogWarning("Ручная автоматическая обработка уже выполняется");
                return Ok(new
                {
                    success = true,
                    message = "Автоматическая обработка уже выполняется. Пожалуйста, подождите."
                });
            }

            try
            {
                _lastProcessingTime = now;
                _logger.LogInformation("Запуск ручной автоматической обработки этапов");

                await _automationService.ProcessAutomaticStageExecution();

                return Ok(new { success = true, message = "Автоматическая обработка этапов выполнена успешно" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка во время ручной автоматической обработки");
                return BadRequest(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
            finally
            {
                // Освобождаем блокировку
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Получение прогнозируемого времени начала этапа
        /// </summary>
        [HttpGet("estimated-start/{stageId}")]
        public async Task<IActionResult> GetEstimatedStartTime(int stageId)
        {
            try
            {
                var estimatedStart = await _automationService.GetEstimatedStartTime(stageId);
                return Ok(new { success = true, estimatedStart });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка получения прогнозируемого времени для этапа {stageId}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Добавление этапа в очередь
        /// </summary>
        [HttpPost("queue/add/{stageId}")]
        public async Task<IActionResult> AddToQueue(int stageId)
        {
            try
            {
                var success = await _automationService.AddStageToQueue(stageId);
                return Ok(new { success, message = success ? "Этап добавлен в очередь" : "Не удалось добавить этап в очередь" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка добавления этапа {stageId} в очередь");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Удаление этапа из очереди
        /// </summary>
        [HttpPost("queue/remove/{stageId}")]
        public async Task<IActionResult> RemoveFromQueue(int stageId)
        {
            try
            {
                var success = await _automationService.RemoveStageFromQueue(stageId);
                return Ok(new { success, message = success ? "Этап удален из очереди" : "Не удалось удалить этап из очереди" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка удаления этапа {stageId} из очереди");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Освобождение станка для срочной задачи
        /// </summary>
        [HttpPost("release-machine")]
        public async Task<IActionResult> ReleaseMachine([FromBody] ReleaseMachineRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Reason))
                {
                    request.Reason = "Срочная задача";
                }

                var success = await _automationService.ReleaseMachine(request.MachineId, request.UrgentStageId, request.Reason);
                return Ok(new
                {
                    success,
                    message = success ? "Станок освобожден для срочной задачи" : "Не удалось освободить станок"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка освобождения станка {request.MachineId}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }

    public class ReleaseMachineRequest
    {
        public int MachineId { get; set; }
        public int UrgentStageId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}