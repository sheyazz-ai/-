using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace SystemMonitor;

internal sealed class HardwareMonitor : IDisposable
{
    private readonly object _sync = new();
    private Thread _thread;
    private volatile bool _stop;

    public volatile float CpuTemp = float.NaN;
    public volatile float GpuTemp = float.NaN;
    public volatile bool Ok;
    public volatile string Status = "датчики: инициализация…";

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "hw-sensors" };
        _thread.Start();
    }

    private void Loop()
    {
        Computer computer = null;
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsMemoryEnabled = false,
                IsNetworkEnabled = false,
                IsControllerEnabled = false,
            };
            computer.Open();
            Ok = true;
            Status = "датчики: ок";
        }
        catch (Exception ex)
        {
            Status = "датчики: " + ex.GetType().Name;
            return;
        }

        while (!_stop)
        {
            try
            {
                foreach (IHardware hw in computer.Hardware)
                {
                    hw.Update();
                    foreach (IHardware sub in hw.SubHardware) sub.Update();
                }

                CpuTemp = ReadTemp(computer, HardwareType.Cpu);
                GpuTemp = ReadTemp(computer, HardwareType.GpuNvidia);
                if (float.IsNaN(GpuTemp)) GpuTemp = ReadTemp(computer, HardwareType.GpuAmd);
                if (float.IsNaN(GpuTemp)) GpuTemp = ReadTemp(computer, HardwareType.GpuIntel);
            }
            catch (Exception ex)
            {
                Status = "датчики: сбой (" + ex.GetType().Name + ")";
            }

            for (int i = 0; i < 20 && !_stop; i++) Thread.Sleep(100);
        }

        try { computer.Close(); } catch { }
    }

    private static float ReadTemp(Computer computer, HardwareType type)
    {
        var values = new List<float>();
        foreach (IHardware hw in computer.Hardware)
        {
            if (hw.HardwareType != type) continue;
            Collect(hw, values);
        }
        if (values.Count == 0) return float.NaN;
        return values.Max();
    }

    private static void Collect(IHardware hw, List<float> values)
    {
        foreach (ISensor s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0 && s.Value.Value < 130)
            {
                values.Add(s.Value.Value);
            }
        }
        foreach (IHardware sub in hw.SubHardware) Collect(sub, values);
    }

    public void Dispose()
    {
        _stop = true;
        try { _thread?.Join(1500); } catch { }
    }
}

internal static class DisplayInfo
{
    public static string Describe()
    {
        var parts = new List<string>();
        uint index = 0;
        var device = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };

        while (Native.EnumDisplayDevicesW(null, index, ref device, 0))
        {
            if ((device.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
                if (Native.EnumDisplaySettingsW(device.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref mode))
                {
                    parts.Add($"{mode.dmPelsWidth}×{mode.dmPelsHeight} @ {mode.dmDisplayFrequency}");
                }
            }
            index++;
            device = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        }

        if (parts.Count == 0)
        {
            var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
            if (Native.EnumDisplaySettingsW(null, Native.ENUM_CURRENT_SETTINGS, ref mode))
            {
                parts.Add($"{mode.dmPelsWidth}×{mode.dmPelsHeight} @ {mode.dmDisplayFrequency}");
            }
        }

        return parts.Count == 0 ? "н/д" : string.Join("  ·  ", parts);
    }

    public static int PrimaryHz()
    {
        var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
        if (Native.EnumDisplaySettingsW(null, Native.ENUM_CURRENT_SETTINGS, ref mode)) return mode.dmDisplayFrequency;
        return 0;
    }

    public static string ResolutionsText()
    {
        var modes = new List<string>();
        uint index = 0;
        var device = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };

        while (Native.EnumDisplayDevicesW(null, index, ref device, 0))
        {
            if ((device.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
                if (Native.EnumDisplaySettingsW(device.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref mode))
                {
                    modes.Add($"{mode.dmPelsWidth}×{mode.dmPelsHeight} @ {mode.dmDisplayFrequency}");
                }
            }
            index++;
            device = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        }

        if (modes.Count == 0)
        {
            var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
            if (Native.EnumDisplaySettingsW(null, Native.ENUM_CURRENT_SETTINGS, ref mode))
            {
                modes.Add($"{mode.dmPelsWidth}×{mode.dmPelsHeight} @ {mode.dmDisplayFrequency}");
            }
        }

        return modes.Count == 0 ? "н/д" : string.Join(" · ", modes);
    }
}
