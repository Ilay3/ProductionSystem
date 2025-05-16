using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;
using ProductionSystem.Services;

namespace ProductionSystem.Controllers
{
    public class GanttController : Controller
    {
        private readonly ProductionContext _context;
        private readonly IStageAutomationService _stageAutomationService;

        public GanttController(ProductionContext context, IStageAutomationService stageAutomationService)
        {
            _context = context;
            _stageAutomationService = stageAutomationService;
        }

        public async Task<IActionResult> Index(int? orderId, int? machineId)
        {
            ViewBag.Orders = await _context.ProductionOrders
                .Select(po => new { po.Id, po.Number, po.Detail.Name })
                .ToListAsync();

            ViewBag.Machines = await _context.Machines
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            ViewBag.SelectedOrderId = orderId;
            ViewBag.SelectedMachineId = machineId;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGanttData(int? orderId, int? machineId, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var query = _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Machine)
                    .Include(rs => rs.Operation)
                    .Include(rs => rs.StageExecutions)
                    .Where(rs => rs.Status != "Pending");

                if (orderId.HasValue)
                {
                    query = query.Where(rs => rs.SubBatch.ProductionOrderId == orderId.Value);
                }

                if (machineId.HasValue)
                {
                    query = query.Where(rs => rs.MachineId == machineId.Value);
                }

                var stages = await query.OrderBy(rs => rs.SubBatch.ProductionOrder.CreatedAt)
                    .ThenBy(rs => rs.SubBatchId)
                    .ThenBy(rs => rs.Order)
                    .ToListAsync();

                var ganttData = new List<object>();

                foreach (var stage in stages)
                {
                    // Определяем даты начала и окончания
                    var startDate_calc = GetStageStartDate(stage);
                    var endDate_calc = GetStageEndDate(stage, startDate_calc);

                    // Определяем зависимости
                    var dependencies = GetStageDependencies(stage);

                    // Вычисляем процент выполнения
                    var percentComplete = GetStageProgress(stage);

                    // Формируем название задачи
                    var taskName = $"{stage.SubBatch.ProductionOrder.Detail.Name} - П{stage.SubBatch.BatchNumber} - {stage.Name}";

                    ganttData.Add(new
                    {
                        taskId = $"task_{stage.Id}",
                        taskName = taskName,
                        resource = stage.Machine?.Name ?? "Не назначен",
                        start = startDate_calc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        end = endDate_calc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        actualStart = GetActualStartDate(stage)?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        actualEnd = GetActualEndDate(stage)?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        duration = (int)(stage.PlannedTime * 24 * 60 * 60 * 1000), // В миллисекундах
                        percentComplete = percentComplete,
                        dependencies = dependencies,
                        status = stage.Status,
                        stageType = stage.StageType,
                        subBatchId = stage.SubBatchId,
                        orderId = stage.SubBatch.ProductionOrderId,
                        stageId = stage.Id,
                        plannedTime = stage.PlannedTime,
                        actualTime = GetActualTime(stage),
                        machine = stage.Machine != null ? new { stage.Machine.Id, stage.Machine.Name } : null,
                        canReleaseMachine = CanReleaseMachine(stage),
                        canAddToQueue = stage.Status == "Ready",
                        canRemoveFromQueue = stage.Status == "Waiting"
                    });
                }

                return Json(ganttData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GetGanttData: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "Ошибка загрузки данных диаграммы Ганта", details = ex.Message });
            }
        }


        [HttpPost]
        public async Task<IActionResult> ReleaseMachine(int machineId, int urgentStageId, string reason)
        {
            try
            {
                var success = await _stageAutomationService.ReleaseMachine(machineId, urgentStageId, reason);

                if (success)
                {
                    return Json(new { success = true, message = "Станок освобожден для срочной задачи" });
                }
                else
                {
                    return Json(new { success = false, message = "Не удалось освободить станок" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddStageToQueue(int stageId)
        {
            try
            {
                var success = await _stageAutomationService.AddStageToQueue(stageId);

                if (success)
                {
                    return Json(new { success = true, message = "Этап добавлен в очередь" });
                }
                else
                {
                    return Json(new { success = false, message = "Не удалось добавить этап в очередь" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveStageFromQueue(int stageId)
        {
            try
            {
                var success = await _stageAutomationService.RemoveStageFromQueue(stageId);

                if (success)
                {
                    return Json(new { success = true, message = "Этап удален из очереди" });
                }
                else
                {
                    return Json(new { success = false, message = "Не удалось удалить этап из очереди" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStageInfo(int stageId)
        {
            var stage = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Machine)
                .Include(rs => rs.Operation)
                .Include(rs => rs.StageExecutions)
                .FirstOrDefaultAsync(rs => rs.Id == stageId);

            if (stage == null)
                return NotFound();

            var estimatedStart = stage.Status == "Waiting"
                ? await _stageAutomationService.GetEstimatedStartTime(stageId)
                : null;

            var stageInfo = new
            {
                id = stage.Id,
                name = stage.Name,
                status = stage.Status,
                stageType = stage.StageType,
                machineId = stage.MachineId,
                machineName = stage.Machine?.Name,
                detailName = stage.SubBatch.ProductionOrder.Detail.Name,
                orderNumber = stage.SubBatch.ProductionOrder.Number,
                plannedTime = stage.PlannedTime,
                estimatedStart = estimatedStart,
                canReleaseMachine = stage.MachineId.HasValue &&
                                  stage.StageExecutions.Any(se => se.Status == "Started" || se.Status == "Paused"),
                canAddToQueue = stage.Status == "Ready",
                canRemoveFromQueue = stage.Status == "Waiting"
            };

            return Json(stageInfo);
        }

        [HttpGet]
        public async Task<IActionResult> GetMachineQueue(int machineId)
        {
            var machine = await _context.Machines
                .Include(m => m.MachineType)
                .FirstOrDefaultAsync(m => m.Id == machineId);

            if (machine == null)
                return NotFound();

            // Получаем все этапы в очереди для этого типа станка
            var queuedStages = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Operation)
                .Where(rs => rs.Status == "Waiting" &&
                           rs.Operation != null &&
                           rs.Operation.MachineTypeId == machine.MachineTypeId)
                .OrderBy(rs => rs.SubBatch.ProductionOrder.CreatedAt)
                .ThenBy(rs => rs.Order)
                .ToListAsync();

            var queueInfo = queuedStages.Select(async stage => new
            {
                id = stage.Id,
                name = stage.Name,
                detailName = stage.SubBatch.ProductionOrder.Detail.Name,
                orderNumber = stage.SubBatch.ProductionOrder.Number,
                estimatedStart = await _stageAutomationService.GetEstimatedStartTime(stage.Id)
            }).Select(t => t.Result).ToList();

            return Json(new { machine = machine.Name, queue = queueInfo });
        }

        // Вспомогательные методы
        private DateTime GetStageStartDate(RouteStage stage)
        {
            // Проверяем, есть ли фактическое время начала
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.StartedAt != null)
                return execution.StartedAt.Value;

            // Если есть плановое время начала
            if (stage.PlannedStartDate != null)
                return stage.PlannedStartDate.Value;

            // Определяем время начала на основе предыдущих этапов
            var previousStage = _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefault();

            if (previousStage != null)
            {
                var previousEndDate = GetStageEndDate(previousStage, GetStageStartDate(previousStage));
                return previousEndDate;
            }

            // Если это первый этап, берем время создания подпартии
            return stage.SubBatch.CreatedAt;
        }

        private DateTime GetStageEndDate(RouteStage stage, DateTime startDate)
        {
            // Проверяем, есть ли фактическое время окончания
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.CompletedAt != null)
                return execution.CompletedAt.Value;

            // Если есть плановое время окончания
            if (stage.PlannedEndDate != null)
                return stage.PlannedEndDate.Value;

            // Рассчитываем на основе планового времени
            return startDate.AddHours((double)stage.PlannedTime);
        }


        private DateTime? GetActualStartDate(RouteStage stage)
        {
            return stage.StageExecutions.FirstOrDefault()?.StartedAt;
        }

        private DateTime? GetActualEndDate(RouteStage stage)
        {
            return stage.StageExecutions.FirstOrDefault()?.CompletedAt;
        }

        private decimal? GetActualTime(RouteStage stage)
        {
            return stage.StageExecutions.FirstOrDefault()?.ActualTime;
        }

        private int GetStageProgress(RouteStage stage)
        {
            return stage.Status switch
            {
                "Completed" => 100,
                "InProgress" => CalculateProgress(stage),
                "Paused" => CalculateProgress(stage),
                "Ready" => 0,
                "Waiting" => 0,
                _ => 0
            };
        }

        private int CalculateProgress(RouteStage stage)
        {
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.StartedAt == null) return 0;

            var elapsedTime = DateTime.UtcNow - execution.StartedAt.Value;
            var elapsedHours = (decimal)elapsedTime.TotalHours - (execution.PauseTime ?? 0);

            if (stage.PlannedTime > 0)
            {
                var progress = (int)((elapsedHours / stage.PlannedTime) * 100);
                return Math.Max(1, Math.Min(99, progress));
            }

            return 25;
        }

        private bool CanReleaseMachine(RouteStage stage)
        {
            return stage.MachineId.HasValue &&
                   stage.StageExecutions.Any(se => se.Status == "Started" || se.Status == "Paused");
        }

        private string GetStageDependencies(RouteStage stage)
        {
            var previousStage = _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefault();

            return previousStage != null ? $"task_{previousStage.Id}" : "";
        }

        private bool CanStageBeMoved(RouteStage stage)
        {
            return stage.Status == "Ready" || stage.Status == "Waiting";
        }

        // Методы из оригинального контроллера
        [HttpPost]
        public async Task<IActionResult> UpdateStageDates(int stageId, DateTime startDate, DateTime endDate)
        {
            var stage = await _context.RouteStages.FindAsync(stageId);
            if (stage != null)
            {
                stage.PlannedStartDate = startDate;
                stage.PlannedEndDate = endDate;

                var timeSpan = endDate - startDate;
                stage.PlannedTime = (decimal)timeSpan.TotalHours;

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false });
        }

        [HttpGet]
        public async Task<IActionResult> GetMachineUtilization(int? machineId, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var query = _context.StageExecutions
                    .Include(se => se.RouteStage)
                    .ThenInclude(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(se => se.Machine)
                    .Where(se => se.Status == "Completed" && se.ActualTime.HasValue);

                if (machineId.HasValue)
                {
                    query = query.Where(se => se.MachineId == machineId.Value);
                }

                if (startDate.HasValue)
                {
                    var startUtc = startDate.Value.ToUniversalTime();
                    query = query.Where(se => se.StartedAt >= startUtc);
                }

                if (endDate.HasValue)
                {
                    var endUtc = endDate.Value.ToUniversalTime();
                    query = query.Where(se => se.CompletedAt <= endUtc);
                }

                var executions = await query.ToListAsync();

                var utilizationData = executions
                    .GroupBy(se => se.Machine?.Name ?? "Не назначен")
                    .Select(g => new
                    {
                        machine = g.Key,
                        totalTime = g.Sum(se => se.ActualTime ?? 0),
                        changeoverTime = g.Where(se => se.RouteStage.StageType == "Changeover")
                                          .Sum(se => se.ActualTime ?? 0),
                        productionTime = g.Where(se => se.RouteStage.StageType == "Operation")
                                          .Sum(se => se.ActualTime ?? 0),
                        completedStages = g.Count()
                    })
                    .OrderByDescending(x => x.totalTime)
                    .ToList();

                return Json(utilizationData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GetMachineUtilization: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "Ошибка загрузки данных о загрузке станков", details = ex.Message });
            }
        }

        public async Task<IActionResult> MachineSchedule()
        {
            ViewBag.Machines = await _context.Machines
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            return View();
        }
    }
}