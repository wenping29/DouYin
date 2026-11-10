using System.Collections.Generic;

namespace WorkLogTool;

/// <summary>
/// 工作记录实体类
/// </summary>
public class WorkRecord
{
    public int Id { get; set; } // 数据库ID
    public DateTime Date { get; set; }
    public string Content { get; set; }
    public double Hours { get; set; } // 工作耗时（小时）
    public decimal Cost { get; set; } // 成本
    public string Status { get; set; } = "进行中"; // 工作状态
    public int Progress { get; set; } = 0; // 工作进度 0-100
    public DateTime? StartTime { get; set; } // 开始时间
    public DateTime? EndTime { get; set; } // 完成时间
    public DateTime CreatedDate { get; set; } // 创建日期
    public List<WorkRecord> SubTasks { get; set; } = new List<WorkRecord>(); // 子任务列表
    public bool IsExpanded { get; set; } = false; // 是否展开
    public WorkRecord Parent { get; set; } // 父任务
    public int? ParentId { get; set; } // 父任务ID
    public int Level { get; set; } = 0; // 层级
    public int IsDeleted { get; set; } = 0; // 软删除标记 0-未删除 1-已删除
}
