using Microsoft.EntityFrameworkCore;
using ProductionSystem.Models;

namespace ProductionSystem.Data
{
    public class ProductionContext : DbContext
    {
        public ProductionContext(DbContextOptions<ProductionContext> options) : base(options)
        {
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
    }
}