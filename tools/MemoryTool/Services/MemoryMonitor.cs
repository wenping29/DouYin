using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MemoryTool.Models;

namespace MemoryTool.Services
{
    public class MemoryMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public MemoryInfo GetSystemMemoryInfo()
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

            if (GlobalMemoryStatusEx(ref memStatus))
            {
                var totalMemory = (long)memStatus.ullTotalPhys;
                var availableMemory = (long)memStatus.ullAvailPhys;
                var usedMemory = totalMemory - availableMemory;
                var usagePercentage = (double)usedMemory / totalMemory * 100;

                return new MemoryInfo
                {
                    TotalMemory = totalMemory,
                    AvailableMemory = availableMemory,
                    UsedMemory = usedMemory,
                    UsagePercentage = usagePercentage
                };
            }

            return new MemoryInfo();
        }

        public List<ProcessMemoryInfo> GetProcessList()
        {
            var processes = new List<ProcessMemoryInfo>();

            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        processes.Add(new ProcessMemoryInfo
                        {
                            ProcessName = process.ProcessName,
                            ProcessId = process.Id,
                            WorkingSet = process.WorkingSet64
                        });
                    }
                    catch
                    {
                        // Skip processes we can't access
                    }
                }
            }
            catch
            {
                // Ignore any errors
            }

            return processes;
        }
    }
}
