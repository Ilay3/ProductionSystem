using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель операции в маршруте детали
    /// </summary>
    public class Operation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("Detail")]
        public int DetailId { get; set; }

        /// <summary>
        /// Номер операции в маршруте (например, 010, 015, 020)
        /// </summary>
        [Required]
        [StringLength(10)]
        public string OperationNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [ForeignKey("MachineType")]
        public int MachineTypeId { get; set; }

        /// <summary>
        /// Норма времени на 1 деталь в часах
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(8,4)")]
        [Range(0.001, double.MaxValue, ErrorMessage = "Время должно быть больше 0")]
        public decimal TimePerPiece { get; set; }

        /// <summary>
        /// Порядок операции в маршруте
        /// </summary>
        [Required]
        public int Order { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        [ValidateNever]
        public virtual Detail Detail { get; set; } = null!;
        [ValidateNever]
        public virtual MachineType MachineType { get; set; } = null!;
        [ValidateNever]
        public virtual ICollection<RouteStage> RouteStages { get; set; } = new List<RouteStage>();
    }
}