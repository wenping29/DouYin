using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace WorkLogTool;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly WorkLogService _workLogService;
    private List<Reminder> reminders = new List<Reminder>();
    private List<KanbanItem> kanbanItems = new List<KanbanItem>();
    private DateTime _currentSelectedDate = DateTime.Today;

    private readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worklog.db");

    // 供XAML绑定的状态选项列表
    public static string[] StatusValues { get; } = new[] { "待办", "进行中", "已完成" };

    // 供XAML绑定的进度选项列表
    public static int[] ProgressValues { get; } = new[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    public MainWindow()
    {
        InitializeComponent();

        _workLogService = new WorkLogService(dbPath);
        _workLogService.Initialize();
        DetailPanel.Initialize(_workLogService, UpdateWorkRecordList, text => StatusText.Text = text);

        Calendar.SelectedDate = DateTime.Now;
        ReminderDatePicker.SelectedDate = DateTime.Now;

        LoadWorkPlanFile(); // 自动加载工作计划文件
        UpdateWorkRecordList();
        UpdateIncompleteTasksStatus();
        CheckTodayReminders();
        InitializeTimers();
        CheckCurrentMinuteReminders();
        CheckFirstOpenToday();
    }

    private void UpdateIncompleteTasksStatus()
    {
        var incompleteTasks = _workLogService.WorkRecords
            .Where(r => r.Status != "已完成" && r.IsDeleted == 0)
            .OrderBy(r => r.Date)
            .Take(10)
            .Select(r => $"[{r.Date:MM-dd}] {r.Content}")
            .ToList();

        if (incompleteTasks.Count > 0)
        {
            StatusText.Text = "待完成: " + string.Join(" | ", incompleteTasks);
        }
        else
        {
            StatusText.Text = "就绪";
        }
    }

    private void LoadWorkPlanFile()
    {
        string workPlanPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worklog", "工作计划.txt");
        if (File.Exists(workPlanPath))
        {
            try
            {
                var lines = File.ReadAllLines(workPlanPath);
                if (WorkLogService.IsWorkPlanFormat(lines))
                {
                    // 工作计划文件格式解析后保存
                    _workLogService.ParseWorkPlanFormat(lines);
                    _workLogService.SaveAllWorkRecords();
                    StatusText.Text = "工作计划文件自动加载成功";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"加载工作计划文件失败: {ex.Message}";
            }
        }
    }

    private void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Calendar.SelectedDate.HasValue)
        {
            UpdateWorkRecordList(Calendar.SelectedDate.Value);
        }
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Calendar.SelectedDate.HasValue)
        {
            UpdateWorkRecordList(Calendar.SelectedDate.Value);
        }
    }

    private void CheckFirstOpenToday()
    {
        string lastOpenFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worklog", "lastopen.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(lastOpenFile));
        
        bool isFirstOpenToday = true;
        if (File.Exists(lastOpenFile))
        {
            string lastOpenDate = File.ReadAllText(lastOpenFile).Trim();
            if (lastOpenDate == DateTime.Today.ToString("yyyy-MM-dd"))
            {
                isFirstOpenToday = false;
            }
        }
        
        // 更新上次打开时间
        File.WriteAllText(lastOpenFile, DateTime.Today.ToString("yyyy-MM-dd"));
        
        // 如果是当天第一次打开，显示新增工作计划的弹框
        if (isFirstOpenToday)
        {
            AddWorkPlanButton_Click(null, null);
        }
    }

    private void CheckTodayReminders()
    {
        var today = DateTime.Today;
        var todayReminders = reminders.Where(r => r.Date.Date == today).ToList();
        
        if (todayReminders.Count > 0)
        {
            var reminderText = string.Join(Environment.NewLine, todayReminders.Select(r => $"{r.Time.Hours:D2}:{r.Time.Minutes:D2} - {r.Content}"));
            MessageBox.Show($"今日提醒:\n{reminderText}", "提醒", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        GenerateRemindersFromIncompleteTasks();
    }

    private void GenerateRemindersFromIncompleteTasks()
    {
        var incompleteRecords = _workLogService.WorkRecords
            .Where(r => r.Status != "已完成" && r.IsDeleted == 0 && r.Date.Date <= DateTime.Today.AddDays(7))
            .OrderBy(r => r.Date)
            .Take(10)
            .ToList();

        var today = DateTime.Today;

        foreach (var record in incompleteRecords)
        {
            bool existsMorning = reminders.Any(r => r.Content == record.Content && r.Date.Date == today && r.Time.Hours == 9 && r.Time.Minutes == 30);
            if (!existsMorning)
            {
                reminders.Add(new Reminder
                {
                    Date = today,
                    Time = new TimeSpan(9, 30, 0),
                    Content = record.Content
                });
            }

            bool existsEvening = reminders.Any(r => r.Content == record.Content && r.Date.Date == today && r.Time.Hours == 17 && r.Time.Minutes == 0);
            if (!existsEvening)
            {
                reminders.Add(new Reminder
                {
                    Date = today,
                    Time = new TimeSpan(17, 0, 0),
                    Content = record.Content
                });
            }
        }

        if (incompleteRecords.Count > 0)
        {
            UpdateReminderList();
        }
    }

    private void AddRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (Calendar.SelectedDate == null )
        {
            MessageBox.Show("请选择日期和填写工作内容", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double hours = 0;
        decimal cost = 0;



        var record = new WorkRecord
        {
            Date = Calendar.SelectedDate.Value,
            Hours = hours,
            Cost = cost,
            Status = "进行中",
            Progress = 0,
            CreatedDate = DateTime.Now
        };

        _workLogService.AddWorkRecord(record);
        UpdateWorkRecordList();
        StatusText.Text = "记录添加成功";
    }

    private void AddWorkPlanButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddWorkPlanDialog(DateTime.Today);
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _workLogService.AddWorkRecord(dialog.Result);
            UpdateWorkRecordList();
            StatusText.Text = "工作计划添加成功";
        }
    }

    private void EditWorkRecordButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;

        var record = button.Tag as WorkRecord;
        if (record == null) return;

        var dialog = new EditWorkRecordDialog(record);
        if (dialog.ShowDialog() == true)
        {
            // 更新父任务的进度
            WorkLogService.UpdateParentProgress(dialog.Record);

            // 保存到数据库
            _workLogService.SaveWorkRecord(dialog.Record);

            UpdateWorkRecordList();
            StatusText.Text = "工作记录更新成功";
        }
    }

    private void AddSubTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;

        var parentRecord = button.Tag as WorkRecord;
        if (parentRecord == null) return;

        var dialog = new AddSubTaskDialog(parentRecord);
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _workLogService.AddSubTask(parentRecord, dialog.Result);
            UpdateWorkRecordList();
            StatusText.Text = "子任务添加成功";
        }
    }


    private void GenerateLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workLogService.WorkRecords.Count == 0)
        {
            MessageBox.Show("没有工作记录可生成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件(*.*)|*.*",
            FileName = $"工作日志_{DateTime.Now.ToString("yyyyMMdd")}.txt"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                using (var writer = new StreamWriter(saveDialog.FileName))
                {
                    writer.WriteLine("工作日志");
                    writer.WriteLine($"生成时间: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
                    writer.WriteLine("====================================");

                    // 按日期分组
                    var recordsByDate = _workLogService.WorkRecords.GroupBy(r => r.Date.Date).OrderBy(g => g.Key);

                    foreach (var group in recordsByDate)
                    {
                        writer.WriteLine($"日期: {group.Key.ToString("yyyy-MM-dd")}");
                        writer.WriteLine("------------------------------------");
                        foreach (var record in group)
                        {
                            writer.WriteLine($"- {record.Content} (耗时: {record.Hours}小时, 成本: ¥{record.Cost})");
                        }
                        writer.WriteLine();
                    }
                }
                StatusText.Text = "工作日志文件生成成功";
                MessageBox.Show($"工作日志文件已生成 {saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "生成文件失败";
                MessageBox.Show($"生成文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadLogButton_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件(*.*)|*.*"
        };

        if (openDialog.ShowDialog() == true)
        {
            try
            {
                var lines = File.ReadAllLines(openDialog.FileName);

                // 检测文件格式并选择解析方法
                if (WorkLogService.IsWorkPlanFormat(lines))
                {
                    _workLogService.ParseWorkPlanFormat(lines);
                }
                else
                {
                    _workLogService.ParseStandardFormat(lines);
                }

                // 保存加载的工作记录到数据库
                _workLogService.SaveAllWorkRecords();

                UpdateWorkRecordList();
                StatusText.Text = "工作日志文件加载成功";
                MessageBox.Show($"工作日志文件已加载 {openDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "加载文件失败";
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void UpdateWorkRecordList()
    {
        if (Calendar.SelectedDate.HasValue)
        {
            UpdateWorkRecordList(Calendar.SelectedDate.Value);
        }
        else
        {
            UpdateWorkRecordList(DateTime.Today);
        }
    }

    private void UpdateWorkRecordList(DateTime date)
    {
        _currentSelectedDate = date;
        List<WorkRecord> selectedDateRecords;

        switch (FilterComboBox.SelectedIndex)
        {
            case 1: // 新增计划
                selectedDateRecords = _workLogService.GetNewRecordsByDate(date);
                break;
            case 2: // 未完成计划
                selectedDateRecords = _workLogService.GetIncompleteRecordsByDate(date);
                break;
            default: // 全部记录
                selectedDateRecords = _workLogService.GetAllTasksByDate(date);
                break;
        }

        WorkRecordDataGrid.ItemsSource = null;
        WorkRecordDataGrid.ItemsSource = selectedDateRecords;
        UpdateTodayTaskList(date);
        UpdateIncompleteTasksStatus();
    }

    // 更新左侧日历下方的当天任务列表
    private void UpdateTodayTaskList(DateTime date)
    {
        var allTasks = _workLogService.GetAllTasksByDate(date);

        // 添加到列表框
        if (allTasks.Count == 0)
        {
            TodayTaskList.ItemsSource = new List<string> { "(暂无任务)" };
        }
        else
        {
            TodayTaskList.ItemsSource = allTasks;
        }
    }

    private void GenerateChartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workLogService.WorkRecords.Count == 0)
        {
            MessageBox.Show("没有工作记录可生成图表", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedChartType = (string)((ComboBoxItem)ChartTypeComboBox.SelectedItem)?.Content;
        if (string.IsNullOrEmpty(selectedChartType))
        {
            selectedChartType = "折线图";
        }

        ChartContainer.Child = null;

        switch (selectedChartType)
        {
            case "折线图":
                CreateLineChart();
                break;
            case "柱状图":
                CreateBarChart();
                break;
            case "饼图":
                CreatePieChart();
                break;
            case "甘特图":
                CreateGanttChart();
                break;
        }
    }

    private void CreateLineChart()
    {
        var recordsByDate = _workLogService.WorkRecords
            .GroupBy(r => r.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(r => r.Hours), TotalCost = g.Sum(r => r.Cost) })
            .ToList();

        var lineChart = new CartesianChart
        {
            Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "工作耗时",
                    Values = recordsByDate.Select(x => x.TotalHours).ToArray(),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Blue),
                    Fill = null
                },
                new LineSeries<decimal>
                {
                    Name = "成本",
                    Values = recordsByDate.Select(x => x.TotalCost).ToArray(),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Red),
                    Fill = null
                }
            },
            XAxes = new[]
            {
                new Axis
                {
                    Labels = recordsByDate.Select(x => x.Date.ToString("MM-dd")).ToArray(),
                    LabelsRotation = 45
                }
            },
            YAxes = new[]
            {
                new Axis { Name = "数值" }
            },
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Top
        };

        ChartContainer.Child = lineChart;
    }

    private void CreateBarChart()
    {
        var recordsByDate = _workLogService.WorkRecords
            .GroupBy(r => r.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(r => r.Hours) })
            .ToList();

        var barChart = new CartesianChart
        {
            Series = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "工作耗时",
                    Values = recordsByDate.Select(x => x.TotalHours).ToArray(),
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Green)
                }
            },
            XAxes = new[]
            {
                new Axis
                {
                    Labels = recordsByDate.Select(x => x.Date.ToString("MM-dd")).ToArray(),
                    LabelsRotation = 45
                }
            },
            YAxes = new[]
            {
                new Axis { Name = "小时" }
            },
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Top
        };

        ChartContainer.Child = barChart;
    }

    private void CreatePieChart()
    {
        var recordsByContent = _workLogService.WorkRecords
            .GroupBy(r => r.Content)
            .Select(g => new { Content = g.Key, TotalHours = g.Sum(r => r.Hours) })
            .Take(8) // 只显示前8个，避免图表过于拥挤
            .ToList();

        var pieChart = new PieChart
        {
            Series = recordsByContent.Select((x, i) => new PieSeries<double>
            {
                Name = x.Content,
                Values = new double[] { x.TotalHours },
                Fill = GetColor(i)
            }).ToArray(),
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Right
        };

        ChartContainer.Child = pieChart;
    }

    private LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint GetColor(int index)
    {
        var colors = new SkiaSharp.SKColor[]
        {
            SkiaSharp.SKColors.Red,
            SkiaSharp.SKColors.Blue,
            SkiaSharp.SKColors.Green,
            SkiaSharp.SKColors.Yellow,
            SkiaSharp.SKColors.Orange,
            SkiaSharp.SKColors.Purple,
            SkiaSharp.SKColors.Pink,
            SkiaSharp.SKColors.Brown
        };
        return new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(colors[index % colors.Length]);
    }

    private void CreateGanttChart()
    {
        // 准备甘特图数据
        var ganttItems = _workLogService.WorkRecords.Select((record, index) => new
        {
            Task = record.Content,
            Duration = record.Hours,
            Index = index
        }).ToList();

        var cartesianChart = new CartesianChart
        {
            Series = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "工作任务",
                    Values = ganttItems.Select(x => x.Duration).ToArray(),
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Blue)
                }
            },
            XAxes = new[]
            {
                new Axis
                {
                    Name = "任务",
                    Labels = ganttItems.Select(x => x.Task).ToArray(),
                    LabelsRotation = 45
                }
            },
            YAxes = new[]
            {
                new Axis
                {
                    Name = "持续时间(小时)"
                }
            },
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Top
        };

        // 添加点击事件，支持编辑
        cartesianChart.MouseDown += (sender, e) =>
        {
            // 这里可以添加点击编辑逻辑
            MessageBox.Show("点击了甘特图，可以在这里添加编辑功能");
        };

        ChartContainer.Child = cartesianChart;
    }

    private void AddReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderDatePicker.SelectedDate == null || string.IsNullOrWhiteSpace(ReminderContent.Text))
        {
            MessageBox.Show("请填写提醒日期和内容", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int hour = 0;
        int minute = 0;
        
        int.TryParse(HourTextBox.Text, out hour);
        int.TryParse(MinuteTextBox.Text, out minute);
        
        // 确保小时和分钟在有效范围
        hour = Math.Max(0, Math.Min(23, hour));
        minute = Math.Max(0, Math.Min(59, minute));

        var reminder = new Reminder
        {
            Date = ReminderDatePicker.SelectedDate.Value,
            Time = new TimeSpan(hour, minute, 0),
            Content = ReminderContent.Text.Trim()
        };

        reminders.Add(reminder);
        UpdateReminderList();
        ReminderContent.Clear();
        StatusText.Text = "提醒添加成功";
    }

    private void AddQuickReminderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (QuickReminderDate.SelectedDate == null || string.IsNullOrWhiteSpace(QuickReminderContent.Text))
            {
                MessageBox.Show("请选择提醒日期和填写提醒内容", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (QuickReminderHour == null || QuickReminderMinute == null)
            {
                MessageBox.Show("时间输入框未正确初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int hour = 0;
            int minute = 0;

            int.TryParse(QuickReminderHour.Text, out hour);
            int.TryParse(QuickReminderMinute.Text, out minute);

            // 确保小时和分钟在有效范围
            hour = Math.Max(0, Math.Min(23, hour));
            minute = Math.Max(0, Math.Min(59, minute));

            var reminder = new Reminder
            {
                Date = QuickReminderDate.SelectedDate.Value,
                Time = new TimeSpan(hour, minute, 0),
                Content = QuickReminderContent.Text.Trim()
            };

            reminders.Add(reminder);
            UpdateReminderList();

            // 清空输入框
            QuickReminderContent.Clear();

            StatusText.Text = "提醒添加成功";
                MessageBox.Show("提醒添加成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"添加提醒时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateReminderList()
    {
        ReminderList.Items.Clear();
        foreach (var reminder in reminders.OrderBy(r => r.Date).ThenBy(r => r.Time))
        {
            string timeStr = $"{reminder.Time.Hours:D2}:{reminder.Time.Minutes:D2}";
            ReminderList.Items.Add($"{reminder.Date:yyyy-MM-dd} {timeStr}: {reminder.Content}");
        }
    }

    private void AddKanbanItemButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddKanbanItemDialog();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            kanbanItems.Add(dialog.Result);
            UpdateKanbanLists();
            StatusText.Text = "看板项添加成功";
        }
    }

    private void UpdateKanbanLists()
    {
        TodoList.Items.Clear();
        InProgressList.Items.Clear();
        CompletedList.Items.Clear();

        foreach (var item in kanbanItems)
        {
            var listBoxItem = new ListBoxItem
            {
                Content = $"{item.Title} (完成度 {item.Completion}%)",
                Tag = item.Id
            };
            listBoxItem.MouseDown += ListBoxItem_MouseDown;

            switch (item.Status)
            {
                case "Todo":
                    TodoList.Items.Add(listBoxItem);
                    break;
                case "InProgress":
                    InProgressList.Items.Add(listBoxItem);
                    break;
                case "Completed":
                    CompletedList.Items.Add(listBoxItem);
                    break;
            }
        }
    }

    private void ListBoxItem_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var listBoxItem = sender as ListBoxItem;
        if (listBoxItem != null)
        {
            DragDrop.DoDragDrop(listBoxItem, listBoxItem.Tag, DragDropEffects.Move);
        }
    }

    private void ListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TodoList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(string)))
        {
            var taskId = (string)e.Data.GetData(typeof(string));
            var task = kanbanItems.FirstOrDefault(item => item.Id == taskId);
            if (task != null)
            {
                task.Status = "Todo";
                task.Completion = 0;
                UpdateKanbanLists();
            }
        }
    }

    private void InProgressList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(string)))
        {
            var taskId = (string)e.Data.GetData(typeof(string));
            var task = kanbanItems.FirstOrDefault(item => item.Id == taskId);
            if (task != null)
            {
                task.Status = "InProgress";
                if (task.Completion == 0)
                {
                    task.Completion = 50; // 默认设置50%
                }
                UpdateKanbanLists();
            }
        }
    }

    private void CompletedList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(string)))
        {
            var taskId = (string)e.Data.GetData(typeof(string));
            var task = kanbanItems.FirstOrDefault(item => item.Id == taskId);
            if (task != null)
            {
                task.Status = "Completed";
                task.Completion = 100; // 已完成设置为100%
                UpdateKanbanLists();
            }
        }
    }

    private void GenerateBurnDownChartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workLogService.WorkRecords.Count == 0)
        {
            MessageBox.Show("没有工作记录可生成燃尽图", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 生成简单的燃尽图
        var recordsByDate = _workLogService.WorkRecords
            .GroupBy(r => r.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(r => r.Hours) })
            .ToList();

        // 计算累计工作时间
        var cumulativeHours = new List<double>();
        double total = 0;
        foreach (var record in recordsByDate)
        {
            total += record.TotalHours;
            cumulativeHours.Add(total);
        }

        // 创建燃尽图
        var burnDownChart = new CartesianChart
        {
            Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "累计工作时间",
                    Values = cumulativeHours.ToArray(),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Blue),
                    Fill = null
                }
            },
            XAxes = new[]
            {
                new Axis
                {
                    Labels = recordsByDate.Select(x => x.Date.ToString("MM-dd")).ToArray(),
                    LabelsRotation = 45
                }
            },
            YAxes = new[]
            {
                new Axis { Name = "小时" }
            },
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Top
        };

        BurnDownChartContainer.Child = burnDownChart;
    }

    private void MorningReminderButton_Click(object sender, RoutedEventArgs e)
    {
        // 清空今日工作列表
        TodayWorkList.Items.Clear();

        // 获取昨天的日期
        var yesterday = DateTime.Today.AddDays(-1);
        var today = DateTime.Today;

        // 查找昨天未完成的工作
        var yesterdayTasks = kanbanItems.Where(item => 
            item.Status != "Completed" && 
            item.CreatedDate.Date <= yesterday).ToList();

        // 查找今天的工作
        var todayTasks = kanbanItems.Where(item => 
            item.CreatedDate.Date == today).ToList();

        // 添加昨天未完成的工作
        if (yesterdayTasks.Count > 0)
        {
            TodayWorkList.Items.Add("=== 昨天未完成的工作 ===");
            foreach (var task in yesterdayTasks)
            {
                TodayWorkList.Items.Add($"[未完成] {task.Title} (完成度 {task.Completion}%)");
            }
        }

        // 添加今天的工作
        if (todayTasks.Count > 0)
        {
            TodayWorkList.Items.Add("=== 今天的工作===");
            foreach (var task in todayTasks)
            {
                TodayWorkList.Items.Add($"[今日] {task.Title} (完成度 {task.Completion}%)");
            }
        }

        // 如果没有工作，显示提示
        if (yesterdayTasks.Count == 0 && todayTasks.Count == 0)
        {
            TodayWorkList.Items.Add("今天没有安排工作，请添加新的工作任务。");
        }

        // 显示早晨提醒
        MessageBox.Show("今日工作提醒已生成，请查看工作列表", "早晨提醒", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateCompletionButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UpdateCompletionDialog(kanbanItems);
        if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
        {
            UpdateKanbanLists();
            StatusText.Text = "完成度更新成功";
        }
    }

    // 初始化时启动定时检查
    private void InitializeTimers()
    {
        var timer = new System.Threading.Timer((state) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CheckReminders();
            });
            CheckEndOfWorkDay();
        }, null, 0, 10 * 1000);
    }

    private void CheckReminders()
    {
        var now = DateTime.Now;
        System.Diagnostics.Debug.WriteLine($"CheckReminders called at {now}, reminders count: {reminders.Count}");

        foreach (var r in reminders)
        {
            System.Diagnostics.Debug.WriteLine($"Reminder: {r.Date} {r.Time}, Content: {r.Content}");
        }

        var currentMinute = now.Hour * 60 + now.Minute;
        var triggeredReminders = new List<Reminder>();

        foreach (var r in reminders.ToList())
        {
            var reminderMinute = r.Time.Hours * 60 + r.Time.Minutes;
            var diff = Math.Abs(reminderMinute - currentMinute);
            System.Diagnostics.Debug.WriteLine($"Checking: {r.Date.Date} == {now.Date}? {r.Date.Date == now.Date}, diff={diff}");
            if (r.Date.Date == now.Date && diff <= 1)
            {
                triggeredReminders.Add(r);
            }
        }

        System.Diagnostics.Debug.WriteLine($"Triggered: {triggeredReminders.Count}");

        if (triggeredReminders.Count > 0)
        {
            var reminderText = string.Join(Environment.NewLine, triggeredReminders.Select(r => r.Content));
            Application.Current.Dispatcher.Invoke(() =>
            {
                PlayReminderSound();
                MessageBox.Show(reminderText, "提醒", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
    }

    private void CheckCurrentMinuteReminders()
    {
        CheckReminders();
    }

    private void PlayReminderSound()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch
        {
        }
    }

    private void ClearDetailPanel()
    {
        DetailPanel.ClearDetailPanel();
    }

    private void CheckEndOfWorkDay()
    {
        var now = DateTime.Now;
        // 检查是否接近下班时间（16:30）
        if (now.Hour == 16 && now.Minute == 30)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 显示下班提醒
                MessageBox.Show("下班时间快到了，请检查今日工作完成情况", "下班提醒", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // 显示完成度统计
                var inProgressTasks = kanbanItems.Where(item => item.Status == "InProgress").ToList();
                if (inProgressTasks.Count > 0)
                {
                    var taskList = string.Join(Environment.NewLine, inProgressTasks.Select(item => $"{item.Title} (完成度 {item.Completion}%)"));
                    MessageBox.Show($"以下任务仍在进行中\n{taskList}", "工作进度", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }
    }

    private void WorkRecordTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        DetailPanel.OnWorkRecordSelected(e.NewValue as WorkRecord);
    }



    // 表格编辑完成事件
    private void WorkRecordDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is WorkRecord record)
        {
            // 编辑完成后保存到数据库
            UpdateWorkRecordInDatabase(record);
            UpdateTodayTaskList(record.Date);
            StatusText.Text = $"工作记录 \"{record.Content}\" 已更新";
        }
    }

    private void WorkRecordDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailPanel.OnWorkRecordSelected(WorkRecordDataGrid.SelectedItem as WorkRecord);
    }

    private void UpdateWorkRecordInDatabase(WorkRecord record)
    {
        _workLogService.UpdateWorkRecord(record);
    }
}
