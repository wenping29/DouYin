using System;
using System.Windows;
using System.Windows.Controls;

namespace WorkLogTool;

public partial class EditWorkRecordDialog : Window
{
    public WorkRecord Record { get; }
    private string _currentStatus;

    public EditWorkRecordDialog(WorkRecord record)
    {
        InitializeComponent();
        Record = record;

        Calendar.SelectedDate = record.Date;
        ContentTextBox.Text = record.Content;
        HoursTextBox.Text = record.Hours.ToString();
        CostTextBox.Text = record.Cost.ToString();
        _currentStatus = record.Status;
        StatusToggleButton.Content = _currentStatus;

        // 设置进度选中项
        foreach (int item in ProgressComboBox.Items)
        {
            if (item == record.Progress)
            {
                ProgressComboBox.SelectedItem = item;
                break;
            }
        }

        // 设置时间
        if (record.StartTime.HasValue)
        {
            StartDatePicker.SelectedDate = record.StartTime.Value.Date;
            StartHourTextBox.Text = record.StartTime.Value.Hour.ToString("00");
            StartMinuteTextBox.Text = record.StartTime.Value.Minute.ToString("00");
        }
        else
        {
            StartDatePicker.SelectedDate = record.Date;
        }

        if (record.EndTime.HasValue)
        {
            EndDatePicker.SelectedDate = record.EndTime.Value.Date;
            EndHourTextBox.Text = record.EndTime.Value.Hour.ToString("00");
            EndMinuteTextBox.Text = record.EndTime.Value.Minute.ToString("00");
        }
        else
        {
            EndDatePicker.SelectedDate = record.Date;
        }
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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
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

        // 更新记录
        Record.Date = Calendar.SelectedDate.Value;
        Record.Content = ContentTextBox.Text.Trim();
        Record.Hours = hours;
        Record.Cost = cost;
        Record.Status = _currentStatus;
        Record.Progress = (int)ProgressComboBox.SelectedValue;

        // 解析开始时间
        if (!string.IsNullOrWhiteSpace(StartHourTextBox?.Text) && !string.IsNullOrWhiteSpace(StartMinuteTextBox?.Text))
        {
            if (int.TryParse(StartHourTextBox.Text, out int startHour) && int.TryParse(StartMinuteTextBox.Text, out int startMinute))
            {
                startHour = Math.Clamp(startHour, 0, 23);
                startMinute = Math.Clamp(startMinute, 0, 59);
                var startDate = StartDatePicker.SelectedDate ?? Record.Date;
                DateTime startTime = startDate.Date.AddHours(startHour).AddMinutes(startMinute);
                Record.StartTime = startTime;
            }
        }
        else
        {
            Record.StartTime = null;
        }

        // 解析完成时间
        if (!string.IsNullOrWhiteSpace(EndHourTextBox?.Text) && !string.IsNullOrWhiteSpace(EndMinuteTextBox?.Text))
        {
            if (int.TryParse(EndHourTextBox.Text, out int endHour) && int.TryParse(EndMinuteTextBox.Text, out int endMinute))
            {
                endHour = Math.Clamp(endHour, 0, 23);
                endMinute = Math.Clamp(endMinute, 0, 59);
                var endDate = EndDatePicker.SelectedDate ?? Record.Date;
                DateTime endTime = endDate.Date.AddHours(endHour).AddMinutes(endMinute);
                Record.EndTime = endTime;
            }
        }
        else
        {
            Record.EndTime = null;
        }

        DialogResult = true;
        Close();
    }
}
