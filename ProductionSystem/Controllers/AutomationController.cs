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
            try
            {
                await _automationService.ProcessAutomaticStageExecution();
                return Ok(new { success = true, message = "Автоматическая обработка этапов выполнена успешно" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual automation processing");
                return BadRequest(new { success = false, message = ex.Message });
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
                _logger.LogError(ex, $"Error getting estimated start time for stage {stageId}");
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
                _logger.LogError(ex, $"Error adding stage {stageId} to queue");
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
                _logger.LogError(ex, $"Error removing stage {stageId} from queue");
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
                var success = await _automationService.ReleaseMachine(request.MachineId, request.UrgentStageId, request.Reason);
                return Ok(new
                {
                    success,
                    message = success ? "Станок освобожден для срочной задачи" : "Не удалось освободить станок"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error releasing machine {request.MachineId}");
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