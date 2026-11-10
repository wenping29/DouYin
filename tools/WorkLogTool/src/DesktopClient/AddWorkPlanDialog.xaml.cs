using System;
using System.Windows;
using System.Windows.Controls;

namespace WorkLogTool;

public partial class AddWorkPlanDialog : Window
{
    public WorkRecord Result { get; private set; }
    private string _currentStatus = "待办";

    public AddWorkPlanDialog()
    {
        InitializeComponent();
        var now = DateTime.Now;
        StartDatePicker.SelectedDate = now.Date;
        StartTimeTextBox.Text = now.ToString("HH:mm");
        EndDatePicker.SelectedDate = now.Date;
        EndTimeTextBox.Text = now.AddHours(1).ToString("HH:mm");
    }

    public AddWorkPlanDialog(DateTime defaultDate) : this()
    {
        Calendar.SelectedDate = defaultDate;
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
        DateTime startTime;
        if (StartDatePicker.SelectedDate == null || !DateTime.TryParse(StartDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") + " " + StartTimeTextBox.Text, out startTime))
        {
            startTime = DateTime.Now;
        }

        // 解析完成时间
        DateTime endTime;
        if (EndDatePicker.SelectedDate == null || !DateTime.TryParse(EndDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") + " " + EndTimeTextBox.Text, out endTime))
        {
            endTime = DateTime.Now.AddHours(1);
        }

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
            CreatedDate = DateTime.Now
        };

        DialogResult = true;
        Close();
    }
}
