// ProductionSystem/Services/ShiftService.cs
using ProductionSystem.Data;

namespace ProductionSystem.Services
{
    public interface IShiftService
    {
        bool IsWorkingTime(DateTime dateTime, int? machineId);
        DateTime GetNextWorkingDateTime(DateTime dateTime, int? machineId);
        Task<decimal> CalculateWorkingHours(DateTime startDate, DateTime endDate, int? machineId);
    }

    public class ShiftService : IShiftService
    {
        private readonly ProductionContext _context;
        private readonly ILogger<ShiftService> _logger;

        public ShiftService(ProductionContext context, ILogger<ShiftService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public bool IsWorkingTime(DateTime dateTime, int? machineId)
        {
            if (!machineId.HasValue)
                return true; // Если станок не указан, считаем что всегда рабочее время

            var dayOfWeek = dateTime.DayOfWeek;
            var timeOfDay = dateTime.TimeOfDay;

            // Получаем все смены для станка
            var shifts = _context.ShiftAssignments
                .Where(sa => sa.MachineId == machineId && sa.IsActive)
                .Select(sa => sa.Shift)
                .ToList();

            if (!shifts.Any())
                return true; // Если нет настроенных смен, считаем всегда рабочее время

            foreach (var shift in shifts)
            {
                // Проверяем день недели
                bool isDayWorking = dayOfWeek switch
                {
                    DayOfWeek.Monday => shift.Monday,
                    DayOfWeek.Tuesday => shift.Tuesday,
                    DayOfWeek.Wednesday => shift.Wednesday,
                    DayOfWeek.Thursday => shift.Thursday,
                    DayOfWeek.Friday => shift.Friday,
                    DayOfWeek.Saturday => shift.Saturday,
                    DayOfWeek.Sunday => shift.Sunday,
                    _ => false
                };

                if (!isDayWorking)
                    continue;

                // Проверяем попадает ли время в смену
                bool isInShift = IsTimeInShift(timeOfDay, shift.StartTime, shift.EndTime);

                // Проверяем не обеденный ли перерыв
                if (isInShift && shift.BreakStartTime.HasValue && shift.BreakEndTime.HasValue)
                {
                    if (IsTimeInShift(timeOfDay, shift.BreakStartTime.Value, shift.BreakEndTime.Value))
                        isInShift = false;
                }

                if (isInShift)
                    return true;
            }

            return false;
        }

        private bool IsTimeInShift(TimeSpan time, TimeSpan start, TimeSpan end)
        {
            if (start < end)
            {
                // Смена в пределах одного дня
                return time >= start && time <= end;
            }
            else
            {
                // Смена переходит на следующий день
                return time >= start || time <= end;
            }
        }

        public DateTime GetNextWorkingDateTime(DateTime dateTime, int? machineId)
        {
            if (!machineId.HasValue || IsWorkingTime(dateTime, machineId))
                return dateTime;

            // Ищем ближайшее рабочее время
            var nextDateTime = dateTime.AddMinutes(1);
            int iterations = 0;
            const int maxIterations = 10000; // Защита от бесконечного цикла

            while (!IsWorkingTime(nextDateTime, machineId) && iterations < maxIterations)
            {
                nextDateTime = nextDateTime.AddMinutes(1);
                iterations++;
            }

            if (iterations >= maxIterations)
                _logger.LogWarning($"Достигнуто максимальное число итераций при поиске рабочего времени");

            return nextDateTime;
        }

        public async Task<decimal> CalculateWorkingHours(DateTime startDate, DateTime endDate, int? machineId)
        {
            if (!machineId.HasValue)
                return (decimal)(endDate - startDate).TotalHours;

            var currentDate = startDate;
            decimal totalHours = 0;

            while (currentDate < endDate)
            {
                if (IsWorkingTime(currentDate, machineId))
                {
                    totalHours += 1 / 60m; // Добавляем 1 минуту
                }
                currentDate = currentDate.AddMinutes(1);
            }

            return totalHours;
        }
    }
}