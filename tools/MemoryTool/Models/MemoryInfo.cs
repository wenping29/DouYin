using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoryTool.Models
{
    public class MemoryInfo : INotifyPropertyChanged
    {
        private long _totalMemory;
        private long _usedMemory;
        private long _availableMemory;
        private double _usagePercentage;

        public long TotalMemory
        {
            get => _totalMemory;
            set { _totalMemory = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalMemoryFormatted)); }
        }

        public long UsedMemory
        {
            get => _usedMemory;
            set { _usedMemory = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsedMemoryFormatted)); }
        }

        public long AvailableMemory
        {
            get => _availableMemory;
            set { _availableMemory = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailableMemoryFormatted)); }
        }

        public double UsagePercentage
        {
            get => _usagePercentage;
            set { _usagePercentage = value; OnPropertyChanged(); }
        }

        public string TotalMemoryFormatted => FormatBytes(TotalMemory);
        public string UsedMemoryFormatted => FormatBytes(UsedMemory);
        public string AvailableMemoryFormatted => FormatBytes(AvailableMemory);

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
