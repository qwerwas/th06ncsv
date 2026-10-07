namespace TH06NCTools;

/// <summary>
/// 左侧内嵌面板的公共骨架 (「选项」与「修改器」共用)。
///
/// 为什么是内嵌面板而不是独立窗口: 独立顶层窗口会在桌面上多出一个能任意拖动的小窗,
/// 和主窗口、游戏窗口叠在一起很乱, 还容易被别的窗口盖住。Dock=Left 内嵌之后,
/// 改一项看一项, 完全不占桌面。展开/收起由菜单控制。
///
/// 布局: 自上而下逐项摆放 (Y 累加)。项数少、间距固定, 手写坐标比 TableLayoutPanel 直观。
/// </summary>
internal abstract class SidePanel : Panel
{
    public const int PanelWidth = 232;

    private const int PadX = 16;
    protected const int CtrlW = 200;

    protected static readonly Font TitleFont = new("Microsoft YaHei UI", 12F, FontStyle.Bold);
    protected static readonly Font HeadFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
    protected static readonly Font TextFont = new("Microsoft YaHei UI", 9.5F);
    protected static readonly Font TipFont = new("Microsoft YaHei UI", 8.5F);
    protected static readonly Font ValueFont = new("Consolas", 10.5F, FontStyle.Bold);

    internal static readonly Color Bg = Color.FromArgb(20, 20, 27);
    internal static readonly Color CtrlBg = Color.FromArgb(34, 34, 44);
    internal static readonly Color CtrlHoverBg = Color.FromArgb(52, 52, 68);
    internal static readonly Color CtrlPressedBg = Color.FromArgb(66, 66, 86);
    internal static readonly Color CtrlBorder = Color.FromArgb(72, 72, 92);
    internal static readonly Color Fg = Color.FromArgb(232, 232, 240);
    internal static readonly Color DimFg = Color.FromArgb(120, 120, 140);
    internal static readonly Color TipFg = Color.FromArgb(150, 150, 168);
    internal static readonly Color WarnFg = Color.FromArgb(255, 170, 60);
    internal static readonly Color DangerFg = Color.FromArgb(255, 120, 120);
    internal static readonly Color OkFg = Color.FromArgb(130, 220, 130);

    protected int Y = 14;

    protected SidePanel()
    {
        BackColor = Bg;
        DoubleBuffered = true;
        AutoScroll = true;
        Dock = DockStyle.Left;
        Width = PanelWidth;
        Visible = false;
    }

    /// <summary>✕ 按钮的落地动作, 由 MainForm 在面板构造完成后赋值 (收起面板)。</summary>
    public Action? CloseAction { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(CtrlBorder);
        e.Graphics.DrawLine(pen, ClientSize.Width - 1, 0, ClientSize.Width - 1, ClientSize.Height);
    }

    protected void OnCloseRequested() => CloseAction?.Invoke();

    // ---------------- 布局辅助 ----------------

    protected void AddTitle(string text)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(PadX, Y),
            AutoSize = true,
            ForeColor = Fg,
            BackColor = Bg,
            Font = TitleFont,
        });

        var close = new FlatButton
        {
            Text = "✕",
            Location = new Point(PanelWidth - 48, Y - 2),
            Size = new Size(30, 26),
            BackColor = CtrlBg,
            ForeColor = Fg,
            Font = TextFont,
        };
        close.Click += (s, e) => OnCloseRequested();
        Controls.Add(close);

        Y += 34;
    }

    protected void AddHead(string text)
    {
        Controls.Add(new Label
        {
            Text = "── " + text + " ──",
            Location = new Point(PadX, Y),
            AutoSize = true,
            ForeColor = DimFg,
            BackColor = Bg,
            Font = HeadFont,
        });
        Y += 24;
    }

    protected void AddCheck(string text, bool init, Action<bool> onChange)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = init,
            Location = new Point(PadX, Y),
            AutoSize = true,
            ForeColor = Fg,
            BackColor = Bg,
            Font = TextFont,
            FlatStyle = FlatStyle.Flat,
        };
        cb.CheckedChanged += (s, e) => onChange(cb.Checked);
        Controls.Add(cb);
        Y += 28;
    }

    protected Button AddButton(string text, Action onClick)
    {
        var b = new FlatButton
        {
            Text = text,
            Location = new Point(PadX, Y),
            Size = new Size(CtrlW, 30),
            BackColor = CtrlBg,
            ForeColor = Fg,
            Font = TextFont,
        };
        b.Click += (s, e) => onClick();
        Controls.Add(b);
        Y += 38;
        return b;
    }

    /// <summary>循环按钮: 点一下切到下一个选项 (替代 ComboBox —— 下拉列表是独立 HWND, 容易漏截图)。</summary>
    protected Button AddCycle(string prefix, string[] labels, int initIdx, Action<int> onPick)
    {
        int idx = initIdx;
        var b = new FlatButton
        {
            Text = $"{prefix}: {labels[idx]}",
            Location = new Point(PadX, Y),
            Size = new Size(CtrlW, 30),
            BackColor = CtrlBg,
            ForeColor = Fg,
            Font = TextFont,
        };
        b.Click += (s, e) =>
        {
            idx = (idx + 1) % labels.Length;
            b.Text = $"{prefix}: {labels[idx]}";
            onPick(idx);
        };
        Controls.Add(b);
        Y += 38;
        return b;
    }

    /// <summary>「标签 + 数字框 + 写入按钮」一行 (一次性写入: 填数字 → 点按钮)。</summary>
    protected void AddNumberRow(string label, decimal init, decimal min, decimal max, Action<decimal> onApply)
    {
        Controls.Add(new Label
        {
            Text = label,
            Location = new Point(PadX, Y + 4),
            AutoSize = true,
            ForeColor = DimFg,
            BackColor = Bg,
            Font = TipFont,
        });

        var nud = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(init, min, max),
            Location = new Point(PadX + 74, Y),
            Size = new Size(58, 24),
            BackColor = CtrlBg,
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle,
            Font = TextFont,
            ThousandsSeparator = true,
        };
        Controls.Add(nud);

        var btn = new FlatButton
        {
            Text = "写入",
            Location = new Point(PadX + 138, Y),
            Size = new Size(62, 26),
            BackColor = CtrlBg,
            ForeColor = Fg,
            Font = TipFont,
        };
        btn.Click += (s, e) => onApply(nud.Value);
        Controls.Add(btn);

        Y += 34;
    }

    /// <summary>「标签 + 数字框」一行, 改动即生效 (用于锁定目标值)。</summary>
    protected void AddInlineNumber(string label, int init, int min, int max, Action<int> onChange)
    {
        Controls.Add(new Label
        {
            Text = label,
            Location = new Point(PadX, Y + 4),
            AutoSize = true,
            ForeColor = DimFg,
            BackColor = Bg,
            Font = TipFont,
        });

        var nud = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(init, min, max),
            Location = new Point(PadX + 74, Y),
            Size = new Size(70, 24),
            BackColor = CtrlBg,
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle,
            Font = TextFont,
        };
        nud.ValueChanged += (s, e) => onChange((int)nud.Value);
        Controls.Add(nud);

        Y += 34;
    }

    protected Label AddTip(string text)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(PadX + 2, Y),
            AutoSize = true,
            MaximumSize = new Size(CtrlW - 6, 0),
            ForeColor = TipFg,
            BackColor = Bg,
            Font = TipFont,
        };
        Controls.Add(lbl);
        Y += lbl.Height + 4;
        return lbl;
    }

    protected Label AddValue(string label, int valueX = 104)
    {
        Controls.Add(new Label
        {
            Text = label,
            Location = new Point(PadX, Y + 3),
            AutoSize = true,
            ForeColor = DimFg,
            BackColor = Bg,
            Font = TipFont,
        });
        var v = new Label
        {
            Text = "—",
            Location = new Point(valueX, Y),
            AutoSize = true,
            ForeColor = Fg,
            BackColor = Bg,
            Font = ValueFont,
        };
        Controls.Add(v);
        Y += 26;
        return v;
    }

    protected void AddGap(int n) => Y += n;
}
