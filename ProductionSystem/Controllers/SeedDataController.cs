using Microsoft.AspNetCore.Mvc;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class SeedDataController : Controller
    {
        private readonly ProductionContext _context;

        public SeedDataController(ProductionContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateSampleData()
        {
            // Проверяем, есть ли уже данные, и предлагаем их очистить
            if (_context.Details.Any())
            {
                // Очищаем существующие данные автоматически
                await ClearAllDataInternal();
            }

            try
            {

                // 1. Создаем типы станков
                var machineTypes = new[]
                {
                new MachineType { Name = "Токарный ЧПУ", Description = "Токарные станки с ЧПУ", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Фрезерный ЧПУ", Description = "Фрезерные станки с ЧПУ", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Сверлильный", Description = "Сверлильные станки", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Заточной", Description = "Заточные станки", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Термический", Description = "Термическая обработка", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Слесарный", Description = "Слесарные работы", CreatedAt = DateTime.UtcNow },
                new MachineType { Name = "Контрольный", Description = "Контроль качества", CreatedAt = DateTime.UtcNow }
            };

                _context.MachineTypes.AddRange(machineTypes);
                await _context.SaveChangesAsync();

                // 2. Создаем станки
                var machines = new[]
                {
                // Токарные ЧПУ
                new Machine { Name = "16К20", InventoryNumber = "16K20-01", MachineTypeId = machineTypes[0].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "HAAS ST-10", InventoryNumber = "ST-10", MachineTypeId = machineTypes[0].Id, Priority = 2, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "HAAS ST-20", InventoryNumber = "ST-20", MachineTypeId = machineTypes[0].Id, Priority = 3, CreatedAt = DateTime.UtcNow },
                
                // Фрезерные ЧПУ  
                new Machine { Name = "HAAS VF-2", InventoryNumber = "VF-2", MachineTypeId = machineTypes[1].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "HAAS VF-3", InventoryNumber = "VF-3", MachineTypeId = machineTypes[1].Id, Priority = 2, CreatedAt = DateTime.UtcNow },
                
                // Остальные типы станков
                new Machine { Name = "Сверлильный-1", InventoryNumber = "DR-01", MachineTypeId = machineTypes[2].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "Заточной-1", InventoryNumber = "ZT-01", MachineTypeId = machineTypes[3].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "Термопечь-1", InventoryNumber = "TP-01", MachineTypeId = machineTypes[4].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "Слесарный участок", InventoryNumber = "SL-01", MachineTypeId = machineTypes[5].Id, Priority = 1, CreatedAt = DateTime.UtcNow },
                new Machine { Name = "Контрольный участок", InventoryNumber = "KT-01", MachineTypeId = machineTypes[6].Id, Priority = 1, CreatedAt = DateTime.UtcNow }
            };

                _context.Machines.AddRange(machines);
                await _context.SaveChangesAsync();

                // 3. Создаем детали из маршрутных карт
                var details = new[]
                {
                new Detail { Name = "Втулка", Number = "43ТК.02.01.111-7", Description = "Втулка основная", CreatedAt = DateTime.UtcNow },
                new Detail { Name = "Затвор", Number = "43ТК.02.01.112-9", Description = "Затвор клапана", CreatedAt = DateTime.UtcNow },
                new Detail { Name = "Втулка Станка", Number = "43ТК.06.01.103-1", Description = "Втулка станка", CreatedAt = DateTime.UtcNow }
            };

                _context.Details.AddRange(details);
                await _context.SaveChangesAsync();

                // 4. Создаем операции для ВТУЛКИ (43ТК.02.01.111-7)
                var bushingOperations = new[]
                {
                new Operation { DetailId = details[0].Id, OperationNumber = "005", Name = "Заготовительная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.007m, Order = 5, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "010", Name = "ЧПУ", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.077m, Order = 10, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "015", Name = "Сверлениe", MachineTypeId = machineTypes[2].Id, TimePerPiece = 0.090m, Order = 15, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "020", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.038m, Order = 20, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "025", Name = "Т.К.", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.001m, Order = 25, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "030", Name = "Фрезерная", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.054m, Order = 30, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "035", Name = "Фрезерная", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.083m, Order = 35, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "040", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.117m, Order = 40, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "045", Name = "Т.К.", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.010m, Order = 45, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "050", Name = "За.Перех", MachineTypeId = machineTypes[3].Id, TimePerPiece = 0.157m, Order = 50, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "055", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.157m, Order = 55, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "060", Name = "Шлифовальная", MachineTypeId = machineTypes[3].Id, TimePerPiece = 0.157m, Order = 60, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "065", Name = "Слесарная ОТК", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.034m, Order = 65, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "070", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.017m, Order = 70, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "075", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.005m, Order = 75, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "080", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.017m, Order = 80, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "085", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.005m, Order = 85, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "090", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.108m, Order = 90, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "095", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.064m, Order = 95, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "100", Name = "Контрольная", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.011m, Order = 100, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "105", Name = "МД", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.034m, Order = 105, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "110", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.005m, Order = 110, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "115", Name = "Навертка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.008m, Order = 115, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "120", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.039m, Order = 120, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "125", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.028m, Order = 125, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "130", Name = "Фрезерная", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.112m, Order = 130, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "135", Name = "Навертка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.008m, Order = 135, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "140", Name = "Шлифовальная", MachineTypeId = machineTypes[3].Id, TimePerPiece = 0.157m, Order = 140, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[0].Id, OperationNumber = "145", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.034m, Order = 145, CreatedAt = DateTime.UtcNow }
            };

                // 5. Создаем операции для ЗАТВОРА (43ТК.02.01.112-9)
                var valveOperations = new[]
                {
                new Operation { DetailId = details[1].Id, OperationNumber = "005", Name = "Заготовительная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.008m, Order = 5, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "010", Name = "ЧПУ", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.025m, Order = 10, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "015", Name = "020 Т.К.", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.074m, Order = 15, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "020", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.034m, Order = 20, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "025", Name = "Термическая", MachineTypeId = machineTypes[4].Id, TimePerPiece = 0.800m, Order = 25, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "030", Name = "Т.К.", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.014m, Order = 30, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "035", Name = "Фрезерная", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.028m, Order = 35, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "040", Name = "Фрезерная", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.017m, Order = 40, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "045", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.042m, Order = 45, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "050", Name = "ЧПУ", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.071m, Order = 50, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "055", Name = "ЧПУ", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.037m, Order = 55, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "060", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.038m, Order = 60, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "065", Name = "Шлифовальная", MachineTypeId = machineTypes[3].Id, TimePerPiece = 0.124m, Order = 65, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "070", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.053m, Order = 70, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "075", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.017m, Order = 75, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "080", Name = "Т.К.", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.038m, Order = 80, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "085", Name = "1140Q", MachineTypeId = machineTypes[1].Id, TimePerPiece = 0.159m, Order = 85, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "090", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.030m, Order = 90, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "095", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.017m, Order = 95, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "100", Name = "Контрольная", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.040m, Order = 100, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "105", Name = "ЧПУ", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.048m, Order = 105, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "110", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.053m, Order = 110, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "115", Name = "Навертка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.017m, Order = 115, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "120", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.022m, Order = 120, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "125", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.032m, Order = 125, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "130", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.053m, Order = 130, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "135", Name = "Сборка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.049m, Order = 135, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "140", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.138m, Order = 140, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "145", Name = "Испытания", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.408m, Order = 145, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "150", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.017m, Order = 150, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[1].Id, OperationNumber = "155", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.058m, Order = 155, CreatedAt = DateTime.UtcNow }
            };

                // 6. Создаем операции для ВТУЛКИ СТАНКА (43ТК.06.01.103-1)
                var machineBushingOperations = new[]
                {
                new Operation { DetailId = details[2].Id, OperationNumber = "010", Name = "Заготовительная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.647m, Order = 10, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "015", Name = "Токарная", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.60m, Order = 15, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "020", Name = "16К20", MachineTypeId = machineTypes[0].Id, TimePerPiece = 3.0m, Order = 20, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "025", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.170m, Order = 25, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "030", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.028m, Order = 30, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "035", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.04m, Order = 35, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "040", Name = "За.Перех", MachineTypeId = machineTypes[3].Id, TimePerPiece = 0.018m, Order = 40, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "045", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.040m, Order = 45, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "050", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.100m, Order = 50, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "055", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.050m, Order = 55, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "060", Name = "Термическая", MachineTypeId = machineTypes[4].Id, TimePerPiece = 0.270m, Order = 60, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "065", Name = "16К20", MachineTypeId = machineTypes[0].Id, TimePerPiece = 0.640m, Order = 65, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "070", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.065m, Order = 70, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "075", Name = "Шлифовальная", MachineTypeId = machineTypes[3].Id, TimePerPiece = 1.018m, Order = 75, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "080", Name = "ОТК", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.031m, Order = 80, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "085", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.028m, Order = 85, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "090", Name = "Навертка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.028m, Order = 90, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "095", Name = "1000Q", MachineTypeId = machineTypes[1].Id, TimePerPiece = 1.000m, Order = 95, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "100", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.1m, Order = 100, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "105", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.081m, Order = 105, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "110", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.008m, Order = 110, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "115", Name = "Навертка", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.008m, Order = 115, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "120", Name = "Слесарная", MachineTypeId = machineTypes[5].Id, TimePerPiece = 0.028m, Order = 120, CreatedAt = DateTime.UtcNow },
                new Operation { DetailId = details[2].Id, OperationNumber = "125", Name = "Контроль", MachineTypeId = machineTypes[6].Id, TimePerPiece = 0.01m, Order = 125, CreatedAt = DateTime.UtcNow }
            };

                _context.Operations.AddRange(bushingOperations);
                _context.Operations.AddRange(valveOperations);
                _context.Operations.AddRange(machineBushingOperations);
                await _context.SaveChangesAsync();

                // 7. Создаем переналадки между деталями
                var changeovers = new List<Changeover>();

                // Переналадки между всеми деталями на всех станках
                foreach (var machine in machines)
                {
                    for (int i = 0; i < details.Length; i++)
                    {
                        for (int j = 0; j < details.Length; j++)
                        {
                            if (i != j) // Не создаем переналадку с той же детали на ту же
                            {
                                var changeoverTime = machine.MachineType.Name switch
                                {
                                    "Токарный ЧПУ" => 0.25m + (decimal)(new Random().NextDouble() * 0.5), // 0.25-0.75 ч
                                    "Фрезерный ЧПУ" => 0.35m + (decimal)(new Random().NextDouble() * 0.5), // 0.35-0.85 ч  
                                    "Термический" => 2.0m + (decimal)(new Random().NextDouble() * 2.0), // 2-4 ч
                                    _ => 0.15m + (decimal)(new Random().NextDouble() * 0.3) // 0.15-0.45 ч для остальных
                                };

                                changeovers.Add(new Changeover
                                {
                                    MachineId = machine.Id,
                                    FromDetailId = details[i].Id,
                                    ToDetailId = details[j].Id,
                                    ChangeoverTime = Math.Round(changeoverTime, 3),
                                    Description = $"Переналадка {machine.Name} с {details[i].Name} на {details[j].Name}",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }

                _context.Changeovers.AddRange(changeovers);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Тестовые данные созданы успешно! Создано: {details.Length} деталей, {machines.Length} станков, {bushingOperations.Length + valveOperations.Length + machineBushingOperations.Length} операций, {changeovers.Count} переналадок." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ошибка при создании данных: {ex.Message}" });
            }
        }

        private async Task ClearAllDataInternal()
        {
            // Удаляем все данные в правильном порядке (из-за внешних ключей)
            _context.ExecutionLogs.RemoveRange(_context.ExecutionLogs);
            _context.StageExecutions.RemoveRange(_context.StageExecutions);
            _context.RouteStages.RemoveRange(_context.RouteStages);
            _context.SubBatches.RemoveRange(_context.SubBatches);
            _context.ProductionOrders.RemoveRange(_context.ProductionOrders);
            _context.Changeovers.RemoveRange(_context.Changeovers);
            _context.Operations.RemoveRange(_context.Operations);
            _context.Machines.RemoveRange(_context.Machines);
            _context.Details.RemoveRange(_context.Details);
            _context.MachineTypes.RemoveRange(_context.MachineTypes);

            await _context.SaveChangesAsync();
        }

        [HttpPost]
        public async Task<IActionResult> ClearAllData()
        {
            try
            {
                await ClearAllDataInternal();
                return Json(new { success = true, message = "Все данные удалены успешно!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ошибка при удалении данных: {ex.Message}" });
            }
        }
    }
}