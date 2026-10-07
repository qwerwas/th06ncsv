namespace TH06NCTools;

internal struct NcBullet
{
    public float X, Y;
    public ushort State;      // 0空 1活跃 2-4生成 5消散; 只有 1 参与碰撞

    /// <summary>当前**生效的**绘制尺寸: 优先 = 判定尺寸(+0x5F4/+0x5F8), 读不到才退回贴图。</summary>
    public float W, H;

    /// <summary>true = W/H 来自条目 +0x5F4/+0x5F8(**判定尺寸**, 游戏命中用的就是它)。</summary>
    public bool FromHitbox;

    /// <summary>描述符(贴图)尺寸, 仅作对照 —— 大玉常常**大于**判定尺寸。</summary>
    public float TexW, TexH;

    public bool SizeKnown;
}

internal struct NcItem
{
    public float X, Y;
    public int Type;

    /// <summary>
    /// ★ 贴图边长(**全宽**, 不是半宽)。道具是正方形, 所以只有一个数。
    /// 来源: [条目+0x138] 描述符 → +0x1C (见 Nc.I_Desc)。实测恒 16。
    /// </summary>
    public float Size;

    /// <summary>true = Size 是从描述符读到的真值; false = 走了 Nc.ItemTexFallback 兜底。</summary>
    public bool SizeKnown;
}

internal struct NcEnemy
{
    public float X, Y, Z;
    public int Life;
    /// <summary>+0xBC 原始值 (s8)。**负数 = 占用中**; >=0 是空槽。见 Nc.E_Alive。</summary>
    public sbyte Alive;
    /// <summary>判定盒边长 (半边长 = Hitbox/2)。字段未确认, 由 UI 滑块给定。</summary>
    public float Hitbox;
}

internal struct NcLaser
{
    /// <summary>起点 (激光发射点)。</summary>
    public float X, Y;
    /// <summary>沿束已延伸长度。</summary>
    public float CurLen;
    /// <summary>激光总长度 (超过就不再延伸)。</summary>
    public float TotalLen;
    /// <summary>方向角 (弧度)。</summary>
    public float Angle;
    /// <summary>半宽 (垂直于束方向)。字段未确认时用默认值。</summary>
    public float HalfWidth;
    /// <summary>起点处半宽 —— "前细后粗"时小于 <see cref="HalfWidthEnd"/>。</summary>
    public float HalfWidthStart;
    /// <summary>终点处半宽 —— "前细后粗"时大于 <see cref="HalfWidthStart"/>。</summary>
    public float HalfWidthEnd;
    /// <summary>宽度是否真的来自内存 (false = 用滑块兜底, 宽度未确认)。</summary>
    public bool WidthFromMemory;
}

/// <summary>
/// 一帧采样结果。两个实例轮换 (双缓冲): 读取线程写其中一个, 写完才把它挂到 Latest,
/// 渲染线程只持有引用 —— 因此不存在"读到半截更新"的中间态, 也不需要加锁。
/// </summary>
internal sealed class NcSnapshot
{
    public bool Valid;
    public long TimestampMs;

    /// <summary>弹幕管理器帧计数器还在自增 = 游戏逻辑在跑 (主菜单/结算时会停)。</summary>
    public bool Active;
    public uint FrameCounter;

    // 局内数值
    public int Misses, BombsUsed, Character, Shot, Stage, Power;
    public long Score, DisplayScore;
    public bool Paused, GameOver, Practice, SpellPractice;
    public int Point, Difficulty, Graze, StageGraze, Lives, Bombs, Extends;
    public float BoundL, BoundT, BoundR, BoundB;
    public int CurrentState, TimelineFrame;
    public ushort Input;

    // 自机
    public float PX, PY, PlayerR;
    public int PlayerState;
    public bool PlayerValid;

    // 实体
    public int BulletCount;
    public readonly NcBullet[] Bullets = new NcBullet[Nc.BulletSlots];
    public int BulletSizeKnown;      // 读到尺寸的条数 (判定或贴图, 校验指标)
    /// <summary>其中来自条目 +0x5F4/+0x5F8 **判定尺寸**的条数。</summary>
    public int BulletHitboxKnown;
    public int BulletActiveState1;   // state==1 的条数 (真正会打死人的)
    public int ItemCount;
    public readonly NcItem[] Items = new NcItem[Nc.ItemSlots];

    // 敌人
    public int EnemyCount;
    public readonly NcEnemy[] Enemies = new NcEnemy[Nc.EnemySlots];
    /// <summary>+0xBC 为**负数**(真正占用中)的条数 —— 这才是"游戏认为活着的敌人数"。</summary>
    public int EnemyAliveNeg;
    /// <summary>其中坐标也落在场地附近的条数 = 实际画出来的数量。</summary>
    public int EnemyActive;
    /// <summary>不论死活, 坐标落在场地附近的条数 (含空槽的 0,0, 只作诊断)。</summary>
    public int EnemyInField;
    public string EnemyInfo = "";

    // 激光
    public int LaserCount;
    public readonly NcLaser[] Lasers = new NcLaser[Nc.LaserSlots];
    public int LaserActive;        // +0x27C 非 0 的条数
    public string LaserInfo = "";

    // 道具逐级计数 —— 显示不出来时靠它判断是被哪一道筛子挡掉的
    public int ItemSlotsUsed;        // 过了"槽占用"判据的条数
    public int ItemCoordOk;          // 坐标有限且落在场地附近
    public int ItemStaticDropped;    // 被"150ms 没挪窝"判成残留
    /// <summary>存活槽(+0x00 非零)里坐标落在场地内的条数 —— 这才是有意义的"坐标像"计数。
    /// 早期版本没按存活过滤, 空槽的 (0,0) 也算命中, 结果恒为 1024, 诊断完全失效。</summary>
    public int ItemCoordLike;
    public int ItemAliveNonZero;     // +0x00 标志字节非零的槽数 (游戏自己认的"在用")
    public string ItemPoolInfo = ""; // 池地址 + 是直接寻址还是经指针

    /// <summary>道具类型分布 ("0×3 1×5" 这样), 用来对着游戏画面反推类型号含义。</summary>
    public string ItemTypeSummary = "";

    /// <summary>当前屏幕上的弹幕尺寸种类 ("12×12 ×23 · 32×32 ×5"), 用于校验描述符读的是不是真尺寸。</summary>
    public string BulletSizeSummary = "";
    /// <summary>贴图与判定不一致的弹种 ("判定 16×16 ← 贴图 32×32 ×12")。</summary>
    public string BulletSizeDiffSummary = "";

    /// <summary>战斗中 = (游戏逻辑在跑 或 已暂停) 且 自机坐标落在场地附近。
    /// 暂停时数据仍然有效, 保留最后一帧画面; 主菜单/结算画面才是真该停画的时候。</summary>
    public bool InPlay => (Active || Paused) && PlayerValid;

    public void Clear()
    {
        Valid = false;
        Active = false;
        PlayerValid = false;
        BulletCount = 0;
        BulletSizeKnown = 0;
        BulletHitboxKnown = 0;
        BulletActiveState1 = 0;
        ItemCount = 0;
        EnemyCount = 0;
        EnemyAliveNeg = 0;
        EnemyActive = 0;
        EnemyInField = 0;
        EnemyInfo = "";
        LaserCount = 0;
        LaserActive = 0;
        LaserInfo = "";
        ItemSlotsUsed = 0;
        ItemCoordOk = 0;
        ItemStaticDropped = 0;
        ItemCoordLike = 0;
        ItemAliveNonZero = 0;
        ItemTypeSummary = "";
        BulletSizeSummary = "";
        BulletSizeDiffSummary = "";
    }
}
