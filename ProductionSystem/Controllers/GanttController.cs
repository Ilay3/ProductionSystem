using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;
using ProductionSystem.Services;
using System.Text.Json;

namespace ProductionSystem.Controllers
{
    public class GanttController : Controller
    {
        private readonly ProductionContext _context;
        private readonly IStageAutomationService _stageAutomationService;
        private readonly ILogger<GanttController> _logger;

        public GanttController(
            ProductionContext context,
            IStageAutomationService stageAutomationService,
            ILogger<GanttController> logger)
        {
            _context = context;
            _stageAutomationService = stageAutomationService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int? orderId, int? machineId, bool hideCompleted = false)
        {
            ViewBag.Orders = await _context.ProductionOrders
                .Select(po => new { po.Id, po.Number, po.Detail.Name })
                .ToListAsync();

            ViewBag.Machines = await _context.Machines
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            ViewBag.SelectedOrderId = orderId;
            ViewBag.SelectedMachineId = machineId;
            ViewBag.HideCompleted = hideCompleted;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGanttData(int? orderId, int? machineId, DateTime? startDate, DateTime? endDate, bool hideCompleted = false)
        {
            try
            {
                _logger.LogInformation("Получение данных для диаграммы Ганта");

                var query = _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Machine)
                    .Include(rs => rs.Operation)
                    .Include(rs => rs.StageExecutions)
                    .AsQueryable();

                // Фильтруем по статусу
                if (hideCompleted)
                {
                    // Если скрываем завершенные - не показываем этапы со статусом Completed
                    query = query.Where(rs => rs.Status != "Pending" && rs.Status != "Completed");
                }
                else
                {
                    // Показываем все кроме ожидающих
                    query = query.Where(rs => rs.Status != "Pending");
                }

                if (orderId.HasValue)
                {
                    query = query.Where(rs => rs.SubBatch.ProductionOrderId == orderId.Value);
                }

                if (machineId.HasValue)
                {
                    query = query.Where(rs => rs.MachineId == machineId.Value);
                }

                // Добавляем фильтры по дате
                if (startDate.HasValue)
                {
                    var startDateUnspecified = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Unspecified);
                    query = query.Where(rs =>
                        rs.PlannedEndDate == null ||
                        rs.PlannedEndDate >= startDateUnspecified);
                }

                if (endDate.HasValue)
                {
                    var endDateUnspecified = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Unspecified);
                    query = query.Where(rs =>
                        rs.PlannedStartDate == null ||
                        rs.PlannedStartDate <= endDateUnspecified);
                }

                var stages = await query
                    .OrderBy(rs => rs.SubBatch.ProductionOrder.CreatedAt)
                    .ThenBy(rs => rs.SubBatchId)
                    .ThenBy(rs => rs.Order)
                    .ToListAsync();

                var ganttData = new List<object>();
                var currentDateTime = ProductionContext.GetLocalNow();

                foreach (var stage in stages)
                {
                    // Определяем даты начала и окончания с учетом фактического выполнения
                    var startDate_calc = GetStageStartDate(stage, currentDateTime);
                    var endDate_calc = GetStageEndDate(stage, startDate_calc, currentDateTime);

                    // Определяем зависимости
                    var dependencies = "";
                    if (stage.Machine != null)
                    {
                        dependencies = await GetStageDependencies(stage);
                    }


                    // Вычисляем процент выполнения
                    var percentComplete = GetStageProgress(stage, currentDateTime);

                    // Формируем название задачи
                    var taskName = $"{stage.SubBatch.ProductionOrder.Detail.Name} - П{stage.SubBatch.BatchNumber} - {stage.Name}";

                    // Формируем цвет для типа этапа
                    string stageColor = GetStageColor(stage);

                    // Определяем плановое время с учетом количества деталей
                    decimal plannedTime = stage.PlannedTime;

                    // Получаем фактическое время из выполнения
                    decimal? actualTime = GetActualTime(stage);

                    // Получаем оценку времени начала для этапов в очереди
                    DateTime? estimatedStart = null;
                    if (stage.Status == "Waiting")
                    {
                        try
                        {
                            estimatedStart = await _stageAutomationService.GetEstimatedStartTime(stage.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Не удалось получить оценку времени для этапа {stage.Id}");
                        }
                    }

                    ganttData.Add(new
                    {
                        taskId = $"task_{stage.Id}",
                        taskName = taskName,
                        resource = stage.Machine?.Name ?? "Не назначен",
                        start = startDate_calc.ToString("yyyy-MM-ddTHH:mm:ss"),
                        end = endDate_calc.ToString("yyyy-MM-ddTHH:mm:ss"),
                        actualStart = GetActualStartDate(stage)?.ToString("yyyy-MM-ddTHH:mm:ss"),
                        actualEnd = GetActualEndDate(stage)?.ToString("yyyy-MM-ddTHH:mm:ss"),
                        duration = (long)(plannedTime * 24 * 60 * 60 * 1000), // В миллисекундах
                        percentComplete = percentComplete,
                        dependencies = dependencies,
                        status = stage.Status,
                        stageType = stage.StageType,
                        subBatchId = stage.SubBatchId,
                        orderId = stage.SubBatch.ProductionOrderId,
                        stageId = stage.Id,
                        plannedTime = plannedTime,
                        actualTime = actualTime,
                        machine = stage.Machine != null ? new { stage.Machine.Id, stage.Machine.Name } : null,
                        canReleaseMachine = CanReleaseMachine(stage),
                        canAddToQueue = stage.Status == "Ready",
                        canRemoveFromQueue = stage.Status == "Waiting",
                        estimatedStart = estimatedStart,
                        color = stageColor,
                        quantity = stage.Quantity,
                    });
                }

                return Json(ganttData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в GetGanttData");
                return StatusCode(500, new { error = "Ошибка загрузки данных диаграммы Ганта", details = ex.Message });
            }
        }

        private string GetStageColor(RouteStage stage)
        {
            // Разные цвета для разных статусов и типов
            if (stage.StageType == "Changeover")
                return "#FF9800"; // Оранжевый для переналадок

            return stage.Status switch
            {
                "Ready" => "#17a2b8", // Голубой
                "Waiting" => "#ffc107", // Желтый 
                "InProgress" => "#0d6efd", // Синий
                "Paused" => "#6c757d", // Серый
                "Completed" => "#198754", // Зеленый
                _ => "#343a40" // Темно-серый
            };
        }

        // Метод для получения истории этапа
        [HttpGet]
        public async Task<IActionResult> GetStageHistory(int stageId)
        {
            try
            {
                // Получаем все логи выполнения этапа
                var executionLogs = await _context.ExecutionLogs
                    .Include(log => log.StageExecution)
                    .Where(log => log.StageExecution.RouteStageId == stageId)
                    .OrderByDescending(log => log.Timestamp)
                    .ToListAsync();

                // ИСПРАВЛЕНО: переименовали свойство operator на operatorName
                var historyItems = executionLogs.Select(log => new
                {
                    id = log.Id,
                    timestamp = log.Timestamp,
                    action = log.Action,
                    operatorName = log.Operator, // Переименовано с operator на operatorName
                    notes = log.Notes
                }).ToList();

                return Json(new { success = true, history = historyItems });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка получения истории для этапа {stageId}");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // Получение всех этапов в очереди
        [HttpGet]
        public async Task<IActionResult> GetWaitingStages(int? machineId = null)
        {
            try
            {
                IQueryable<RouteStage> query = _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Operation)
                    .Include(rs => rs.Machine)
                    .Where(rs => rs.Status == "Waiting");

                if (machineId.HasValue)
                {
                    var machine = await _context.Machines
                        .Include(m => m.MachineType)
                        .FirstOrDefaultAsync(m => m.Id == machineId);

                    if (machine != null)
                    {
                        // Фильтруем по типу станка
                        query = query.Where(rs => rs.Operation != null && rs.Operation.MachineTypeId == machine.MachineTypeId);
                    }
                }

                var stages = await query.OrderBy(rs => rs.PlannedStartDate).ToListAsync();

                var result = stages.Select(stage => new
                {
                    stageId = stage.Id,
                    name = stage.Name,
                    detailName = stage.SubBatch.ProductionOrder.Detail.Name,
                    orderNumber = stage.SubBatch.ProductionOrder.Number,
                    stageType = stage.StageType,
                    targetMachine = stage.Machine?.Name,
                    estimatedStart = _stageAutomationService.GetEstimatedStartTime(stage.Id).Result
                }).ToList();

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка этапов в очереди");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Получение доступных станков для переназначения
        [HttpGet]
        public async Task<IActionResult> GetAvailableMachines(int stageId)
        {
            try
            {
                var stage = await _context.RouteStages
                    .Include(rs => rs.Operation)
                    .FirstOrDefaultAsync(rs => rs.Id == stageId);

                if (stage == null)
                    return Json(new { success = false, message = "Этап не найден" });

                var machineTypeId = stage.Operation?.MachineTypeId;

                if (machineTypeId == null)
                    return Json(new { success = false, message = "Тип станка не определен" });

                // Получаем все станки данного типа
                var machines = await _context.Machines
                    .Where(m => m.MachineTypeId == machineTypeId)
                    .Select(m => new
                    {
                        id = m.Id,
                        name = m.Name,
                        // Станок занят, если на нем есть активные выполнения
                        isBusy = _context.StageExecutions.Any(
                            se => se.MachineId == m.Id && se.Status == "Started")
                    })
                    .OrderBy(m => m.isBusy)
                    .ThenBy(m => m.name)
                    .ToListAsync();

                return Json(machines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка получения доступных станков для этапа {stageId}");
                return Json(new { success = false, message = ex.Message });
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
                _logger.LogError(ex, $"Ошибка освобождения станка {machineId}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddStageToQueue(int id)
        {
            try
            {
                var success = await _stageAutomationService.AddStageToQueue(id);

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
                _logger.LogError(ex, $"Ошибка при добавлении этапа {id} в очередь");
                return Json(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }


        [HttpPost]
        public async Task<IActionResult> RemoveStageFromQueue(int id)
        {
            try
            {
                var success = await _stageAutomationService.RemoveStageFromQueue(id);

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
                _logger.LogError(ex, $"Ошибка при удалении этапа {id} из очереди");
                return Json(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }


        public async Task<IActionResult> GetStageInfo(int id)
        {
            try
            {
                var stage = await _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Machine)
                    .Include(rs => rs.Operation)
                    .Include(rs => rs.StageExecutions)
                    .FirstOrDefaultAsync(rs => rs.Id == id);

                if (stage == null)
                    return Json(new { success = false, message = "Этап не найден" });

                DateTime? estimatedStart = null;
                if (stage.Status == "Waiting")
                {
                    try
                    {
                        estimatedStart = await _stageAutomationService.GetEstimatedStartTime(id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Ошибка при получении прогнозируемого времени для этапа {id}");
                    }
                }

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
                                      stage.StageExecutions.Any(se => se.Status == "Started"),
                    canAddToQueue = stage.Status == "Ready",
                    canRemoveFromQueue = stage.Status == "Waiting",
                    quantity = stage.Quantity
                };

                return Json(stageInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении информации о этапе {id}");
                return Json(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }


        public async Task<IActionResult> GetMachineQueue(int id)
        {
            try
            {
                var machine = await _context.Machines
                    .Include(m => m.MachineType)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (machine == null)
                    return Json(new { success = false, message = "Станок не найден" });

                // Получаем все этапы в очереди для этого типа станка
                var queuedStages = await _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Operation)
                    .Where(rs => rs.Status == "Waiting" &&
                               rs.Operation != null &&
                               rs.Operation.MachineTypeId == machine.MachineTypeId)
                    .OrderBy(rs => rs.PlannedStartDate) // Сортируем по времени добавления в очередь
                    .ToListAsync();

                var queueInfo = new List<object>();

                foreach (var stage in queuedStages)
                {
                    DateTime? estimatedStart = null;
                    try
                    {
                        estimatedStart = await _stageAutomationService.GetEstimatedStartTime(stage.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Ошибка при получении прогнозируемого времени для этапа {stage.Id}");
                    }

                    queueInfo.Add(new
                    {
                        id = stage.Id,
                        name = stage.Name,
                        detailName = stage.SubBatch.ProductionOrder.Detail.Name,
                        orderNumber = stage.SubBatch.ProductionOrder.Number,
                        estimatedStart = estimatedStart,
                        stageType = stage.StageType,
                        quantity = stage.Quantity
                    });
                }

                return Json(new { success = true, machine = machine.Name, queue = queueInfo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении очереди для станка {id}");
                return Json(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        // Вспомогательные методы - начало
        private DateTime GetStageStartDate(RouteStage stage, DateTime currentTime)
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
                var previousEndDate = GetStageEndDate(previousStage, GetStageStartDate(previousStage, currentTime), currentTime);
                return previousEndDate;
            }

            // Если это первый этап, берем текущее время
            return currentTime;
        }

        private DateTime GetStageEndDate(RouteStage stage, DateTime startDate, DateTime currentTime)
        {
            // Проверяем, есть ли фактическое время окончания
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.CompletedAt != null)
                return execution.CompletedAt.Value;

            // Если есть плановое время окончания
            if (stage.PlannedEndDate != null)
                return stage.PlannedEndDate.Value;

            // Для запущенных этапов оцениваем прогрессивно
            if (stage.Status == "InProgress" && execution?.StartedAt != null)
            {
                var elapsedTime = currentTime - execution.StartedAt.Value;
                var elapsedHours = (decimal)elapsedTime.TotalHours;

                // Учитываем паузы
                if (execution.PauseTime.HasValue)
                {
                    elapsedHours -= execution.PauseTime.Value;
                }

                // Если уже превысили, считаем завершение через 10% оставшегося времени
                if (elapsedHours >= stage.PlannedTime)
                {
                    return currentTime.AddHours((double)(stage.PlannedTime * 0.1m));
                }

                // Иначе расчет от прогресса
                var remainingTime = stage.PlannedTime - elapsedHours;
                return currentTime.AddHours((double)remainingTime);
            }

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

        private int GetStageProgress(RouteStage stage, DateTime currentTime)
        {
            return stage.Status switch
            {
                "Completed" => 100,
                "InProgress" => CalculateProgress(stage, currentTime),
                "Paused" => CalculateProgress(stage, currentTime),
                "Ready" => 0,
                "Waiting" => 0,
                _ => 0
            };
        }

        private int CalculateProgress(RouteStage stage, DateTime currentTime)
        {
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.StartedAt == null) return 0;

            // Определяем конечное время для расчета
            var endTime = execution.Status == "Paused" && execution.PausedAt.HasValue
                        ? execution.PausedAt.Value
                        : currentTime;

            var elapsedTime = endTime - execution.StartedAt.Value;
            var elapsedMinutes = elapsedTime.TotalMinutes;

            // Вычитаем время пауз в минутах
            var pauseMinutes = (execution.PauseTime ?? 0) * 60;
            elapsedMinutes -= (double)pauseMinutes;

            // Конвертируем в часы
            var elapsedHours = (decimal)(elapsedMinutes / 60.0);

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
                   stage.StageExecutions.Any(se => se.Status == "Started");
        }

        private async Task<string> GetStageDependencies(RouteStage stage)
        {
            // Если это не первый этап в подпартии, связываем только с непосредственно предыдущим
            // этапом той же подпартии и того же типа операции
            var previousStage = await _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId &&
                           rs.Order < stage.Order &&
                           rs.StageType == stage.StageType)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefaultAsync();

            // Если предыдущий этап того же типа не найден, не создаем зависимость
            return previousStage != null ? $"task_{previousStage.Id}" : "";
        }

        // Вспомогательные методы 

        [HttpPost]
        public async Task<IActionResult> UpdateStageDates(int stageId, DateTime startDate, DateTime endDate)
        {
            // ИСПРАВЛЕНИЕ: Преобразуем даты к Kind=Unspecified
            startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
            endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

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
                    // ИСПРАВЛЕНИЕ: Преобразуем дату к Kind=Unspecified
                    var start = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Unspecified);
                    query = query.Where(se => se.StartedAt >= start);
                }

                if (endDate.HasValue)
                {
                    // ИСПРАВЛЕНИЕ: Преобразуем дату к Kind=Unspecified
                    var end = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Unspecified);
                    query = query.Where(se => se.CompletedAt <= end);
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
                _logger.LogError(ex, $"Ошибка в GetMachineUtilization");
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