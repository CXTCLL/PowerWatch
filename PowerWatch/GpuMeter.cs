using System.Runtime.InteropServices;

namespace PowerWatch;

/// <summary>
/// GPU 引擎占用率。走 PDH 的 \GPU Engine 计数集，把各路 3D/Compute 引擎求和。
/// 这样不用装 NVML、也不用引 NuGet。
/// </summary>
internal sealed class GpuMeter : IDisposable
{
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";

    private IntPtr _query = IntPtr.Zero;
    private IntPtr _counter = IntPtr.Zero;
    private IntPtr _buffer = IntPtr.Zero;
    private uint _bufferSize;
    private float _last;
    private int _itemSize = Marshal.SizeOf<Native.PDH_FMT_COUNTERVALUE_ITEM>();

    public bool Available { get; private set; }
    public string LastError { get; private set; } = "";

    public bool Init()
    {
        uint rc = Native.PdhOpenQuery(null, IntPtr.Zero, out _query);
        if (rc != 0)
        {
            LastError = "PdhOpenQuery 失败 0x" + rc.ToString("X8");
            return false;
        }

        rc = Native.PdhAddEnglishCounter(_query, CounterPath, IntPtr.Zero, out _counter);
        if (rc != 0)
        {
            LastError = "GPU 计数集不可用 0x" + rc.ToString("X8");
            Native.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            return false;
        }

        // 百分比计数器需要两次采集才有值
        Native.PdhCollectQueryData(_query);
        Available = true;
        return true;
    }

    /// <summary>返回 0–100，失败返回上一次的有效值。</summary>
    public float Sample()
    {
        if (!Available) return -1f;
        if (Native.PdhCollectQueryData(_query) != 0) return _last;

        uint size = 0, count = 0;
        uint rc = Native.PdhGetFormattedCounterArray(
            _counter, Native.PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);

        if (rc != Native.PDH_MORE_DATA && rc != 0) return _last;
        if (count == 0) { _last = 0; return 0; }

        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = size;
        }

        uint size2 = _bufferSize;
        rc = Native.PdhGetFormattedCounterArray(
            _counter, Native.PDH_FMT_DOUBLE, ref size2, out count, _buffer);
        if (rc != 0) return _last;

        double sum = 0;
        for (uint i = 0; i < count; i++)
        {
            IntPtr p = IntPtr.Add(_buffer, (int)i * _itemSize);
            var item = Marshal.PtrToStructure<Native.PDH_FMT_COUNTERVALUE_ITEM>(p);
            string name = item.szName == IntPtr.Zero ? null : Marshal.PtrToStringUni(item.szName);
            if (name == null) continue;

            bool wanted =
                name.Contains("_engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("_engtype_Compute", StringComparison.OrdinalIgnoreCase);
            if (!wanted) continue;

            double v = item.FmtValue.doubleValue;
            if (v > 0) sum += v;
        }

        if (sum > 100) sum = 100;
        if (sum < 0) sum = 0;
        _last = (float)sum;
        return _last;
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
        if (_query != IntPtr.Zero) { Native.PdhCloseQuery(_query); _query = IntPtr.Zero; }
        Available = false;
    }
}
