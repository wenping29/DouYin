using System.Windows;

namespace WorkLogTool;

public partial class AddKanbanItemDialog : Window
{
    public KanbanItem Result { get; private set; }

    public AddKanbanItemDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            Result = new KanbanItem
            {
                Title = TitleTextBox.Text.Trim(),
                Description = DescriptionTextBox.Text.Trim(),
                Status = "Todo"
            };
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("请填写标题", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
