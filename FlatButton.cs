namespace TH06NCTools;

/// <summary>
/// 深色主题下能可靠控制文字颜色的自绘按钮。
///
/// 原生 Button 的文字由系统自己上色 —— 禁用状态尤其明显, 会画成很暗的灰/黑,
/// 在深色底上几乎看不见, 改 ForeColor 也压不住。所以背景/边框/文字全部自绘:
/// 颜色只由属性决定, 只有色值随启用/悬停/按下变化。
/// </summary>
internal sealed class FlatButton : Button
{
    private bool _hover;
    private bool _pressed;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw, true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        TabStop = false;
    }

    private static readonly Color HoverBg = SidePanel.CtrlHoverBg;
    private static readonly Color PressedBg = SidePanel.CtrlPressedBg;
    private static readonly Color BorderOn = SidePanel.CtrlBorder;
    private static readonly Color BorderOff = Color.FromArgb(50, 50, 64);
    private static readonly Color DisabledFore = SidePanel.DimFg;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(mevent);
    }
    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        if (_pressed) { _pressed = false; Invalidate(); }
        base.OnMouseUp(mevent);
    }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(BackColor);

        if (Enabled && _pressed)
        {
            using var b = new SolidBrush(PressedBg);
            g.FillRectangle(b, 1, 1, Width - 2, Height - 2);
        }
        else if (Enabled && _hover)
        {
            using var b = new SolidBrush(HoverBg);
            g.FillRectangle(b, 1, 1, Width - 2, Height - 2);
        }

        using (var pen = new Pen(Enabled ? BorderOn : BorderOff))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        TextRenderer.DrawText(
            g, Text, Font, ClientRectangle,
            Enabled ? ForeColor : DisabledFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
