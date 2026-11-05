using System.Runtime.InteropServices;

namespace PowerWatch;

/// <summary>所有非托管调用集中在此，避免依赖任何 NuGet 包。</summary>
internal static class Native
{
    // ---------- 系统电源状态（最轻量的电源变化探测） ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0=离线 1=在线 255=未知
        public byte BatteryFlag;
        public byte BatteryLifePercent;  // 255=未知
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // ---------- 电池聚合状态（AcOnLine / 充电 / 放电 / 功率） ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_BATTERY_STATE
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public byte Spare1, Spare2, Spare3;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public uint Rate;          // mW
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    public const int SystemBatteryStateLevel = 5;

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer, uint inputBufferSize,
        IntPtr outputBuffer, uint outputBufferSize);

    // ---------- CPU 占用（GetSystemTimes，比性能计数器便宜两个数量级） ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    // ---------- 电池设备接口枚举 ----------

    public static readonly Guid GUID_DEVICE_BATTERY = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");
    public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000001;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int CM_Get_Device_Interface_List_Size(
        out uint pulLen, ref Guid interfaceClassGuid, uint devID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int CM_Get_Device_Interface_List(
        ref Guid interfaceClassGuid, uint devID, char[] buffer, uint bufferLen, uint ulFlags);

    // ---------- 句柄与 IOCTL ----------

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr device, uint ioControlCode,
        ref uint inBuffer, int inBufferSize,
        ref uint outBuffer, int outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr device, uint ioControlCode,
        ref BATTERY_WAIT_STATUS inBuffer, int inBufferSize,
        ref BATTERY_STATUS outBuffer, int outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct BATTERY_WAIT_STATUS
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BATTERY_STATUS
    {
        public uint PowerState;
        public uint Capacity;   // mWh
        public uint Voltage;    // mV
        public int Rate;        // mW，正为放电，负为充电
    }

    // ---------- PDH：GPU 引擎占用率 ----------

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_MORE_DATA = 0x800007D2;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQuery(string szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath,
        IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat,
        ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr hQuery);

    [StructLayout(LayoutKind.Explicit)]
    public struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }
}
