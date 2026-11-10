using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoryTool.Models
{
    public class ProcessMemoryInfo : INotifyPropertyChanged
    {
        private string _processName = string.Empty;
        private int _processId;
        private long _workingSet;

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(); }
        }

        public long WorkingSet
        {
            get => _workingSet;
            set { _workingSet = value; OnPropertyChanged(); OnPropertyChanged(nameof(WorkingSetFormatted)); }
        }

        public string WorkingSetFormatted => FormatBytes(WorkingSet);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }
}
