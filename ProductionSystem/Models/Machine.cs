using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель станка с привязкой к типу станка
    /// </summary>
    public class Machine
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string InventoryNumber { get; set; } = string.Empty;

        [Required]
        [ForeignKey("MachineType")]
        public int MachineTypeId { get; set; }

        /// <summary>
        /// Приоритет станка (если несколько одинаковых станков)
        /// </summary>
        [Required]
        public int Priority { get; set; } = 1;

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства (не участвуют в валидации формы)
        [ValidateNever]
        public virtual MachineType MachineType { get; set; } = null!;
        public virtual ICollection<RouteStage> RouteStages { get; set; } = new List<RouteStage>();
        public virtual ICollection<StageExecution> StageExecutions { get; set; } = new List<StageExecution>();
        public virtual ICollection<Changeover> ChangeoversPrev { get; set; } = new List<Changeover>();
        public virtual ICollection<Changeover> ChangeoversNext { get; set; } = new List<Changeover>();
    }
}