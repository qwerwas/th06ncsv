namespace TH06NCTools;

/// <summary>
/// 「选项」面板: 纯显示类设置, 全部只读, 不碰游戏内存。
/// 会写内存的都在 <see cref="CheatPanel"/> —— 两边分开免得误触。
///
/// ★ 发布版只保留**普通用户真的会改**的东西 (2026-10-06):
///   窗口置顶 + 五个显示开关 + 配色说明 + 重新附加/关闭。
///   原来这里的帧率、敌人闸门、激光宽度来源、道具占用判据、诊断按钮等
///   全部是开发期排查工具, 已随诊断代码一并删除 (开发版见 1Q2W3E 备份)。
///   凡是"关掉就会画错"的开关一律写死成const, 不做成可点的选项。
/// </summary>
internal sealed class OptionsPanel : SidePanel
{
    public OptionsPanel(RenderPanel render, NcReader reader,
                        bool topmostInit, Action<bool> setTopmost)
    {
        AddTitle("选项");

        AddHead("窗口");
        AddCheck("窗口置顶", topmostInit, setTopmost);

        AddHead("显示");
        AddCheck("显示敌弹", render.ShowBullets, v => render.ShowBullets = v);
        AddCheck("显示擦弹范围", render.ShowGraze, v => render.ShowGraze = v);
        AddCheck("显示道具", render.ShowItems, v => render.ShowItems = v);
        AddCheck("显示敌人", render.ShowEnemies, v => render.ShowEnemies = v);
        AddCheck("显示激光", render.ShowLasers, v => render.ShowLasers = v);

      

        AddGap(6);
        AddButton("重新附加进程", () => reader.RequestReattach());

        AddGap(14);
        AddButton("关闭", OnCloseRequested);
    }
}