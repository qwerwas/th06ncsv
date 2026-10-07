using System.Collections.Concurrent;

namespace TH06NCTools;

internal readonly struct WriteRequest
{
    public readonly uint Rva;
    public readonly byte[] Data;
    public WriteRequest(uint rva, byte[] data) { Rva = rva; Data = data; }
}

/// <summary>
/// 作弊逻辑。全部只在"游戏逻辑在跑 (或已暂停)"时写入, 主菜单/结算画面一律不动手 ——
/// 那时全局结构可能还没初始化, 写进去就是往随机内存里塞数字。
///
/// 残机/灵击用**水位线锁**而不是写死大数字: 记录游戏自己给过的最高值, 只在数值下降时补回。
/// 好处是永远不会写入游戏从未出现过的数值, 即便偏移对不上也只是写了个合理值;
/// 游戏正常加命/回满时水位自动抬高, 不会出现"锁完就再也涨不回去"。
/// </summary>
internal sealed class NcCheats
{
    // ---- 持续生效的开关 ----
    public volatile bool InfiniteLives;
    public volatile bool InfiniteBombs;
    public volatile bool InfinitePower;
    public volatile bool Invincible;       // 实验性: 钉 Player+0x7898 = 3

    /// <summary>火力锁定值。新典火力上限未确认, 128 是原版上限, 显示异常就往小调。</summary>
    public volatile int PowerValue = 128;

    // ---- 一次性写入 ----
    private readonly ConcurrentQueue<WriteRequest> _queue = new();

    public void SetU8(uint rva, byte v) => _queue.Enqueue(new WriteRequest(rva, new[] { v }));
    public void SetU16(uint rva, ushort v) => _queue.Enqueue(new WriteRequest(rva, BitConverter.GetBytes(v)));
    public void SetI32(uint rva, int v) => _queue.Enqueue(new WriteRequest(rva, BitConverter.GetBytes(v)));
    public void SetI64(uint rva, long v) => _queue.Enqueue(new WriteRequest(rva, BitConverter.GetBytes(v)));

    // ---- 水位线 ----
    private int _livesHigh = -1;
    private int _bombsHigh = -1;
    private bool _invincibleHeld;

    public void ResetWatermarks() { _livesHigh = -1; _bombsHigh = -1; }

    /// <summary>处理一次写入请求 + 应用持续锁。由读取线程每帧调用。</summary>
    public void Apply(NcMemory mem, NcSnapshot s)
    {
        ulong b = mem.Base;

        while (_queue.TryDequeue(out var req))
            mem.Write(b + req.Rva, req.Data, req.Data.Length);

        if (InfiniteLives)
        {
            if (_livesHigh < 0 || s.Lives > _livesHigh) _livesHigh = s.Lives;
            else if (s.Lives < _livesHigh) mem.Write(b + Nc.RVA_Lives, new[] { (byte)_livesHigh }, 1);
        }
        else _livesHigh = -1;

        if (InfiniteBombs)
        {
            if (_bombsHigh < 0 || s.Bombs > _bombsHigh) _bombsHigh = s.Bombs;
            else if (s.Bombs < _bombsHigh) mem.Write(b + Nc.RVA_Bombs, new[] { (byte)_bombsHigh }, 1);
        }
        else _bombsHigh = -1;

        if (InfinitePower)
            mem.Write(b + Nc.RVA_Power, BitConverter.GetBytes((ushort)Math.Clamp(PowerValue, 0, 65535)), 2);

        if (Invincible)
        {
            // 自机状态钉成 3 (死亡/无敌分支)。来源: th12_hfr devnotes + 实测, 标为实验性
            mem.Write(b + Nc.RVA_Player + Nc.P_State, BitConverter.GetBytes(3), 4);
            _invincibleHeld = true;
        }
        else if (_invincibleHeld)
        {
            // 关闭时释放: 写回 0 (存活), 让游戏自己接手
            mem.Write(b + Nc.RVA_Player + Nc.P_State, BitConverter.GetBytes(0), 4);
            _invincibleHeld = false;
        }
    }
}
