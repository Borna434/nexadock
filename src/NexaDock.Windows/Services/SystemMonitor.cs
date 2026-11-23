using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>CPU, memory, top processes, battery and connectivity from documented Win32 APIs.</summary>
public sealed class SystemMonitor : ISystemMonitor
{
    private long _lastIdle;
    private long _lastTotal;
    private readonly Dictionary<int, (string Name, TimeSpan Cpu)> _lastCpu = new();
    private DateTime _lastCpuSample = DateTime.MinValue;

    public SystemSnapshot Sample()
    {
        var cpu = 0.0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user; // kernel time includes idle time
            var dTotal = total - _lastTotal;
            var dIdle = idle - _lastIdle;
            if (_lastTotal != 0 && dTotal > 0)
            {
                cpu = Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
            }

            _lastTotal = total;
            _lastIdle = idle;
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        var used = mem.ullTotalPhys - mem.ullAvailPhys;
        var memPercent = mem.ullTotalPhys == 0 ? 0 : 100.0 * used / mem.ullTotalPhys;

        return new SystemSnapshot(cpu, memPercent, used, mem.ullTotalPhys, TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    public IReadOnlyList<ProcessUsage> TopByMemory(int count)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Bytes: g.Sum(p => SafeWorkingSet(p))))
                .OrderByDescending(x => x.Bytes)
                .Take(count)
                .Select(x => new ProcessUsage(Pretty(x.Name), x.Bytes, FormatBytes(x.Bytes)))
                .ToList();
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public IReadOnlyList<ProcessUsage> TopByCpu(int count)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastCpuSample).TotalMilliseconds;
        var current = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        var usage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var processes = Process.GetProcesses();

        try
        {
            foreach (var p in processes)
            {
                TimeSpan cpu;
                try
                {
                    cpu = p.TotalProcessorTime;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    continue; // protected or exited process
                }

                current[p.Id] = (p.ProcessName, cpu);
                if (_lastCpu.TryGetValue(p.Id, out var prev) && prev.Name == p.ProcessName && elapsed > 0)
                {
                    var share = (cpu - prev.Cpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount) * 100;
                    usage[p.ProcessName] = usage.GetValueOrDefault(p.ProcessName) + Math.Max(0, share);
                }
            }
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        _lastCpu.Clear();
        foreach (var kv in current)
        {
            _lastCpu[kv.Key] = kv.Value;
        }

        _lastCpuSample = now;
        return usage
            .Where(kv => !string.Equals(kv.Key, "Idle", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv => new ProcessUsage(Pretty(kv.Key), kv.Value, $"{kv.Value:0.0}%"))
            .ToList();
    }

    public PowerStatus GetPower()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryFlag == 255)
        {
            return new PowerStatus(false, 100, false);
        }

        var percent = s.BatteryLifePercent == 255 ? 100 : s.BatteryLifePercent;
        return new PowerStatus(true, percent, s.ACLineStatus == 1);
    }

    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

    private static long SafeWorkingSet(Process p)
    {
        try
        {
            return p.WorkingSet64;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static string Pretty(string processName) =>
        processName.Length > 0 ? char.ToUpperInvariant(processName[0]) + processName[1..] : processName;

    public static string FormatBytes(double bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (1L << 20):0} MB",
        _ => $"{bytes / 1024:0} KB",
    };
}
