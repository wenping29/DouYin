using System;
using System.Windows;
using System.Windows.Controls;

namespace WorkLogTool;

public partial class AddSubTaskDialog : Window
{
    public WorkRecord Result { get; private set; }
    private string _currentStatus = "待办";
    private readonly WorkRecord _parentRecord;

    public AddSubTaskDialog(WorkRecord parentRecord)
    {
        InitializeComponent();
        _parentRecord = parentRecord;
        Calendar.SelectedDate = parentRecord.Date;
        StartDatePicker.SelectedDate = parentRecord.Date;
        EndDatePicker.SelectedDate = parentRecord.Date;

        var now = DateTime.Now;
        StartHourTextBox.Text = now.Hour.ToString();
        StartMinuteTextBox.Text = now.Minute.ToString();
        EndHourTextBox.Text = now.AddHours(1).Hour.ToString();
        EndMinuteTextBox.Text = now.AddHours(1).Minute.ToString();
    }

    private void StatusToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStatus == "待办") _currentStatus = "进行中";
        else if (_currentStatus == "进行中") _currentStatus = "已完成";
        else _currentStatus = "待办";
        StatusToggleButton.Content = _currentStatus;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (Calendar.SelectedDate == null || string.IsNullOrWhiteSpace(ContentTextBox.Text))
        {
            MessageBox.Show("请填写日期和工作内容", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double hours = 0;
        decimal cost = 0;

        if (!double.TryParse(HoursTextBox.Text, out hours))
        {
            hours = 0;
        }

        if (!decimal.TryParse(CostTextBox.Text, out cost))
        {
            cost = 0;
        }

        // 解析开始时间
        int startHour = 0, startMinute = 0;
        int.TryParse(StartHourTextBox.Text, out startHour);
        int.TryParse(StartMinuteTextBox.Text, out startMinute);
        startHour = Math.Clamp(startHour, 0, 23);
        startMinute = Math.Clamp(startMinute, 0, 59);
        var startDate = StartDatePicker.SelectedDate ?? Calendar.SelectedDate.Value;
        var startTime = startDate.Date.AddHours(startHour).AddMinutes(startMinute);

        // 解析完成时间
        int endHour = 0, endMinute = 0;
        int.TryParse(EndHourTextBox.Text, out endHour);
        int.TryParse(EndMinuteTextBox.Text, out endMinute);
        endHour = Math.Clamp(endHour, 0, 23);
        endMinute = Math.Clamp(endMinute, 0, 59);
        var endDate = EndDatePicker.SelectedDate ?? Calendar.SelectedDate.Value;
        var endTime = endDate.Date.AddHours(endHour).AddMinutes(endMinute);

        Result = new WorkRecord
        {
            Date = Calendar.SelectedDate.Value,
            Content = ContentTextBox.Text.Trim(),
            Hours = hours,
            Cost = cost,
            Status = _currentStatus,
            Progress = (int)ProgressComboBox.SelectedValue,
            StartTime = startTime,
            EndTime = endTime,
            Parent = _parentRecord,
            ParentId = _parentRecord.Id,
            Level = _parentRecord.Level + 1
        };

        DialogResult = true;
        Close();
    }
}
