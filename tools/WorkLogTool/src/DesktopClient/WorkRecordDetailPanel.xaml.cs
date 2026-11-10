using System;
using System.Windows;
using System.Windows.Controls;

namespace WorkLogTool;

public partial class WorkRecordDetailPanel : UserControl
{
    private WorkRecord? _currentRecord;
    private Action? _updateWorkRecordList;
    private Action<string>? _setStatusText;
    private WorkLogService? _workLogService;

    public WorkRecordDetailPanel()
    {
        InitializeComponent();
    }

    public void Initialize(WorkLogService workLogService, Action updateWorkRecordList, Action<string> setStatusText)
    {
        _workLogService = workLogService;
        _updateWorkRecordList = updateWorkRecordList;
        _setStatusText = setStatusText;
    }

    public void ClearDetailPanel()
    {
        _currentRecord = null;
        PlaceholderText.Visibility = Visibility.Visible;
        EditPanel.Visibility = Visibility.Collapsed;
    }

    public void OnWorkRecordSelected(WorkRecord? record)
    {
        ClearDetailPanel();

        if (record == null)
        {
            PlaceholderText.Visibility = Visibility.Visible;
            return;
        }

        _currentRecord = record;
        PlaceholderText.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;

        ContentTextBox.Text = record.Content;
        DatePicker.SelectedDate = record.Date;
        HoursTextBox.Text = record.Hours.ToString();
        CostTextBox.Text = record.Cost.ToString();
        StatusComboBox.SelectedItem = record.Status;
        ProgressTextBox.Text = record.Progress.ToString();
        StartDatePicker.SelectedDate = record.StartTime?.Date;
        StartTimeTextBox.Text = record.StartTime?.ToString("HH:mm") ?? "";
        EndDatePicker.SelectedDate = record.EndTime?.Date;
        EndTimeTextBox.Text = record.EndTime?.ToString("HH:mm") ?? "";

        SubTasksCountText.Text = record.SubTasks.Count > 0 ? $"子任务数量: {record.SubTasks.Count}" : "";
        SubTasksCountText.Visibility = record.SubTasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRecord == null || _workLogService == null) return;

        if (!string.IsNullOrWhiteSpace(ContentTextBox.Text))
            _currentRecord.Content = ContentTextBox.Text.Trim();

        if (DatePicker.SelectedDate.HasValue)
            _currentRecord.Date = DatePicker.SelectedDate.Value;

        if (double.TryParse(HoursTextBox.Text, out double hours))
            _currentRecord.Hours = hours;

        if (decimal.TryParse(CostTextBox.Text, out decimal cost))
            _currentRecord.Cost = cost;

        if (StatusComboBox.SelectedItem is ComboBoxItem statusItem)
            _currentRecord.Status = statusItem.Content?.ToString() ?? "进行中";

        if (int.TryParse(ProgressTextBox.Text, out int progress))
            _currentRecord.Progress = Math.Clamp(progress, 0, 100);

        DateTime? startDateTime = null;
        try
        {
            if (StartDatePicker != null && StartDatePicker.SelectedDate != null && StartTimeTextBox != null && !string.IsNullOrWhiteSpace(StartTimeTextBox.Text))
            {
                if (DateTime.TryParse(StartDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") + " " + StartTimeTextBox.Text, out DateTime parsedStart))
                    startDateTime = parsedStart;
            }
        }
        catch { }
        _currentRecord.StartTime = startDateTime;

        DateTime? endDateTime = null;
        try
        {
            if (EndDatePicker != null && EndDatePicker.SelectedDate != null && EndTimeTextBox != null && !string.IsNullOrWhiteSpace(EndTimeTextBox.Text))
            {
                if (DateTime.TryParse(EndDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") + " " + EndTimeTextBox.Text, out DateTime parsedEnd))
                    endDateTime = parsedEnd;
            }
        }
        catch { }
        _currentRecord.EndTime = endDateTime;

        _workLogService.UpdateWorkRecord(_currentRecord);

        if (_updateWorkRecordList != null)
            _updateWorkRecordList();
        if (_setStatusText != null)
            _setStatusText($"工作记录 \"{_currentRecord.Content}\" 已保存");

        MessageBox.Show("保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRecord == null || _workLogService == null) return;

        var result = MessageBox.Show($"确定要删除记录 \"{_currentRecord.Content}\" 吗？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _workLogService.DeleteWorkRecord(_currentRecord);

        if (_updateWorkRecordList != null)
            _updateWorkRecordList();
        ClearDetailPanel();

        if (_setStatusText != null)
            _setStatusText($"工作记录 \"{_currentRecord.Content}\" 已删除");
    }
}