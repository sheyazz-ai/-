using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SystemMonitor;

internal sealed class ProcRow
{
    public int Pid;
    public int Session;
    public string Name = "";
    public string Path = "";
    public double Cpu;
    public long WorkingSet;
    public DateTime Start;
    public bool Heavy;
}

internal sealed class ProcessSampler
{
    private readonly Dictionary<int, ulong> _prevCpu = new();
    private DateTime _prevTime = DateTime.UtcNow;
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _prevReady;

    public double TotalCpu { get; private set; }
    public ulong MemTotal { get; private set; }
    public ulong MemAvail { get; private set; }
    public int Cores { get; } = Environment.ProcessorCount;

    public List<ProcRow> Sample()
    {
        var now = DateTime.UtcNow;
        double elapsed = (now - _prevTime).TotalSeconds;
        if (elapsed < 0.2) elapsed = 0.2;
        _prevTime = now;

        UpdateSystemCpu();
        UpdateMemory();

        var result = new List<ProcRow>(300);
        var alive = new HashSet<int>();
        var sb = new StringBuilder(1024);
        uint memSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.PROCESS_MEMORY_COUNTERS>();

        foreach (var proc in Process.GetProcesses())
        {
            int pid = proc.Id;
            alive.Add(pid);
            try
            {
                var row = new ProcRow { Pid = pid };

                int session = -1;
                try { session = proc.SessionId; } catch { }
                row.Session = session;

                IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_VM_READ, false, pid);
                if (handle == IntPtr.Zero)
                {
                    handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                }

                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        sb.Clear();
                        int size = sb.Capacity;
                        if (Native.QueryFullProcessImageNameW(handle, 0, sb, ref size)) row.Path = sb.ToString();
                        if (row.Path.Length == 0) row.Path = proc.ProcessName;
                        row.Name = Path.GetFileNameWithoutExtension(row.Path);

                        Native.FILETIME creation, exit, kernel, user;
                        if (Native.GetProcessTimes(handle, out creation, out exit, out kernel, out user))
                        {
                            ulong cpu100ns = kernel.Value + user.Value;
                            if (_prevCpu.TryGetValue(pid, out ulong prev) && cpu100ns >= prev)
                            {
                                double delta = cpu100ns - prev;
                                row.Cpu = Math.Clamp(delta / (elapsed * 10_000_000.0) / Cores * 100.0, 0, 100);
                            }
                            _prevCpu[pid] = cpu100ns;

                            if (creation.Value > 0)
                            {
                                try { row.Start = DateTime.FromFileTimeUtc((long)creation.Value).ToLocalTime(); } catch { }
                            }
                        }

                        Native.PROCESS_MEMORY_COUNTERS counters;
                        if (Native.GetProcessMemoryInfo(handle, out counters, memSize))
                        {
                            row.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                        }
                    }
                    finally
                    {
                        Native.CloseHandle(handle);
                    }
                }
                else
                {
                    row.Name = SafeName(proc);
                    row.Path = "";
                }

                if (string.IsNullOrEmpty(row.Name)) row.Name = SafeName(proc);
                result.Add(row);
            }
            catch
            {
                // процесс завершился между вызовами — пропускаем
            }
            finally
            {
                proc.Dispose();
            }
        }

        foreach (var key in new List<int>(_prevCpu.Keys))
        {
            if (!alive.Contains(key)) _prevCpu.Remove(key);
        }

        return result;
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; } catch { return ""; }
    }

    private void UpdateSystemCpu()
    {
        Native.FILETIME idle, kernel, user;
        if (!Native.GetSystemTimes(out idle, out kernel, out user)) return;

        if (_prevReady)
        {
            ulong dIdle = idle.Value - _prevIdle;
            ulong dKernel = kernel.Value - _prevKernel;
            ulong dUser = user.Value - _prevUser;
            ulong total = dKernel + dUser;
            if (total > 0)
            {
                TotalCpu = Math.Clamp((double)(total - dIdle) / total * 100.0, 0, 100);
            }
        }

        _prevIdle = idle.Value;
        _prevKernel = kernel.Value;
        _prevUser = user.Value;
        _prevReady = true;
    }

    private void UpdateMemory()
    {
        var status = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref status))
        {
            MemTotal = status.ullTotalPhys;
            MemAvail = status.ullAvailPhys;
        }
    }
}
