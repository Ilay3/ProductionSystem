namespace ProductionSystem.Enums
{
    /// <summary>
    /// Статусы производственного задания
    /// </summary>
    public enum ProductionOrderStatus
    {
        Created,
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Статусы подпартии
    /// </summary>
    public enum SubBatchStatus
    {
        Created,
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Статусы этапа маршрута
    /// </summary>
    public enum RouteStageStatus
    {
        Pending,
        Ready,
        InProgress,
        Paused,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Типы этапов
    /// </summary>
    public enum StageType
    {
        Operation,
        Changeover
    }

    /// <summary>
    /// Статусы выполнения этапа
    /// </summary>
    public enum ExecutionStatus
    {
        Pending,
        Started,
        Paused,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Типы действий в логе
    /// </summary>
    public enum LogAction
    {
        Started,
        Paused,
        Resumed,
        Completed,
        Cancelled,
        Modified,
        TimeModified
    }
}