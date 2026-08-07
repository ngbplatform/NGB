namespace NGB.Contracts.WorkCenter;

public enum WorkCenterItemKind
{
    Task = 1,
    Notification = 2
}

public enum WorkCenterPreferenceKind
{
    Task = 1,
    Notification = 2
}

public enum WorkCenterTaskStatus
{
    Open = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

public enum WorkCenterPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public enum NotificationSeverity
{
    Information = 1,
    Success = 2,
    Warning = 3,
    Critical = 4
}

public enum NotificationChannel
{
    InApp = 1
}

public enum WorkCenterTab
{
    Attention = 1,
    Tasks = 2,
    Notifications = 3,
    Completed = 4
}
