﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;
using ProductionSystem.Services;

namespace ProductionSystem.Controllers
{
    public class StageExecutionController : Controller
    {
        private readonly ProductionContext _context;
        private readonly IStageAutomationService _automationService;
        private readonly ILogger<StageExecutionController> _logger;

        public StageExecutionController(
            ProductionContext context,
            IStageAutomationService automationService,
            ILogger<StageExecutionController> logger)
        {
            _context = context;
            _automationService = automationService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int? machineId)
        {
            IQueryable<StageExecution> executions = _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .Include(se => se.ExecutionLogs);

            if (machineId.HasValue)
            {
                executions = executions.Where(se => se.MachineId == machineId.Value);
                var machine = await _context.Machines.FindAsync(machineId);
                ViewBag.MachineName = machine?.Name;
            }

            return View(await executions.OrderByDescending(se => se.CreatedAt).ToListAsync());
        }

        // Исправьте методы StartStage, PauseStage, ResumeStage и CompleteStage:

        [HttpPost]
        public async Task<IActionResult> StartStage(int id, string? @operator)
        {
            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var routeStage = await _context.RouteStages
                        .Include(rs => rs.SubBatch)
                        .FirstOrDefaultAsync(rs => rs.Id == id);

                    if (routeStage == null || routeStage.Status != "Ready")
                    {
                        TempData["Error"] = "Этап недоступен для запуска";
                        return RedirectToAction("Index", "RouteStages", new { subBatchId = routeStage?.SubBatchId });
                    }

                    // Проверяем, что предыдущий этап завершен
                    var previousStage = await _context.RouteStages
                        .Where(rs => rs.SubBatchId == routeStage.SubBatchId && rs.Order < routeStage.Order)
                        .OrderByDescending(rs => rs.Order)
                        .FirstOrDefaultAsync();

                    if (previousStage != null && previousStage.Status != "Completed")
                    {
                        TempData["Error"] = "Предыдущий этап не завершен";
                        return RedirectToAction("Index", "RouteStages", new { subBatchId = routeStage.SubBatchId });
                    }

                    // Проверяем, не занят ли станок другой АКТИВНОЙ операцией (не считаем паузу)
                    if (routeStage.MachineId.HasValue)
                    {
                        var isStageRunningOnMachine = await _context.StageExecutions
                            .AnyAsync(se => se.MachineId == routeStage.MachineId && se.Status == "Started");

                        if (isStageRunningOnMachine)
                        {
                            TempData["Error"] = "Станок занят выполнением другой операции";
                            return RedirectToAction("Index", "RouteStages", new { subBatchId = routeStage.SubBatchId });
                        }
                    }

                    // Создаем выполнение этапа
                    var execution = new StageExecution
                    {
                        RouteStageId = id,
                        MachineId = routeStage.MachineId,
                        Operator = string.IsNullOrEmpty(@operator) ? "ОПЕРАТОР" : @operator,
                        Status = "Started",
                        StartedAt = ProductionContext.GetLocalNow(),
                        CreatedAt = ProductionContext.GetLocalNow()
                    };

                    _context.StageExecutions.Add(execution);

                    // Обновляем статус этапа
                    routeStage.Status = "InProgress";

                    await _context.SaveChangesAsync();

                    // Добавляем лог
                    await AddExecutionLog(execution.Id, "Started", @operator, "Этап запущен оператором");

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Этап {id} запущен оператором {@operator}");
                    TempData["Message"] = "Этап успешно запущен";

                    return RedirectToAction("Details", new { id = execution.Id });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при запуске этапа {id}");
                    TempData["Error"] = $"Ошибка при запуске этапа: {ex.Message}";
                    return RedirectToAction("Index", "RouteStages");
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> PauseStage(int id)
        {
            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var execution = await _context.StageExecutions
                        .Include(se => se.RouteStage)
                        .FirstOrDefaultAsync(se => se.Id == id);

                    if (execution == null || execution.Status != "Started")
                    {
                        TempData["Error"] = "Этап нельзя поставить на паузу";
                        return RedirectToAction("Details", new { id });
                    }

                    execution.Status = "Paused";
                    execution.PausedAt = ProductionContext.GetLocalNow();
                    execution.RouteStage.Status = "Paused";

                    await _context.SaveChangesAsync();
                    await AddExecutionLog(id, "Paused", execution.Operator, "Этап поставлен на паузу");

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Этап {id} поставлен на паузу");
                    TempData["Message"] = "Этап поставлен на паузу";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при постановке на паузу этапа {id}");
                    TempData["Error"] = $"Ошибка при постановке на паузу: {ex.Message}";
                }

                return RedirectToAction("Details", new { id });
            });
        }

        // Исправьте методы ResumeStage и CompleteStage:

        [HttpPost]
        public async Task<IActionResult> ResumeStage(int id)
        {
            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var execution = await _context.StageExecutions
                        .Include(se => se.RouteStage)
                        .FirstOrDefaultAsync(se => se.Id == id);

                    if (execution == null || execution.Status != "Paused")
                    {
                        TempData["Error"] = "Этап нельзя возобновить";
                        return RedirectToAction("Details", new { id });
                    }

                    // Проверяем, не запущен ли другой этап на этом станке
                    if (execution.MachineId.HasValue)
                    {
                        var isStageRunningOnMachine = await _context.StageExecutions
                            .AnyAsync(se => se.MachineId == execution.MachineId &&
                                        se.Status == "Started" &&
                                        se.Id != execution.Id);

                        if (isStageRunningOnMachine)
                        {
                            TempData["Error"] = "Станок занят выполнением другой операции";
                            return RedirectToAction("Details", new { id });
                        }
                    }

                    // Вычисляем время паузы
                    if (execution.PausedAt.HasValue)
                    {
                        var pauseTime = ProductionContext.GetLocalNow() - execution.PausedAt.Value;
                        execution.PauseTime = (execution.PauseTime ?? 0) + (decimal)pauseTime.TotalHours;
                    }

                    execution.Status = "Started";
                    execution.ResumedAt = ProductionContext.GetLocalNow();
                    execution.RouteStage.Status = "InProgress";

                    await _context.SaveChangesAsync();
                    await AddExecutionLog(id, "Resumed", execution.Operator, "Этап возобновлен");

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Этап {id} возобновлен");
                    TempData["Message"] = "Этап возобновлен";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при возобновлении этапа {id}");
                    TempData["Error"] = $"Ошибка при возобновлении: {ex.Message}";
                }

                return RedirectToAction("Details", new { id });
            });
        }

        [HttpPost]
        public async Task<IActionResult> CompleteStage(int id, string? notes, string? timeExceededReason)
        {
            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var execution = await _context.StageExecutions
                        .Include(se => se.RouteStage)
                        .ThenInclude(rs => rs.SubBatch)
                        .ThenInclude(sb => sb.ProductionOrder)
                        .FirstOrDefaultAsync(se => se.Id == id);

                    if (execution == null || (execution.Status != "Started" && execution.Status != "Paused"))
                    {
                        TempData["Error"] = "Этап нельзя завершить";
                        return RedirectToAction("Details", new { id });
                    }

                    // Вычисляем точное фактическое время выполнения в минутах, конвертируем в часы
                    var actualMinutes = await CalculateActualWorkTimeInMinutes(execution);

                    // Преобразуем минуты в часы и округляем до 2 знаков
                    execution.ActualTime = Math.Round((decimal)(actualMinutes / 60.0), 2);
                    if (execution.ActualTime < 0.01m) execution.ActualTime = 0.01m; // Минимум 0.01 часа

                    execution.Status = "Completed";
                    execution.CompletedAt = ProductionContext.GetLocalNow();
                    execution.Notes = notes;

                    // Проверяем превышение времени
                    var plannedTime = execution.RouteStage.PlannedTime;
                    if (execution.ActualTime > plannedTime)
                    {
                        execution.TimeExceededReason = string.IsNullOrEmpty(timeExceededReason) ?
                            "Превышение планового времени" : timeExceededReason;

                        var exceededBy = execution.ActualTime - plannedTime;
                        TempData["Warning"] = $"Этап превысил плановое время на {exceededBy:F2} ч ({(exceededBy / plannedTime * 100):F1}%)";
                    }

                    execution.RouteStage.Status = "Completed";

                    // Если это была переналадка, активируем последующие этапы
                    await ProcessNextStagesAfterChangeover(execution);

                    await _context.SaveChangesAsync();
                    await AddExecutionLog(id, "Completed", execution.Operator, $"Этап завершен. {notes}");

                    // Проверяем завершение всей подпартии
                    await CheckSubBatchCompletion(execution.RouteStage.SubBatchId);

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Этап {id} успешно завершен с фактическим временем {execution.ActualTime} ч");
                    TempData["Message"] = "Этап успешно завершен";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при завершении этапа {id}");
                    TempData["Error"] = $"Ошибка при завершении этапа: {ex.Message}";
                }

                // Перенаправляем на список этапов подпартии
                return RedirectToAction("Index", "RouteStages", new { subBatchId = FindSubBatchIdByExecutionId(id) });
            });
        }
        private int? FindSubBatchIdByExecutionId(int executionId)
        {
            var routeStageId = _context.StageExecutions
                .Where(se => se.Id == executionId)
                .Select(se => se.RouteStageId)
                .FirstOrDefault();

            if (routeStageId > 0)
            {
                return _context.RouteStages
                    .Where(rs => rs.Id == routeStageId)
                    .Select(rs => rs.SubBatchId)
                    .FirstOrDefault();
            }

            return null;
        }

        private async Task<double> CalculateActualWorkTimeInMinutes(StageExecution execution)
        {
            if (!execution.StartedAt.HasValue)
                return 0;

            // Определяем конечное время для расчета
            var endTime = execution.Status == "Paused" && execution.PausedAt.HasValue
                ? execution.PausedAt.Value
                : ProductionContext.GetLocalNow();

            var totalTimeSpan = endTime - execution.StartedAt.Value;
            var totalMinutes = totalTimeSpan.TotalMinutes;

            // Вычитаем время пауз
            if (execution.PauseTime.HasValue)
            {
                totalMinutes -= (double)(execution.PauseTime * 60); // Конвертируем часы в минуты
            }

            // Проверяем значения на отрицательные (на всякий случай)
            return Math.Max(1, totalMinutes); // Минимум 1 минута
        }

        private async Task ProcessNextStagesAfterChangeover(StageExecution execution)
        {
            if (execution.RouteStage.StageType != "Changeover")
                return;

            // Находим следующий этап после завершенной переналадки
            var nextStage = await _context.RouteStages
                .Where(rs => rs.SubBatchId == execution.RouteStage.SubBatchId &&
                            rs.Order > execution.RouteStage.Order)
                .OrderBy(rs => rs.Order)
                .FirstOrDefaultAsync();

            if (nextStage != null && nextStage.Status == "Ready" && nextStage.MachineId == execution.MachineId)
            {
                // Проверяем, не занят ли станок другой операцией
                var isStageRunningOnMachine = await _context.StageExecutions
                    .AnyAsync(se => se.MachineId == execution.MachineId && se.Status == "Started");

                if (!isStageRunningOnMachine)
                {
                    // Автоматически запускаем следующий этап
                    nextStage.Status = "InProgress";

                    var nextExecution = new StageExecution
                    {
                        RouteStageId = nextStage.Id,
                        MachineId = nextStage.MachineId,
                        Operator = execution.Operator,
                        Status = "Started",
                        StartedAt = ProductionContext.GetLocalNow(),
                        CreatedAt = ProductionContext.GetLocalNow()
                    };

                    _context.StageExecutions.Add(nextExecution);
                    await _context.SaveChangesAsync();

                    // Добавляем лог
                    await AddExecutionLog(nextExecution.Id, "Started", execution.Operator,
                        "Автоматический запуск после переналадки");

                    _logger.LogInformation($"Этап {nextStage.Id} автоматически запущен после переналадки");
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateActualTime(int id, decimal actualTime, string? reason)
        {
            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var execution = await _context.StageExecutions
                        .Include(se => se.RouteStage)
                        .FirstOrDefaultAsync(se => se.Id == id);

                    if (execution == null || execution.Status != "Completed")
                    {
                        TempData["Error"] = "Можно редактировать время только завершенных этапов";
                        return RedirectToAction("Details", new { id });
                    }

                    var oldTime = execution.ActualTime;
                    execution.ActualTime = actualTime;

                    // Обновляем заметки с информацией о корректировке
                    var correctionNote = $"\nВремя скорректировано {DateTime.Now:dd.MM.yyyy HH:mm}: с {oldTime:F2} на {actualTime:F2} ч. Причина: {reason}";
                    execution.Notes = (execution.Notes ?? "") + correctionNote;

                    await _context.SaveChangesAsync();

                    // Добавляем лог о корректировке
                    await AddExecutionLog(id, "TimeModified", execution.Operator,
                        $"Время изменено с {oldTime:F2} на {actualTime:F2} ч. Причина: {reason}");

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Фактическое время этапа {id} обновлено с {oldTime} на {actualTime} ч");
                    TempData["Message"] = $"Фактическое время обновлено с {oldTime:F2} на {actualTime:F2} ч";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при обновлении времени этапа {id}");
                    TempData["Error"] = $"Ошибка при обновлении времени: {ex.Message}";
                }

                return RedirectToAction("Details", new { id });
            });
        }


        public async Task<IActionResult> EditTime(int id)
        {
            var execution = await _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .FirstOrDefaultAsync(se => se.Id == id);

            if (execution == null || execution.Status != "Completed")
            {
                TempData["Error"] = "Можно редактировать время только завершенных этапов";
                return RedirectToAction("Details", new { id });
            }

            return View(execution);
        }

        public async Task<IActionResult> Details(int id)
        {
            var execution = await _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.Operation)
                .Include(se => se.Machine)
                .Include(se => se.ExecutionLogs)
                .FirstOrDefaultAsync(se => se.Id == id);

            if (execution == null) return NotFound();

            // Вычисляем текущее время выполнения
            if (execution.Status == "Started" && execution.StartedAt.HasValue)
            {
                var currentTime = ProductionContext.GetLocalNow() - execution.StartedAt.Value;
                ViewBag.CurrentTime = currentTime.TotalHours - (double)(execution.PauseTime ?? 0);
            }

            // Проверяем, не занят ли станок другой операцией (для возможности возобновления)
            if (execution.Status == "Paused" && execution.MachineId.HasValue)
            {
                ViewBag.CanResume = !await _context.StageExecutions
                    .AnyAsync(se => se.MachineId == execution.MachineId &&
                                se.Status == "Started" &&
                                se.Id != execution.Id);
            }

            return View(execution);
        }

        private async Task AddExecutionLog(int executionId, string action, string? @operator, string? notes)
        {
            var log = new ExecutionLog
            {
                StageExecutionId = executionId,
                Action = action,
                Operator = @operator,
                Notes = notes,
                Timestamp = ProductionContext.GetLocalNow()
            };

            _context.ExecutionLogs.Add(log);
            await _context.SaveChangesAsync();
        }


        private async Task CheckSubBatchCompletion(int subBatchId)
        {
            var subBatch = await _context.SubBatches
                .Include(sb => sb.RouteStages)
                .FirstOrDefaultAsync(sb => sb.Id == subBatchId);

            if (subBatch == null) return;

            // Проверяем, все ли основные этапы завершены
            var allMainStagesCompleted = subBatch.RouteStages
                .Where(rs => rs.StageType == "Operation")
                .All(rs => rs.Status == "Completed");

            if (allMainStagesCompleted)
            {
                subBatch.Status = "Completed";
                subBatch.CompletedAt = ProductionContext.GetLocalNow();
                await _context.SaveChangesAsync();

                // Проверяем завершение всего заказа
                await CheckProductionOrderCompletion(subBatch.ProductionOrderId);

                _logger.LogInformation($"Подпартия {subBatchId} автоматически завершена");
            }
        }

        private async Task CheckProductionOrderCompletion(int orderId)
        {
            var order = await _context.ProductionOrders
                .Include(po => po.SubBatches)
                .FirstOrDefaultAsync(po => po.Id == orderId);

            if (order == null) return;

            var allSubBatchesCompleted = order.SubBatches.All(sb => sb.Status == "Completed");

            if (allSubBatchesCompleted)
            {
                order.Status = "Completed";
                order.CompletedAt = ProductionContext.GetLocalNow();
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Производственный заказ {orderId} автоматически завершен");
            }
        }

        [HttpPost]
        public async Task<IActionResult> StartAllStages(int? subBatchId, string? @operator = "AUTO")
        {
            int startedCount = 0;
            int queuedCount = 0;
            int preparedCount = 0;
            var errors = new List<string>();

            // Используем стратегию повторных попыток
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Создаем транзакцию внутри стратегии выполнения
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // ИСПРАВЛЕНО: Сначала подготавливаем все этапы Pending -> Ready
                    // Находим все этапы в статусе Pending
                    var pendingStages = await _context.RouteStages
                        .Where(rs => rs.Status == "Pending" && rs.MachineId.HasValue)
                        .Include(rs => rs.SubBatch)
                        .OrderBy(rs => rs.SubBatch.BatchNumber)
                        .ThenBy(rs => rs.Order)
                        .ToListAsync();

                    foreach (var stage in pendingStages)
                    {
                        // Проверяем, что все предыдущие этапы завершены или в процессе
                        var previousStage = await _context.RouteStages
                            .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                            .OrderByDescending(rs => rs.Order)
                            .FirstOrDefaultAsync();

                        if (previousStage == null ||
                            previousStage.Status == "Completed" ||
                            previousStage.Status == "InProgress" ||
                            previousStage.Status == "Waiting" ||
                            previousStage.Status == "Ready")
                        {
                            // Если предыдущий этап завершен или в процессе, изменяем статус на Ready
                            stage.Status = "Ready";
                            preparedCount++;
                        }
                    }

                    // Сохраняем изменения для этапов, переведенных в Ready
                    if (preparedCount > 0)
                    {
                        await _context.SaveChangesAsync();
                    }

                    // ИСПРАВЛЕНО: Теперь получаем все этапы в статусе Ready
                    IQueryable<RouteStage> query = _context.RouteStages
                        .Where(rs => rs.Status == "Ready" && rs.MachineId.HasValue);

                    if (subBatchId.HasValue)
                    {
                        query = query.Where(rs => rs.SubBatchId == subBatchId.Value);
                    }

                    var readyStages = await query
                        .Include(rs => rs.SubBatch)
                        .ThenInclude(sb => sb.ProductionOrder)
                        .ThenInclude(po => po.Detail)
                        .OrderBy(rs => rs.SubBatch.BatchNumber)
                        .ThenBy(rs => rs.Order)
                        .ToListAsync();

                    // Группируем этапы по детали и станку
                    var groupedStages = readyStages
                        .GroupBy(rs => new {
                            DetailId = rs.SubBatch.ProductionOrder.DetailId,
                            MachineId = rs.MachineId.Value
                        })
                        .ToList();

                    // Обрабатываем каждую группу этапов
                    foreach (var group in groupedStages)
                    {
                        // Проверяем, занят ли станок
                        bool isStageRunningOnMachine = await _context.StageExecutions
                            .AnyAsync(se => se.MachineId == group.Key.MachineId && se.Status == "Started");

                        // ИСПРАВЛЕНО: Сортируем этапы внутри группы по порядку
                        var sortedStages = group.OrderBy(s => s.Order).ToList();

                        if (isStageRunningOnMachine)
                        {
                            // Станок занят - ставим все этапы в очередь
                            foreach (var stage in sortedStages)
                            {
                                // Проверяем корректные зависимости
                                bool canQueue = true;
                                var previousStage = await _context.RouteStages
                                    .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                                    .OrderByDescending(rs => rs.Order)
                                    .FirstOrDefaultAsync();

                                if (previousStage != null &&
                                    !(previousStage.Status == "Completed" ||
                                      previousStage.Status == "InProgress" ||
                                      previousStage.Status == "Waiting"))
                                {
                                    // Если предыдущий этап не готов, ставим его в очередь
                                    if (previousStage.Status == "Ready" ||
                                        previousStage.Status == "Pending")
                                    {
                                        previousStage.Status = "Waiting";
                                        previousStage.PlannedStartDate = ProductionContext.GetLocalNow();
                                        queuedCount++;

                                        _logger.LogInformation($"Предыдущий этап {previousStage.Id} добавлен в очередь для последовательности");
                                    }
                                    else
                                    {
                                        canQueue = false;
                                        errors.Add($"Этап {stage.Name} не поставлен в очередь: предыдущий этап в статусе {previousStage.Status}");
                                        continue;
                                    }
                                }

                                if (canQueue)
                                {
                                    // Добавляем этап в очередь
                                    stage.Status = "Waiting";
                                    stage.PlannedStartDate = ProductionContext.GetLocalNow();
                                    queuedCount++;

                                    _logger.LogInformation($"Этап {stage.Id} добавлен в очередь (станок занят)");
                                }
                            }
                        }
                        else
                        {
                            // Станок свободен - запускаем первый этап в группе
                            var firstStage = sortedStages.FirstOrDefault();
                            if (firstStage != null)
                            {
                                // Проверяем, завершен ли предыдущий этап
                                var previousStage = await _context.RouteStages
                                    .Where(rs => rs.SubBatchId == firstStage.SubBatchId && rs.Order < firstStage.Order)
                                    .OrderByDescending(rs => rs.Order)
                                    .FirstOrDefaultAsync();

                                bool canStart = true;
                                if (previousStage != null && previousStage.Status != "Completed")
                                {
                                    if (previousStage.Status == "Ready")
                                    {
                                        // Рекурсивно запускаем предыдущий этап
                                        var prevExecution = new StageExecution
                                        {
                                            RouteStageId = previousStage.Id,
                                            MachineId = previousStage.MachineId,
                                            Operator = @operator,
                                            Status = "Started",
                                            StartedAt = ProductionContext.GetLocalNow(),
                                            CreatedAt = ProductionContext.GetLocalNow()
                                        };

                                        _context.StageExecutions.Add(prevExecution);
                                        previousStage.Status = "InProgress";

                                        await _context.SaveChangesAsync();
                                        await AddExecutionLog(prevExecution.Id, "Started", @operator,
                                            "Этап запущен автоматически как предыдущий для последовательности");

                                        startedCount++;

                                        // Теперь первый этап из группы ставим в очередь
                                        firstStage.Status = "Waiting";
                                        firstStage.PlannedStartDate = ProductionContext.GetLocalNow();
                                        queuedCount++;

                                        canStart = false; // Предыдущий этап запущен, этот ставим в очередь
                                    }
                                    else if (previousStage.Status == "Pending")
                                    {
                                        // Предыдущий этап переводим в Ready, а текущий в Waiting
                                        previousStage.Status = "Ready";
                                        firstStage.Status = "Waiting";
                                        firstStage.PlannedStartDate = ProductionContext.GetLocalNow();

                                        preparedCount++;
                                        queuedCount++;

                                        canStart = false;
                                    }
                                    else
                                    {
                                        errors.Add($"Этап {firstStage.Name} не запущен: предыдущий этап не завершен ({previousStage.Status})");
                                        canStart = false;
                                    }
                                }

                                if (canStart)
                                {
                                    // Создаем выполнение этапа
                                    var execution = new StageExecution
                                    {
                                        RouteStageId = firstStage.Id,
                                        MachineId = firstStage.MachineId,
                                        Operator = @operator,
                                        Status = "Started",
                                        StartedAt = ProductionContext.GetLocalNow(),
                                        CreatedAt = ProductionContext.GetLocalNow()
                                    };

                                    _context.StageExecutions.Add(execution);

                                    // Обновляем статус этапа
                                    firstStage.Status = "InProgress";

                                    await _context.SaveChangesAsync();

                                    // Добавляем лог
                                    await AddExecutionLog(execution.Id, "Started", @operator, "Этап запущен массово");

                                    startedCount++;

                                    // Все остальные этапы группы добавляем в очередь
                                    foreach (var stage in sortedStages.Skip(1))
                                    {
                                        stage.Status = "Waiting";
                                        stage.PlannedStartDate = ProductionContext.GetLocalNow();
                                        queuedCount++;

                                        _logger.LogInformation($"Этап {stage.Id} добавлен в очередь (автоматически как следующий)");
                                    }
                                }
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при массовом запуске этапов");
                    errors.Add($"Ошибка при массовом запуске этапов: {ex.Message}");
                    // Не нужно делать rollback, так как при ошибке транзакция автоматически откатится
                }
            });

            var messages = new List<string>();
            if (preparedCount > 0)
            {
                messages.Add($"Подготовлено к запуску: {preparedCount} этапов");
            }
            if (startedCount > 0)
            {
                messages.Add($"Запущено: {startedCount} этапов");
            }
            if (queuedCount > 0)
            {
                messages.Add($"Добавлено в очередь: {queuedCount} этапов");
            }

            if (messages.Any())
            {
                TempData["Message"] = string.Join(". ", messages);
                _logger.LogInformation($"Массовый запуск: {string.Join(", ", messages)}");
            }
            else if (errors.Any())
            {
                TempData["Warning"] = string.Join("<br>", errors);
            }
            else
            {
                TempData["Warning"] = "Нет доступных этапов для запуска";
            }

            return RedirectToAction("Index", "RouteStages", new { subBatchId });
        }

        [HttpPost]
        public async Task<IActionResult> CompleteAllStages(int? subBatchId, string? notes = null)
        {
            int completedCount = 0;
            var errors = new List<string>();

            // Используем стратегию повторных попыток
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Создаем транзакцию внутри стратегии выполнения
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    IQueryable<RouteStage> query = _context.RouteStages
                        .Where(rs => (rs.Status == "InProgress" || rs.Status == "Paused") && rs.MachineId.HasValue);

                    if (subBatchId.HasValue)
                    {
                        query = query.Where(rs => rs.SubBatchId == subBatchId.Value);
                    }

                    var activeStages = await query
                        .Include(rs => rs.StageExecutions)
                        .Include(rs => rs.SubBatch)
                        .OrderBy(rs => rs.SubBatch.BatchNumber)
                        .ThenBy(rs => rs.Order)
                        .ToListAsync();

                    foreach (var stage in activeStages)
                    {
                        try
                        {
                            var execution = stage.StageExecutions
                                .FirstOrDefault(se => se.Status == "Started" || se.Status == "Paused");

                            if (execution == null) continue;

                            // Вычисляем фактическое время
                            var actualMinutes = await CalculateActualWorkTimeInMinutes(execution);

                            // Преобразуем минуты в часы и округляем до 2 знаков
                            execution.ActualTime = Math.Round((decimal)(actualMinutes / 60.0), 2);
                            if (execution.ActualTime < 0.01m) execution.ActualTime = 0.01m;

                            execution.Status = "Completed";
                            execution.CompletedAt = ProductionContext.GetLocalNow();
                            execution.Notes = notes;

                            // Обновляем статус этапа
                            stage.Status = "Completed";

                            // Добавляем лог
                            await AddExecutionLog(execution.Id, "Completed", execution.Operator,
                                $"Этап завершен массово. {notes}");

                            completedCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Ошибка при завершении этапа {stage.Name}: {ex.Message}");
                        }
                    }

                    await _context.SaveChangesAsync();

                    // Для каждой затронутой подпартии проверяем завершение
                    var affectedSubBatchIds = activeStages
                        .Select(s => s.SubBatchId)
                        .Distinct()
                        .ToList();

                    foreach (var sbId in affectedSubBatchIds)
                    {
                        await CheckSubBatchCompletion(sbId);
                    }

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при массовом завершении этапов");
                    errors.Add($"Ошибка при массовом завершении этапов: {ex.Message}");
                    // Не нужно делать rollback, так как при ошибке транзакция автоматически откатится
                }
            });

            if (errors.Any())
            {
                TempData["Warning"] = string.Join("<br>", errors);
            }

            if (completedCount > 0)
            {
                _logger.LogInformation($"Массово завершено {completedCount} этапов");
                TempData["Message"] = $"Успешно завершено {completedCount} этапов";
            }
            else if (!errors.Any())
            {
                TempData["Warning"] = "Нет активных этапов для завершения";
            }

            return RedirectToAction("Index", "RouteStages", new { subBatchId });
        }

        public async Task<IActionResult> BulkEditTime(int subBatchId)
        {
            var completedExecutions = await _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .Where(se => se.RouteStage.SubBatchId == subBatchId && se.Status == "Completed")
                .OrderBy(se => se.RouteStage.Order)
                .ToListAsync();

            ViewBag.SubBatchId = subBatchId;
            return View(completedExecutions);
        }

        public class BulkTimeEditModel
        {
            public List<ExecutionTimeModel> Executions { get; set; } = new List<ExecutionTimeModel>();
            public string BulkReason { get; set; } = string.Empty;
        }

        public class ExecutionTimeModel
        {
            public int Id { get; set; }
            public decimal ActualTime { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveBulkTimes(BulkTimeEditModel model, int subBatchId)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Проверьте правильность заполнения формы";
                return RedirectToAction("BulkEditTime", new { subBatchId });
            }

            // Используем стратегию повторных попыток
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    int changedCount = 0;

                    foreach (var executionModel in model.Executions)
                    {
                        var execution = await _context.StageExecutions.FindAsync(executionModel.Id);
                        if (execution == null || execution.Status != "Completed") continue;

                        if (execution.ActualTime != executionModel.ActualTime)
                        {
                            var oldTime = execution.ActualTime;
                            execution.ActualTime = executionModel.ActualTime;

                            // Обновляем заметки с информацией о корректировке
                            var correctionNote = $"\nВремя скорректировано {DateTime.Now:dd.MM.yyyy HH:mm}: с {oldTime:F2} на {executionModel.ActualTime:F2} ч. Причина: {model.BulkReason}";
                            execution.Notes = (execution.Notes ?? "") + correctionNote;

                            // Добавляем лог о корректировке
                            await AddExecutionLog(execution.Id, "TimeModified", execution.Operator,
                                $"Время изменено с {oldTime:F2} на {executionModel.ActualTime:F2} ч при массовом редактировании. Причина: {model.BulkReason}");

                            changedCount++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    if (changedCount > 0)
                    {
                        _logger.LogInformation($"Массово обновлено время для {changedCount} этапов");
                        TempData["Message"] = $"Фактическое время обновлено для {changedCount} этапов";
                    }
                    else
                    {
                        TempData["Warning"] = "Изменений не было произведено";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при массовом редактировании времени");
                    TempData["Error"] = $"Ошибка при массовом редактировании времени: {ex.Message}";
                }

                return RedirectToAction("Index", "RouteStages", new { subBatchId });
            });
        }

    }
}