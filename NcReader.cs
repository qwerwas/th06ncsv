using System.Diagnostics;
using System.Text;

namespace TH06NCTools;

/// <summary>
/// 后台采样线程: 附加进程 → 每帧读一份快照 → 交给渲染。
///
/// 数据范围严格限定在 Nc.cs 里"已确认"的那部分:
///   局内数值 / 残机灵击火力 / 自机 / 敌弹池 / 道具池 / 敌人池 / 激光池。
/// 敌人与激光自 2026-10-05 起接入 (字段偏移由 th06nc.exe 静态反汇编确认, 见 Nc.cs 表 12/13);
/// 判定盒尺寸这两项 (敌人 hitboxDimensions / 激光宽度) 偏移未逐字节确认, 走可调滑块。
/// </summary>
internal sealed class NcReader
{
    private readonly NcMemory _mem = new();
    private readonly Thread _thread;
    private volatile bool _running = true;

    public readonly NcCheats Cheats = new();

    /// <summary>采样频率 (Hz)。游戏逻辑固定 60Hz, 默认就按 60 采。</summary>
    public int PollHz = 60;

    public double ActualHz;

    /// <summary>最新完整快照 (双缓冲, 引用赋值原子)。</summary>
    public NcSnapshot Latest = new();
    private readonly NcSnapshot _sa = new();
    private readonly NcSnapshot _sb = new();
    private NcSnapshot _write;

    // ---- 转发状态 ----
    public bool Attached => _mem.Attached;
    public bool CanWrite => _mem.CanWrite;
    public string AttachStatus => _mem.AttachStatus;
    public string VersionWarning => _mem.VersionWarning;
    public string Hint => _mem.Hint;
    public string ShaShort => _mem.DigestShort;
    public string ShaFull => _mem.Sha256;
    public string ProcessName => _mem.ProcessName;
    public string LastError => _mem.LastError;
    public uint ImageSize => _mem.ImageSize;
    public string ExePath => _mem.ExePath;
    public int VersionWarnLevel => _mem.VersionWarnLevel;
    public uint TimeDateStamp => _mem.TimeDateStamp;

// ================================================================
    // ★ 判定策略 (2026-10-06 发布版): 全部写死成 const, 不再做成可点的选项。
    //
    //   判据 —— **关掉它, 显示的东西会不会变成错的 / 少掉信息?**
    //     会   → 写死。用户没有第二个"正确"选择, 给开关等于给他一个陷阱。
    //     不会 (只是换种画法) → 才配当选项。
    //   下面每一条都属于前者, 所以在 UI 上只会让人踩坑, 删掉是净收益。
    //
    //   为什么保留 `X && 判据` 而不手工化简成 `判据`:
    //     const 会被编译器消解, 运行时零开销; 而判据在源码里留着名字、
    //     搜得到、能查证 —— 比"只剩一行光秃秃的判断"好读, 将来真要放开
    //     也知道这里曾经有过什么。
    // ================================================================

    /// <summary>道具用 VM 绘制坐标 (+0x100/+0x104)。实测这才是道具**真正被画出来**的位置,
    /// 读 +0x10/+0x14 那对会在部分符卡上偏出场地 —— 关掉就画错位置, 写死。</summary>
    public const bool UseVmItemPos = true;

    /// <summary>过滤"尸体"道具: 位置纹丝不动超过 150ms 就当槽位残留剔掉。
    /// 新典槽位释放后坐标不清零, 残留槽会一直挂在场地里 —— 关掉就会画出道具尸体, 写死。
    /// (暂停时本过滤自动冻结, 见 ReadItems。)</summary>
    public const bool FilterStaticItems = true;

    /// <summary>槽位占用判据。1 = 只看坐标落不落在场地里。
    /// 0(看标志位) 实测不如它准, 2(不判) 是纯排查用、会画出一堆残留 —— 写死 1。</summary>
    public const int ItemOccupancy = 1;

    /// <summary>★★ 只画**已入场**的敌人 (+0xBD bit2)。这是敌人更新函数的第一道闸门,
    /// 不满足的槽游戏根本不更新、也不画。关掉 = 画出屏幕上根本不存在的敌人, 写死 true。</summary>
    public const bool OnlyEnteredEnemies = true;

    /// <summary>★★ 敌人更新函数的第二道闸门 (+0xBE bit3 置位就整条跳过: 不移动、不吃伤害)。
  /// 命中的槽 alive 仍是负数、HP 也已写好, 工具照画 ⇒ 就是用户报的
    /// "血量 900、打也不掉血的小方框"。关掉 = 画出幽灵, 写死 true。
    /// 证据: 0x039312 test al,4 / 0x03931A test byte [rbx+0xbe],8 / 0x039327 读 HP。</summary>
    public const bool SkipNonUpdatingEnemies = true;

    // ---- 缓冲 (预分配, 避免每帧 GC) ----
    private readonly byte[] _frameBuf = new byte[8];
    private readonly byte[] _statA = new byte[0x30];            // RVA_Misses .. Power
    private readonly byte[] _statB = new byte[0x30];            // RVA_Score .. Difficulty
    private readonly byte[] _statC = new byte[0x30];            // RVA_StageGraze .. Extends
    private readonly byte[] _misc4 = new byte[4];
    private readonly byte[] _misc8 = new byte[8];
    private readonly byte[] _inputBuf = new byte[2];
    private readonly byte[] _playerBuf = new byte[0x170];       // Player +0x7730 .. +0x789C
    private readonly byte[] _bulletBuf = new byte[Nc.BulletSlots * Nc.BulletStride];
    private readonly byte[] _itemBuf = new byte[Nc.ItemSlots * Nc.ItemStride];
    private readonly byte[] _enemyBuf = new byte[Nc.EnemySlots * Nc.EnemyStride];
    private readonly byte[] _laserBuf = new byte[Nc.LaserSlots * Nc.LaserStride];
    private readonly byte[] _descBuf = new byte[8];

    // 描述符地址 → 尺寸。描述符是静态的精灵数据, 地址恒定, 缓存后每帧只需几次读取。
    private readonly Dictionary<ulong, (float W, float H)> _descCache = new();

    // ★ 道具贴图描述符 → 边长。同上, 缓存后每帧几乎不用真的读内存。
    //   (实测 1024 个道具槽里 201 个不同描述符地址, 去重后缓存命中率极高)
    private readonly Dictionary<ulong, float> _itemDescCache = new();
    private readonly byte[] _itemDescBuf = new byte[8];

    // 道具槽位的上一帧位置 (过滤静止残留用)。读取线程私有, 无并发问题。
    private readonly float[] _itemPrevX = new float[Nc.ItemSlots];
    private readonly float[] _itemPrevY = new float[Nc.ItemSlots];
    private readonly long[] _itemPrevMs = new long[Nc.ItemSlots];

    /// <summary>
    /// 上一帧"该画的道具"。暂停时游戏逻辑整个冻住, 池里的数据是完全静止的 ——
    /// 此时若按正常流程重新判定, 依赖"是否移动"的那套过滤会瞬间失效
    /// (所有槽都"没动"), 于是暂停前被剔掉的槽位残留会全部冒出来,
    /// 表现就是"一暂停突然多出一堆没刷新的道具"。
    ///
    /// 所以暂停期间直接沿用这份快照, 不重新判定 —— 画面冻结在按下暂停的那一刻,
    /// 既不会多也不会少。这也是玩家对"暂停"的预期。
    /// </summary>
    private readonly NcItem[] _itemFrozen = new NcItem[Nc.ItemSlots];
    private int _itemFrozenCount;
    private string _itemFrozenSummary = "";

    // 道具池定位: RVA 0xBFB2F8 已由 6 处独立 lea 实锤, 不再需要"直寻址 vs 指针解引用"二选一。
    // 这里只保留一个节流计时器 —— 万一模块基址算错导致整池读不出东西, 2 秒后再试一次,
    // 免得刚进游戏时池还没初始化就把"读不到"永久锁死。
    private long _itemProbeNextMs;
    private bool _itemPoolDetected;
    private ulong _itemPoolAddr;
    private bool _itemPoolViaPtr;
    private int _itemPoolBest;      // 探测时数到的"坐标像道具"的槽数; 0 = 这一路没读出东西
    private readonly Dictionary<int, int> _typeCount = new();
    private readonly StringBuilder _typeSb = new();
    private readonly Dictionary<(float, float), int> _sizeCount = new();
    /// <summary>(判定w, 判定h, 贴图w, 贴图h) → 条数。用来抓"贴图≠判定"的弹种。</summary>
    private readonly Dictionary<(float, float, float, float), int> _sizeDiff = new();
    private readonly StringBuilder _sizeSb = new();

    private uint _lastFrame;
    private int _staleCount;
    private int _attachFails;
    private int _readFails;
    private int _hzCount;
    private long _hzWindow;

    private readonly Stopwatch _sw = Stopwatch.StartNew();

    public NcReader()
    {
        _write = _sa;
        _thread = new Thread(ThreadLoop) { IsBackground = true, Name = "TH06NC-Reader" };
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _thread.Join(1500); } catch { }
        _mem.Detach();
    }

    public void RequestReattach()
    {
        _mem.Detach();
        Cheats.ResetWatermarks();
        ResetItemState();
    }

    /// <summary>清掉所有"跟这一次进程绑定"的道具缓存 —— 换进程/重连后池地址可能不一样。</summary>
    private void ResetItemState()
    {
        Array.Clear(_itemPrevX);
        Array.Clear(_itemPrevY);
        Array.Clear(_itemPrevMs);
        _itemPoolDetected = false;
        _itemPoolAddr = 0;
        _itemPoolViaPtr = false;
        _itemPoolBest = 0;
        _itemProbeNextMs = 0;

        // 暂停冻结快照也要丢 —— 换了进程/重连后它属于上一次会话
        _itemFrozenCount = 0;
        _itemFrozenSummary = "";
    }

    private void ThreadLoop()
    {
        while (_running)
        {
            long t0 = _sw.ElapsedMilliseconds;

            if (!_mem.Attached)
            {
                if (!_mem.TryAttach())
                {
                    Latest.Clear();
                    // 重试退避: 枚举一遍进程要几百毫秒, 游戏没开时没必要每半秒扫一次
                    _attachFails = Math.Min(_attachFails + 1, 4);
                    Thread.Sleep(new[] { 500, 1000, 1500, 2000, 2000 }[_attachFails]);
                    continue;
                }
                _attachFails = 0;
                _lastFrame = 0;
                _staleCount = 0;
            }

            // ★★ 2026-10-06 14:40 ReadFrame 不再抛异常, 只在真的读不动时返回 false。
            //   各个池自己的失败一律**降级到上一帧**(见 Fail 的注释),
            //   所以这里只有"连帧计数器都读不出来"这种整体失效才会走到。
            bool frameOk = ReadFrame();
            if (!frameOk)
            {
                //
                // ★ 关键改动 1: **不再 Latest.Clear()。**
                //   原来一次偶发读失败就清空整个快照, 画面上敌人/弹幕/道具
                //   全部消失, 直到下一次读成功才回来 —— 用户报的
                //   "敌人有的时候没有及时刷新" 主要就是这个。
                //
                // ★ 关键改动 2: 重试间隔 250ms → **分级退避**。
                //   偶发抖动时 250ms 太久(画面会明显卡顿一下);
                //   真读不动时才需要慢慢退避。
                //     前 10 次: 16ms  (~一个轮询周期, 抖动无感)
                //     11-25 次: 60ms
                //     26 次以上: 250ms (多半基址错了, 别空转)
                if (_mem.IsProcessAlive() && ++_readFails <= 40)
                {
                    Thread.Sleep(_readFails <= 10 ? 16 : (_readFails <= 25 ? 60 : 250));
                    continue;
                }

                // 连续 40 次都没读通 —— 这回是真不行了(进程退出 / 基址错),
                // 这时候才清空画面并重连。
                _readFails = 0;
                Latest.Clear();
                _mem.Cooldown(1500);   // 多半是基址抓错了, 冷却一下让下次扫描重新算
                _mem.Detach();
                Thread.Sleep(300);
                continue;
            }

            _readFails = 0;   // 读通了就清零, 别把历史的失败累积到下次
            _hzCount++;
            long now = _sw.ElapsedMilliseconds;
            if (now - _hzWindow >= 1000)
            {
                ActualHz = _hzCount * 1000.0 / (now - _hzWindow);
                _hzCount = 0;
                _hzWindow = now;
            }

            int interval = 1000 / Math.Max(10, PollHz);
            int elapsed = (int)(_sw.ElapsedMilliseconds - t0);
            if (elapsed < interval) Thread.Sleep(interval - elapsed);
        }
    }

    // ---------------- 读一帧 ----------------

    /// <summary>
    /// ★★ 记录一次读失败, **不抛异常**。
    ///
    /// 为什么改成不抛 —— 原来 throw 会在 ThreadLoop 里被 catch 成
    /// 「清空整个快照 + 睡 250ms 重试」。于是**任何一次偶发读失败**
    /// (弹幕池 396KB / 敌人池 1MB 跨上百页, 偶尔读不动很正常)
    /// 都会让画面上的敌人/弹幕整片消失再等 1/4 秒才回来 ——
    /// 用户看到的现象就是「敌人有的时候没有及时刷新」。
    ///
    /// 改成记录后: 失败处跳过的赋值会**保留 s 里的旧值**。
    /// 因为快照是 A/B 轮换的, s 上一次被写的内容就是**上一帧**,
    /// 所以「什么都不做」天然等于「降级到上一帧」, 不需要额外拷贝。
    /// </summary>
    private bool Fail(string what)
    {
        _lastReadError = what + ": " + _mem.LastError;
        return false;
    }

    private string _lastReadError = "";

    /// <summary>最近一次读失败说明 (空 = 一直正常)。诊断面板会显示。</summary>
    public string LastReadError => _lastReadError;

    private bool ReadFrame()
    {
        var s = _write;
        var mem = _mem;
        ulong b = mem.Base;

        // --- 帧计数器: 判断游戏逻辑是否还在跑 ---
        // ★ 失败必须**整帧早退**: 后面的 _staleCount 依赖 fc, 用旧值会被误判成
        //   "游戏没在跑" → Active=false → 画面按"不在战斗中"处理。
        if (!mem.Read(b + Nc.RVA_BulletManager, _frameBuf, 8)) return Fail("frame counter");
        uint fc = BitConverter.ToUInt32(_frameBuf, 0);
        bool ticking = fc != _lastFrame;
        _lastFrame = fc;
        _staleCount = ticking ? 0 : Math.Min(_staleCount + 1, 999);
        s.FrameCounter = fc;
        s.Active = _staleCount < 5;   // 连续 5 次采样 (~80ms) 没动 = 不在战斗中

        // --- 局内数值 ---
        if (!mem.Read(b + Nc.RVA_Misses, _statA, _statA.Length)) Fail("statA");
        s.Misses = BitConverter.ToInt32(_statA, 0);
        s.BombsUsed = BitConverter.ToInt32(_statA, 4);
        s.Character = _statA[0x20];
        s.Shot = _statA[0x21];
        s.Stage = BitConverter.ToInt32(_statA, 0x24);
        s.Power = BitConverter.ToUInt16(_statA, 0x28);

        if (!mem.Read(b + Nc.RVA_Score, _statB, _statB.Length)) Fail("statB");
        s.Score = BitConverter.ToInt64(_statB, 0);
        s.Paused = _statB[0x18] != 0;
        s.GameOver = _statB[0x19] != 0;
        s.Practice = _statB[0x1C] != 0;
        s.SpellPractice = _statB[0x1D] != 0;
        s.Point = BitConverter.ToInt32(_statB, 0x24);
        s.Difficulty = BitConverter.ToInt32(_statB, 0x28);

        if (mem.Read(b + Nc.RVA_DisplayScore, _misc8, 8)) s.DisplayScore = BitConverter.ToInt64(_misc8, 0);

        if (!mem.Read(b + Nc.RVA_StageGraze, _statC, _statC.Length)) Fail("statC");
        s.StageGraze = BitConverter.ToInt32(_statC, 0);
        s.Graze = BitConverter.ToInt32(_statC, 4);
        s.BoundL = BitConverter.ToSingle(_statC, 0x14);
        s.BoundT = BitConverter.ToSingle(_statC, 0x18);
        s.BoundR = BitConverter.ToSingle(_statC, 0x1C);
        s.BoundB = BitConverter.ToSingle(_statC, 0x20);
        s.Lives = _statC[0x24];
        s.Bombs = _statC[0x25];
        s.Extends = _statC[0x26];

        if (mem.Read(b + Nc.RVA_CurrentState, _misc4, 4)) s.CurrentState = BitConverter.ToInt32(_misc4, 0);
        if (mem.Read(b + Nc.RVA_TimelineFrame, _misc4, 4)) s.TimelineFrame = BitConverter.ToInt32(_misc4, 0);
        if (mem.Read(b + Nc.RVA_Input, _inputBuf, 2)) s.Input = BitConverter.ToUInt16(_inputBuf, 0);

        // --- 自机 ---
        // Player 结构很大, 只取坐标 + 判定半径 + 状态这一段
        if (!mem.Read(b + Nc.RVA_Player + Nc.P_PosX, _playerBuf, _playerBuf.Length))
            Fail("player");
        s.PX = BitConverter.ToSingle(_playerBuf, 0);
        s.PY = BitConverter.ToSingle(_playerBuf, 4);
        s.PlayerR = BitConverter.ToSingle(_playerBuf, Nc.P_HitRadius - Nc.P_PosX);
        s.PlayerState = BitConverter.ToInt32(_playerBuf, Nc.P_State - Nc.P_PosX);
        s.PlayerValid = IsFinite(s.PX) && IsFinite(s.PY)
                    && s.PX > -160f && s.PX < 560f && s.PY > -160f && s.PY < 620f
                    && s.PlayerR > 0.01f && s.PlayerR < 64f;

        // --- 敌弹池 ---
        ReadBullets(s);

        // --- 道具池 ---
        ReadItems(s);

        // --- 敌人池 ---
        ReadEnemies(s);

        // --- 激光池 ---
        ReadLasers(s);

        s.TimestampMs = Environment.TickCount64;
        s.Valid = true;

        // --- 写入 (只在游戏逻辑在跑 / 已暂停时) ---
        if (s.Valid && (s.Active || s.Paused) && s.PlayerValid)
            Cheats.Apply(mem, s);

        // 双缓冲交换: 写完才挂到 Latest
        Latest = s;
        _write = ReferenceEquals(s, _sa) ? _sb : _sa;
        return true;
    }

    private void ReadBullets(NcSnapshot s)
    {
        var mem = _mem;
        // 分块读: 396KB 跨 ~97 页, 末尾任一页未提交就会整块失败
        if (!mem.ReadChunked(mem.Base + Nc.RVA_BulletPool, _bulletBuf, _bulletBuf.Length,
                            out int bulletGot) || bulletGot < Nc.BulletStride)
        {
            // ★ 不抛异常: 早退后 s.Bullets / s.BulletCount 保持上一帧, 画面不会闪空
            Fail("bullets: " + mem.LastError);
            return;
        }

        int n = 0, known = 0, knownHit = 0, active = 0;
        var arr = s.Bullets;
        _sizeCount.Clear();
        _sizeDiff.Clear();

        for (int i = 0; i < Nc.BulletSlots; i++)
        {
            int off = i * Nc.BulletStride;
            ushort state = BitConverter.ToUInt16(_bulletBuf, off + Nc.B_State);
            if (state == 0) continue;

            float x = BitConverter.ToSingle(_bulletBuf, off + Nc.B_PosX);
            float y = BitConverter.ToSingle(_bulletBuf, off + Nc.B_PosY);
            if (!IsFinite(x) || !IsFinite(y)) continue;
            if (x < -200f || x > 600f || y < -300f || y > 800f) continue;

            float w = 0, h = 0;
            bool ok = false;
            bool fromHitbox = false;

            // ★★ 2026-10-06 15:20 **优先读判定尺寸** 条目 +0x5F4/+0x5F8。
            //   这是游戏命中判定真正用的值(见 Nc.B_HitW 的证据链), 来自弹种表。
            //   此前只画描述符尺寸(贴图), 于是"大玉判定小于贴图"的情况全线画大。
            float hw = BitConverter.ToSingle(_bulletBuf, off + Nc.B_HitW);
            float hh = BitConverter.ToSingle(_bulletBuf, off + Nc.B_HitH);
            // 判据: 有限、为正、且不超过一个合理上限(弹幕判定不会大过 128 像素)
            if (IsFinite(hw) && IsFinite(hh) && hw > 0.5f && hh > 0.5f && hw <= 256f && hh <= 256f)
            {
                w = hw; h = hh; ok = true; fromHitbox = true;
            }

            // 描述符尺寸(贴图)只作为**兜底 / 对照**: 判定尺寸读不到时才用,
            // 并且两者都记录到诊断里, 好让人一眼看出"贴图 ≠ 判定"。
            float texW = 0, texH = 0;

            // +0x150 是**绝对线性地址** (多数为堆分配), 不能再加模块基址 ——
            // 叠两次基址会读到别处的内存, 尺寸全读不出来, 只能走兜底
            ulong desc = BitConverter.ToUInt64(_bulletBuf, off + Nc.B_Desc);
            if (IsPlausiblePtr(desc))
            {
                if (!_descCache.TryGetValue(desc, out var wh))
                {
                    wh = (0, 0);
                    if (mem.Read(desc + Nc.D_Width, _descBuf, 8))
                    {
                        float dw = BitConverter.ToSingle(_descBuf, 0);
                        float dh = BitConverter.ToSingle(_descBuf, 4);
                        // 下限 1.5 是为了避开 ScaleX 那类恒为 1.0 的相邻 float (实测踩过坑)
                        if (dw >= 1.5f && dw <= 256f && dh >= 1.5f && dh <= 256f)
                        {
                            wh = (dw, dh);
                            if (_descCache.Count > 4096) _descCache.Clear();
                            _descCache[desc] = wh;
                        }
                    }
                }
                texW = wh.W; texH = wh.H;
                if (!ok) { w = wh.W; h = wh.H; ok = w > 0 && h > 0; }
            }

            // 贴图与判定都拿到时, 记下差异 —— 用于诊断"哪些弹贴图≠判定"
            if (fromHitbox && texW > 0 && texH > 0)
            {
                var kd = (MathF.Round(w), MathF.Round(h), MathF.Round(texW), MathF.Round(texH));
                _sizeDiff[kd] = _sizeDiff.TryGetValue(kd, out int c2) ? c2 + 1 : 1;
            }

            if (state == 1)
            {
                active++;
                if (ok) known++;
                if (fromHitbox) knownHit++;
                if (ok)
                {
                    var k = (MathF.Round(w), MathF.Round(h));
                    _sizeCount[k] = _sizeCount.TryGetValue(k, out int c) ? c + 1 : 1;
                }
            }

            arr[n++] = new NcBullet { X = x, Y = y, State = state, W = w, H = h,
                                     SizeKnown = ok, FromHitbox = fromHitbox,
                                     TexW = texW, TexH = texH };
        }

        s.BulletCount = n;
        s.BulletActiveState1 = active;
        s.BulletSizeKnown = known;
        s.BulletHitboxKnown = knownHit;
        s.BulletSizeSummary = BuildSizeSummary();
        s.BulletSizeDiffSummary = BuildSizeDiffSummary();
    }

    /// <summary>
    /// ★ 2026-10-06 15:25 新增: 列出**贴图尺寸 ≠ 判定尺寸**的弹种。
    ///   格式 "判定 16×16 ← 贴图 32×32 ×12"。
    ///   这是"大玉判定小于贴图"这类差异的唯一直接证据 —— 以前只画贴图,
    ///   所以看不出游戏判定其实更小。
    /// </summary>
    private string BuildSizeDiffSummary()
    {
        if (_sizeDiff.Count == 0) return "";
        _sizeSb.Clear();
        int shown = 0;
        foreach (var kv in _sizeDiff)
        {
            var (hw, hh, tw, th) = kv.Key;
            // 只报**真有差异**的: 大玉往往贴图比判定大一圈
            if (MathF.Abs(hw - tw) < 0.5f && MathF.Abs(hh - th) < 0.5f) continue;
            if (shown++ >= 5) { _sizeSb.Append(" …"); break; }
            if (shown > 1) _sizeSb.Append(" · ");
            _sizeSb.Append($"判定 {hw:0}×{hh:0} ← 贴图 {tw:0}×{th:0} ×{kv.Value}");
        }
        return _sizeSb.ToString();
    }

    /// <summary>
    /// 把当前屏幕上读到的弹幕尺寸种类拼成 "12×12 ×23 · 32×32 ×5"。
    /// 这是校验"描述符里的 w/h 到底是不是弹幕尺寸"最直观的一手证据:
    /// 如果能看到几个整整齐齐的常见值 (8 / 12 / 16 / 24 / 32 这类), 说明读的是真尺寸;
    /// 如果全是同一个数、或者是一堆没有规律的怪值, 那多半读偏了 (读到别的 float 了)。
    /// </summary>
    private string BuildSizeSummary()
    {
        if (_sizeCount.Count == 0) return "";
        _sizeSb.Clear();
        int shown = 0;
        foreach (var kv in _sizeCount)
        {
            if (shown++ >= 6) { _sizeSb.Append(" …"); break; }
            if (shown > 1) _sizeSb.Append(" · ");
            _sizeSb.Append(kv.Key.Item1.ToString("0")).Append('×').Append(kv.Key.Item2.ToString("0"))
                    .Append(" ×").Append(kv.Value);
        }
        return _sizeSb.ToString();
    }

    /// <summary>
    /// 定位道具池。地址表给的 RVA 有两种可能: 那里就是数组本体, 或者那里先存了一个堆指针。
    /// 两种都读一遍, 取"占用槽更多"的那个, 检测一次就记住 —— 省得每次都靠猜。
    /// </summary>
    private ulong ResolveItemPool()
    {
        var mem = _mem;

        // 池地址已由 6 处独立的 lea 指令实锤 (0xF674 / 0xF87A / 0x11A0F / 0x11B6C /
        // 0x43FD2 全部算出 0xBFB2F8), 不需要再"探测"。
        //
        // 旧版本这里会去读池头 8 字节当指针试着解引用, 那是净负作用: 池头就是第一个道具
        // 条目的数据, 解引用出来的是一堆 float 位模式, 会被误判成"更像的池地址"。
        ulong direct = mem.Base + Nc.RVA_ItemPool;

        // 只有直寻址读不出东西时才值得再探 —— 顺带看看是不是模块基址算错了,
        // 那就在基址附近找一个偏移, 让"基址不对"这种情况还能自愈。
        if (!_itemPoolDetected || _itemPoolBest <= 0)
        {
            if (Environment.TickCount64 >= _itemProbeNextMs)
            {
                _itemProbeNextMs = Environment.TickCount64 + 2000;
                int best = -1;
                if (mem.ReadChunked(direct, _itemBuf, _itemBuf.Length, out int got)
                    && got >= Nc.ItemStride)
                {
                    best = CountOccupied(_itemBuf, Math.Min(Nc.ItemSlots, got / Nc.ItemStride));
                }
                _itemPoolBest = best;
                _itemPoolDetected = true;
            }
        }

        _itemPoolAddr = direct;
        _itemPoolViaPtr = false;
        return _itemPoolAddr;
    }

    /// <summary>数一下这块缓冲里有多少个"看着像在用"的道具槽: 坐标有限、且落在场地附近。
    /// 这里**故意不看 script** —— 判定池地址对不对时不能拿一个本身就有待证实的字段当前提。</summary>
    private static int CountOccupied(byte[] buf, int slots)
    {
        int n = 0;
        for (int i = 0; i < slots; i++)
        {
            int off = i * Nc.ItemStride;
            float x = BitConverter.ToSingle(buf, off + Nc.I_PosX);
            float y = BitConverter.ToSingle(buf, off + Nc.I_PosY);
            if (!IsFinite(x) || !IsFinite(y)) continue;
            if (x < -64f || x > Nc.FieldW + 64f || y < -64f || y > Nc.FieldH + 64f) continue;
            n++;
        }
        return n;
    }

    public string ItemPoolInfo =>
        _itemPoolDetected ? $"0x{_itemPoolAddr:X} · {(_itemPoolViaPtr ? "经指针" : "直接")}" : "";

    private void ReadItems(NcSnapshot s)
    {
        var mem = _mem;
        ulong pool = ResolveItemPool();

        // 分块读: 256KB 横跨很多页, 末尾只要有一页没提交, 整块 ReadProcessMemory
        // 就会失败 (err=299) —— 那会让道具一个都显示不出来。分块 + 部分成功即可。
        if (!mem.ReadChunked(pool, _itemBuf, _itemBuf.Length, out int got) || got < Nc.ItemStride)
        {
            // ★ 同敌人/激光: 保留上一帧, 不闪空
            Fail("items");
            s.ItemPoolInfo = "★读取失败(沿用上一帧): " + mem.LastError;
            return;
        }

        s.ItemPoolInfo = $"0x{pool:X} · {(_itemPoolViaPtr ? "经指针" : "直接")} · 读 {got / Nc.ItemStride} 槽";

        // ---- 暂停: 沿用冻结快照, 不重新判定 ----
        // 暂停时游戏逻辑整个停住, 池数据完全静止。此时若照常跑判定流程, 凡是依赖
        // "是否移动"的过滤 (过滤静止残留) 会瞬间全部失效 —— 因为所有槽都"没动",
        // 于是暂停前被剔掉的槽位残留在这一刻全被当成有效道具画出来, 表现就是
        // "一暂停突然冒出一堆没刷新的道具"。直接复用暂停前最后一次结果即可。
        if (s.Paused)
        {
            int fc = Math.Min(_itemFrozenCount, s.Items.Length);
            for (int i = 0; i < fc; i++) s.Items[i] = _itemFrozen[i];
            s.ItemCount = fc;
            s.ItemSlotsUsed = s.ItemCoordOk = s.ItemStaticDropped = 0;
            s.ItemCoordLike = s.ItemAliveNonZero = 0;
            s.ItemTypeSummary = _itemFrozenSummary;
            s.ItemPoolInfo += " · 暂停(冻结)";
            return;
        }

        int n = 0, used = 0, coordOk = 0, dropped = 0, coordLike = 0;
        int aliveNz = 0, texKnown = 0;
        int slotsRead = got / Nc.ItemStride;
        int px = UseVmItemPos ? Nc.I_VmPosX : Nc.I_PosX;
        int py = UseVmItemPos ? Nc.I_VmPosY : Nc.I_PosY;
        var arr = s.Items;
        long now = Environment.TickCount64;

        // 暂停已在上方提前返回, 这里不再需要考虑暂停 —— 只在游戏跑的时候才用静止过滤
        bool filter = FilterStaticItems;

        _typeCount.Clear();

        // 只扫真正读回来的槽数, 别去扫那些被清零的页 (清零区所有偏移都是 0, 会算出假数据)
        for (int i = 0; i < slotsRead; i++)
        {
            int off = i * Nc.ItemStride;

            // +0x00 非零的槽有几个 —— 这是"游戏认为这条槽在用"的原始事实,
            // 跟任何过滤无关。右侧面板直接显示它, 一眼就能分清是读不到还是筛掉了。
            bool alive = _itemBuf[off + Nc.I_Alive] != 0;
            if (alive) aliveNz++;

            // "坐标像"的计数**必须只在存活槽里做**。
            // 空槽的坐标是 (0,0), 而 (0,0) 落在场地内 —— 不过滤的话 1024 个空槽全部命中,
            // 这个数会恒等于槽数, 变成一句永远正确的废话, 诊断价值归零。
            if (alive)
            {
                float cxr = BitConverter.ToSingle(_itemBuf, off + Nc.I_PosX);
                float cyr = BitConverter.ToSingle(_itemBuf, off + Nc.I_PosY);
                if (IsFinite(cxr) && IsFinite(cyr) && cxr > -64f && cxr < Nc.FieldW + 64f
                                                && cyr > -64f && cyr < Nc.FieldH + 64f)
                    coordLike++;
            }

            // 槽位占用判据: 默认看 +0x00 的标志字节 (游戏自己就是这么判的)。
            // 槽被回收时清掉 movement 历史, 免得槽位复用时拿旧坐标把新道具误判成尸体
            if (ItemOccupancy == 0 && !alive)
            {
                _itemPrevMs[i] = 0;
                continue;
            }

            float x = BitConverter.ToSingle(_itemBuf, off + px);
            float y = BitConverter.ToSingle(_itemBuf, off + py);
            if (!IsFinite(x) || !IsFinite(y)) continue;
            // 放宽到 ±200: 道具刚生成/被吸出屏幕时会短暂越界, 卡太死会把真道具剔掉
            if (x < -200f || x > Nc.FieldW + 200f || y < -200f || y > Nc.FieldH + 200f) continue;

            // 没有标志位可判时 (模式 1/2), 空槽坐标一般是 0,0 —— 用这个挡掉。
            // 注意: 会误伤恰好落在左上角 (0,0) 的真道具, 所以只在"该槽标志字节确实为 0"
            // 时才生效 —— 模式 0 下有标志位可判, 这条判据根本不参与。
            if (ItemOccupancy != 0 && !alive && x == 0f && y == 0f) continue;

            used++;
            coordOk++;

            bool moved = MathF.Abs(x - _itemPrevX[i]) > 0.01f || MathF.Abs(y - _itemPrevY[i]) > 0.01f;
            if (moved || _itemPrevMs[i] == 0)
            {
                _itemPrevX[i] = x;
                _itemPrevY[i] = y;
                _itemPrevMs[i] = now;
            }
            else if (filter && now - _itemPrevMs[i] > 150)
            {
                dropped++;
                continue;   // 位置纹丝不动超过 150ms: 是槽位残留, 不是真道具
            }

            // 类型是 1 字节 (反汇编: mov byte ptr [item+0x34], 6)。
            // 旧代码按 i32 读, 会把后面 3 个字节也吃进来 → 类型号全是乱七八糟的大数
            int type = _itemBuf[off + Nc.I_Type];
            _typeCount[type] = _typeCount.TryGetValue(type, out int c) ? c + 1 : 1;

            // ---- ★ 贴图尺寸: [条目+0x138] → 描述符 +0x1C/+0x20 ----
            // 用户要求道具按**贴图大小**显示(正方形)。实测 200/201 个描述符都是 16×16。
            // 读不到(槽刚分配 / 指针还没填)就走 Nc.ItemTexFallback 兜底, 不至于画不出来。
            float texSize = Nc.ItemTexFallback;
            bool okTex = false;
            ulong desc = BitConverter.ToUInt64(_itemBuf, off + Nc.I_Desc);
            if (IsPlausiblePtr(desc))
            {
                if (!_itemDescCache.TryGetValue(desc, out float cached))
                {
                    cached = 0f;
                    if (mem.Read(desc + Nc.D_Width, _itemDescBuf, 8))
                    {
                        float dw = BitConverter.ToSingle(_itemDescBuf, 0);
                        float dh = BitConverter.ToSingle(_itemDescBuf, 4);
                        // 下限 1.5 避开 Scale 那类恒为 1.0 的相邻 float(弹幕那边踩过同样的坑)
                        if (dw >= 1.5f && dw <= 256f && dh >= 1.5f && dh <= 256f)
                        {
                            // 正方形: 取宽(与实测 w==h 一致; 万一不等, 取较大者保守一点)
                            cached = MathF.Max(dw, dh);
                            if (_itemDescCache.Count > 4096) _itemDescCache.Clear();
                            _itemDescCache[desc] = cached;
                        }
                    }
                }
                if (cached > 0f) { texSize = cached; texKnown++; okTex = true; }
            }

            arr[n++] = new NcItem { X = x, Y = y, Type = type,
                                    Size = texSize, SizeKnown = okTex };
        }

        s.ItemCount = n;
        s.ItemSlotsUsed = used;
        s.ItemCoordOk = coordOk;
        s.ItemStaticDropped = dropped;
        s.ItemCoordLike = coordLike;
        s.ItemAliveNonZero = aliveNz;
        s.ItemTypeSummary = BuildTypeSummary();
        s.ItemPoolInfo = $"0x{pool:X} · {slotsRead}/{Nc.ItemSlots} 槽 · " +
                        $"标志非零 {aliveNz} · 坐标像 {coordLike} · 画出 {n} · " +
                        $"贴图尺寸读到 {texKnown}";

        // 存一份"该画的道具"快照, 供暂停期间沿用 (见 ReadItems 开头的冻结分支)
        int keep = Math.Min(n, _itemFrozen.Length);
        for (int i = 0; i < keep; i++) _itemFrozen[i] = arr[i];
        _itemFrozenCount = keep;
        _itemFrozenSummary = s.ItemTypeSummary;
    }

    /// <summary>
    /// 敌人池 (池 0xAEE0B8, 257 槽 x 0x10B0)。
    ///
    /// 字段全部来自反汇编 (Nc.cs 表 12)。★活跃判据是**负数**, 不是非零:
    ///   0x37C20 找空槽:  cmp byte [rbx+0xBC], 0 ; jge 0x37C45  ->  >=0 才是空槽
    ///   0x38DC0 更新  :  cmp byte [rbx+0xBC], 0 ; jge skip ; inc [r12] -> 负数才更新+计数
    /// 旧代码写的是 <c>if (alive == 0) continue</c> —— 恰好把真正在用的敌人全过滤掉,
    /// 却把空槽全放进来 (空槽坐标 0,0 落在场内, 坐标检查挡不住)。
    ///
    /// +0xB0/+0xB4/+0xB8 是**每帧由速度重算并 clamp 过的最终绘制坐标**, 直接拿来画。
    /// 判定盒用滑块值 (原版 hitboxDimensions 默认 12.0, th06nc 侧偏移未逐字节确认)。
    /// </summary>
    private void ReadEnemies(NcSnapshot s)
    {
        var mem = _mem;
        if (!mem.ReadChunked(mem.Base + Nc.RVA_EnemyPool, _enemyBuf, _enemyBuf.Length, out int got)
            || got < Nc.EnemyStride)
        {
            //
            // ★★ 2026-10-06 14:40 不再把计数清零。
            //   原来一读失败就把 EnemyCount 置 0 —— 画面上敌人**整片消失一帧**,
            //   然后下一帧才回来, 肉眼看就是"敌人没有及时刷新"。
            //   这里直接 return: s 是双缓冲轮换来的, 里面留着的就是上一帧,
            //   于是"读失败"降级成"沿用上一帧", 不会闪。
            Fail("enemies: " + mem.LastError);
            s.EnemyInfo = "★读失败(沿用上一帧): " + mem.LastError;
            return;
        }

        int n = 0, active = 0, inField = 0, negAlive = 0, pending = 0, skipped = 0;
        // 仅当直读失败(非有限/非正)时才顶替。判定盒已经能直读 enemy+0x2C0/+0x2C4,
    // 这个数字只是"万一读不到"的兜底, 原来还挂在 UI 滑块上 —— 已删。
float hbFallback = Nc.EnemyHitboxDefault;
        var arr = s.Enemies;

        for (int i = 0; i < Nc.EnemySlots; i++)
        {
            int off = i * Nc.EnemyStride;

            float x = BitConverter.ToSingle(_enemyBuf, off + Nc.E_PosX);
            float y = BitConverter.ToSingle(_enemyBuf, off + Nc.E_PosY);
            if (!IsFinite(x) || !IsFinite(y)) continue;

            // 生成时默认位置是 (192, -64) = 屏幕中心上方, 说明活着时坐标必在场地附近。
            // 放宽到 -200..600 / -300..800 与弹幕同一套, 兼容出场动画还没落到场内的。
            bool onField = x > -200f && x < 600f && y > -300f && y < 800f;
            if (onField) inField++;

            // ★ 必须按 sbyte 读: 判据是"负数 = 占用中"。>=0 一律是空槽。
            sbyte alive = (sbyte)_enemyBuf[off + Nc.E_Alive];
            if (alive >= 0) continue;
            negAlive++;
            if (!onField) continue;   // 占用中但坐标离谱(正被移出场地回收) -> 不画

            byte flags = _enemyBuf[off + Nc.E_Flags];

            // ★★ 2026-10-06 15:30 过滤**未入场**的槽。
            //   这类槽 alive 已是负数、HP 也已写好, 但还在场地外待命,
            //   游戏并没有把它们画出来 —— 工具画了就变成"屏幕上不存在的敌人"。
            //   判据就是游戏自己用的那个 bit2(见 Nc.E_FlagEntered 的汇编证据)。
            if (OnlyEnteredEnemies && (flags & Nc.E_FlagEntered) == 0)
            {
                pending++;
                continue;
            }

            // ★★ 闸门②: +0xBE bit3 置位 ⇒ 游戏主更新循环整条跳过这条敌人 ⇒ 不画。
            //    (证据见 Nc.E_SkipUpdate 的汇编引文 / NcReader.SkipNonUpdatingEnemies 注释)
            byte flags2 = _enemyBuf[off + Nc.E_Flags2];
            if (SkipNonUpdatingEnemies && (flags2 & Nc.E_SkipUpdate) != 0)
            {
                skipped++;
                continue;
            }
            active++;

            float z = BitConverter.ToSingle(_enemyBuf, off + Nc.E_PosZ);
            int life = BitConverter.ToInt32(_enemyBuf, off + Nc.E_Life);

            // ★★ 判定盒直读 enemy+0x2C0/+0x2C4 (表 12.2, 由 ECL ins_103 设置)。
            //   这三个值是**盒宽/盒高**, 画方框要的是半径 → 乘 0.5。
            //   游戏自己的算法也是 min(w,h)*0.5 + 玩家半径 (0x6C122/0x6C139/0x6C145)。
            float bx = BitConverter.ToSingle(_enemyBuf, off + Nc.E_HitboxX);
            float by = BitConverter.ToSingle(_enemyBuf, off + Nc.E_HitboxY);
            float hb;
            if (IsFinite(bx) && IsFinite(by) && bx > 0f && by > 0f)
                hb = Math.Min(bx, by) * 0.5f;
            else
                hb = hbFallback;

            arr[n++] = new NcEnemy { X = x, Y = y, Z = z, Life = life, Alive = alive, Hitbox = hb };
        }

        s.EnemyCount = n;
        s.EnemyActive = active;
        s.EnemyInField = inField;
        s.EnemyAliveNeg = negAlive;
        s.EnemyInfo = $"池 0x{Nc.RVA_EnemyPool:X} · 画出 {n} " +
                        $"(占用 {negAlive} / 坐标合法 {inField}"
                      + (pending > 0 ? $" / 未入场滤掉 {pending}" : "")
                      + (skipped > 0 ? $" / 被游戏跳过滤掉 {skipped}" : "") + ")";
    }

    /// <summary>
    /// 激光池 (池 0x52BF00, 64 槽 × 0x298)。
    /// 字段来自反汇编 (表 13), 激光更新循环 A 0xF810..0xF9F0:
    ///   起点 X/Y (+0x284/+0x280)、角度 (+0x268, sinf/cosf 入参)、
    ///   当前延伸 (+0x18, 每轮 += 32.0)、总长 (+0x1C)、活跃 (+0x27C 非 0)。
    /// 判定几何: 起点沿 (cos,sin) 方向延伸 当前长度 —— 直接照这个画线段。
    ///
    /// ★池基址是 0x52BF00 而不是曾经写的 0x52BF10 —— 后者差 0x10, 落在条目内部,
    /// 读到的全是错位字节, 结果就是一条激光都画不出来。推导过程见 Nc.cs "表 4.1"。
    /// </summary>

    /// <summary>ECL 里出现过的全部激光宽度取值(全宽)。见地址表 12.11。</summary>
    internal static readonly int[] EclWidths = { 6, 8, 12, 16, 24, 32, 64 };

    /// <summary>
    /// ★★ 宽度字段解码: 同时按 **int32** 与 **float** 解释同一 4 字节,
    /// 取落在 ECL 宽度值域 {6,8,12,16,24,32,64} 里的那一种。
    ///
    /// 为什么必须双解释 —— 初始化函数 0x10F56 用的是**整数寄存器**:
    ///     mov   edx, dword ptr [rdi + 0x30]
    ///     test  edx, edx            ← 对 float 不会这么判零
    ///     mov   dword ptr [rbx + 0x270], edx
    /// 虽然 MSVC 搬 float 也会用 edx, 但 `test edx,edx` 更像整数语义。
    /// 而工具此前**一律按 float 解释**, 若实际是 int32:
    ///     int 24 → 位模式 0x00000018 → float 3.4e-44 → 半宽≈0 → 画成一条线
    /// 反过来若实际是 float 而我们按 int 读, 24.0f 的位模式是 0x41C00000
    /// → int 1102053376, 一眼就不在值域, 不会误判。
    /// ⇒ 两种编码**互不冲突**, 双解释是安全的, 且能自动适配。
    ///
    /// 返回值: true = 解出了可信宽度; false = 两种解释都不在值域(构建不匹配等)。
    /// </summary>
    /// <param name="zeroOk">是否允许 0(P5 的 0 表示"等粗细", 是合法值; P8 不应为 0)。</param>
    internal static bool DecodeWidth(byte[] buf, int off, bool zeroOk,
                                     out float w, out uint raw, out string how)
    {
        raw = BitConverter.ToUInt32(buf, off);
        int iVal = unchecked((int)raw);
        float fVal = BitConverter.ToSingle(buf, off);

        // ① 整数解释优先(汇编用 edx 搬运 + test 判零)
        if (iVal == 0)
        {
            w = 0f; how = "int0";
            return zeroOk;                       // P5 的 0 = 合法; P8 的 0 = 不可信
        }
        foreach (int e in EclWidths)
            if (iVal == e) { w = e; how = "int"; return true; }

        // ② 浮点解释
        if (IsFinite(fVal))
            foreach (int e in EclWidths)
                if (MathF.Abs(fVal - e) < 0.01f) { w = fVal; how = "flt"; return true; }

        w = 0f; how = "??";
        return false;
    }

    private void ReadLasers(NcSnapshot s)
    {
        var mem = _mem;
        if (!mem.ReadChunked(mem.Base + Nc.RVA_LaserPool, _laserBuf, _laserBuf.Length, out int got)
            || got < Nc.LaserStride)
        {
            // ★ 同上: 保留上一帧, 不闪空
            Fail("lasers: " + mem.LastError);
            s.LaserInfo = "★读失败(沿用上一帧): " + mem.LastError;
            return;
        }

        int n = 0, active = 0;
        // ★ 兜底半宽: 只有内存宽度读不到时才用 (读不到时状态栏会标出来, 不会伪装成正确值)。
   //   正常情况下 wStart/wEnd 都被内存值覆盖, 这个字段只是让代码有定义。
        float hw = Nc.LaserHalfWidthDefault;
        var arr = s.Lasers;

        for (int i = 0; i < Nc.LaserSlots; i++)
        {
            int off = i * Nc.LaserStride;

            if (_laserBuf[off + Nc.L_Flush] == 0) continue;
            active++;

            float sx = BitConverter.ToSingle(_laserBuf, off + Nc.L_StartX);
            float sy = BitConverter.ToSingle(_laserBuf, off + Nc.L_StartY);
            float ang = BitConverter.ToSingle(_laserBuf, off + Nc.L_Angle);
            float cur = BitConverter.ToSingle(_laserBuf, off + Nc.L_CurLen);
            float total = BitConverter.ToSingle(_laserBuf, off + Nc.L_TotalLen);
            if (!IsFinite(sx) || !IsFinite(sy) || !IsFinite(ang) || !IsFinite(cur)) continue;

            // 生成/未激活时起点会被写 -999 (0xFFFFFC19), 挡掉
            if (sx < -200f || sx > 600f || sy < -300f || sy > 800f) continue;

            // 累积距离不应超过总长 (循环里 comiss 就是这么比的), 超了夹一下防手滑
            if (IsFinite(total) && total > 0f && cur > total) cur = total;
            if (cur < 0f) cur = 0f;

            // ★2026-10-05 20:30 实测: 48 个活跃槽的 +0x18 **全部为 0.0**, 但 +0x1C 稳定 112.0。
            //   所以"用 CurLen 当束长"会让 MainForm 的 `len <= 0.01f → continue` 把激光全跳过,
            //   表现就是"一条都画不出来"。几何也已验过: 从 (173.27,86.06) 沿 -2.196rad 走 112
            //   得到 (107.92,-4.90), 与另一槽实测 (107.70,-4.75) 吻合 ⇒ +0x1C 才是束长。
            //   这里在 CurLen 无效时回退到 TotalLen, 蓄力期(两值都有效)仍用 CurLen 画短段。
            if (cur <= 0.01f && total > 0.01f) cur = total;

            // ---- 宽度 ----
            //
            // ★★ 2026-10-06 第四次重写(加了形状预设)。
            //
            // ECL 侧语义已**定死**(见 Nc.cs 12.11, 与构建无关的可靠结论):
            //     P8 = ★起点宽(细端)
            //     P5 = ★终点宽(粗端)
            //     P5 == 0.0f ⇒ **不做锥形**, 两端都用 P8  ⇒ "等粗细"
            // 决定性依据: 154 条 ins_85/86 里 **(P5,P8) 从来没有一组相等**,
            // 且按关卡拆分后与用户观察逐一吻合:
            //     第一关 ecldata1P5=0     ⇒ 等粗细      ★用户: "第一关boss是等粗细的"
            //     第七关 ecldata7  P5=32   ⇒ 24→32 锥形  ★用户: "最后一关前面细后面粗很明显"
            //
            // ⇒ 形状**不止一种**, 工具必须能逐条不同才覆盖得了全部符卡。
            //   预设表把 ECL 出现的全部 8 种 (P5,P8) 组合做成可选项,
            //   不用等宽度字段定位完成就能用上正确的形状。
            //
            // ⚠★★2026-10-06 12:40 宽度字段**已由静态分析定案**（不再是猜测）。
            //
            //   来源: 激光条目初始化函数 0x10D7D / 0x10ECB（.pdata 精确定界）。
            //   其中 0x10F56..0x10F64 三条指令就是宽度落点：
            //       mov   edx, [rdi+0x30]
            //       test  edx, edx                ← ★ 零判断
            //       mov   [rbx+0x270], edx        ⇒ 条目+0x278 = P5（粗端 / 终点宽）
            //       mov   ecx, [rdi+0x34]
            //       mov   [rbx+0x268], ecx        ⇒ 条目+0x270 = P8（细端 / 起点宽）
            //       sete  cl                ← ZF 来自上面那个 test
            //       mov   byte [rbx+0x290], cl    ⇒ "无锥形"标志
            //   （rbx = 条目0 + 0x8，由 0x10D3A 的 lea 定死）
            //
            //   ⇒ 这段汇编**直接印证了 ECL 侧的语义定死**（12.11）：
            //      P5 == 0 ⇒ 不做锥形 ⇒ 两端都用 P8（等粗细）。
            //      代码里就是靠 P5 的零判断来分支的，不是靠推测。
            //
            //   ★ 所以判据不是"两个值都有效"，而是**只看 P5 是否为 0**：
            //       P5 == 0 → 等粗细，半宽 = P8/2
            //       P5 != 0 → 锥形，细端 P8/2 → 粗端 P5/2
            //   旧写法要求两端都 >0 才插值，遇到 P5=0 就退化成等宽/尖头，
            //   这正是"梯形永远出不来"的根因。
            float wStart, wEnd;
            bool wOk;

            // ★★ 2026-10-06 发布版: 只走"自动"一条路(读内存)。
            //   原来还有"手动/预设"两个分支, 现在已删 —— 宽度字段早就定案了,
            //   让用户手填半宽只能骗到自己(形状看着对, 判定是错的)。
            //
            //   ★★ 2026-10-06 13:10 用 DecodeWidth **双解释**(int32 / float)。
        //   此前一律按 float 解释, 结果"锥形也画成等宽"。两种失败模式:
        //     · 实际是 int32 → float 解释成 1e-44 级 → 半宽≈0 → 走兜底等宽
        //     · 实际是 float 但值域判据没兜住 → 同样退回兜底半宽
        //   双解释后两种编码都能自动命中, 不再靠猜。
        bool p8Ok = DecodeWidth(_laserBuf, off + Nc.L_WidthB, false,
                               out float p8, out uint raw8, out string how8);
        // P5 的 0 是**合法值**(test edx,edx 的语义: 0 ⇒ 不做锥形 ⇒ 等粗细)
        bool p5Ok = DecodeWidth(_laserBuf, off + Nc.L_WidthA, true,
                               out float p5, out uint raw5, out string how5);

        if (p8Ok)
        {
            wStart = p8 * 0.5f;
            // P5 == 0 ⇒ 等粗细（两端都用 P8）；否则用 P5
            wEnd = (p5Ok && p5 > 0f) ? p5 * 0.5f : wStart;
            wOk = true;
        }
        else
        {
            // 读不到（槽未激活 / 构建不匹配）⇒ 退回到一个保守的半宽,
            //   此时画出来的宽度**只能当参考**, 状态栏会标成"读不到"。
            wStart = wEnd = hw;
            wOk = false;
        }


            arr[n++] = new NcLaser
            {
                X = sx, Y = sy, Angle = ang,
                CurLen = cur, TotalLen = total, HalfWidth = hw,
                HalfWidthStart = wStart, HalfWidthEnd = wEnd,
                WidthFromMemory = wOk,
            };
        }

        s.LaserCount = n;
        s.LaserActive = active;
        int wMem = 0, wTaper = 0;
        for (int i = 0; i < n; i++)
        {
            if (!arr[i].WidthFromMemory) continue;
            wMem++;
            // ★ 两端宽度**不同**才算真梯形 —— 只有一端为 0 的属于"尖头", 也算梯形
            if (MathF.Abs(arr[i].HalfWidthEnd - arr[i].HalfWidthStart) > 0.01f) wTaper++;
        }
// ★ 正常情况下宽度 100% 来自内存, 所以状态栏只需要说清一件事:
        //   "读到没有, 读到的里面几条是锥形"。形状不对时靠这条定位。
     string srcTag;
        if (wMem > 0)
        {
            srcTag = $"内存 {wMem}/{n} · 锥形 {wTaper} 等宽 {wMem - wTaper}";
        }
        else srcTag = "★读不到(用兜底半宽, 别拿它当判定用)";
        s.LaserInfo = $"池 0x{Nc.RVA_LaserPool:X} · {n} 在用 (标志非0 {active})"
       + $" · 宽度:{srcTag}";
        // ★★ 宽度已定案(0x10F56..0x10F64 + rbx=条目0+0x8, 4/4 交叉吻合),
        //   所以「等粗细」现在是**正常结果**而非故障 —— 不再报警告。
    }


    /// <summary>把道具类型分布拼成 "0×3 1×5 2×1", 用来对着游戏画面反推每个类型号是什么道具。</summary>
    private string BuildTypeSummary()
    {
        if (_typeCount.Count == 0) return "";
        _typeSb.Clear();
        int shown = 0;
        foreach (var kv in _typeCount)
        {
            if (shown++ >= 6) { _typeSb.Append(" …"); break; }
            if (shown > 1) _typeSb.Append(' ');
            _typeSb.Append(kv.Key).Append('×').Append(kv.Value);
        }
        return _typeSb.ToString();
    }

    // ---------------- 小工具 ----------------

    private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

    private static bool IsPlausiblePtr(ulong p) => p >= 0x10000UL && p < 0x7FFF_FFFF_FFFFUL;
}
