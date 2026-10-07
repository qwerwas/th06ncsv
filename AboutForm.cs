namespace TH06NCTools;

/// <summary>
/// 「关于」窗口 (2026-10-06)。
///
/// 就一块内容: <b>你自己写的项目介绍</b> —— 见 <see cref="AboutMessage"/>。
///
/// ★ 曾经这里有一份图例(敌弹/自机/敌人/激光/道具共 19 项), 后来去掉了:
///   实测对不上 —— 图例的颜色形状是从 RenderPanel 抄来的常量, 游戏区一改配色
///   图例就变成"看起来很像但其实在骗人"的东西, 比没有图例更糟。
///   真看不懂某个形状的时候, 双击左侧信息面板(开发版)或直接问作者更靠谱。
/// </summary>
internal sealed class AboutForm : Form
{
    private const string AboutMessage = "Th06simpleview ver.新典 \n\n" +
        "Mrrty1 vs. Agent (?。 \n\n" +
        "好难做。\n\n" ;
    /// <summary>标题下方那一行灰字。</summary>
    private const string Tagline = "Th06simpleview ver.新典";

    // ---- 配色 ----
    private static readonly Color Bg = Color.FromArgb(20, 20, 27);
    private static readonly Color Fg = Color.FromArgb(232, 232, 240);
    private static readonly Color Dim = Color.FromArgb(140, 140, 158);
    private static readonly Color Faint = Color.FromArgb(95, 95, 112);
    private static readonly Color Rule = Color.FromArgb(58, 58, 74);
    private static readonly Color Body = Color.FromArgb(205, 205, 218);

    private const int ML = 24;          // 左边距
    private const int MaxW = 560;        // 窗口内容宽度
    private const int LineH = 21;        // 正文每行高

    private readonly Font _fontBig = new("Microsoft YaHei UI", 12F, FontStyle.Bold);
    private readonly Font _font = new("Microsoft YaHei UI", 9.5F);
    private readonly Font _fontSm = new("Microsoft YaHei UI", 8.5F);

    public AboutForm()
    {
        Text = "关于";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Bg;
        ForeColor = Fg;
        Padding = Padding.Empty;
        DoubleBuffered = true;

        // ★ 高度由正文行数算出来, 不写死 —— 你写多少行废话都不会被裁掉。
        ClientSize = new Size(MaxW, MeasureHeight());

        var close = new FlatButton
        {
            Text = "关闭",
            Size = new Size(88, 30),
            Location = new Point(ClientSize.Width - ML - 88, ClientSize.Height - 40),
            BackColor = Color.FromArgb(34, 34, 44),
            ForeColor = Fg,
            Font = _font,
        };
        close.Click += (s, e) => Close();
        Controls.Add(close);
    }

    /// <summary>AboutMessage 为空时返回空数组。</summary>
    private static string[] MessageLines =>
        AboutMessage.Length == 0 ? Array.Empty<string>() : AboutMessage.Split('\n');

    /// <summary>按正文行数算窗口高度。</summary>
    private int MeasureHeight()
    {
        int h = 22 + 26 + 24 + 1 + 20    // 标题 + 副标题 + 分隔线 + 留白
              + 46;                       // 底部版本行 + 关闭按钮
        var lines = MessageLines;
        h += lines.Length > 0 ? lines.Length * LineH + 12 : 24;   // 正文 或 空状态提示
        return h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Bg);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int right = ClientSize.Width - ML;
        float y = 22;

        // ---------------- 标题 ----------------
        PutText(g, "TH06NCSV", _fontBig, Fg, ML, y);
        y += 26;
        PutText(g, Tagline, _font, Dim, ML, y);
        y += 24;

        using (var p = new Pen(Rule, 1F))
            g.DrawLine(p, ML, y, right, y);
        y += 20;

        // ---------------- 正文(你写的部分) ----------------
        // AboutMessage 为空时这里就是空的 —— 不画"请在此填写"之类的提示,
        // 你没写东西就不该有东西出现在窗口里。
        foreach (var line in MessageLines)
        {
            PutText(g, line, _font, Body, ML, y);
            y += LineH;
        }

        // ---------------- 底部 ----------------
       //  var ver = typeof(AboutForm).Assembly.GetName().Version?.ToString(3) ?? "build 20261006";
        PutText(g, $"ver. build 20261006     ",
                _fontSm, Faint, ML, ClientSize.Height - 40);
    }

    /// <summary>DrawString 的第 3 参要 Brush, 这里用一次性 SolidBrush 包一层。</summary>
    private static void PutText(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y);
    }
}