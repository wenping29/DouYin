using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WorkLogTool;

/// <summary>
/// SQLite 数据库操作助手
/// </summary>
public class SqliteHelper
{
    private readonly string _dbPath;

    public string DbPath => _dbPath;

    public SqliteHelper(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// 初始化数据库，创建表结构
    /// </summary>
    public void InitializeDatabase()
    {
        // 创建数据库目录
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));

        // 创建数据库和表
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS WorkRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Content TEXT NOT NULL,
                Hours REAL DEFAULT 0,
                Cost REAL DEFAULT 0,
                Status TEXT DEFAULT '待办',
                Progress INTEGER DEFAULT 0,
                StartTime TEXT,
                EndTime TEXT,
                CreatedDate TEXT,
                ParentId INTEGER,
                Level INTEGER DEFAULT 0,
                IsDeleted INTEGER DEFAULT 0,
                FOREIGN KEY (ParentId) REFERENCES WorkRecords(Id)
            );
        ";
        createTableCommand.ExecuteNonQuery();

        // 尝试添加 IsDeleted 列
        try
        {
            var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE WorkRecords ADD COLUMN IsDeleted INTEGER DEFAULT 0";
            addColumnCommand.ExecuteNonQuery();
        }
        catch
        {
            // 列已经存在，忽略错误
        }

        // 尝试添加 CreatedDate 列
        try
        {
            var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE WorkRecords ADD COLUMN CreatedDate TEXT";
            addColumnCommand.ExecuteNonQuery();
        }
        catch
        {
            // 列已经存在，忽略错误
        }
    }

    /// <summary>
    /// 保存工作记录到数据库（插入或更新）
    /// </summary>
    public void SaveWorkRecord(WorkRecord record)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        if (record.Id > 0)
        {
            // 更新现有记录
            UpdateWorkRecord(record, connection);
        }
        else
        {
            // 检查是否已存在相同记录（按 Content 和 Date 判断）
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM WorkRecords WHERE Content = @Content AND Date = @Date AND IsDeleted = 0";
            checkCommand.Parameters.AddWithValue("@Content", record.Content);
            checkCommand.Parameters.AddWithValue("@Date", record.Date.ToString("yyyy-MM-dd"));
            long count = (long)checkCommand.ExecuteScalar();

            if (count > 0)
            {
                // 更新现有记录
                UpdateWorkRecordByContentAndDate(record, connection);
            }
            else
            {
                // 插入新记录
                InsertWorkRecord(record, connection);
            }
        }
    }

    /// <summary>
    /// 更新现有记录（按 Id）
    /// </summary>
    private void UpdateWorkRecord(WorkRecord record, SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE WorkRecords
            SET Content = $content, Date = $date, Hours = $hours, Cost = $cost,
                Status = $status, Progress = $progress, StartTime = $startTime, EndTime = $endTime,
                CreatedDate = $createdDate, ParentId = $parentId, Level = $level, IsDeleted = $isDeleted
            WHERE Id = $id";

        command.Parameters.AddWithValue("$content", record.Content);
        command.Parameters.AddWithValue("$date", record.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$hours", record.Hours);
        command.Parameters.AddWithValue("$cost", record.Cost);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$progress", record.Progress);
        command.Parameters.AddWithValue("$startTime", record.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$endTime", record.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdDate", record.CreatedDate == default ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : record.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$parentId", record.ParentId.HasValue ? (object)record.ParentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$level", record.Level);
        command.Parameters.AddWithValue("$isDeleted", record.IsDeleted);
        command.Parameters.AddWithValue("$id", record.Id);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 更新现有记录（按 Content 和 Date）
    /// </summary>
    private void UpdateWorkRecordByContentAndDate(WorkRecord record, SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE WorkRecords
            SET Hours = @Hours, Cost = @Cost, Status = @Status, Progress = @Progress,
                StartTime = @StartTime, EndTime = @EndTime, CreatedDate = @CreatedDate, ParentId = @ParentId, Level = @Level, IsDeleted = @IsDeleted
            WHERE Content = @Content AND Date = @Date AND IsDeleted = 0";

        command.Parameters.AddWithValue("@Hours", record.Hours);
        command.Parameters.AddWithValue("@Cost", record.Cost);
        command.Parameters.AddWithValue("@Status", record.Status);
        command.Parameters.AddWithValue("@Progress", record.Progress);
        command.Parameters.AddWithValue("@StartTime", record.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@EndTime", record.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", record.CreatedDate == default ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : record.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@ParentId", record.ParentId.HasValue ? (object)record.ParentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Level", record.Level);
        command.Parameters.AddWithValue("@IsDeleted", record.IsDeleted);
        command.Parameters.AddWithValue("@Content", record.Content);
        command.Parameters.AddWithValue("@Date", record.Date.ToString("yyyy-MM-dd"));

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 插入新记录
    /// </summary>
    private void InsertWorkRecord(WorkRecord record, SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO WorkRecords (Date, Content, Hours, Cost, Status, Progress, StartTime, EndTime, CreatedDate, ParentId, Level, IsDeleted)
            VALUES (@Date, @Content, @Hours, @Cost, @Status, @Progress, @StartTime, @EndTime, @CreatedDate, @ParentId, @Level, @IsDeleted)";

        command.Parameters.AddWithValue("@Date", record.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("@Content", record.Content);
        command.Parameters.AddWithValue("@Hours", record.Hours);
        command.Parameters.AddWithValue("@Cost", record.Cost);
        command.Parameters.AddWithValue("@Status", record.Status);
        command.Parameters.AddWithValue("@Progress", record.Progress);
        command.Parameters.AddWithValue("@StartTime", record.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@EndTime", record.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", record.CreatedDate == default ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : record.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@ParentId", record.ParentId.HasValue ? (object)record.ParentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Level", record.Level);
        command.Parameters.AddWithValue("@IsDeleted", record.IsDeleted);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 从数据库加载所有未删除的工作记录
    /// </summary>
    public List<WorkRecord> LoadAllWorkRecords()
    {
        var workRecords = new List<WorkRecord>();

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = "SELECT * FROM WorkRecords WHERE IsDeleted = 0 OR IsDeleted IS NULL ORDER BY Date DESC";

        using var reader = selectCommand.ExecuteReader();
        while (reader.Read())
        {
            var record = new WorkRecord
            {
                Id = reader.GetInt32(0),
                Date = DateTime.Parse(reader.GetString(1)),
                Content = reader.GetString(2),
                Hours = reader.GetDouble(3),
                Cost = reader.GetDecimal(4),
                Status = reader.GetString(5),
                Progress = reader.GetInt32(6),
                StartTime = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                EndTime = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                CreatedDate = reader.IsDBNull(9) ? DateTime.MinValue : DateTime.Parse(reader.GetString(9)),
                ParentId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Level = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                IsDeleted = 0
            };
            workRecords.Add(record);
        }

        return workRecords;
    }

    /// <summary>
    /// 更新工作记录
    /// </summary>
    public void UpdateWorkRecordInDatabase(WorkRecord record)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        UpdateWorkRecord(record, connection);
    }

    /// <summary>
    /// 标记记录及其所有子记录为已删除（软删除）
    /// </summary>
    public void MarkAsDeleted(WorkRecord record)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            MarkAsDeletedInternal(record, connection);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 递归标记当前记录和所有子任务为已删除
    /// </summary>
    private void MarkAsDeletedInternal(WorkRecord record, SqliteConnection connection)
    {
        if (record.Id <= 0) return;
        
        record.IsDeleted = 1;

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE WorkRecords SET IsDeleted = 1 WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", record.Id);
        command.ExecuteNonQuery();

        foreach (var subTask in record.SubTasks)
        {
            MarkAsDeletedInternal(subTask, connection);
        }
    }
}
