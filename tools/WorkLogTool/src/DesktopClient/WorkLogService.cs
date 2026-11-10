using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace WorkLogTool;

/// <summary>
/// 工作日志业务服务类
/// </summary>
public class WorkLogService
{
    private readonly SqliteHelper _sqliteHelper;
    private List<WorkRecord> _workRecords = new List<WorkRecord>();

    public string DbPath { get; }
    public IReadOnlyList<WorkRecord> WorkRecords => _workRecords.AsReadOnly();

    public WorkLogService(string dbPath)
    {
        DbPath = dbPath;
        _sqliteHelper = new SqliteHelper(dbPath);
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public void Initialize()
    {
        _sqliteHelper.InitializeDatabase();
        LoadAllWorkRecords();
        BuildParentChildRelationships();
    }

    /// <summary>
    /// 从数据库加载所有工作记录
    /// </summary>
    private void LoadAllWorkRecords()
    {
        _workRecords = _sqliteHelper.LoadAllWorkRecords();
    }

    /// <summary>
    /// 构建父子关系
    /// </summary>
    private void BuildParentChildRelationships()
    {
        // 首先创建ID到记录的映射
        var recordMap = _workRecords.ToDictionary(r => r.Id);

        // 构建父子关系
        foreach (var record in _workRecords)
        {
            if (record.ParentId.HasValue && recordMap.ContainsKey(record.ParentId.Value))
            {
                record.Parent = recordMap[record.ParentId.Value];
                record.Parent.SubTasks.Add(record);
            }
        }
    }

    /// <summary>
    /// 保存工作记录
    /// </summary>
    public void SaveWorkRecord(WorkRecord record)
    {
        _sqliteHelper.SaveWorkRecord(record);
    }

    /// <summary>
    /// 更新工作记录
    /// </summary>
    public void UpdateWorkRecord(WorkRecord record)
    {
        _sqliteHelper.UpdateWorkRecordInDatabase(record);
    }

    /// <summary>
    /// 删除工作记录（软删除，包括所有子任务）
    /// </summary>
    public void DeleteWorkRecord(WorkRecord record)
    {
        _sqliteHelper.MarkAsDeleted(record);
        DeleteWorkRecordAndSubtasksFromMemory(record);
    }

    /// <summary>
    /// 从内存集合中删除记录及其所有子任务
    /// </summary>
    private void DeleteWorkRecordAndSubtasksFromMemory(WorkRecord record)
    {
        // 先删除所有子任务
        foreach (var subTask in record.SubTasks)
        {
            DeleteWorkRecordAndSubtasksFromMemory(subTask);
        }

        // 从全局列表删除
        _workRecords.Remove(record);
    }

    /// <summary>
    /// 获取指定日期的顶级工作记录
    /// </summary>
    public List<WorkRecord> GetTopLevelRecordsByDate(DateTime date)
    {
        return _workRecords.Where(r => r.Date.Date <= date.Date && r.Parent == null && r.IsDeleted == 0).OrderByDescending(r => r.Date).ToList();
    }

    /// <summary>
    /// 获取指定日期的所有任务（包括子任务）
    /// </summary>
    public List<WorkRecord> GetAllTasksByDate(DateTime date)
    {
        var allTasks = new List<WorkRecord>();
        foreach (var record in _workRecords.Where(r => r.Date.Date <= date.Date && r.IsDeleted == 0))
        {
            CollectAllTasks(record, allTasks);
        }
        return allTasks;
    }

    /// <summary>
    /// 递归收集所有任务（包括子任务）
    /// </summary>
    private static void CollectAllTasks(WorkRecord record, List<WorkRecord> allTasks)
    {
        allTasks.Add(record);
        foreach (var subTask in record.SubTasks)
        {
            CollectAllTasks(subTask, allTasks);
        }
    }

    /// <summary>
    /// 获取指定日期新增的工作计划（顶级任务）
    /// </summary>
    public List<WorkRecord> GetNewRecordsByDate(DateTime date)
    {
        return _workRecords.Where(r => r.CreatedDate.Date == date.Date && r.Parent == null && r.IsDeleted == 0).ToList();
    }

    /// <summary>
    /// 获取指定日期未完成的工作计划
    /// </summary>
    public List<WorkRecord> GetIncompleteRecordsByDate(DateTime date)
    {
        return _workRecords.Where(r => r.Date.Date <= date.Date && r.Status != "已完成" && r.Parent == null && r.IsDeleted == 0).ToList();
    }

    /// <summary>
    /// 从工作计划文件解析并添加记录
    /// </summary>
    public void ParseWorkPlanFormat(string[] lines)
    {
        DateTime currentDate = DateTime.MinValue;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 检查日期行：包含年月日和分隔符
            if (trimmedLine.Contains("年") && trimmedLine.Contains("月") && trimmedLine.Contains("日") && trimmedLine.Contains("-------------------------------------"))
            {
                // 提取日期部分，格式如 2025年10月1日
                var dateStr = trimmedLine.Split(new[] { "-------------------------------------" }, StringSplitOptions.None)[0].Trim();
                if (DateTime.TryParseExact(dateStr, "yyyy年MM月dd日", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    currentDate = date;
                }
                else
                {
                    currentDate = DateTime.MinValue;
                }
            }
            // 检查工作记录行
            else if (!string.IsNullOrWhiteSpace(trimmedLine) && currentDate != DateTime.MinValue)
            {
                // 移除开头的编号（如1. 2. 等）
                var content = System.Text.RegularExpressions.Regex.Replace(trimmedLine, @"^\d+\.\s*", "");
                // 移除末尾的分号或空格
                content = content.TrimEnd(';', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    _workRecords.Add(new WorkRecord { Date = currentDate, Content = content });
                }
            }
        }
    }

    /// <summary>
    /// 从标准格式日志文件解析并添加记录
    /// </summary>
    public void ParseStandardFormat(string[] lines)
    {
        DateTime currentDate = DateTime.MinValue;

        foreach (var line in lines)
        {
            if (line.StartsWith("日期: "))
            {
                var dateStr = line.Substring(3).Trim();
                if (DateTime.TryParse(dateStr, out var date))
                {
                    currentDate = date;
                }
            }
            else if (line.StartsWith("- ") && currentDate != DateTime.MinValue)
            {
                var content = line.Substring(2).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    _workRecords.Add(new WorkRecord { Date = currentDate, Content = content });
                }
            }
        }
    }

    /// <summary>
    /// 检测是否为工作计划格式
    /// </summary>
    public static bool IsWorkPlanFormat(string[] lines)
    {
        // 检查是否包含工作计划格式的日期
        foreach (var line in lines)
        {
            if (line.Contains("年") && line.Contains("月") && line.Contains("日") && line.Contains("-------------------------------------"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 更新父任务进度
    /// </summary>
    public static void UpdateParentProgress(WorkRecord childRecord)
    {
        // 从子任务开始，向上更新所有父任务的进度
        var current = childRecord.Parent;
        while (current != null)
        {
            // 计算子任务的平均进度
            if (current.SubTasks.Count > 0)
            {
                int totalProgress = current.SubTasks.Sum(t => t.Progress);
                current.Progress = totalProgress / current.SubTasks.Count;
            }
            current = current.Parent;
        }
    }

    /// <summary>
    /// 添加新工作记录
    /// </summary>
    public void AddWorkRecord(WorkRecord record)
    {
        _workRecords.Add(record);
        SaveWorkRecord(record);
    }

    /// <summary>
    /// 添加子任务
    /// </summary>
    public void AddSubTask(WorkRecord parentRecord, WorkRecord subTask)
    {
        parentRecord.SubTasks.Add(subTask);
        _workRecords.Add(subTask);
        SaveWorkRecord(subTask);
        UpdateParentProgress(subTask);
        SaveWorkRecord(parentRecord);
    }

    /// <summary>
    /// 保存所有工作记录到数据库
    /// </summary>
    public void SaveAllWorkRecords()
    {
        foreach (var record in _workRecords.Where(r => r.IsDeleted == 0))
        {
            SaveWorkRecord(record);
        }
    }
}
