using System.Runtime.InteropServices;

namespace PowerWatch;

/// <summary>
/// 直接和电池管理芯片对话，读取电压 / 瞬时功率 / 剩余容量。
/// 走 IOCTL 而不是 WMI：单次查询在微秒级，WMI 要几毫秒。
/// </summary>
internal sealed class BatteryReader : IDisposable
{
    // CTL_CODE(FILE_DEVICE_BATTERY=0x29, fn, METHOD_BUFFERED=0, FILE_READ_ACCESS=1)
    private const uint IOCTL_BATTERY_QUERY_TAG = 0x294040;
    private const uint IOCTL_BATTERY_QUERY_STATUS = 0x29404C;

    private const uint BATTERY_POWER_ON_LINE = 0x00000001;
    private const uint BATTERY_DISCHARGING = 0x00000002;
    private const uint BATTERY_CHARGING = 0x00000004;
    private const uint BATTERY_CRITICAL = 0x00000008;

    private IntPtr _handle = IntPtr.Zero;
    private uint _tag;
    private DateTime _tagStamp = DateTime.MinValue;

    public bool Available => _handle != IntPtr.Zero && _handle != new IntPtr(-1);
    public string LastError { get; private set; } = "";

    /// <summary>打开电池设备。失败不影响主程序，只是没有电压数据。</summary>
    public bool Open()
    {
        Close();

        string path = FindBatteryInterface();
        if (string.IsNullOrEmpty(path))
        {
            LastError = "未找到电池设备接口";
            return false;
        }

        IntPtr h = Native.CreateFile(
            path,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            Native.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            LastError = "无法打开电池设备 (err=" + Marshal.GetLastWin32Error() + ")";
            return false;
        }

        _handle = h;
        if (!RefreshTag())
        {
            LastError = "读取电池标签失败";
            Close();
            return false;
        }
        return true;
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            Native.CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _tag = 0;
        _tagStamp = DateTime.MinValue;
    }

    public void Dispose() => Close();

    private static string FindBatteryInterface()
    {
        Guid guid = Native.GUID_DEVICE_BATTERY;
        int cr = Native.CM_Get_Device_Interface_List_Size(
            out uint len, ref guid, 0, Native.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        if (cr != 0 || len == 0) return null;

        var buffer = new char[len];
        cr = Native.CM_Get_Device_Interface_List(
            ref guid, 0, buffer, len, Native.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        if (cr != 0) return null;

        int end = Array.IndexOf(buffer, '\0');
        if (end <= 0) return null;
        return new string(buffer, 0, end);
    }

    private bool RefreshTag()
    {
        uint wait = 0, tag = 0, ret = 0;
        if (!Native.DeviceIoControl(_handle, IOCTL_BATTERY_QUERY_TAG,
                ref wait, 4, ref tag, 4, out ret, IntPtr.Zero))
            return false;

        _tag = tag;
        _tagStamp = DateTime.UtcNow;
        return true;
    }

    /// <summary>读一次电池状态。返回 false 表示本次读取失败（不影响下一轮）。</summary>
    public bool TryRead(out BatterySample s)
    {
        s = default;
        if (!Available) return false;

        uint ret = 0;
        var wait = new Native.BATTERY_WAIT_STATUS
        {
            BatteryTag = _tag,
            Timeout = 0,
            PowerState = 0,
            LowCapacity = 0,
            HighCapacity = 0
        };
        var st = new Native.BATTERY_STATUS();

        bool ok = Native.DeviceIoControl(_handle, IOCTL_BATTERY_QUERY_STATUS,
            ref wait, Marshal.SizeOf<Native.BATTERY_WAIT_STATUS>(),
            ref st, Marshal.SizeOf<Native.BATTERY_STATUS>(),
            out ret, IntPtr.Zero);

        if (!ok)
        {
            // 标签失效（换电池 / 驱动重载）时重新取一次
            int err = Marshal.GetLastWin32Error();
            if (err == 2 || err == 1168)
            {
                if (RefreshTag()) return false;
            }
            return false;
        }

        s.Ok = true;
        s.VoltageMv = (int)st.Voltage;

        // 电流由功率与电压反推。注意：Rate 的符号各家驱动并不统一
        // （实测本机充电时 Rate 仍为正），所以符号一律以 PowerState 状态位为准。
        int ma = st.Voltage > 0 ? (int)Math.Round(st.Rate * 1000.0 / st.Voltage) : 0;
        bool charging = (st.PowerState & BATTERY_CHARGING) != 0;
        bool discharging = (st.PowerState & BATTERY_DISCHARGING) != 0;

        if (charging && !discharging) ma = -Math.Abs(ma);   // 充电记为负
        else if (discharging) ma = Math.Abs(ma);            // 放电记为正

        s.CurrentMa = ma;
        s.RateMw = charging && !discharging ? -Math.Abs(st.Rate) : Math.Abs(st.Rate);
        s.CapacityMwh = (int)st.Capacity;
        s.OnLine = (st.PowerState & BATTERY_POWER_ON_LINE) != 0;
        s.Charging = charging;
        s.Discharging = discharging;
        s.Critical = (st.PowerState & BATTERY_CRITICAL) != 0;
        s.RawPowerState = st.PowerState;
        return true;
    }

    public struct BatterySample
    {
        public bool Ok;
        public int VoltageMv;
        public int RateMw;
        public int CurrentMa;
        public int CapacityMwh;
        public bool OnLine;
        public bool Charging;
        public bool Discharging;
        public bool Critical;
        public uint RawPowerState;
    }
}
