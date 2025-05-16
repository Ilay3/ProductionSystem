using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель типа станка (например, "Токарный ЧПУ", "Фрезерный")
    /// </summary>
    public class MachineType
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        [ValidateNever]
        public virtual ICollection<Machine> Machines { get; set; } = new List<Machine>();
        [ValidateNever]
        public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
    }
}