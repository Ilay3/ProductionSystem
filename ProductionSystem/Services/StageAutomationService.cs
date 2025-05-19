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
            try
            {
                _logger.LogInformation("Starting automatic stage execution processing");

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
                    .ToListAsync();

                // Проверяем рабочее время перед обработкой
                var currentDateTime = ProductionContext.GetLocalNow();
                foreach (var stage in readyStages)
                {
                    // Если есть назначенный станок, проверяем, работает ли он сейчас
                    if (stage.MachineId.HasValue && !_shiftService.IsWorkingTime(currentDateTime, stage.MachineId))
                    {
                        _logger.LogInformation($"Skipping stage {stage.Id} as machine {stage.MachineId} is not in working shift");
                        continue;
                    }

                    await ProcessSingleStage(stage);
                }

                // Обрабатываем этапы в очереди
                await ProcessStagesInQueue();

                _logger.LogInformation($"Processed {readyStages.Count} ready stages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during automatic stage execution processing");
            }
        }


        private async Task ProcessSingleStage(RouteStage stage)
        {
            // Проверяем, что предыдущий этап завершен
            if (!await IsPreviousStageCompleted(stage))
            {
                return;
            }

            // Получаем подходящие станки
            var suitableMachines = await GetSuitableMachines(stage);

            // Ищем свободный станок
            var freeMachine = await FindFreeMachine(suitableMachines);

            if (freeMachine != null)
            {
                // Назначаем станок и запускаем этап
                await AssignMachineAndStartStage(stage, freeMachine);
            }
            else
            {
                // Ставим в очередь
                await AddStageToQueue(stage.Id);
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
                // Проверяем, есть ли активные выполнения на этом станке
                var hasActiveExecution = await _context.StageExecutions
                    .AnyAsync(se => se.MachineId == machine.Id &&
                                  (se.Status == "Started" || se.Status == "Paused"));

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
                    .AnyAsync(se => se.MachineId == machine.Id &&
                                  (se.Status == "Started" || se.Status == "Paused"));

                if (isStageRunningOnMachine)
                {
                    _logger.LogWarning($"Cannot start stage {stage.Id} - machine {machine.Id} is busy");
                    await transaction.RollbackAsync();
                    return;
                }

                // Проверяем, нужна ли переналадка
                await CheckAndCreateChangeover(stage, machine);

                // Назначаем станок
                stage.MachineId = machine.Id;
                stage.Status = "InProgress";

                // Создаем выполнение этапа
                var execution = new StageExecution
                {
                    RouteStageId = stage.Id,
                    MachineId = machine.Id,
                    Status = "Started",
                    StartedAt = ProductionContext.GetLocalNow(),
                    Operator = "AUTO", // Автоматический запуск
                    CreatedAt = ProductionContext.GetLocalNow()
                };

                _context.StageExecutions.Add(execution);
                await _context.SaveChangesAsync();

                // Добавляем лог
                await AddExecutionLog(execution.Id, "Started", "AUTO", "Автоматический запуск");

                await transaction.CommitAsync();

                _logger.LogInformation($"Stage {stage.Id} automatically started on machine {machine.Name}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error starting stage {stage.Id} on machine {machine.Name}");
                throw;
            }
        }


        private async Task CheckAndCreateChangeover(RouteStage stage, Machine machine)
        {
            if (stage.SubBatch?.ProductionOrder?.DetailId == null)
                return;

            var lastDetailId = await _stageAssignmentService.GetLastDetailOnMachine(machine.Id);
            var currentDetailId = stage.SubBatch.ProductionOrder.DetailId;

            if (lastDetailId.HasValue && lastDetailId.Value != currentDetailId)
            {
                // Создаем этап переналадки
                var changeoverTime = await _stageAssignmentService.GetChangeoverTime(machine.Id, lastDetailId.Value, currentDetailId);

                var changeoverStage = new RouteStage
                {
                    SubBatchId = stage.SubBatchId,
                    MachineId = machine.Id,
                    StageNumber = $"{stage.StageNumber}_CO",
                    Name = $"Переналадка для {stage.Name}",
                    StageType = "Changeover",
                    Order = stage.Order - 1,
                    PlannedTime = changeoverTime,
                    Quantity = 1,
                    Status = "InProgress",
                    CreatedAt = ProductionContext.GetLocalNow()
                };

                _context.RouteStages.Add(changeoverStage);

                // Корректируем порядок следующих этапов
                var subsequentStages = await _context.RouteStages
                    .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order >= stage.Order && rs.Id != stage.Id)
                    .ToListAsync();

                foreach (var s in subsequentStages)
                {
                    s.Order += 1;
                }

                await _context.SaveChangesAsync();

                // Создаем выполнение для переналадки
                var changeoverExecution = new StageExecution
                {
                    RouteStageId = changeoverStage.Id,
                    MachineId = machine.Id,
                    Status = "Started",
                    StartedAt = ProductionContext.GetLocalNow(),
                    Operator = "AUTO",
                    CreatedAt = ProductionContext.GetLocalNow()
                };

                _context.StageExecutions.Add(changeoverExecution);
                await _context.SaveChangesAsync();

                // Автоматически завершаем переналадку
                changeoverExecution.Status = "Completed";
                changeoverExecution.CompletedAt = ProductionContext.GetLocalNow();
                changeoverExecution.ActualTime = changeoverTime;
                changeoverStage.Status = "Completed";

                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> AddStageToQueue(int routeStageId)
        {
            var stage = await _context.RouteStages
                .Include(rs => rs.Operation)
                .FirstOrDefaultAsync(rs => rs.Id == routeStageId);

            if (stage == null || stage.Status != "Ready")
                return false;

            stage.Status = "Waiting";

            // Приоритезация (сохраняем текущее время для сортировки в очереди)
            stage.PlannedStartDate = ProductionContext.GetLocalNow();

            await _context.SaveChangesAsync();

            _logger.LogInformation($"Stage {routeStageId} added to queue");
            return true;
        }


        public async Task<bool> RemoveStageFromQueue(int routeStageId)
        {
            var stage = await _context.RouteStages.FindAsync(routeStageId);
            if (stage == null || stage.Status != "Waiting")
                return false;

            stage.Status = "Ready";
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Stage {routeStageId} removed from queue");
            return true;
        }

        public async Task<DateTime?> GetEstimatedStartTime(int routeStageId)
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

            // Вычисляем примерное время освобождения
            var earliestFreeTime = ProductionContext.GetLocalNow();

            foreach (var execution in activeExecutions)
            {
                if (execution.StartedAt.HasValue)
                {
                    var elapsedTime = ProductionContext.GetLocalNow() - execution.StartedAt.Value;
                    var remainingTime = execution.RouteStage.PlannedTime - (decimal)elapsedTime.TotalHours;

                    if (remainingTime > 0)
                    {
                        var estimatedEndTime = ProductionContext.GetLocalNow().AddHours((double)remainingTime);
                        if (estimatedEndTime < earliestFreeTime)
                            earliestFreeTime = estimatedEndTime;
                    }
                }
            }

            return earliestFreeTime;
        }


        // ProductionSystem/Services/StageAutomationService.cs - обновить метод
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
                                            (se.Status == "Started" || se.Status == "Paused"));

                if (currentExecution == null)
                    return false;

                // Ставим текущее выполнение на паузу
                currentExecution.Status = "Paused";
                currentExecution.PausedAt = ProductionContext.GetLocalNow();
                currentExecution.RouteStage.Status = "Paused";

                // Добавляем лог
                await AddExecutionLog(currentExecution.Id, "Paused", "AUTO", reason);
                await _context.SaveChangesAsync();

                // Ищем ожидающие операции для этого типа станка
                var machineTypeId = currentExecution.Machine?.MachineTypeId;
                if (machineTypeId.HasValue)
                {
                    await StartWaitingStages(machineTypeId.Value);
                }

                // Запускаем срочный этап
                if (urgentStageId > 0)
                {
                    var urgentStage = await _context.RouteStages.FindAsync(urgentStageId);
                    if (urgentStage != null)
                    {
                        var machine = await _context.Machines.FindAsync(machineId);
                        await AssignMachineAndStartStage(urgentStage, machine!);
                    }
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Machine {machineId} released for urgent stage {urgentStageId}. Reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error releasing machine {machineId}");
                return false;
            }
        }
        private async Task StartWaitingStages(int machineTypeId)
        {
            // Получаем все этапы в очереди для этого типа станка
            var waitingStages = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .Include(rs => rs.Operation)
                .Where(rs => rs.Status == "Waiting" &&
                            rs.Operation != null &&
                            rs.Operation.MachineTypeId == machineTypeId)
                .OrderBy(rs => rs.SubBatch.ProductionOrder.CreatedAt)
                .ThenBy(rs => rs.Order)
                .ToListAsync();

            foreach (var stage in waitingStages)
            {
                // Проверяем, что предыдущий этап завершен
                if (!await IsPreviousStageCompleted(stage))
                    continue;

                // Получаем подходящие станки
                var suitableMachines = await GetSuitableMachines(stage);

                // Ищем свободный станок
                var freeMachine = await FindFreeMachine(suitableMachines);

                if (freeMachine != null)
                {
                    // Назначаем станок и запускаем этап
                    await AssignMachineAndStartStage(stage, freeMachine);
                    break; // Запускаем только один этап за раз
                }
            }
        }

        private async Task ProcessStagesInQueue()
        {
            // Получаем список типов станков с освободившимися станками
            var availableMachineTypes = await _context.Machines
                .Where(m => !_context.StageExecutions.Any(se =>
                    se.MachineId == m.Id && (se.Status == "Started" || se.Status == "Paused")))
                .Select(m => m.MachineTypeId)
                .Distinct()
                .ToListAsync();

            if (!availableMachineTypes.Any())
                return;

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
                    .ToListAsync();

                foreach (var stage in waitingStages)
                {
                    // Проверяем, что предыдущий этап завершен
                    if (!await IsPreviousStageCompleted(stage))
                        continue;

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
                        stage.Status = "Ready"; // Переводим из Waiting в Ready
                        await _context.SaveChangesAsync();

                        // Назначаем станок и запускаем этап
                        await AssignMachineAndStartStage(stage, freeMachine);
                        break; // Запускаем только один этап за раз
                    }
                }
            }
        }


        private async Task AddExecutionLog(int executionId, string action, string operatorName, string notes)
        {
            var log = new ExecutionLog
            {
                StageExecutionId = executionId,
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