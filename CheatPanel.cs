namespace TH06NCTools;

/// <summary>
/// 「修改器」面板: 会**改写游戏内存**的都在这里, 和纯显示的选项分开放。
///
/// 两类操作:
///  · 锁定 (残机/灵击/火力/无敌) —— 持续生效, 由读取线程每帧写入;
///  · 一次性写入 (填数字 → 点「写入」) —— 写一次就完, 之后游戏该减还是减。
/// 两者都只在游戏逻辑运行时生效, 且需要写权限。
/// </summary>
internal sealed class CheatPanel : SidePanel
{
    private readonly Label _valLives, _valBombs, _valPower, _valWrite, _stateTip;

    public CheatPanel(NcReader reader)
    {
        var ch = reader.Cheats;

        AddTitle("修改器");

        AddHead("锁定");
        AddCheck("无限残机", false, v => ch.InfiniteLives = v);
        AddCheck("无限符卡", false, v => ch.InfiniteBombs = v);
        AddCheck("满火力", false, v => ch.InfinitePower = v);
        AddInlineNumber("火力值", ch.PowerValue, 0, 65535, v => ch.PowerValue = v);
        AddCheck("无敌 ", false, v => ch.Invincible = v);
        AddTip("本区会改写游戏内存, 其余功能仍是纯只读; 只在战斗中生效");
        AddTip("残机/符卡 = 锁在「不再减少」的水位, 不是写死成大数字");
        

        AddHead("直接改数值");
        AddNumberRow("残机 →", 8, 0, 255, v => ch.SetU8(Nc.RVA_Lives, (byte)v));
        AddNumberRow("符卡 →", 8, 0, 255, v => ch.SetU8(Nc.RVA_Bombs, (byte)v));
        AddNumberRow("火力 →", 128, 0, 65535, v => ch.SetU16(Nc.RVA_Power, (ushort)v));
        AddNumberRow("擦弹 →", 0, 0, 9_999_999, v => ch.SetI32(Nc.RVA_Graze, (int)v));
        AddNumberRow("点道具 →", 0, 0, 9_999_999, v => ch.SetI32(Nc.RVA_Point, (int)v));
        AddNumberRow("Misses →", 0, 0, 9_999_999, v => ch.SetI32(Nc.RVA_Misses, (int)v));
        AddNumberRow("得分 →", 0, 0, 99_999_999_999, v => ch.SetI64(Nc.RVA_Score, (long)v));
        
        AddHead("当前");
        _valLives = AddValue("残机");
        _valBombs = AddValue("符卡");
        _valPower = AddValue("火力");
        _valWrite = AddValue("写入权限");

        _stateTip = AddTip("等待游戏进程…");
        _stateTip.MaximumSize = new Size(CtrlW - 6, 0);

        AddGap(14);
        AddButton("关闭", OnCloseRequested);
    }

    /// <summary>由主窗口 10Hz 调用: 刷新实时读数与可用状态。</summary>
    public void SetStatus(bool attached, bool inPlay, bool canWrite, NcSnapshot s)
    {
        _valLives.Text = attached ? s.Lives.ToString() : "—";
        _valBombs.Text = attached ? s.Bombs.ToString() : "—";
        _valPower.Text = attached ? s.Power.ToString() : "—";
        _valWrite.Text = !attached ? "—" : canWrite ? "可用" : "无";

        _valLives.ForeColor = attached && s.Lives > 0 ? Fg : WarnFg;
        _valBombs.ForeColor = attached && s.Bombs > 0 ? Fg : WarnFg;
        _valPower.ForeColor = Fg;
        _valWrite.ForeColor = !attached ? DimFg : canWrite ? OkFg : WarnFg;

        if (!attached)
        {
            _stateTip.Text = "等待游戏进程…";
            _stateTip.ForeColor = TipFg;
        }
        else if (!canWrite)
        {
            _stateTip.Text = "未取得写入权限.试试管理员启动。";
            _stateTip.ForeColor = WarnFg;
        }
        else if (!inPlay)
        {
            _stateTip.Text = "已连接, 但当前不在战斗中 —— 所有写入只在战斗中生效。";
            _stateTip.ForeColor = TipFg;
        }
        else
        {
            _stateTip.Text = "✓ 可写入: 锁定与「写入」即时生效。";
            _stateTip.ForeColor = OkFg;
        }
    }
}
