using Microsoft.AspNetCore.Mvc;
using ProductionSystem.Services;

namespace ProductionSystem.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SystemController : ControllerBase
    {
        private readonly IStageAutomationService _automationService;
        private readonly ILogger<SystemController> _logger;

        public SystemController(IStageAutomationService automationService, ILogger<SystemController> logger)
        {
            _automationService = automationService;
            _logger = logger;
        }

        /// <summary>
        /// Получение статуса системы
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetSystemStatus()
        {
            try
            {
                return Ok(new
                {
                    status = "online",
                    timestamp = DateTime.UtcNow,
                    version = "1.0.0"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system status");
                return Ok(new
                {
                    status = "error",
                    timestamp = DateTime.UtcNow,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Получение статистики системы
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetSystemStats()
        {
            try
            {
                // Здесь можно добавить получение различной статистики
                return Ok(new
                {
                    success = true,
                    uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system stats");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}