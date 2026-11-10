namespace WorkLogTool;

/// <summary>
/// 看板项实体类
/// </summary>
public class KanbanItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
    public string Description { get; set; }
    public string Status { get; set; } // Todo, InProgress, Completed
    public int Completion { get; set; } = 0; // 完成度，0-100
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
