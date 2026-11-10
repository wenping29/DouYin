using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WorkLogTool;

public partial class UpdateCompletionDialog : Window
{
    private readonly List<KanbanItem> _items;
    public KanbanItem SelectedItem { get; private set; }

    public UpdateCompletionDialog(List<KanbanItem> items)
    {
        InitializeComponent();
        _items = items;

        foreach (var item in items)
        {
            TaskComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{item.Title} (当前完成度 {item.Completion}%)",
                Tag = item.Id
            });
        }
    }

    private void CompletionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        CompletionText.Text = $"{(int)CompletionSlider.Value}%";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (TaskComboBox.SelectedItem != null)
        {
            var selectedItem = (ComboBoxItem)TaskComboBox.SelectedItem;
            var taskId = (string)selectedItem.Tag;
            SelectedItem = _items.Find(item => item.Id == taskId);

            if (SelectedItem != null)
            {
                SelectedItem.Completion = (int)CompletionSlider.Value;

                // 根据完成度更新状态
                if (SelectedItem.Completion == 100)
                {
                    SelectedItem.Status = "Completed";
                }
                else if (SelectedItem.Completion > 0)
                {
                    SelectedItem.Status = "InProgress";
                }
                else
                {
                    SelectedItem.Status = "Todo";
                }

                DialogResult = true;
                Close();
            }
        }
        else
        {
            MessageBox.Show("请选择一个任务", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
