using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using MemoryTool.Models;
using MemoryTool.Services;

namespace MemoryTool.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly MemoryMonitor _monitor;
        private MemoryInfo _systemMemory = null!;
        private bool _isRefreshing;

        // 预定义颜色数组，给饼图切片使用
        private readonly Color[] _sliceColors = new[]
        {
            Colors.Red,
            Colors.Orange,
            Colors.Gold,
            Colors.LimeGreen,
            Colors.SteelBlue,
            Colors.Purple,
            Colors.Gray
        };

        public MemoryInfo SystemMemory
        {
            get => _systemMemory;
            set { _systemMemory = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ProcessMemoryInfo> Processes { get; }

        public ObservableCollection<PieSlice> ProcessPieSlices { get; }

        public ICommand RefreshCommand { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _monitor = new MemoryMonitor();
            SystemMemory = new MemoryInfo();
            Processes = new ObservableCollection<ProcessMemoryInfo>();
            ProcessPieSlices = new ObservableCollection<PieSlice>();
            RefreshCommand = new RelayCommand(Refresh);
        }

        public void Refresh()
        {
            IsRefreshing = true;

            try
            {
                SystemMemory = _monitor.GetSystemMemoryInfo();

                var processes = _monitor.GetProcessList();
                processes.Sort((a, b) => b.WorkingSet.CompareTo(a.WorkingSet));

                Processes.Clear();
                foreach (var process in processes)
                {
                    Processes.Add(process);
                }

                // 更新进程饼图 - 取TOP 5 + 其他归为一类
                UpdateProcessPieChart(processes);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void UpdateProcessPieChart(List<ProcessMemoryInfo> processes)
        {
            ProcessPieSlices.Clear();

            if (!processes.Any()) return;

            long totalAllProcessMemory = processes.Sum(p => p.WorkingSet);
            const int topCount = 5;
            var topProcesses = processes.Take(topCount).ToList();

            double startAngle = 0;
            double centerX = 125;
            double centerY = 125;
            double radius = 125;

            for (int i = 0; i < topProcesses.Count; i++)
            {
                var process = topProcesses[i];
                double percentage = (double)process.WorkingSet / totalAllProcessMemory * 100;
                double sweepAngle = percentage / 100 * 360;

                var slice = new PieSlice
                {
                    ProcessName = process.ProcessName,
                    Size = process.WorkingSet,
                    Percentage = percentage,
                    Color = _sliceColors[i % _sliceColors.Length]
                };

                slice.Geometry = CreatePieSliceGeometry(centerX, centerY, radius, startAngle, startAngle + sweepAngle);
                startAngle += sweepAngle;

                ProcessPieSlices.Add(slice);
            }

            // 添加其他进程
            long otherTotal = totalAllProcessMemory - topProcesses.Sum(p => p.WorkingSet);
            if (otherTotal > 0)
            {
                double otherPercentage = (double)otherTotal / totalAllProcessMemory * 100;
                double sweepAngle = otherPercentage / 100 * 360;

                var otherSlice = new PieSlice
                {
                    ProcessName = "其他进程",
                    Size = otherTotal,
                    Percentage = otherPercentage,
                    Color = _sliceColors[topProcesses.Count % _sliceColors.Length]
                };

                otherSlice.Geometry = CreatePieSliceGeometry(centerX, centerY, radius, startAngle, startAngle + sweepAngle);
                ProcessPieSlices.Add(otherSlice);
            }
        }

        private Geometry CreatePieSliceGeometry(double centerX, double centerY, double radius, double startAngle, double endAngle)
        {
            var pathFigure = new PathFigure { StartPoint = new Point(centerX, centerY) };

            // 转换为弧度
            double startRadians = startAngle * Math.PI / 180;
            double endRadians = endAngle * Math.PI / 180;

            double startX = centerX + radius * Math.Sin(startRadians);
            double startY = centerY - radius * Math.Cos(startRadians);
            double endX = centerX + radius * Math.Sin(endRadians);
            double endY = centerY - radius * Math.Cos(endRadians);

            pathFigure.Segments.Add(new LineSegment(new Point(startX, startY), true));

            bool isLargeArc = (endAngle - startAngle) > 180;

            pathFigure.Segments.Add(new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            });

            pathFigure.Segments.Add(new LineSegment(new Point(centerX, centerY), true));

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            return pathGeometry;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
