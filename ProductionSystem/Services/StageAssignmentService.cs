using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Services
{
    public interface IStageAssignmentService
    {
        Task<bool> AssignStageToMachine(int routeStageId, int machineId);
        Task<decimal> GetChangeoverTime(int machineId, int fromDetailId, int toDetailId);
        Task<int?> GetLastDetailOnMachine(int machineId);
    }

    public class StageAssignmentService : IStageAssignmentService
    {
        private readonly ProductionContext _context;

        public StageAssignmentService(ProductionContext context)
        {
            _context = context;
        }

        public async Task<bool> AssignStageToMachine(int routeStageId, int machineId)
        {
            var routeStage = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Operation)
                .FirstOrDefaultAsync(rs => rs.Id == routeStageId);

            if (routeStage == null) return false;

            // Проверяем, подходит ли станок для этой операции
            var machine = await _context.Machines
                .Include(m => m.MachineType)
                .FirstOrDefaultAsync(m => m.Id == machineId);

            if (machine == null) return false;

            // Проверяем совместимость типа станка с операцией
            if (routeStage.Operation != null && routeStage.Operation.MachineTypeId != machine.MachineTypeId)
                return false;

            // Получаем последнюю деталь на этом станке
            var lastDetailId = await GetLastDetailOnMachine(machineId);
            var currentDetailId = routeStage.SubBatch.ProductionOrder.DetailId;

            // Если это другая деталь - добавляем этап переналадки
            if (lastDetailId.HasValue && lastDetailId.Value != currentDetailId)
            {
                await CreateChangeoverStage(routeStage, machineId, lastDetailId.Value, currentDetailId);
            }

            // Назначаем станок основному этапу
            routeStage.MachineId = machineId;
            routeStage.Status = "Ready";

            await _context.SaveChangesAsync();
            return true;
        }

        private async Task CreateChangeoverStage(RouteStage mainStage, int machineId, int fromDetailId, int toDetailId)
        {
            var changeoverTime = await GetChangeoverTime(machineId, fromDetailId, toDetailId);

            var changeoverStage = new RouteStage
            {
                SubBatchId = mainStage.SubBatchId,
                MachineId = machineId,
                StageNumber = $"{mainStage.StageNumber}_CO",
                Name = $"Переналадка на {mainStage.Name}",
                StageType = "Changeover",
                Order = mainStage.Order - 1, // Ставим перед основным этапом
                PlannedTime = changeoverTime,
                Quantity = 1,
                Status = "Ready",
                CreatedAt = ProductionContext.GetLocalNow()
            };

            _context.RouteStages.Add(changeoverStage);

            // Корректируем порядок основного этапа и следующих
            var subsequentStages = await _context.RouteStages
                .Where(rs => rs.SubBatchId == mainStage.SubBatchId && rs.Order >= mainStage.Order && rs.Id != mainStage.Id)
                .ToListAsync();

            foreach (var stage in subsequentStages)
            {
                stage.Order += 1;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<decimal> GetChangeoverTime(int machineId, int fromDetailId, int toDetailId)
        {
            var changeover = await _context.Changeovers
                .FirstOrDefaultAsync(c => c.MachineId == machineId &&
                                         c.FromDetailId == fromDetailId &&
                                         c.ToDetailId == toDetailId);

            // Если переналадка не найдена, возвращаем время по умолчанию
            return changeover?.ChangeoverTime ?? 0.25m; // 15 минут по умолчанию
        }

        public async Task<int?> GetLastDetailOnMachine(int machineId)
        {
            // Ищем последний завершенный этап на этом станке
            var lastExecution = await _context.StageExecutions
                .Where(se => se.MachineId == machineId && se.Status == "Completed")
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .OrderByDescending(se => se.CompletedAt)
                .FirstOrDefaultAsync();

            return lastExecution?.RouteStage?.SubBatch?.ProductionOrder?.DetailId;
        }
    }
}