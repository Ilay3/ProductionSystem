using Microsoft.EntityFrameworkCore;
using ProductionSystem.Models;

namespace ProductionSystem.Data
{
    public class ProductionContext : DbContext
    {
        private readonly string? _connectionString;

        // Исправляем часовой пояс на UTC+4 для Самары/Саратова
        private static readonly TimeZoneInfo LocalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");

        public ProductionContext(DbContextOptions<ProductionContext> options) : base(options)
        {
        }

        // Конструктор для передачи строки подключения
        public ProductionContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Если есть строка подключения, используем её
                var connectionString = _connectionString ??
                    "Host=localhost;Database=ProductionSystemDB;Username=postgres;Password=postgres";

                // Добавляем настройки для правильной работы с DateTime
                optionsBuilder.UseNpgsql(connectionString, options =>
                {
                    // Включаем повторные попытки при сбоях
                    options.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });

                // Включаем детальное логирование в режиме разработки
                optionsBuilder.EnableSensitiveDataLogging();
                optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
            }

            base.OnConfiguring(optionsBuilder);
        }

        // DbSets для всех сущностей
        public DbSet<Detail> Details { get; set; }
        public DbSet<MachineType> MachineTypes { get; set; }
        public DbSet<Machine> Machines { get; set; }
        public DbSet<Operation> Operations { get; set; }
        public DbSet<Changeover> Changeovers { get; set; }
        public DbSet<ProductionOrder> ProductionOrders { get; set; }
        public DbSet<SubBatch> SubBatches { get; set; }
        public DbSet<RouteStage> RouteStages { get; set; }
        public DbSet<StageExecution> StageExecutions { get; set; }
        public DbSet<ExecutionLog> ExecutionLogs { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<ShiftAssignment> ShiftAssignments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Конфигурация для правильной обработки DateTime
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    {
                        // Устанавливаем тип колонки для PostgreSQL
                        property.SetColumnType("timestamp without time zone");
                    }
                }
            }

            modelBuilder.Entity<Shift>(entity =>
            {
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            });

            modelBuilder.Entity<ShiftAssignment>(entity =>
            {
                entity.HasOne(e => e.Machine)
                    .WithMany()
                    .HasForeignKey(e => e.MachineId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Shift)
                    .WithMany(e => e.ShiftAssignments)
                    .HasForeignKey(e => e.ShiftId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // Конфигурация Detail
            modelBuilder.Entity<Detail>(entity =>
            {
                entity.HasIndex(e => e.Number).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            });

            // Конфигурация MachineType
            modelBuilder.Entity<MachineType>(entity =>
            {
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            });

            // Конфигурация Machine
            modelBuilder.Entity<Machine>(entity =>
            {
                entity.HasIndex(e => e.InventoryNumber).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.InventoryNumber).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.MachineType)
                    .WithMany(e => e.Machines)
                    .HasForeignKey(e => e.MachineTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Конфигурация Operation
            modelBuilder.Entity<Operation>(entity =>
            {
                entity.HasIndex(e => new { e.DetailId, e.OperationNumber }).IsUnique();
                entity.Property(e => e.OperationNumber).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.TimePerPiece).HasColumnType("decimal(8,4)");

                entity.HasOne(e => e.Detail)
                    .WithMany(e => e.Operations)
                    .HasForeignKey(e => e.DetailId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MachineType)
                    .WithMany(e => e.Operations)
                    .HasForeignKey(e => e.MachineTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Конфигурация Changeover
            modelBuilder.Entity<Changeover>(entity =>
            {
                entity.HasIndex(e => new { e.MachineId, e.FromDetailId, e.ToDetailId }).IsUnique();
                entity.Property(e => e.ChangeoverTime).HasColumnType("decimal(6,4)");

                entity.HasOne(e => e.Machine)
                    .WithMany(e => e.ChangeoversPrev)
                    .HasForeignKey(e => e.MachineId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.FromDetail)
                    .WithMany()
                    .HasForeignKey(e => e.FromDetailId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ToDetail)
                    .WithMany()
                    .HasForeignKey(e => e.ToDetailId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Конфигурация ProductionOrder
            modelBuilder.Entity<ProductionOrder>(entity =>
            {
                entity.HasIndex(e => e.Number).IsUnique();
                entity.Property(e => e.Number).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.Detail)
                    .WithMany(e => e.ProductionOrders)
                    .HasForeignKey(e => e.DetailId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Конфигурация SubBatch
            modelBuilder.Entity<SubBatch>(entity =>
            {
                entity.HasIndex(e => new { e.ProductionOrderId, e.BatchNumber }).IsUnique();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.ProductionOrder)
                    .WithMany(e => e.SubBatches)
                    .HasForeignKey(e => e.ProductionOrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Конфигурация RouteStage
            modelBuilder.Entity<RouteStage>(entity =>
            {
                entity.Property(e => e.StageNumber).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.StageType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PlannedTime).HasColumnType("decimal(8,4)");

                entity.HasOne(e => e.SubBatch)
                    .WithMany(e => e.RouteStages)
                    .HasForeignKey(e => e.SubBatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Operation)
                    .WithMany(e => e.RouteStages)
                    .HasForeignKey(e => e.OperationId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Machine)
                    .WithMany(e => e.RouteStages)
                    .HasForeignKey(e => e.MachineId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Конфигурация StageExecution
            modelBuilder.Entity<StageExecution>(entity =>
            {
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ActualTime).HasColumnType("decimal(8,4)");
                entity.Property(e => e.PauseTime).HasColumnType("decimal(8,4)");

                entity.HasOne(e => e.RouteStage)
                    .WithMany(e => e.StageExecutions)
                    .HasForeignKey(e => e.RouteStageId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Machine)
                    .WithMany(e => e.StageExecutions)
                    .HasForeignKey(e => e.MachineId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Конфигурация ExecutionLog
            modelBuilder.Entity<ExecutionLog>(entity =>
            {
                entity.Property(e => e.Action).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.StageExecution)
                    .WithMany(e => e.ExecutionLogs)
                    .HasForeignKey(e => e.StageExecutionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        // Методы для конвертации времени между UTC+4 и UTC
        private static DateTime ConvertToUtc(DateTime localDateTime)
        {
            if (localDateTime.Kind == DateTimeKind.Utc)
                return localDateTime;

            if (localDateTime.Kind == DateTimeKind.Unspecified)
            {
                // Считаем неопределенное время как локальное (UTC+4)
                localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
            }

            // Конвертируем из UTC+4 в UTC
            return TimeZoneInfo.ConvertTimeToUtc(localDateTime, LocalTimeZone);
        }

        private static DateTime ConvertFromUtc(DateTime utcDateTime)
        {
            if (utcDateTime.Kind != DateTimeKind.Utc)
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

            // Конвертируем из UTC в UTC+4
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, LocalTimeZone);
        }

        // Методы для получения текущего времени в UTC+4
        public static DateTime GetLocalNow()
        {
            // Получаем текущее время по UTC+4 (Самара/Саратов)
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LocalTimeZone);
        }

        public static DateTime ConvertToLocalTime(DateTime utcTime)
        {
            if (utcTime.Kind != DateTimeKind.Utc)
                utcTime = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, LocalTimeZone);
        }

        public static DateTime ConvertToUtcTime(DateTime localTime)
        {
            return DateTime.SpecifyKind(ConvertToUtc(localTime), DateTimeKind.Unspecified);
        }
    }
}