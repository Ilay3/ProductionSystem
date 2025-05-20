using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Services
{
    public interface IStageAutomationService
    {
        /// <summary>
        /// Проверяет готовые этапы и автоматически запускает их на свободных станках
        /// </summary>
        Task ProcessAutomaticStageExecution();

        /// <summary>
        /// Ставит этап в очередь ожидания
        /// </summary>
        Task<bool> AddStageToQueue(int routeStageId);

        /// <summary>
        /// Убирает этап из очереди
        /// </summary>
        Task<bool> RemoveStageFromQueue(int routeStageId);

        /// <summary>
        /// Получает прогнозируемое время начала этапа
        /// </summary>
        Task<DateTime?> GetEstimatedStartTime(int routeStageId);

        /// <summary>
        /// Освобождает станок (ставит текущую операцию на паузу)
        /// </summary>
        Task<bool> ReleaseMachine(int machineId, int routeStageId, string reason);
    }

    public class StageAutomationService : IStageAutomationService
    {
        private readonly ProductionContext _context;
        private readonly IStageAssignmentService _stageAssignmentService;
        private readonly ILogger<StageAutomationService> _logger;
        private readonly IShiftService _shiftService;

        // Добавляем время последнего запуска для предотвращения частых запусков
        private static DateTime _lastProcessingTime = DateTime.MinValue;
        // Минимальный интервал между запусками автоматики в секундах
        private const int MIN_PROCESSING_INTERVAL = 10;
        // Добавляем счетчик активных обработок для предотвращения параллельных запусков
        private static int _processingCount = 0;

        public StageAutomationService(
            ProductionContext context,
            IStageAssignmentService stageAssignmentService,
            IShiftService shiftService,
            ILogger<StageAutomationService> logger)
        {
            _context = context;
            _stageAssignmentService = stageAssignmentService;
            _shiftService = shiftService;
            _logger = logger;
        }

        public async Task ProcessAutomaticStageExecution()
        {
            // Проверяем интервал времени с последнего запуска
            var now = DateTime.UtcNow;
            var timeSinceLastProcessing = (now - _lastProcessingTime).TotalSeconds;

            if (timeSinceLastProcessing < MIN_PROCESSING_INTERVAL)
            {
                _logger.LogDebug($"Пропуск автоматического запуска: прошло только {timeSinceLastProcessing:F1} сек.");
                return;
            }

            // Проверяем, не запущена ли уже обработка
            if (System.Threading.Interlocked.CompareExchange(ref _processingCount, 1, 0) != 0)
            {
                _logger.LogDebug("Пропуск автоматической обработки: уже выполняется");
                return;
            }

            try
            {
                _lastProcessingTime = now;
                _logger.LogInformation("Запуск автоматической обработки этапов");

                // Получаем все этапы в статусе Ready (готовы к выполнению)
                var readyStages = await _context.RouteStages
                    .Include(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(rs => rs.Operation)
                    .ThenInclude(o => o.MachineType)
                    .Where(rs => rs.Status == "Ready")
                    .OrderBy(rs => rs.SubBatch.ProductionOrder.CreatedAt) // Приоритет по времени создания заказа
                    .ThenBy(rs => rs.Order) // Затем по порядку в маршруте
                    .Take(10) // Обрабатываем только первые 10, чтобы не перегрузить систему
                    .ToListAsync();

                // Проверяем рабочее время перед обработкой
                var currentDateTime = ProductionContext.GetLocalNow();
                int processedCount = 0;

                foreach (var stage in readyStages)
                {
                    // Если есть назначенный станок, проверяем, работает ли он сейчас
                    if (stage.MachineId.HasValue && !_shiftService.IsWorkingTime(currentDateTime, stage.MachineId))
                    {
                        _logger.LogInformation($"Пропуск этапа {stage.Id}: станок {stage.MachineId} не в рабочей смене");
                        continue;
                    }

                    // ИСПРАВЛЕНИЕ: Проверяем завершенность предыдущего этапа
                    if (!await IsPreviousStageCompleted(stage))
                    {
                        _logger.LogInformation($"Пропуск этапа {stage.Id}: предыдущий этап не завершен");
                        // Добавляем этап в очередь вместо пропуска
                        await AddStageToQueue(stage.Id);
                        continue;
                    }

                    await ProcessSingleStage(stage);
                    processedCount++;

                    // Обрабатываем не более 3 этапов за один запуск
                    if (processedCount >= 3)
                    {
                        _logger.LogInformation("Достигнут лимит обработки этапов за один запуск");
                        break;
                    }
                }

                // Обрабатываем этапы в очереди только если есть свободные станки
                await ProcessStagesInQueue();

                _logger.LogInformation($"Обработано {processedCount} готовых этапов из {readyStages.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка во время автоматической обработки этапов");
            }
            finally
            {
                // Сбрасываем счетчик активных обработок
                System.Threading.Interlocked.Exchange(ref _processingCount, 0);
            }
        }

        private async Task ProcessSingleStage(RouteStage stage)
        {
            try
            {
                _logger.LogInformation($"Обработка этапа {stage.Id} ({stage.Name})");

                // Получаем подходящие станки
                var suitableMachines = await GetSuitableMachines(stage);

                if (!suitableMachines.Any())
                {
                    _logger.LogInformation($"Пропуск этапа {stage.Id}: не найдено подходящих станков");
                    return;
                }

                // Ищем свободный станок
                var freeMachine = await FindFreeMachine(suitableMachines);

                if (freeMachine != null)
                {
                    // Назначаем станок и запускаем этап
                    await AssignMachineAndStartStage(stage, freeMachine);
                }
                else
                {
                    // Проверяем, что этап еще не в очереди
                    if (stage.Status != "Waiting")
                    {
                        // Ставим в очередь
                        await AddStageToQueue(stage.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обработке этапа {stage.Id}");
            }
        }

        private async Task<bool> IsPreviousStageCompleted(RouteStage stage)
        {
            var previousStage = await _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefaultAsync();

            return previousStage == null || previousStage.Status == "Completed";
        }

        private async Task<List<Machine>> GetSuitableMachines(RouteStage stage)
        {
            if (stage.Operation?.MachineTypeId == null)
                return new List<Machine>();

            return await _context.Machines
                .Where(m => m.MachineTypeId == stage.Operation.MachineTypeId)
                .OrderBy(m => m.Priority) // Сортируем по приоритету
                .ToListAsync();
        }

        private async Task<Machine?> FindFreeMachine(List<Machine> machines)
        {
            foreach (var machine in machines)
            {
                // Изменено! Теперь станок считается занятым, только если на нем есть активные операции в статусе "Started"
                // Если операция на паузе (Paused), станок считается доступным для новых операций
                var hasActiveExecution = await _context.StageExecutions
                    .AnyAsync(se => se.MachineId == machine.Id && se.Status == "Started");

                if (!hasActiveExecution)
                {
                    return machine;
                }
            }

            return null;
        }

        private async Task AssignMachineAndStartStage(RouteStage stage, Machine machine)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Проверяем, не занят ли станок другой операцией
                var isStageRunningOnMachine = await _context.StageExecutions
                    .AnyAsync(se => se.MachineId == machine.Id && se.Status == "Started");

                if (isStageRunningOnMachine)
                {
                    _logger.LogWarning($"Невозможно запустить этап {stage.Id} - станок {machine.Id} занят");
                    await transaction.RollbackAsync();
                    return;
                }

                // Проверяем, нужна ли переналадка
                var needChangeover = await CheckNeedChangeover(stage, machine);

                if (needChangeover)
                {
                    // Создаем этап переналадки
                    var changeoverStage = await CreateChangeoverStage(stage, machine);
                    if (changeoverStage != null)
                    {
                        // Запускаем переналадку
                        await StartStageExecution(changeoverStage, machine.Id, "AUTO");

                        // Не запускаем основной этап сразу - он запустится после выполнения переналадки
                        _logger.LogInformation($"Этап {stage.Id} ожидает завершения переналадки {changeoverStage.Id}");

                        await transaction.CommitAsync();
                        return;
                    }
                }

                // Назначаем станок
                stage.MachineId = machine.Id;
                stage.Status = "InProgress";
                await _context.SaveChangesAsync();

                // Создаем и запускаем выполнение этапа
                await StartStageExecution(stage, machine.Id, "AUTO");

                await transaction.CommitAsync();

                _logger.LogInformation($"Этап {stage.Id} автоматически запущен на станке {machine.Name}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Ошибка запуска этапа {stage.Id} на станке {machine.Name}");
                throw;
            }
        }

        private async Task<bool> CheckNeedChangeover(RouteStage stage, Machine machine)
        {
            if (stage.SubBatch?.ProductionOrder?.DetailId == null)
                return false;

            var lastDetailId = await _stageAssignmentService.GetLastDetailOnMachine(machine.Id);
            var currentDetailId = stage.SubBatch.ProductionOrder.DetailId;

            return lastDetailId.HasValue && lastDetailId.Value != currentDetailId;
        }

        private async Task<RouteStage?> CreateChangeoverStage(RouteStage stage, Machine machine)
        {
            try
            {
                var lastDetailId = await _stageAssignmentService.GetLastDetailOnMachine(machine.Id);
                if (!lastDetailId.HasValue) return null;

                var currentDetailId = stage.SubBatch.ProductionOrder.DetailId;

                // Получаем время переналадки
                var changeoverTime = await _stageAssignmentService.GetChangeoverTime(machine.Id, lastDetailId.Value, currentDetailId);

                // Проверяем, нет ли уже созданной переналадки для этого этапа
                var existingChangeover = await _context.RouteStages
                    .Where(rs => rs.SubBatchId == stage.SubBatchId &&
                                rs.StageType == "Changeover" &&
                                rs.Order < stage.Order &&
                                rs.Status != "Completed")
                    .OrderByDescending(rs => rs.Order)
                    .FirstOrDefaultAsync();

                if (existingChangeover != null)
                {
                    _logger.LogInformation($"Обнаружена существующая переналадка {existingChangeover.Id} для этапа {stage.Id}");
                    return existingChangeover;
                }

                // Проверяем минимальное время переналадки
                if (changeoverTime < 0.01m)
                    changeoverTime = 0.01m;

                // Создаем этап переналадки
                var changeoverStage = new RouteStage
                {
                    SubBatchId = stage.SubBatchId,
                    MachineId = machine.Id,
                    StageNumber = $"{stage.StageNumber}_CO",
                    Name = $"Переналадка для {stage.Name}",
                    StageType = "Changeover",
                    Order = stage.Order - 1,
                    PlannedTime = changeoverTime,
                    Quantity = 1, // Всегда 1 для переналадки
                    Status = "Ready", // Устанавливаем статус Ready вместо сразу InProgress
                    CreatedAt = ProductionContext.GetLocalNow()
                };

                _context.RouteStages.Add(changeoverStage);

                // Корректируем порядок следующих этапов если нужно
                var subsequentStages = await _context.RouteStages
                    .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order >= stage.Order && rs.Id != stage.Id)
                    .ToListAsync();

                foreach (var s in subsequentStages)
                {
                    s.Order += 1;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Создана переналадка {changeoverStage.Id} для этапа {stage.Id}, время: {changeoverTime} ч");

                return changeoverStage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при создании переналадки для этапа {stage.Id}");
                return null;
            }
        }

        private async Task StartStageExecution(RouteStage stage, int machineId, string operatorName)
        {
            stage.Status = "InProgress";

            // Создаем выполнение этапа
            var execution = new StageExecution
            {
                RouteStageId = stage.Id,
                MachineId = machineId,
                Status = "Started",
                StartedAt = ProductionContext.GetLocalNow(),
                Operator = operatorName,
                CreatedAt = ProductionContext.GetLocalNow()
            };

            _context.StageExecutions.Add(execution);
            await _context.SaveChangesAsync();

            // Добавляем лог
            await AddExecutionLog(execution.Id, "Started", operatorName, "Автоматический запуск");

            _logger.LogInformation($"Этап {stage.Id} {stage.Name} запущен на станке {machineId}, исполнитель: {operatorName}");
        }

        public async Task<bool> AddStageToQueue(int routeStageId)
        {
            try
            {
                var stage = await _context.RouteStages
                    .Include(rs => rs.Operation)
                    .Include(rs => rs.SubBatch) // ИСПРАВЛЕНИЕ: Добавляем связь с SubBatch
                    .ThenInclude(sb => sb.ProductionOrder) // ИСПРАВЛЕНИЕ: Добавляем связь с ProductionOrder
                    .FirstOrDefaultAsync(rs => rs.Id == routeStageId);

                if (stage == null)
                {
                    _logger.LogWarning($"Не удалось добавить в очередь этап {routeStageId}: не найден");
                    return false;
                }

                // ИСПРАВЛЕНИЕ: Проверяем, завершен ли предыдущий этап
                bool canAddToQueue = true;

                if (stage.Status != "Ready")
                {
                    _logger.LogWarning($"Не удалось добавить в очередь этап {routeStageId}: этап не в статусе Ready");
                    canAddToQueue = false;
                }

                // Проверяем, завершены ли все предыдущие этапы маршрута
                var previousStages = await _context.RouteStages
                    .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                    .ToListAsync();

                var incompleteStages = previousStages.Where(rs => rs.Status != "Completed").ToList();

                if (incompleteStages.Any())
                {
                    _logger.LogWarning($"Этап {routeStageId} имеет незавершенные предыдущие этапы. Проверка возможности добавления в очередь.");

                    // Проверяем, можно ли добавить предыдущие этапы в очередь
                    foreach (var prevStage in incompleteStages.OrderBy(s => s.Order))
                    {
                        if (prevStage.Status == "Ready")
                        {
                            // Добавляем предыдущий этап в очередь
                            await AddStageToQueue(prevStage.Id);
                        }
                        else if (prevStage.Status != "Waiting" && prevStage.Status != "InProgress")
                        {
                            _logger.LogWarning($"Предыдущий этап {prevStage.Id} в статусе {prevStage.Status}, невозможно добавить текущий этап в очередь");
                            canAddToQueue = false;
                            break;
                        }
                    }
                }

                if (!canAddToQueue)
                {
                    return false;
                }

                // Если всё в порядке, добавляем в очередь
                stage.Status = "Waiting";

                // Приоритезация (сохраняем текущее время для сортировки в очереди)
                stage.PlannedStartDate = ProductionContext.GetLocalNow();

                await _context.SaveChangesAsync();

                // Добавляем лог
                await AddExecutionLog(
                    (await _context.StageExecutions.FirstOrDefaultAsync(se => se.RouteStageId == routeStageId))?.Id,
                    "QueueAdded",
                    "SYSTEM",
                    "Этап добавлен в очередь"
                );

                _logger.LogInformation($"Этап {routeStageId} добавлен в очередь");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при добавлении этапа {routeStageId} в очередь");
                return false;
            }
        }

        public async Task<bool> RemoveStageFromQueue(int routeStageId)
        {
            try
            {
                var stage = await _context.RouteStages.FindAsync(routeStageId);
                if (stage == null || stage.Status != "Waiting")
                {
                    _logger.LogWarning($"Не удалось удалить из очереди этап {routeStageId}: не найден или не в очереди");
                    return false;
                }

                stage.Status = "Ready";
                await _context.SaveChangesAsync();

                // Добавляем лог
                await AddExecutionLog(
                    (await _context.StageExecutions.FirstOrDefaultAsync(se => se.RouteStageId == routeStageId))?.Id,
                    "QueueRemoved",
                    "SYSTEM",
                    "Этап удален из очереди"
                );

                _logger.LogInformation($"Этап {routeStageId} удален из очереди");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении этапа {routeStageId} из очереди");
                return false;
            }
        }

        public async Task<DateTime?> GetEstimatedStartTime(int routeStageId)
        {
            try
            {
                var stage = await _context.RouteStages
                    .Include(rs => rs.Operation)
                    .ThenInclude(o => o.MachineType)
                    .FirstOrDefaultAsync(rs => rs.Id == routeStageId);

                if (stage?.Operation?.MachineTypeId == null)
                    return null;

                // Получаем все активные выполнения на станках этого типа
                var activeExecutions = await _context.StageExecutions
                    .Include(se => se.RouteStage)
                    .Include(se => se.Machine)
                    .Where(se => se.Machine.MachineTypeId == stage.Operation.MachineTypeId &&
                                (se.Status == "Started" || se.Status == "Paused"))
                    .OrderBy(se => se.StartedAt)
                    .ToListAsync();

                if (!activeExecutions.Any())
                    return ProductionContext.GetLocalNow(); // Станки свободны

                // Получаем все этапы в очереди для этого типа станка, которые идут перед текущим
                var waitingStagesCount = await _context.RouteStages
                    .Where(rs => rs.Status == "Waiting" &&
                               rs.Operation != null &&
                               rs.Operation.MachineTypeId == stage.Operation.MachineTypeId &&
                               rs.PlannedStartDate < stage.PlannedStartDate)
                    .CountAsync();

                // Вычисляем примерное время освобождения
                var currentTime = ProductionContext.GetLocalNow();
                var machinesCount = await _context.Machines
                    .Where(m => m.MachineTypeId == stage.Operation.MachineTypeId)
                    .CountAsync();

                var estimatedEndTimes = new List<DateTime>();

                // Время освобождения для каждого активного выполнения
                foreach (var execution in activeExecutions)
                {
                    if (execution.StartedAt.HasValue)
                    {
                        var elapsedTime = currentTime - execution.StartedAt.Value;
                        var elapsedHours = (decimal)elapsedTime.TotalHours;
                        if (execution.PauseTime.HasValue)
                        {
                            elapsedHours -= execution.PauseTime.Value;
                        }

                        // ИСПРАВЛЕНИЕ: Учитываем, что PlannedTime теперь учитывает количество деталей
                        var remainingTime = execution.RouteStage.PlannedTime - elapsedHours;
                        if (remainingTime <= 0)
                            remainingTime = 0.1m; // Минимум 6 минут если задача должна быть уже завершена

                        var estimatedEndTime = currentTime.AddHours((double)remainingTime);
                        estimatedEndTimes.Add(estimatedEndTime);
                    }
                }

                // Сортируем времена освобождения
                estimatedEndTimes.Sort();

                // Рассчитываем индекс ожидания на основе позиции в очереди и количества станков
                var waitIndex = Math.Max(0, waitingStagesCount - Math.Max(0, machinesCount - activeExecutions.Count));
                if (waitIndex >= estimatedEndTimes.Count)
                    waitIndex = estimatedEndTimes.Count - 1;

                if (waitIndex >= 0 && estimatedEndTimes.Count > 0)
                {
                    return estimatedEndTimes[waitIndex];
                }

                return currentTime.AddMinutes(30); // По умолчанию через 30 минут
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при расчете прогнозируемого времени для этапа {routeStageId}");
                return ProductionContext.GetLocalNow().AddHours(1); // По умолчанию через час при ошибке
            }
        }

        private async Task ProcessStagesInQueue()
        {
            try
            {
                // Получаем список типов станков с освободившимися станками
                var availableMachineTypes = await _context.Machines
                    .Where(m => !_context.StageExecutions.Any(se =>
                        se.MachineId == m.Id && se.Status == "Started"))
                    .Select(m => m.MachineTypeId)
                    .Distinct()
                    .ToListAsync();

                if (!availableMachineTypes.Any())
                {
                    _logger.LogDebug("Нет свободных станков для обработки очереди");
                    return;
                }

                int processedQueueCount = 0;

                foreach (var machineTypeId in availableMachineTypes)
                {
                    // Получаем все этапы в очереди для этого типа станка
                    var waitingStages = await _context.RouteStages
                        .Include(rs => rs.SubBatch)
                        .ThenInclude(sb => sb.ProductionOrder)
                        .Include(rs => rs.Operation)
                        .Where(rs => rs.Status == "Waiting" &&
                                    rs.Operation != null &&
                                    rs.Operation.MachineTypeId == machineTypeId)
                        .OrderBy(rs => rs.PlannedStartDate) // Сортировка по времени добавления в очередь
                        .Take(5) // Берем только первые 5 для оптимизации
                        .ToListAsync();

                    // ИСПРАВЛЕНИЕ: Группируем этапы по подпартии и обрабатываем в правильном порядке
                    // Это обеспечит соблюдение последовательности операций
                    var stagesBySubBatch = waitingStages
                        .GroupBy(rs => rs.SubBatchId)
                        .ToDictionary(g => g.Key, g => g.OrderBy(rs => rs.Order).ToList());

                    foreach (var subBatchGroup in stagesBySubBatch)
                    {
                        var stages = subBatchGroup.Value;

                        // Берем только первый этап из каждой подпартии
                        var stage = stages.FirstOrDefault();
                        if (stage == null) continue;

                        // Проверяем, что предыдущий этап завершен
                        if (!await IsPreviousStageCompleted(stage))
                        {
                            _logger.LogInformation($"Пропуск этапа {stage.Id} из очереди: предыдущий этап не завершен");
                            continue;
                        }

                        // Получаем подходящие станки
                        var suitableMachines = await GetSuitableMachines(stage);

                        // Проверяем рабочее время для каждого станка
                        var currentDateTime = ProductionContext.GetLocalNow();
                        var availableMachines = suitableMachines
                            .Where(m => _shiftService.IsWorkingTime(currentDateTime, m.Id))
                            .ToList();

                        // Ищем свободный станок
                        var freeMachine = await FindFreeMachine(availableMachines);

                        if (freeMachine != null)
                        {
                            // Изменяем статус с Waiting на Ready
                            stage.Status = "Ready";
                            await _context.SaveChangesAsync();

                            _logger.LogInformation($"Этап {stage.Id} переведен из очереди в статус Ready для запуска");

                            // Назначаем станок и запускаем этап
                            await AssignMachineAndStartStage(stage, freeMachine);

                            processedQueueCount++;

                            // Обрабатываем не более 2 этапов из очереди за один запуск
                            if (processedQueueCount >= 2)
                            {
                                _logger.LogInformation("Достигнут лимит обработки этапов из очереди");
                                return;
                            }
                        }
                    }
                }

                _logger.LogInformation($"Обработано {processedQueueCount} этапов из очереди");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке этапов из очереди");
            }
        }

        public async Task<bool> ReleaseMachine(int machineId, int urgentStageId, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Находим текущее выполнение на станке
                var currentExecution = await _context.StageExecutions
                    .Include(se => se.RouteStage)
                    .Include(se => se.Machine)
                    .ThenInclude(m => m.MachineType)
                    .FirstOrDefaultAsync(se => se.MachineId == machineId &&
                                            (se.Status == "Started"));

                if (currentExecution == null)
                {
                    _logger.LogWarning($"Не удалось освободить станок {machineId}: не найдено активных выполнений");
                    return false;
                }

                // Ставим текущее выполнение на паузу
                currentExecution.Status = "Paused";
                currentExecution.PausedAt = ProductionContext.GetLocalNow();
                currentExecution.RouteStage.Status = "Paused";

                // Добавляем лог
                await AddExecutionLog(currentExecution.Id, "Paused", "AUTO", $"Станок освобожден для срочной задачи: {reason}");
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Текущее выполнение {currentExecution.Id} поставлено на паузу");

                // Запускаем срочный этап
                if (urgentStageId > 0)
                {
                    var urgentStage = await _context.RouteStages
                        .Include(rs => rs.SubBatch)
                        .ThenInclude(sb => sb.ProductionOrder)
                        .FirstOrDefaultAsync(rs => rs.Id == urgentStageId);

                    if (urgentStage != null)
                    {
                        // Проверяем, что этап в статусе Ready
                        if (urgentStage.Status != "Ready")
                        {
                            urgentStage.Status = "Ready";
                            await _context.SaveChangesAsync();
                        }

                        // Назначаем станок
                        urgentStage.MachineId = machineId;
                        await _context.SaveChangesAsync();

                        // Запускаем этап
                        await StartStageExecution(urgentStage, machineId, "URGENT");

                        _logger.LogInformation($"Срочный этап {urgentStageId} запущен на освобожденном станке {machineId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Срочный этап {urgentStageId} не найден");
                    }
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Станок {machineId} освобожден для срочного этапа {urgentStageId}. Причина: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Ошибка освобождения станка {machineId}");
                return false;
            }
        }

        private async Task AddExecutionLog(int? executionId, string action, string operatorName, string notes)
        {
            if (!executionId.HasValue) return;

            var log = new ExecutionLog
            {
                StageExecutionId = executionId.Value,
                Action = action,
                Operator = operatorName,
                Notes = notes,
                Timestamp = ProductionContext.GetLocalNow()
            };

            _context.ExecutionLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}