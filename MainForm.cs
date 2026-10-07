using System.Diagnostics;

namespace TH06NCTools;

public sealed class MainForm : Form
{
    // ★ 窗口标题 = 用户第一眼看到的名字, 所以放"发布版"的名字 (2026-10-06 第7 条)。
    //   命名空间 / 类名 / 工程名仍是 TH06NCTools, 那是代码框架内部的事, 不动。
    private const string BaseTitle = "TH06NCSV build1006";

    private readonly NcReader _reader = new();
    private readonly RenderPanel _render;
    private readonly InfoPanel _info;
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly OptionsPanel _optionsPanel;
    private readonly CheatPanel _cheatPanel;
    private readonly ToolStripMenuItem _miOptions = new("选项(&O)");
    private readonly ToolStripMenuItem _miCheats = new("修改器(&C)");
    private readonly ToolStripMenuItem _miAbout = new("关于(&A)");

    /// <summary>★ 渲染固定 60Hz —— 帧率调节功能已删除 (2026-10-06 第 2 条), 理由见 RenderFrame。
    /// 为什么不是"越高越顺": th06nc 的逻辑是固定 60Hz 的整数帧计数器, 内存里的
    /// 位置/角度/HP 每1/60 秒才变一次。渲染快过60 只是拿同一份快照重复画,
    /// CPU 和 GDI+ 调用翻倍而画面一帧不多 —— 实测反而更卡。</summary>
    private const int RenderHz = 60;
    private long _lastInfoUpdate;
    private long _nextFrameTicks;
    private long _nextIdleFrame;

    public MainForm()
    {
        Text = BaseTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 520);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _info = new InfoPanel { Dock = DockStyle.Right, Width = 232, Padding = new Padding(8, 8, 8, 8) };
        _render = new RenderPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 13, 18) };

_optionsPanel = new OptionsPanel(_render, _reader, TopMost, v => TopMost = v);
  _cheatPanel = new CheatPanel(_reader);
   _optionsPanel.CloseAction = () => HidePanel(_optionsPanel);
        _cheatPanel.CloseAction = () => HidePanel(_cheatPanel);

        var menu = new MenuStrip();
        _miOptions.Click += (s, e) => TogglePanel(_optionsPanel);
  _miCheats.Click += (s, e) => TogglePanel(_cheatPanel);
        // AboutForm 自己没有静态包装, 直接 new + ShowDialog(this) —— 有owner 才会盖在主窗上方
        _miAbout.Click += (s, e) => new AboutForm().ShowDialog(this);
 menu.Items.Add(_miOptions);
     menu.Items.Add(_miCheats);
      menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miAbout);
        MainMenuStrip = menu;

        // 添加顺序 = 停靠顺序的反序: 后加的占更靠外的位置
        Controls.Add(_render);
        Controls.Add(_cheatPanel);
        Controls.Add(_optionsPanel);
        Controls.Add(_info);
        Controls.Add(menu);

        var status = new StatusStrip();
        status.Items.Add(_statusLabel);
        Controls.Add(status);

        Size = new Size(768 + 232 + 16, 896 + 24 + 40 + 80);

        // 未处理异常不再弹系统对话框: 只在底部状态栏留一行, 工具继续可用
        Application.ThreadException += OnThreadException;

        FormClosed += (s, e) => _reader.Dispose();
    }

    /// <summary>未处理异常只写进底部状态栏 (不弹框), 免得绘制里的错误被每帧重复弹出来。</summary>
    private void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        try
        {
            var ex = e.Exception;
            _statusLabel.Text = $"出错: {ex.GetType().Name} — {ex.Message}";
            _statusLabel.ForeColor = Color.OrangeRed;
        }
        catch { }
    }

/// <summary>
    /// 渲染一帧, 由 Program.cs 的 DoEvents 主循环每轮调用。
  /// 不用 WinForms Timer: 精度受系统时钟 ~15.6ms 限制, 实测上限 ~40fps;
    /// 也不用 async 自投递: 高帧率下会饿死消息泵, 窗口表现为"无响应"。
    ///
    /// ★★ 2026-10-06: 帧率调节(读取Hz / 渲染fps)整个删掉了, 这里固定 60Hz。
    /// 依据 (回答"调高到底有没有用"):
    ///   · **渲染 &gt; 60Hz 无意义。** th06nc 走的是固定 60Hz 的整数帧计数器 ——
    ///     位置/角度/HP/激光长度全都在那个 tick 里推进, 内存数据每 1/60 秒才变一次。
    ///     渲染 240Hz 拿到的还是同一份快照, 只是重复画 4遍: CPU 与 GDI+ 调用翻倍,
    ///     画面一帧不多, 观感上还更容易出现撕裂感。
    ///   · **读取 &gt; 60Hz 也不划算。** 快照平均滞后会从 8.3ms 降到 2.1ms, 但道具池
    ///     单次 1024×0x160 ≈ 426KB, 按 240Hz 读就是约 100MB/s 的 ReadProcessMemory,
    ///     而省下的那 6ms 滞后在60Hz 显示链上根本看不出来(渲染瓶颈)。
    ///   所以两边都锁 60 —— 数据源、显示端、瓶颈点三者是自洽的。
    /// </summary>
    internal void RenderFrame()
    {
   if (IsDisposed || Disposing) return;

// 固定 60Hz 限速: 睡到点→ 自旋补精度 → 累进下一帧时刻。
        // 用"累进"而不是"now+interval", 长时间运行不会累积漂移;
        // 一旦落后超过一帧就重新对齐(now+interval), 避免"追帧"式连续补画。
        long interval = Stopwatch.Frequency / RenderHz;
        {
            long now = Stopwatch.GetTimestamp();
            long wait = _nextFrameTicks - now;
 if (wait > 0)
  {
     int sleepMs = (int)(wait * 1000 / Stopwatch.Frequency);
         if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (Stopwatch.GetTimestamp() < _nextFrameTicks) Thread.SpinWait(32);
   }
     _nextFrameTicks += interval;
 if (_nextFrameTicks < now) _nextFrameTicks = now + interval;
        }

        // 没连上游戏时降到 10Hz: 全速重绘既没内容可画, 又让状态文字抖个不停
        if (!_reader.Attached)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _nextIdleFrame)
            {
                Thread.Sleep(15);
                return;
            }
            _nextIdleFrame = now + Stopwatch.Frequency / 10;
        }

        if (_render.IsHandleCreated)
        {
            _render.Snapshot = _reader.Latest;
            _render.Invalidate();
            _render.Update();   // 强制同步重绘, 不排队等消息循环
        }

        if (Environment.TickCount64 - _lastInfoUpdate >= 100)
        {
            _lastInfoUpdate = Environment.TickCount64;
            UpdateInfo();
        }
    }

    // ---------------- 面板展开 / 收起 ----------------

    private readonly HashSet<SidePanel> _grown = new();

    private void TogglePanel(SidePanel p)
    {
        if (p.Visible) HidePanel(p); else ShowPanel(p);
    }

    private void ShowPanel(SidePanel p)
    {
        if (p.Visible) return;
        if (ResizeForPanel(+SidePanel.PanelWidth)) _grown.Add(p);
        p.Visible = true;
        SyncMenuChecks();
    }

    private void HidePanel(SidePanel p)
    {
        if (!p.Visible) return;
        p.Visible = false;
        if (_grown.Remove(p)) ResizeForPanel(-SidePanel.PanelWidth);
        SyncMenuChecks();
    }

    /// <summary>展开/收起面板时同步调整主窗口宽度, 保证游戏区尺寸不变。</summary>
    private bool ResizeForPanel(int delta)
    {
        if (WindowState != FormWindowState.Normal) return false;
        int target = Width + delta;
        if (delta > 0 && target > Screen.FromControl(this).WorkingArea.Width) return false;
        int min = Math.Max(MinimumSize.Width, SidePanel.PanelWidth * 2 + 260);
        target = Math.Max(min, target);
        if (target == Width) return false;
        Width = target;
        return true;
    }

    private void SyncMenuChecks()
    {
        _miOptions.Checked = _optionsPanel.Visible;
        _miCheats.Checked = _cheatPanel.Visible;
    }

    private void UpdateInfo()
    {
        var snap = _reader.Latest;
        _statusLabel.Text = $"{_reader.AttachStatus} | 读取 {_reader.ActualHz:F0} Hz";
        _statusLabel.ForeColor = Color.FromKnownColor(KnownColor.ControlText);

        string wantTitle = _reader.Attached && snap.Valid && snap.InPlay
            ? $"{BaseTitle} — 残机 {snap.Lives} · 灵击 {snap.Bombs}"
            : BaseTitle;
        if (Text != wantTitle) Text = wantTitle;

        var ch = _reader.Cheats;
        _info.SetData(new InfoPanel.InfoData
        {
            Attached = _reader.Attached,
            Valid = snap.Valid,
            InPlay = snap.InPlay,
            Paused = snap.Paused,
            AttachStatus = _reader.AttachStatus ?? "",
            VersionWarning = _reader.VersionWarning ?? "",
            ShaShort = _reader.ShaShort ?? "",
            Hint = _reader.Hint ?? "",
            ExePath = _reader.ExePath ?? "",
            ShaFull = _reader.ShaFull ?? "",
            ProcessName = _reader.ProcessName ?? "",
            LastError = _reader.LastError ?? "",
            VersionWarnLevel = _reader.VersionWarnLevel,
            ImageSize = _reader.ImageSize,
            TimeDateStamp = _reader.TimeDateStamp,
            Difficulty = snap.Difficulty,
            Stage = snap.Stage,
            Score = snap.Score,
            Graze = snap.Graze,
            Point = snap.Point,
            Lives = snap.Lives,
            Bombs = snap.Bombs,
            Power = snap.Power,
            PlayerX = snap.PX,
            PlayerY = snap.PY,
            PlayerR = snap.PlayerR,
            PlayerState = snap.PlayerState,
            BoundL = snap.BoundL, BoundT = snap.BoundT, BoundR = snap.BoundR, BoundB = snap.BoundB,
            BulletActive = snap.BulletActiveState1,
            BulletTotal = snap.BulletCount,
            BulletSizeKnown = snap.BulletSizeKnown,
            ItemCount = snap.ItemCount,
            ItemTypeSummary = snap.ItemTypeSummary ?? "",
            ItemPoolInfo = snap.ItemPoolInfo ?? "",
            ItemSlotsUsed = snap.ItemSlotsUsed,
            ItemCoordOk = snap.ItemCoordOk,
            ItemStaticDropped = snap.ItemStaticDropped,
            ItemCoordLike = snap.ItemCoordLike,
            ItemAliveNonZero = snap.ItemAliveNonZero,
            BulletSizeSummary = snap.BulletSizeSummary ?? "",
            BulletSizeDiff = snap.BulletSizeDiffSummary ?? "",
            // 激光 —— 宽度来源走 LaserInfo 字符串, 计数单独带过来好让面板能上色
            LaserCount = snap.LaserCount,
            LaserActive = snap.LaserActive,
            LaserInfo = snap.LaserInfo ?? "",
            RenderFps = _render.Fps,
            PollHz = _reader.ActualHz,
            CanWrite = _reader.CanWrite,
            CheatOn = ch.InfiniteLives || ch.InfiniteBombs || ch.InfinitePower || ch.Invincible,
        });

        _cheatPanel.SetStatus(_reader.Attached, snap.Valid && snap.InPlay, _reader.CanWrite, snap);
    }
}

// ------------------------------------------------------------------

/// <summary>右侧信息面板。</summary>
internal sealed class InfoPanel : Panel
{
/// <summary>
    /// ★★ 注意: 这是**数据载体**, 不是"显示清单" (2026-10-07)。
  ///
 ///   字段比面板上实际画出来的行**多得多** —— 指纹 /映像大小 / PE 时间戳 /
    ///   exe 路径 / 弹尺寸种类 / 道具池地址 / 各道筛子的计数 / 活动范围 / 帧率
 ///   都在这里, 但 Draw() 里**不显示**它们。
    ///
    ///   为什么不清掉字段: 这些值仍被 <see cref="Key"/> 用来做"内容变了才重绘"
 ///   的签名。删字段就得连带改 Key, 而 Key 少一项就可能让面板该重绘时不重绘。
///   显示层少画几行只是少几个 string.Join —— 数据在、逻辑不动, 是最省的改法。
    /// </summary>
    public struct InfoData
    {
   public bool Attached, Valid, InPlay, Paused, CanWrite, CheatOn;
        public string AttachStatus, VersionWarning, ShaShort, ShaFull, Hint, ItemTypeSummary,
       ExePath, ProcessName, LastError, BulletSizeSummary, ItemPoolInfo,
 LaserInfo;
        public int VersionWarnLevel;
        public uint ImageSize, TimeDateStamp;
        public int Difficulty, Stage, Point, Lives, Bombs, Power, PlayerState;
        public long Score, Graze;
        public float PlayerX, PlayerY, PlayerR;
      public float BoundL, BoundT, BoundR, BoundB;
        public int BulletActive, BulletTotal, BulletSizeKnown, ItemCount;
        public string BulletSizeDiff;
        public int ItemSlotsUsed, ItemCoordOk, ItemStaticDropped, ItemCoordLike, ItemAliveNonZero;
        public int LaserCount, LaserActive;
        public double RenderFps, PollHz;
    }

    private InfoData _d;
    private readonly Font _font = new("Microsoft YaHei UI", 9F);
    private readonly Font _fontBig = new("Consolas", 10F);

    private string _lastKey = "\u0000";

/// <summary>
  /// 只在**内容真的变了**时才重绘。信息面板是 10Hz 刷的, 如果每次都 Invalidate,
    /// 未连接时面板会被反复擦写 —— 这就是"一闪一闪"的来源之一。
    /// </summary>
    public void SetData(InfoData d)
    {
        string key = Key(d);
        if (key == _lastKey) return;
        _lastKey = key;
        _d = d;
        Invalidate();
    }

    private static string Key(in InfoData d) => string.Join('|',
  d.Attached, d.Valid, d.InPlay, d.Paused, d.CanWrite, d.CheatOn,
  d.AttachStatus, d.VersionWarning, d.ShaShort, d.Hint, d.ItemTypeSummary, d.ExePath, d.LastError,
        d.BulletSizeSummary,
        d.BulletSizeDiff,
        d.VersionWarnLevel, d.ImageSize, d.TimeDateStamp, d.Difficulty, d.Stage, d.Score, d.Graze, d.Point,
        d.Lives, d.Bombs, d.Power, d.PlayerState,
        d.PlayerX.ToString("F1"), d.PlayerY.ToString("F1"), d.PlayerR.ToString("F2"),
        d.BoundL.ToString("F0"), d.BoundT.ToString("F0"), d.BoundR.ToString("F0"), d.BoundB.ToString("F0"),
        d.BulletActive, d.BulletTotal, d.BulletSizeKnown, d.ItemCount,
        d.ItemSlotsUsed, d.ItemCoordOk, d.ItemStaticDropped, d.ItemCoordLike,
        d.ItemAliveNonZero, d.ItemPoolInfo,
      d.LaserCount, d.LaserActive, d.LaserInfo,
        (int)d.RenderFps, (int)d.PollHz);

    public InfoPanel()
    {
        // struct 的默认构造把 string 字段留成 null, 而面板一显示就会先画一帧
        // (早于第一次 SetData) —— 不在这里初始化就是每帧一次 NullReferenceException
        _d = new InfoData
        {
            AttachStatus = "", VersionWarning = "", ShaShort = "", ShaFull = "",
    Hint = "", ItemTypeSummary = "", ExePath = "", ProcessName = "", LastError = "",
    BulletSizeSummary = "", BulletSizeDiff = "", ItemPoolInfo = "", LaserInfo = ""
        };

        BackColor = Color.FromArgb(20, 20, 27);
        DoubleBuffered = true;
        Resize += (s, e) => Invalidate();
   Paint += Draw;

        // ★ 原来这里有"双击面板复制版本诊断"(给维护地址表的人贴数据用的),
        //   属于开发功能, 已随诊断代码一起删 (2026-10-06 第 1 条)。
    }

private static readonly string[] DiffNames = { "Easy", "Normal", "Hard", "Lunatic", "Extra" };

    // ★ ShortPath() 已删 (2026-10-07) —— 它只为显示 exe 路径服务,
    //   而"文件"那一行已经随开发信息一起删掉了。C# 不报未使用的私有方法,
    //   所以留着它不会变红, 但那就是死代码: 留着只会让人以为路径还在显示。
    //

    /// <summary>
    /// ★★ 发布版信息面板 (2026-10-07): 只留**普通用户真的会看、且看得懂**的行。
    ///
    /// 删除的判据只有一条: **看到这一行, 用户能做出什么决定?**
    ///   能⇒ 留 (比如"敌弹 240 个"直接决定"现在能不能躲")
    ///   不能 ⇒ 删 (指纹/映像大小/PE 时间戳/池地址/筛子计数……
    ///   这些是给维护偏移表的人看的, 用户拿着 sha256 片段也干不了任何事)
    ///
    /// ★★ 顺带修掉一个**比"显示开发信息"严重得多**的问题:
    ///   原先有两条故障提示教用户去点选项里的按钮 ——
    ///     "去选项把『占用判据』切成『只看坐标』"
    ///     "用选项里的『转储激光槽』看原始字段, 重点看 +0x27C"
    ///   而这两个按钮 12.31 已经删掉了。照着提示做的用户只会找不到,
    ///   越排查越困惑。现在改成只描述现象 + 给可执行的下一步。
    /// </summary>
    private void Draw(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        float y = 12;
        int wrapW = Math.Max(40, ClientSize.Width - 16);

        void Line(string label, string value, Brush? vb = null)
        {
            g.DrawString(label, _font, Brushes.Gray, 8, y);
            var size = g.MeasureString(value, _fontBig, wrapW);
            g.DrawString(value, _fontBig, vb ?? Brushes.WhiteSmoke,
                        new RectangleF(8, y + 16, wrapW, size.Height + 4));
            y += 16 + Math.Max(24, size.Height + 2);
        }
        void Head(string t)
        {
            g.DrawString(t, _font, Brushes.DimGray, 8, y);
            y += 20;
        }
        void Note(string t, Brush b)
        {
            var size = g.MeasureString(t, _font, wrapW);
            g.DrawString(t, _font, b, new RectangleF(8, y, wrapW, size.Height + 4));
            y += size.Height + 8;
        }

string warn = _d.VersionWarning ?? "";
        string hint = _d.Hint ?? "";
        string err = _d.LastError ?? "";
        // ★ 原本这里还有 4 个局部变量: sha (指纹) / types (道具类型分布) /
        //   sizes (弹尺寸种类) / pool (道具池地址与筛子计数) ——
        //   全是随着对应显示行一起删掉的, 变量留着就是"取了值不用"的死代码。
      //   数据本身仍在 InfoData 里(见下方注释), 只是不再显示。
      //
        //   为什么保留 InfoData 的字段而只删显示: 那些字段仍被 Key() 用来做
        //   "内容变了才重绘"的签名, 删字段会连带改坏重绘逻辑。而显示层
        //   少画几行 = 少几个 string.Join, 没有任何副作用。

        Head("── 连接 ──");
        Line("进程", _d.Attached ? "已连接" : "未连接",
            _d.Attached ? Brushes.LimeGreen : Brushes.Orange);

        // ★ 版本不符**必须留** —— 这不是开发信息, 是"你看到的画面可能是错的"这类
        //   关键警告。原文案被简化过(原文会印 sha256 与映像大小), 但保留"有警告"这件事。
        if (warn.Length > 0)
            Note("⚠ " + warn, _d.VersionWarnLevel >= 2 ? Brushes.Red : Brushes.Orange);
        if (hint.Length > 0) Note(hint, Brushes.Orange);
        if (!_d.Attached)
        {
            Note("等待 th06nc.exe 启动…", Brushes.Orange);
            return;
        }
        if (!_d.Valid)
        {
            Note("已连接, 等待数据…", Brushes.Gray);
            if (err.Length > 0) Note("读取失败: " + err, Brushes.OrangeRed);
            return;
        }
        if (!_d.InPlay)
        {
            Note(_d.Paused ? "游戏已暂停" : "未在战斗中 —— 实体显示停止", Brushes.Orange);
        }

        Head("── 对局 ──");
        Line("难度", _d.Difficulty is >= 0 and <= 4 ? DiffNames[_d.Difficulty] : "?");
        Line("面", _d.Stage.ToString());
        Line("得分", _d.Score.ToString("N0"));
        Line("擦弹", _d.Graze.ToString("N0"));
        Line("点", _d.Point.ToString("N0"));

Head("── 自机 ──");
        Line("残机 / 符卡", $"{_d.Lives} / {_d.Bombs}",
            _d.Lives > 0 ? Brushes.WhiteSmoke : Brushes.OrangeRed);
        Line("火力", _d.Power.ToString());
        // ★ 原本这里还有: 坐标 (X, Y) / 判定半径 / 状态字段 三个原始字段。
        //   都是给维护偏移表的人看的 —— 用户看到自己的坐标数字并不能做什么决定,
        //   而判定半径该由画面上那个红点直接体现, 不该用数字重复一遍。已删。

Head("── 实体 ──");
        Line("敌弹 (致死)", _d.BulletActive.ToString(),
     _d.BulletActive > 0 ? Brushes.OrangeRed : Brushes.WhiteSmoke);

        // ★ 删掉的行: "尺寸已读到 N/M (x%)" / "尺寸种类" / "贴图≠判定"提示 /
   //   "敌弹 (全部槽)" —— 这四行都是排查"弹尺寸有没有读到"用的。
        //   用户看到"尺寸已读到 87%"并不能决定任何事, 只会困惑"那 13% 是什么意思"。
        //   真要确认弹尺寸对不对, 看画面: 灰色空心弹 = 没读到, 别照着躲。够用了。

  Line("道具", _d.ItemCount.ToString(),
        _d.ItemCount > 0 ? Brushes.LimeGreen : Brushes.Gray);

        // ★ 删掉的行: "池内标志非零" / "道具槽N 用 / N像 / N剔" / "道具类型" /
    //   "道具池"(含 RVA 地址与逐级筛子计数) —— 全部是排查"道具为什么没显示"的
        //   中间量, 每一行都要读者懂内部的槽位模型才有意义。
        //
        //   下面这条提示是**降级版**: 原文三条分支都在教用户去点选项里的按钮
        //   ("占用判据切只看坐标" / "转储道具槽看原始 float"), 而那两个按钮
        //   12.31 已经删了 —— 照着做的用户只会找不到。现在只描述现象 + 给真能做的事。
  if (_d.ItemCount == 0)
        {
       if (_d.ItemAliveNonZero == 0)
           Note("当前没读到道具。 " + "", Brushes.Orange);
       else
    Note("道具在游戏里存在, 但出现问题没能显示"
        + "", Brushes.Orange);
        }

  // ---- 激光 ----
    Line("激光", _d.LaserCount.ToString(),
            _d.LaserCount > 0 ? Brushes.Magenta : Brushes.Gray);

        // ★ 删掉的行: "激光 N 条 (池标志非0 M)" 的括号部分 + "激光池"(含 RVA 地址与
        //   "宽度:内存 N/M · 锥形 X 等宽 Y")。
        //   宽度来源那串是判断梯形宽度有没有生效用的硬信号, 但它只有对照 ECL 才读得懂。
        //
        //   这条提示也是**降级版**: 原文让用户"用选项里的『转储激光槽』看 +0x27C",
        //   那个按钮同样已经删了。
        if (_d.LaserCount == 0)
            Note("没有激光。 "
                + "连接状态有没有警告", Brushes.Orange);

// ★ 删掉的"活动范围"一行(场地边界坐标 236,16 → 620,464): 那是内部坐标系常量,
        //   用户看到四个数字也判断不出任何事。场地边界在画面上有画, 不需要数字复述。

        Head("── 工具 ──");
        Line("修改器", _d.CheatOn ? (_d.CanWrite ? "生效中" : "已开但无写权限") : "未启用",
          _d.CheatOn ? (_d.CanWrite ? Brushes.LimeGreen : Brushes.OrangeRed) : Brushes.Gray);
        // ★ 删掉的"渲染帧率 / 读取频率"两行: 12.31 之后两者都**写死60**,
        //   数字永远不会变 —— 显示一个恒定值没有任何信息量。

   //Head("── 未收录 ──");
        // ★ 这段保留: 它说明的是**用户能不能依赖这个工具**的边界, 不是开发信息。
        //   但把"池地址与条目布局仍缺"这种内部说法去掉了 —— 用户不需要知道缺哪层布局。
        Note("\n" +
      "",
          Brushes.DimGray);
    }
}

/// <summary>游戏区: 把 384x448 局部坐标放大, 只画判定形状。</summary>
internal sealed class RenderPanel : Panel
{
    public NcSnapshot? Snapshot { get; set; }

    public bool ShowBullets = true;
    public bool ShowGraze;
    public bool ShowItems = true;
    public bool ShowEnemies = true;
    public bool ShowLasers = true;
    // ★ 下面三个是"关掉就会画错/丢信息"的常开项, 已写死成 const (2026-10-06 第 5 条)。
    // 判据同NcReader: 用户没有第二个"正确"选择, 给开关等于给陷阱。

    /// <summary>激光按"前细后粗"画成梯形。宽度已由反汇编定位到一对相邻 float
    /// (条目+0x278 / +0x270, ECL 一次搬两个), 起点宽 ≠ 终点宽。
 /// 关掉 = 两端都画成同一宽度, 两端的判定范围会同时判错。</summary>
    public const bool TaperLasers = true;

    /// <summary>道具按类型着色。类型号已由反汇编确认 (贴图 id = 533 + 类型号)。
    /// 关掉 = 全部单色, "这是大P还是小P、是得分还是火力"这个最关键的信息全丢。</summary>
    public const bool ColorItemsByType = true;

    /// <summary>敌弹致死圆要把自机判定半径也算进去。这是游戏自己的判定:
    /// 圆心进 ⇒ 死 (0x6C122/0x6C139/0x6C145)。关掉 = 画出来的圈比真实致死圈小一圈。</summary>
    public const bool IncludePlayerRadius = true;

    public float ItemOuterPx = 5f;      // 道具标记半径(px)

    public double Fps;
    private int _frames;
    private long _fpsWindow;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    private static readonly SolidBrush BrushBulletDeadly = new(Color.FromArgb(255, 77, 109));
    private static readonly Pen PenBulletSpawning = new(Color.FromArgb(255, 166, 48), 1.4F);
    private static readonly Pen PenBulletFade = new(Color.FromArgb(120, 110, 120), 1.2F);
    private static readonly Pen PenBulletUnknown = new(Color.FromArgb(170, 170, 185), 1.3F);
    private static readonly Pen PenGraze = new(Color.FromArgb(90, 80, 250, 123));
    private static readonly SolidBrush BrushPlayerCore = new(Color.FromArgb(255, 42, 42));
    private static readonly SolidBrush BrushItem = new(Color.FromArgb(90, 220, 220));

    // 敌人 / 激光 —— 位置与几何全部来自反汇编 (Nc.cs 表 12/13)
    private static readonly Pen PenEnemyBox = new(Color.FromArgb(255, 214, 64), 1.6F);
    private static readonly Pen PenEnemyCore = new(Color.FromArgb(255, 120, 40), 1.2F);
    private static readonly SolidBrush BrushEnemyLabel = new(Color.FromArgb(255, 200, 90));
    private static readonly Pen PenLaserBody = new(Color.FromArgb(255, 110, 200), 2.2F);
    private static readonly Pen PenLaserEdge = new(Color.FromArgb(140, 255, 140), 1F);
    /// <summary>激光蓄力目标 (+0x1C 总长) 的虚线延伸, 只画"还没伸到"的那一段。</summary>
    private static readonly Pen PenLaserGhost =
        new(Color.FromArgb(90, 255, 140), 1F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
    /// <summary>梯形激光的半透明填充 —— 让"前细后粗"一眼可见, 而不只是两条平行线。</summary>
    private static readonly SolidBrush BrushLaserBody = new(Color.FromArgb(55, 255, 120, 200));

    // 道具配色 —— 按**类型语义**固定, 不是"按类型号循环取色"。
    // 依据: 反汇编确认类型号直接写进 +0x34 (生成函数 0x4408D `mov byte ptr [rbx+0x34], r8b`),
    //       且贴图 id = 533 + 类型号 (0x441BF), 即类型号就是道具种类本身。
    //
    // ★2026-10-05 按用户要求**红蓝互换**: 现在是 得点=红 / 小P=蓝 / 大P=绿。
    //   (最初版本是 得点=蓝 / P=红, 与用户习惯相反, 已互换)
    // ★2026-10-05 19:55 大 P 拆出来单独一色。
    //   上一轮把类型 1/2 合并成同一支蓝刷子, 导致"大P 还是蓝色"——
    //   大 P 和小 P 在游戏里是两个独立道具(类型号 1 / 2), 必须能一眼区分,
    //   否则满屏掉道具时根本认不出哪个是值钱的大 P。
    private static readonly SolidBrush BrushItemPoint = new(Color.FromArgb(255, 72, 72));   // 得点: 红
    private static readonly SolidBrush BrushItemPower = new(Color.FromArgb(80, 150, 255));  // 小 P: 蓝
    private static readonly SolidBrush BrushItemBigPower = new(Color.FromArgb(90, 225, 120));// 大 P: 绿
    private static readonly SolidBrush BrushItemDeath = new(Color.FromArgb(200, 200, 210));// 死亡掉落: 灰白
    private static readonly SolidBrush BrushItemBomb = new(Color.FromArgb(255, 210, 90));   // 弹幕消除: 金
    private static readonly SolidBrush BrushItemLife = new(Color.FromArgb(190, 120, 255));  // 1UP/命: 紫

    /// <summary>按类型号取画笔。类型语义来自反汇编 + ECL 全量统计 (见 Nc.cs 表 8.1)。
    /// 七个取值全部有 ECL 实证: 0=得分(434处) 1=小P(718) 2=大P(126, 全在第4关高血量精英)
    /// -1/-2=不掉落(920/14) 4=死亡掉落 5=命(1) 6=弹幕消除。
    /// **小P(1) 蓝、大P(2) 绿必须分开** —— 满屏道具时要能一眼挑出大 P。</summary>
    private static SolidBrush ItemBrushOf(int type) => type switch
    {
        Nc.ItemTypePoint => BrushItemPoint,     // 0 得分   → 红
        Nc.ItemTypePower => BrushItemPower,     // 1 小 P   → 蓝
        Nc.ItemTypeBigPower => BrushItemBigPower,// 2 大 P   → 绿 ★独立色
        Nc.ItemTypeDeath => BrushItemDeath,     // 4 死亡掉落 → 灰白
        Nc.ItemTypeBomb => BrushItemBomb,       // 6 弹幕消除 → 金
        Nc.ItemTypeLife => BrushItemLife,       // 5 命      → 紫
        _ => BrushItem,                         // 其余(含负数不该出现的) → 青
    };
    private static readonly SolidBrush BrushFieldBg = new(Color.FromArgb(20, 20, 28));

    private readonly Font _font = new("Microsoft YaHei UI", 9F);

    public RenderPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float scale = Math.Min(ClientSize.Width / Nc.FieldW, ClientSize.Height / Nc.FieldH);
        float ox = (ClientSize.Width - Nc.FieldW * scale) / 2;
        float oy = (ClientSize.Height - Nc.FieldH * scale) / 2;
        float fw = Nc.FieldW * scale, fh = Nc.FieldH * scale;
        float GX(float x) => ox + x * scale;
        float GY(float y) => oy + y * scale;

        g.FillRectangle(BrushFieldBg, ox, oy, fw, fh);
        g.SetClip(new RectangleF(ox, oy, fw, fh));

        _frames++;
        if (_sw.ElapsedMilliseconds - _fpsWindow >= 1000)
        {
            Fps = _frames * 1000.0 / (_sw.ElapsedMilliseconds - _fpsWindow);
            _frames = 0;
            _fpsWindow = _sw.ElapsedMilliseconds;
        }

        var s = Snapshot;
        if (s is not { Valid: true })
        {
            g.ResetClip();
            g.DrawString("等待游戏进程与数据…", _font, Brushes.Gray, ox + 8, oy + 8);
            DrawFieldBorder(g, ox, oy, fw, fh);
            return;
        }

        if (!s.InPlay)
        {
            g.ResetClip();
            g.DrawString(s.Paused ? "已暂停" : "未在战斗中 · 实体显示停止", _font, Brushes.Gray, ox + 8, oy + 8);
            DrawFieldBorder(g, ox, oy, fw, fh);
            return;
        }

        // 判定尺寸全部来自反汇编 (表 11):
        //   弹盒 = [x ∓ w*0.5, y ∓ h*0.5]                    ← 0x6C09E/0x6C0D9 乘的 0.5
        //   致死 = 自机点裁剪进弹盒后 dist <= 自机半径        ← 0x6C166/0x6C1A4
        //   擦弹 = 弹盒四边各扩 48.0, 再与旋转的自机盒判交    ← 0x6C2B0 阶段2 (0x6C4EF~0x6C4FC)
        // 贴图真值 18/28/32/36/68/72/142/144 (thtk thanm -l 实读), 描述符读到的就是全尺寸。
        const float factor = Nc.FactorHalf;
        float pr = IncludePlayerRadius ? s.PlayerR : 0f;

        // ---- 敌弹 ----
        if (ShowBullets)
        {
            for (int i = 0; i < s.BulletCount; i++)
            {
                ref readonly var b = ref s.Bullets[i];
                // ★ 判定盒是 w×h 的**矩形**(反汇编: [x∓w*0.5, y∓h*0.5]),
                //   宽高分开用 —— 旧写法取 min() 画正圆, 遇到非正方形弹盒会在长轴上画小。
                float brx = b.SizeKnown ? b.W * factor : Nc.FallbackRadius;
                float bry = b.SizeKnown ? b.H * factor : Nc.FallbackRadius;
                float br = MathF.Min(brx, bry);
                float cx = GX(b.X), cy = GY(b.Y);

                if (b.State == 1)
                {
                    // 会打死人的: 实心红圆。开了"含自机半径"后 = 自机中心点进去就死
                    float rr = (br + pr) * scale;
                    if (b.SizeKnown)
                    {
                        float rx = (brx + pr) * scale, ry = (bry + pr) * scale;
                        g.FillEllipse(BrushBulletDeadly, cx - rx, cy - ry, rx * 2, ry * 2);
                    }
                    else
                        g.DrawEllipse(PenBulletUnknown, cx - rr, cy - rr, rr * 2, rr * 2);

                    if (ShowGraze)
                    {
                        // 擦弹范围 (反汇编 0x6C2B0 阶段2):
                        //   真实逻辑 = 弹盒四边各向外扩 48.0, 再与**旋转的自机盒**裁剪后判 dist² <= 阈值²。
                        //   站在"这颗弹"的视角, 就是弹盒四周 48.0 宽的擦弹带, 所以画**矩形**。
                        //
                        // ⚠ 近似说明: 严格说还要并上自机盒自身的半宽/半高 (自机盒在 Player+0x7754,
                        //   是 16 段 ×0x10 的旋转盒表)。自机盒未接入本工具, 这里只画 48.0 那圈 ——
                        //   所以实际擦弹范围比画出来的**略大** (大出约自机盒半宽)。要看严格值需再读 0x7754。
                        //
                        // 旧版画的是圆、且扩边量写的是 20.0, 两个都错 (圆→矩形, 20→48)。
                        float hw = b.SizeKnown ? b.W * factor : Nc.FallbackRadius;
                        float hh = b.SizeKnown ? b.H * factor : Nc.FallbackRadius;
                        float x0 = cx - (hw + Nc.GrazeExtra) * scale;
                        float y0 = cy - (hh + Nc.GrazeExtra) * scale;
                        float x1 = cx + (hw + Nc.GrazeExtra) * scale;
                        float y1 = cy + (hh + Nc.GrazeExtra) * scale;
                        g.DrawRectangle(PenGraze, x0, y0, x1 - x0, y1 - y0);
                    }
                }
                else if (b.State is >= 2 and <= 4)
                {
                    float rr = br * scale;
                    g.DrawEllipse(PenBulletSpawning, cx - rr, cy - rr, rr * 2, rr * 2);
                }
                else
                {
                    float rr = br * scale;
                    g.DrawEllipse(PenBulletFade, cx - rr, cy - rr, rr * 2, rr * 2);
                }
            }
        }

        // ---- 道具 ----
        // 必须画在**敌弹之后**: 密集弹幕下实心红圆会把青色小点完全盖住,
        // 这正是之前"道具读到了但看不见"的原因之一。
        if (ShowItems && s.ItemCount > 0)
        {
            for (int i = 0; i < s.ItemCount; i++)
            {
                ref readonly var it = ref s.Items[i];
                float cx = GX(it.X), cy = GY(it.Y);

                var br = ColorItemsByType ? ItemBrushOf(it.Type) : BrushItem;

                // ★ 2026-10-06 16:10 道具改成**贴图大小的正方形**。
                //   尺寸来源: [条目+0x138] 描述符 → +0x1C/+0x20 (见 Nc.I_Desc), 实测恒 16×16。
                //   此前画的是固定半径 ItemOuterPx 的圆 —— 跟游戏里的实际大小完全没关系。
                //   读不到描述符时退回 ItemOuterPx(当半宽用), 保证不消失。
                float hs = (it.SizeKnown ? it.Size * factor : ItemOuterPx) * scale;
                g.FillRectangle(br, cx - hs, cy - hs, hs * 2, hs * 2);
                // 描边: 密集时也能分辨出彼此
                using var pen = new Pen(Color.FromArgb(200, 20, 20, 20), 1F);
                g.DrawRectangle(pen, cx - hs, cy - hs, hs * 2, hs * 2);
            }
        }

        // ---- 激光 ----
        // 几何: 起点沿 (cos,sin) 方向延伸 **CurLen**(无效时 NcReader 已回退成 TotalLen)。
        // ★ 这批激光实测是"环形放射"阵: 48 条 = 6 圈 × 8 向, 起点分布在以 (192,112) 为心的
        //   若干同心圆上 (r=32 / 144 / ...), 方向角步进正好 π/4 ⇒ 典型的多层环形激光符卡。
        // 激光画在敌人之下、弹幕之上 —— 它比弹大, 但比自机判定重要。
        if (ShowLasers && s.LaserCount > 0)
        {
            // 复用同一个多边形缓冲 —— 激光可有 48 条, 每帧 new 4 个 PointF 会制造大量 GC 压力
            var taperPts = new PointF[4];
            for (int i = 0; i < s.LaserCount; i++)
            {
                ref readonly var lz = ref s.Lasers[i];
                float dx = (float)Math.Cos(lz.Angle);
                float dy = (float)Math.Sin(lz.Angle);
                float len = lz.CurLen;
                if (len <= 0.01f) continue;

                float x0 = GX(lz.X), y0 = GY(lz.Y);
                float x1 = GX(lz.X + dx * len), y1 = GY(lz.Y + dy * len);

      // ---- 半宽: 梯形(沿线插值), 允许某一端为 0(尖头) ----
      // 2026-10-05 22:00 反汇编确认宽度是**一对**相邻 float(条目+0x278 / +0x270),
      // 对应 ECL 的 ins_85 一次搬两个 float ⇒ 起点宽 ≠ 终点宽。
      // 用户观察"最后一关敌人的激光前细后粗, 且粗细分属不同判定范围"
                // ⇒ 判定范围本身就是梯形, 画等宽矩形会**两头都判错**。
      //
      // ★★ 2026-10-06 修正: 旧的 MathF.Max(..., 0.5f) 会把 NcReader 故意设成的
      //   "零宽端"(尖头)抬到 0.5, 于是 |hwE-hwS| 可能又 < 0.01 → 走等宽分支
      //   → 画出来仍然是一条线(用户实测现象)。零宽端必须**原样保留**。
                float hwS = lz.WidthFromMemory ? lz.HalfWidthStart : lz.HalfWidth;
        float hwE = lz.WidthFromMemory ? lz.HalfWidthEnd : lz.HalfWidth;
          // 兜底值: 0.5 游戏单位在 scale 下不足 1 像素, 看着仍是线, 所以给 1.5
        const float HWFallback = 1.5f;
        if (lz.WidthFromMemory)
        {
    hwS = MathF.Max(hwS, 0f);
        hwE = MathF.Max(hwE, 0f);
            // 两端都 0 ⇒ 零面积多边形, FillPolygon 不出图 -> 给个可见值
                if (hwS < 0.01f && hwE < 0.01f) { hwS = hwE = HWFallback; }
        }
else { hwS = hwE = MathF.Max(hwS, HWFallback); }

                // 法线(垂直于束方向), 两端各用自己的半宽
float nsx = -dy, nsy = dx;
        float s0x = x0 + nsx * hwS * scale, s0y = y0 + nsy * hwS * scale;
        float s1x = x0 - nsx * hwS * scale, s1y = y0 - nsy * hwS * scale;
        float e0x = x1 + nsx * hwE * scale, e0y = y1 + nsy * hwE * scale;
        float e1x = x1 - nsx * hwE * scale, e1y = y1 - nsy * hwE * scale;

            if (TaperLasers && MathF.Abs(hwE - hwS) > 0.01f)
    {
   // 梯形: 填充半透明 + 描四条边, 粗细变化一眼可见
        taperPts[0] = new PointF(s0x, s0y);
        taperPts[1] = new PointF(e0x, e0y);
    taperPts[2] = new PointF(e1x, e1y);
                    taperPts[3] = new PointF(s1x, s1y);
g.FillPolygon(BrushLaserBody, taperPts);
g.DrawLine(PenLaserEdge, s0x, s0y, e0x, e0y);
g.DrawLine(PenLaserEdge, s1x, s1y, e1x, e1y);
              // 端面横线: 标出"此处判定就这么多宽"(零宽端退化成点, 跳过)
            if (hwE >= 0.01f) g.DrawLine(PenLaserEdge, e0x, e0y, e1x, e1y);
        if (hwS >= 0.01f) g.DrawLine(PenLaserEdge, s0x, s0y, s1x, s1y);
        }
else
    {
            // 等宽: 两条边 + 中线
        g.DrawLine(PenLaserEdge, s0x, s0y, e0x, e0y);
g.DrawLine(PenLaserEdge, s1x, s1y, e1x, e1y);
                }
                g.DrawLine(PenLaserBody, x0, y0, x1, y1);

                // 蓄力中的激光: 用点线把"最终会伸到哪"画出来 (+0x1C 总长)。
                // 实测 +0x18 恒 0, 所以这条虚线正常情况下不出现;
                // 万一某个符卡让 +0x18 生效(短于总长), 就能立刻看出蓄力段。
                if (float.IsFinite(lz.TotalLen) && lz.TotalLen > len + 1f)
                {
                    g.DrawLine(PenLaserGhost, x1, y1,
                        GX(lz.X + dx * lz.TotalLen), GY(lz.Y + dy * lz.TotalLen));
                }

                // 起点小十字, 方便对准发射口
                g.DrawLine(PenLaserEdge, x0 - 3, y0, x0 + 3, y0);
                g.DrawLine(PenLaserEdge, x0, y0 - 3, x0, y0 + 3);
            }
        }

        // ---- 敌人 ----
        // 判定盒来自滑块值 (原版 hitboxDimensions 默认 12.0), 位置是反汇编确认的 +0xB0/+0xB4。
        if (ShowEnemies && s.EnemyCount > 0)
        {
            for (int i = 0; i < s.EnemyCount; i++)
            {
                ref readonly var en = ref s.Enemies[i];
                float cx = GX(en.X), cy = GY(en.Y);
                float half = MathF.Max(en.Hitbox, 1f) * 0.5f * scale;

                g.DrawRectangle(PenEnemyBox, cx - half, cy - half, half * 2, half * 2);
                g.DrawLine(PenEnemyCore, cx - 2, cy, cx + 2, cy);
                g.DrawLine(PenEnemyCore, cx, cy - 2, cx, cy + 2);

                if (en.Life > 1)
                    g.DrawString(en.Life.ToString(), _font, BrushEnemyLabel, cx + half + 2, cy - half);
            }
        }

// ---- 自机 ----
        // ★★ 2026-10-06 第 3 条: **只画判定点本身**, 一比一, 不加白边、不加像素下限。
        //   半径真值 =内存 Player+0x774C, 实测 1.25 (反汇编 6C166 `movss xmm2,[rbx+0x774C]`)。
        //   原来这里是 `Math.Max(真实半径, MinPlayerPx=3)` 撑一个近白色实心圆盘 ——
        //   那个 3px 兜底让点看起来比真实判定大一圈, 躲的时候会按错的信息躲。
        //   现在只保留 1px 下限: 它只在读数无效(本来就没数据)时才生效, 不影响正常情况。
        float px = GX(s.PX), py = GY(s.PY);
        float rp = MathF.Max(s.PlayerR * scale, 1f);
        g.FillEllipse(BrushPlayerCore, px - rp, py - rp, rp * 2, rp * 2);

        // 瞬移提示圈 —— 也是按真实半径画, 不跟任何显示设置走
        if (s.PlayerState == 3)
     {
            using var pen = new Pen(Color.FromArgb(120, 250, 123), 1.2F)
        { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawEllipse(pen, px - rp, py - rp, rp * 2, rp * 2);
        }

        g.ResetClip();
        DrawFieldBorder(g, ox, oy, fw, fh);
    }

    private static void DrawFieldBorder(Graphics g, float ox, float oy, float fw, float fh)
    {
        using var penOuter = new Pen(Color.FromArgb(64, 64, 84), 4F);
        g.DrawRectangle(penOuter, ox - 3, oy - 3, fw + 6, fh + 6);
        using var penInner = new Pen(Color.FromArgb(188, 188, 208), 1.6F);
        g.DrawRectangle(penInner, ox, oy, fw, fh);
    }
}
