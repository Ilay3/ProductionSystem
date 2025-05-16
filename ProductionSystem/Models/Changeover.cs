using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель переналадки для комбинации "Станок + Предыдущая деталь + Новая деталь"
    /// </summary>
    public class Changeover
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("Machine")]
        public int MachineId { get; set; }

        [Required]
        [ForeignKey("FromDetail")]
        public int FromDetailId { get; set; }

        [Required]
        [ForeignKey("ToDetail")]
        public int ToDetailId { get; set; }

        /// <summary>
        /// Время переналадки в часах
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(6,4)")]
        [Range(0.001, double.MaxValue, ErrorMessage = "Время переналадки должно быть больше 0")]
        public decimal ChangeoverTime { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        [ValidateNever]
        public virtual Machine Machine { get; set; } = null!;
        [ValidateNever]
        public virtual Detail FromDetail { get; set; } = null!;
        [ValidateNever]
        public virtual Detail ToDetail { get; set; } = null!;
    }
}