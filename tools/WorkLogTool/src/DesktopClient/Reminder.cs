namespace WorkLogTool;

/// <summary>
/// 提醒实体类
/// </summary>
public class Reminder
{
    public DateTime Date { get; set; }
    public string Content { get; set; }
    public TimeSpan Time { get; set; } // 提醒时间
}
