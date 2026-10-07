namespace TH06NCTools;

/// <summary>
/// 东方红魔乡 新典 (th06nc.exe, Steam 25306795) 地址表。
///
/// 收录原则: **只写已确认项**。来源标记
///   T = thprac-th06nc `thprac/src/thprac/th06nc/addresses.h`
///   H = th12_hfr `docs/games/TH06NC_DEVNOTES.md` + `src/games/th06nc.c`
///   R = 本机运行时实测
/// 未确认的东西 (敌人池 Steam 地址 / 激光池 / 特效池 / 描述符是否半尺寸) 一律不进这张表,
/// 也不在本工具里画 —— 猜出来的图形比不画更害人。
///
/// 全部为 RVA: 实际地址 = 模块基址 + RVA (x64 有 ASLR, 基址每次启动都不同)。
/// </summary>
internal static class Nc
{
    // ---- 版本指纹 (2026-09-24 更新版 / Steam 25306795) ----
    public const string KnownSha256 = "48630a42a2eb6762d0db2a7e0d151203efbe67220fef7d2f9d0d51857928ac82";
    public const uint KnownImageSize = 0xCBC000;

    // ---- 表 1　局内数值 ----
    public const uint RVA_Misses = 0x53CAB0;         // i32  T+R
    public const uint RVA_BombsUsed = 0x53CAB4;      // i32  T
    public const uint RVA_Character = 0x53CAD0;      // u8   T+R  0=灵梦 1=魔理沙
    public const uint RVA_Shot = 0x53CAD1;           // u8   T+R  0=A 1=B
    public const uint RVA_Stage = 0x53CAD4;          // i32  T+R
    public const uint RVA_Power = 0x53CAD8;          // u16  T+R
    public const uint RVA_Replay = 0x53D3DC;         // u8   T
    public const uint RVA_DisplayScore = 0x53D3E0;   // i64  T
    public const uint RVA_Score = 0x53D3E8;          // i64  T+R
    public const uint RVA_Paused = 0x53D400;         // u8   T+R
    public const uint RVA_GameOver = 0x53D401;       // u8   T+R
    public const uint RVA_Practice = 0x53D404;       // u8   T+R
    public const uint RVA_SpellPractice = 0x53D405;  // u8   T+R
    public const uint RVA_Point = 0x53D40C;          // i32  T+R
    public const uint RVA_Difficulty = 0x53D410;     // u32  T+R  0Easy 1Normal 2Hard 3Lunatic 4Extra

    // ---- 表 2　残机 / 擦弹 / 场地 ----
    public const uint RVA_StageGraze = 0x549D1C;     // i32  T+R
    public const uint RVA_Graze = 0x549D20;          // i32  T+R
    public const uint RVA_Bounds = 0x549D30;         // float x4  T+R
    public const uint RVA_Lives = 0x549D40;          // u8   T+R
    public const uint RVA_Bombs = 0x549D41;          // u8   T+R
    public const uint RVA_Extends = 0x549D42;        // u8   T+R
    public const uint RVA_TimelineFrame = 0xBFA16C;  // i32  T+R

    // ---- 表 3　场景 / 输入 ----
    public const uint RVA_CurrentState = 0xC6DFB8;   // i32  T+R
    public const uint RVA_NextState = 0xC6DFBC;      // i32  T
    public const uint RVA_Input = 0xABAE80;          // u16  T+H
    public const uint RVA_PreviousInput = 0xABAE84;  // u16  T+H

    // ---- 表 4　对象池 ----
    // 全部由本机 th06nc.exe (48630a42…) 的 .text 反汇编定死, 2026-10-05。
    // 每条都有"池初始化循环"作铁证 (lea 取基址 / mov 取槽数 / add|lea 取 stride),
    // 并且区间互不重叠 —— 见 C:\123\TH06NC_地址表_已确认.md 表 4 与表 15。
    public const uint RVA_BulletManager = 0x436EF0;  // 8B: 两个 dword, 每帧自增 (H 验证 True)
    public const uint RVA_BulletPool = 0x436EF8;     // 640 x 0x620   .text 0x10C4 初始化循环
    // RVA_LaserPool 见下方"表 4.1"—— 0x52BF10 是错的, 正确是 0x52BF00
    public const uint RVA_EnemyPool = 0xAEE0B8;      // 257 x 0x10B0  .text 0x12F0 初始化循环
    public const uint RVA_EffectPool = 0xABAEF0;     // 512 x 0x198   .text 0x1250 初始化循环
    public const uint RVA_ItemPool = 0xBFB2F8;       // 1024 x 0x160  9 处 imul 0x160

    public const int BulletSlots = 640;
    public const int BulletStride = 0x620;
    public const int LaserSlots = 64;
    public const int LaserStride = 0x298;
    public const int EnemySlots = 257;
    public const int EnemyStride = 0x10B0;
    public const int EffectSlots = 512;
    public const int EffectStride = 0x198;
    public const int ItemSlots = 1024;
    public const int ItemStride = 0x160;

    // 管理头相对偏移 (更新/绘制循环里用的是这种写法, 不是绝对地址)
    // .text 0x10130: add rbx, 8; add rbx, rsi        → 敌弹池 = mgr + 8
    // .text 0x10120: movsxd rdx, [rsi + 0xFF618]     → 敌弹索引游标
    // .text 0x10588: add rdx, 0xFF620; add rdx, rsi   → 激光子池
    public const int M_BulletPool = 0x08;
    public const int M_BulletCursor = 0xFF618;
    public const int M_LaserSub = 0xFF620;

    // ---- 表 5　Player 内部偏移 ----
    public const uint RVA_Player = 0x549FF0;
    public const int P_PosX = 0x7730;                // float  H+R
    public const int P_PosY = 0x7734;                // float  H+R
    public const int P_PosZ = 0x7738;                // float  H
    public const int P_HitRadius = 0x774C;           // float  H+R  实测 1.25
    public const int P_State = 0x7898;               // i32    H+R  钉成 3 可无敌

    // ---- 表 6　弹条目内部偏移 (stride 0x620) ----
    public const int B_PosX = 0x30;                  // float  H 机器码 + R
    public const int B_PosY = 0x34;
    public const int B_State = 0x44;                 // u16 0空/1活跃/2-4生成/5消散
    public const int B_Desc = 0x150;                 // ptr **绝对线性地址** (不是 RVA)
    public const int B_GrazeFlag = 0x618;            // byte

    // ---- ★★ 判定尺寸 vs 贴图尺寸 ----
    //
    // ★★★ 2026-10-06 15:20 **定案**: 真正的**判定尺寸**是条目 +0x5F4/+0x5F8,
    //   而描述符 +0x1C/+0x20 是**贴图尺寸** —— 两者是**互相独立**的两套数。
    //
    //   证据链(全部来自本机 Steam 版 .text, 按 .pdata 定界):
    //
    //   ① 命中判定函数 0x6C090 是叶子函数, 尺寸指针由调用方给:
    //        0x11B32  lea  r8, [rbx + 0x5f4]     ← r8 = 弹条目 + 0x5F4
    //        0x11B3D  lea  rdx, [rbx + 0x30]     ← rdx = 弹条目 + 0x30 (坐标, 与 B_PosX 吻合)
    //        0x11B48  call 0x6C090
    //      函数体内: movss xmm3,[r8] (w) / movss xmm6,[r8+4] (h)
    //
    //   ② 写入点在弹幕初始化函数 0x10048..0x10C92:
    //        0x10674  movsx  rax, word [rdi + 0x2c]      ; 弹种 ID
    //        0x10679  imul   rax, rax, 0x5b0             ; 弹种表 stride = 0x5B0
    //        0x10680  movsd  xmm0, qword [rax + rsi + 0xffbc4]   ; 从**弹种表**读 8 字节
    //        0x10689  movsd  qword [rbx + 0x5f4], xmm0   ; ⇒ 一次写 w(+0x5F4) 和 h(+0x5F8)
    //
    //   ⇒ 判定尺寸来自**弹种定义表**, 与贴图无关。这正好解释用户实测:
    //     "最大的大玉判定范围**小于**贴图" —— 贴图 32×32, 弹种表里判定可能只给 16×16。
    //
    //   ⚠ 工具此前一直画的是描述符尺寸(贴图), 所以大玉画得比真实判定**大一圈**。
    public const int B_HitW = 0x5F4;                 // float ★判定宽 w (弹种表 → 条目)
    public const int B_HitH = 0x5F8;                 // float ★判定高 h
    /// <summary>判定盒之后的第三个值(弹种表 +0x...bcc)。语义未定, 仅供诊断对照。</summary>
    public const int B_HitExtra = 0x5FC;             // u32
    /// <summary>弹种表项里的标志字节(弹种表 +0x...bc0 → 条目)。供诊断对照。</summary>
    public const int B_HitFlag = 0x5F0;              // u8

    // ---- 表 7　弹描述符内部偏移 ----
    public const int D_Width = 0x1C;                 // float  H+R
    public const int D_Height = 0x20;                // float  H+R

    // ---- 表 12　敌人条目内部偏移 (stride 0x10B0, 257 槽) ----
    //
    // 2026-10-05 反汇编实锤。敌人管理器里有两类 0xBC 访问, 语义**完全相反**, 必须分清:
    //
    // (A) 分配槽位 0x37C20 —— 找**空槽**:
    //       37C20  cmp byte [rbx+0xBC], sil(sil=0)
    //       37C27  jge  0x37C45        ; >=0 → 认定是空槽, 跳去生成
    //       37C45  mov r8d, 0x10B0 / lea rdx,[0xBFA1E0] / call 0x2F1C90
    //              ; 整条拷贝模板 → 写 192/-64/0 → 写 HP 三副本  ⇒ 这是在"生成敌人"
    //       ⇒ 所以 [rbx+0xBC] >= 0 是**空槽**, 负数才是占用中
    //
    // (B) 更新循环 0x38DC0 —— 处理**已占用的槽** (同时就是每帧算最终绘制坐标的地方):
    //       38DC0  cmp byte [rbx+0xBC], 0
    //       38DC7  jge  0x39A94        ; >=0 → 跳过, 不更新
    //       38DCD  inc dword [r12]      ; 负数才计数 (存活敌人数)
    //       38DD1  lea rsi, [rbx+0xB0]
    //       38DD8  movzx edx, byte [rbx+0xBC]
    //       38DEC  test dl, 0x40        ; bit6 = X 轴镜像 (决定加还是减)
    //       38DE4  movss xmm1, [rbx+0x1084]   ; vx
    //       38E09  movss xmm3, [rbx+0x1088]   ; vy
    //       38E19  movss xmm0, [rbx+0x108C]   ; vz
    //       38DFE  movss [rsi],  xmm1   ; → 写回 +0xB0
    //       38E29  movss [rbx+0xB4], xmm3        ; → 写回 +0xB4
    //       38E34  movss [rbx+0xB8], xmm0        ; → 写回 +0xB8
    //       38F4D  and  dl, 0x7f         ; 飞出场地 → 清 bit7 (标记回收)
    //       38F50  mov  [rbx+0xBC], dl
    //       38E9C  movzx eax, byte [rbx+0xBD]   ; bit2 = 已做过入场判定, bit5 = 擦弹过
    //
    // ★ 由此可确认 +0xB0/+0xB4/+0xB8 = **每帧重算并 clamp 过的最终绘制坐标**
    //   (不是原始脚本坐标), 且 +0x1084/+0x1088/+0x108C 是速度分量。
    //   生成时写入的默认值 (0x37C5A..0x37C77): X=192.0(.rdata 0x352CA4) / Y=-64.0(0x352DEC) / Z=0
    //   —— 192 = 384/2 屏幕中心, -64 = 场地上方待命位。
    //   37CB3  mov eax, [rbx+0x234] → 复制到 +0x238/+0x23C (HP 三个副本)
    public const int E_PosX = 0xB0;                  // float  ★最终绘制 X (每帧由 vx 累加)
    public const int E_PosY = 0xB4;                  // float  ★最终绘制 Y
    public const int E_PosZ = 0xB8;                  // float  ★最终绘制 Z
    public const int E_Alive = 0xBC;                 // s8   ★负数 = 占用中(活跃); >=0 = 空槽
    public const int E_Flags = 0xBD;                 // u8   bit2 入场判定完成 / bit5 已擦弹

    /// <summary>
    /// ★ +0xBD 的 bit2 = **已完全进入场地**。
    ///
    /// 汇编(敌人更新函数 0x38CD0, .pdata 定界):
    ///   0x38E9C  movzx eax, byte [rbx + 0xbd]
    ///   0x38EA3  test  al, 4
    ///   0x38EA5  jne   0x38f05        ← 已置位就不再做入场检测
    ///   0x38EA7  mov   rcx, [rbx + 0x208]   ; 精灵
    ///   0x38EAE  movss xmm3, [rcx + 0x20]
    ///   ... 与场地四边比较 ...
    ///   0x38EF5  or    al, 4          ← ★ 进入场地才置 bit2
    ///   0x38EF7  mov   byte [rbx + 0xbd], al
    ///
    /// ★★ 用途: 滤掉**已分配但还在场地外待命**的敌人。
    ///   这类槽 alive(+0xBC) 已经是负数(占用中), HP 也已由 ECL 写好,
    ///   判定盒还停留在模板默认值(所以画出来是个"小方框"),
    ///   但游戏**根本没把它画到屏幕上** —— 于是工具会画出"屏幕上不存在的敌人"。
    ///   (用户实测现象: 几个血量为 900 的小方框)
    /// </summary>
    public const byte E_FlagEntered = 0x04;

    /// <summary>
    /// ★★ 2026-10-06 16:50 **新增**: 敌人条目第二组标志字节 `+0xBE`。
    ///
    /// 在敌人更新函数 0x38CD0..0x39BAD 里, 读 HP(+0x234) **之前**有两道闸门:
    /// ```asm
    /// 039300  movzx eax, byte ptr [rbx + 0xbd]
    /// 039312  test  al, 4                        ; ← 闸门①: +0xBD bit2 = 已入场
    /// 039314  je    0x399b5                      ;   未置位 → 跳过
    /// 03931A  test  byte ptr [rbx + 0xbe], 8     ; ← 闸门②: +0xBE bit3
    /// 039321  jne   0x399b5                      ;   ★置位 → 同样跳过
    /// 039327  mov   r15d, dword ptr [rbx + 0x234]; 到这里才读 HP
    /// ```
    /// 之前工具只实现了闸门①(OnlyEnteredEnemies), **漏了闸门②** ——
    /// 于是那些"游戏自己都跳过不更新"的槽被画了出来, 表现就是
    /// **"屏幕上出现游戏里根本没有、打也不掉血的固定小方框"**(用户实测 HP=900)。
    /// </summary>
    public const int E_Flags2 = 0xBE;                // u8   第二组标志

    /// <summary>+0xBE 的 bit3: 置位 ⇒ 游戏主更新循环**整条跳过**这条敌人 ⇒ 不该画。</summary>
    public const byte E_SkipUpdate = 0x08;
    public const int E_VelX = 0x1084;                // float  vx (bit6 决定符号)
    public const int E_VelY = 0x1088;                // float  vy
    public const int E_VelZ = 0x108C;                // float  vz
    public const int E_Life = 0x234;                 // i32   HP
    public const int E_Life2 = 0x238;                // i32   HP 副本
    public const int E_Life3 = 0x23C;                // i32   HP 副本

    // ---- 表 12.2　敌人判定盒 (hitboxDimensions)　★2026-10-05 20:10 实锤 ----
    //
    // th06-plus 反编译写死 D3DXVECTOR3(12,12,12), 但 th06nc 不同版本默认值不同,
    // 必须自己找。结论: **判定盒 = enemy+0x2C0 / 0x2C4 / 0x2C8 (三个 float)**。
    //
    // 【证据1: ECL 指令写入点】ECL 解释器 0x2753F:
    //     02753F  mov eax, [rsi]        ; ← ECL 指令第 1 个参数
    //     027541  mov [rdi+0x2C0], eax
    //     027547  mov eax, [r14+0x10]   ; ← 第 2 个参数
    //     02754B  mov [rdi+0x2C4], eax
    //     027551  mov eax, [r14+0x14]   ; ← 第 3 个参数
    //     027555  mov [rdi+0x2C8], eax
    //     02755B  jmp 0x25C2D           ; 回到解释器主循环
    //   三条连续读参数 + 写三个连续 float ⇒ 这条 ECL 指令就是"设判定盒"。
    //   ECL 文本里对应 **ins_103(x, y, z)**, 实际值全是 (28.0, 28.0, 32.0)。
    //
    // 【证据2: 碰撞处读出】0x39300 段 (rbx = enemy 条目基址):
    //     039349  movss xmm0, [rbx+0x2C0]   ; ★读判定盒 X
    //     039358  movss xmm1, [rbx+0x2C4]   ; ★读判定盒 Y
    //     039374  movss xmm0, [rbx+0x2C8]   ; ★读判定盒 Z
    //     03936E  movss [rsp+0x38], xmm0    ; 弹 w = X * 缩放
    //     039380  movss [rsp+0x3C], xmm1    ; 弹 h = Y * 缩放
    //     03938C  call 0x6C090              ; 碰撞函数
    //   0x6C090 的 r9b==0 分支把 rdx 当 {minX,minY,maxX,maxY} 用
    //   (0x6C0CC subss / 0x6C0D1 addss 各乘 0.5), 说明它确实是**碰撞盒尺寸**。
    //
    // 【单位】这三个值是**盒子的宽/高/纵深**(不是半径)。
    // 0x6C090 里算的是 min(w,h) * 0.5 + 玩家半径, 而 0.5 正是"盒宽 → 半径"的换算,
    // 所以 28.0 意味着"判定盒是 28×28 的方, 半径 14"。
    // ★旧的 12.0 滑块默认值是照抄 th06-plus 的, 对 th06nc 不适用, 已改为直读。
    public const int E_HitboxX = 0x2C0;              // float  判定盒 宽
    public const int E_HitboxY = 0x2C4;              // float  判定盒 高
    public const int E_HitboxZ = 0x2C8;              // float  判定盒 纵深(3D 用, 2D 判定不参与)


    /// <summary>敌人判定盒边长 (D3DXVECTOR3 hitboxDimensions)。</summary>
    /// <remarks>
    /// 原版 th06 反编译 (Fairy-Oracle-Sanctuary/th06-plus, EnemyManager::Initialize) 写死
    /// <c>enemy-&gt;hitboxDimensions = D3DXVECTOR3(12.0f, 12.0f, 12.0f)</c> 作为模板默认值,
    /// ECL 指令可逐场覆盖。th06nc 侧该字段偏移尚未逐字节确认, 先用同值 + 手动可调。
    /// </remarks>
    public const float EnemyHitboxDefault = 12.0f;

    // ---- 表 4.1　激光池 (★2026-10-05 晚修正: 基址之前是错的) ----
    //
    // 【错在哪】之前写的是 RVA 0x52BF10, 来源是"0x10FB 初始化循环 lea 到 0x52BF10"。
    // 但把那个循环逐条读完就知道它**根本不是激光池**:
    //   0x10FB  mov ecx, 0x40              ; 循环次数 64
    //   0x1100  lea rax, [rip+0x52AE09]    ; -> 0x52BF10
    //   0x1110  mov [rax+0x00], ebp / 0x1113 [rax+0x00], esi
    //   0x1115  mov [rax+0x40], ebp / 0x1118 [rax+0x3C], esi
    //   0x111B  mov [rax+0xB0], ebp / 0x1121 [rax+0xAC], esi
    //   0x1127  mov [rax+0xD0], ebp / 0x112D [rax+0xCC], esi
    //   0x1133  mov [rax+0xF8], ebp / 0x1139 [rax+0xF4], esi
    // 写的是 +0x00/+0x40/+0xB0/+0xD0 这种 **0x2C 步长**的 4 组 (0, 0x40, 0xAC, 0xCC...),
    // 是另一套 0x2C 大小的子结构 —— 拿它当 0x298 的激光条目池, 读到的全是错位字节,
    // 表现就是**激光一条都画不出来**。这个错误只在运行时才暴露 (静态上"看起来自洽")。
    //
    // 【真基址怎么定的】激光更新循环 A 完整闭合:
    //   0xF68A  xor r13d, r13d            ; r13 = 0
    //   0xF7F7  lea rsi, [rip+0x51C99A]   ; -> 0x52C198   ← rsi 初值
    //   0xF7FE  mov r15d, 0x40            ; 64 槽
    //   0xF810  cmp byte [rsi-0x1C], r13b ; 首条访问 (rsi-0x1C = 条目+0x27C)
    //   ...
    //   0xF9DD  add rsi, 0x298            ; 推进 stride
    //   0xF9E4  sub r15, 1
    //   0xF9F0  jne 0xF810                ; 回到循环头
    // 首轮 rsi = 0x52C198 访问的却是**当前条目**的 +0x27C ⇒ 0x52C198 = 条目1
    // ⇒ **条目0 = 0x52C198 - 0x298 = 0x52BF00**
    // 旁证: 池初始化三处 (0x117A lea rax,[rax+0x298] / 0x10D4C / 0x11E5F add rbx,0x298)
    //       都从 0x52BF00 起。
    public const uint RVA_LaserPool = 0x52BF00;      // 64 x 0x298  ★不是 0x52BF10

    // ---- 表 13　激光条目内部偏移 (stride 0x298, 64 槽) ----
    //
    // 2026-10-05 反汇编实锤 (激光更新循环 A 0xF810..0xF9F0 / B 0xFC30..0xFEE2, 结构相同)。
    // 循环里 `rsi` 已经前进到下一条目, 所以指令里的负偏移要换算: 实际 = 0x298 - |负值|
    // (例: [rsi-0x30] → +0x268, [rsi-0x27C] → +0x1C, [rsi-0x18] → +0x280)
    // ★下表已在 0xF810-0xF9F0 / 0xFC30-0xFEE2 两段里**全量重新汇总核对过一遍**
    //   (把该区间内所有 [rsi-K] 逐条列出来, 换算成条目偏移后去重):
    //     +0x008 mov   每轮清 0          +0x01C movss 激光总长 (4 处引用, 交叉确认)
    //     +0x010 mov   恒写 -999          +0x268 movss 角度 → sinf/cosf 入参
    //     +0x014 mov   每轮清 0          +0x27C cmp   活跃标志 (非 0 才处理)
    //     +0x018 movss 沿束累积距离       +0x280 addss 起点 Y
    //                                  +0x284 addss 起点 X   (+0x280/+0x284 是相邻两 float:
    //                                                  0xFCB5 movsd 一次读 8 字节 = X+Y)
    //                                  +0x288 mov   (仅 B 循环 0xFCBE 引用, 语义待查)
    //   0xF840  movss xmm6, [rsi-0x30]    ; 角度 → 0xF850 call 0x2EFFF0(sinf) / 0xF85C call 0x2EF700(cosf)
    //   0xF8AC  addss xmm0, [rsi-0x18]    ; 起点Y + cos(角度)*累积距离
    //   0xF8B1  addss xmm1, [rsi-0x14]    ; 起点X + sin(角度)*累积距离
    //   0xF861  comiss xmm1, [rsi-0x27C]  ; 总长 vs 累积距离 (超出就停延伸)
    //   0xF9B7  addss xmm7, xmm8          ; 累积距离 += 32.0  (0x352C04 = 32)
    //   0xF810  cmp byte [rsi-0x1C], r13b ; r13 = 0xF68A xor 得出 ⇒ +0x27C 非 0 = 活跃
    // ---- 运行时实锤 (2026-10-05 20:30 laser_solve.py 实测 48 个活跃槽) ----
    // ① ★ X/Y 之前是**反的**, 已修正。
    //    实测槽 0..7 的 +0x280 序列 = 173.27 197.10 217.94 223.59 210.73 186.90 166.06 160.41
    //       两两配对和恒为 384.0  → 对称中心 **192.0 = 384/2** ⇒ 这是 **X**
    //    实测 +0x284 序列 = 86.06 80.41 93.27 117.10 137.94 143.59 130.73 106.90
    //       两两配对和恒为 224.0  → 对称中心 **112.0** ⇒ 这是 **Y** (环形激光发射环的圆心)
    //    独立佐证: 以 (192,112) 为心算极角, 槽 0..7 依次 = -125.8 -80.8 -35.8 9.2 54.2 99.2 144.2 189.2
    //       与 +0x268 实测值 (-2.20 -1.41 -0.63 0.16 0.95 1.73 2.52 3.30 rad) **逐条吻合**
    //       → 激光沿半径朝外发射, +0.268 确实是方向角。
    //    反过来若 X/Y 互换, 极角会变成 -144.2°, 与实测 -126.0° 不符 ⇒ 排除。
    //
    // ② ★ +0x18 实测**恒为 0.0**(48 个活跃槽无一例外), 所以它**不能**直接当"当前长度"用。
    //    旧代码 MainForm.cs `if (len <= 0.01f) continue;` 因此把 48 条激光**全部跳过**
    //    —— 这就是"激光一条都画不出来"的直接机制(不是池基址问题)。
    //    实测 +0x1C 稳定 = 112.0, 且几何自洽:从槽 0 起点 (173.27,86.06) 沿 -2.196 rad 走 112 →
    //    (107.92, -4.90), 与槽 8 实测坐标 (107.70,-4.75) 吻合 ⇒ **+0x1C 就是束长**。
    //    处理: CurLen 为 0 时回退用 TotalLen(见 NcReader.ReadLasers)。
    //
    // ④ ★★ 8 字节歧义**已解开** (2026-10-05 22:00)。认出 ECL 的 ins_85 / ins_86 = 激光指令
    //    (参数3 = 角度, 取值 ±0.7853982=±π/4、±2.3561945=±3π/4, 各 32 次
    //     ⇒ 8 向均布步进 π/4, 与实测 48 槽环形放射阵完全吻合), 用它反推分配函数 0x10D3A:
    //
    //      lea rbx, [rip+0x51b1c7]  →  0x52BF08
    //      池基址                     =  0x52BF00
    //      ⇒ rbx = 条目0 + 0x8   (不是条目0!)
    //      ⇒ 凡是 [rbx+X], 实际条目偏移都是 X + 0x8
    //
    //    换算后**所有字段一次性全部对上**:
    //      cmp byte [rbx+0x274], 0  → 条目+0x27C  ★找空槽判据
    //                                 实测 48 槽 = 1 / 16 槽 = 0, 占用数精确吻合 ✓
    //      movsd [rbx+0x278], xmm0  → 条目+0x280 / +0x284
    //                                 ★★搬的就是起点 X / Y 两个 float
    //                                 (这解释了此前"movsd 一次搬 8 字节会覆盖 +0x284
    //                                  坐标"的矛盾 —— 矛盾不存在, 它搬的正是坐标)
    //      mov    [rbx+0x14]←[rdi+0x1C] → 条目+0x1C  = 112.0  束长 ✓
    //      movss  [rbx+0x260]←[rdi+0x3C] → 条目+0x268  = -2.20…  方向角 ✓
    //      mov    [rbx+4]    ←[rdi+0x10] → 条目+0x0C   = 28.0 ✓
    //      mov word[rbx+0x264]          → 条目+0x26C  = 2 ✓
    //
    // ★★ 2026-10-06 宽度字段: **定位失败, 转为"值域命中扫描"**。
    //
    //  ECL 激光指令 `ins_85(a, b, angle, c, P5, P6, P7, P8, ...)` 全量统计(154 条):
    //
    //      第 5 参数 P5: 64.0 x 128 / 0.0 x 21 / 32.0 x 5
    //      第 8 参数 P8: 24.0 x 136 / 16.0 x 6 / 6.0 x 6 / 32.0 x 3 / 8.0 x 2 / 12.0 x 1
    //
    //  ★ 每一组的 P5 与 P8 都不同 ⇒ "前细后粗"是全部激光的通用设计, 不是个别符卡特例。
    //    主体符卡 (P5=64, P8=24); 最后一关 ecldata7:1183 = (P5=32, P8=24)。
    //
    // ★★★ 推翻: 之前写的 "entry+0x278 = 粗端 / +0x270 = 细端" **不成立**。两条反证:
    //
    //  (1) 分配函数 0x10ECB 里的搬运是
    //          mov  edx,[rdi+0x30] ; mov [rbx+0x270],edx   ⇒ entry+0x278
    //          mov  ecx,[rdi+0x34] ; mov [rbx+0x268],ecx   ⇒ entry+0x270
    //      但同一函数里 `movss xmm6,[rdi+0x3C] → entry+0x268` 才写入**实测能验出的方向角**,
    //      说明 ins_85 的第 3 参数(角度)落在 [rdi+0x3C]。若 P1 在 rdi+0x34, 则参数区
    //      占 rdi+0x34..rdi+0x6C,  P5 应在 rdi+0x44、P8 在 rdi+0x50 ——
    //      **rdi+0x30 / rdi+0x34 落在参数区之前, 是别的运行时结构, 不是 ins_85 参数。**
    //
    //  (2) 实测 entry+0x278 恒 = 60.0, 而 P5 值域 {0,32,64} **不含 60**;
    //      entry+0x270 实测恒 = 0, 而 P8 值域 {6..32} **不含 0**。
    //      ⇒ 这两个偏移都不是宽度。
    //
    //  ⇒ 结论: 不再靠反汇编猜偏移, 改用**运行时值域命中扫描**:
    //     扫条目 +0x260..+0x290 每个 4 字节, 统计哪些偏移的值落在
    //     ECL 宽度值域 {6,8,12,16,24,32,64} 内, 且在多条活跃槽上重复出现。
    //     真宽度字段必然命中 ECL 值域 —— 这个判据比"哪个偏移看起来像"强得多。
    //     诊断按钮『诊断敌人 / 激光池』的 [L6] 段就是干这个的。
    //
    //  —— 在 [L6] 给出结论之前, 宽度一律走**手动滑块**(选项里可切"起点半宽/终点半宽"),
    //     保证用户当下就能把梯形调到与游戏画面一致, 不被字段定位阻塞。

    public const int L_State = 0x00;                 // u8   实测仅 17 槽非 0(值多为 1)
    public const int L_CurLen = 0x18;                // float ★实测恒 0 —— 语义存疑, 不用作长度
    public const int L_TotalLen = 0x1C;              // float ★实测 112.0 = 束长(已验几何自洽)
    public const int L_Angle = 0x268;                // float 弧度 ★实测 8 向均布(步进 π/4) ✓已确认
    public const int L_Flush = 0x27C;                // u8   ★实测恒为 1 = 在用 (48/64 槽)
                                                    //      ★ 由 rbx=条目0+0x8 反推确认
    public const int L_StartX = 0x280;               // float 起点 X   ★实测对称中心 192.0
    public const int L_StartY = 0x284;               // float 起点 Y   ★实测对称中心 112.0
                                                    //      ★ 这两个是 movsd 一次搬的同一对

    // ---- 宽度字段(★★2026-10-06 12:40 由静态分析**定案**, 12.8 的"证伪"已撤销)
    //
    // 来源: 激光条目初始化函数 0x10ECB, 0x10F56..0x10F80:
    //     mov   edx, [rdi+0x30]
    //     test  edx, edx           ← ★ 零判断, 决定要不要做锥形
    //     mov   [rbx+0x270], edx   ⇒ 条目+0x278 = P5 (终点宽 / 粗端)
    //     mov   ecx, [rdi+0x34]
    //     mov   [rbx+0x268], ecx   ⇒ 条目+0x270 = P8 (起点宽 / 细端)
    //     sete  cl                 ← ZF 来自上面那个 test
    //     mov   byte [rbx+0x290], cl
    // (rbx = 条目0 + 0x8, 由 0x10D3A 的 lea rbx,[rip+0x51b1c7] → 0x52BF08 定死)
    //
    // ★ 这段汇编**直接印证**了 ECL 侧的语义(12.11): P5==0 ⇒ 不做锥形 ⇒ 等粗细。
    //   所以 12.8 那句"实测恒 60 / 恒 0, 不在 ECL 值域 → 大概率不是宽度"
    //   **是错的** —— 那些"实测"是在 32 位 thprac 上读到的垃圾数据(见 12.10)。
    //   偏移本身从一开始就是对的, 读错了而已。
    public const int L_WidthA = 0x278;               // float P5 = 终点宽(粗端); ==0 ⇒ 等粗细
    public const int L_WidthB = 0x270;               // float P8 = 起点宽(细端)
    /// <summary>兼容旧名(= L_WidthA)。</summary>
    public const int L_WidthGuess = L_WidthA;

    /// <summary>
    /// "不做锥形"标志(= (P5 == 0))。
    ///
    /// ★★2026-10-06 12:40 **撤销作废判定**。之前写它"越界"是因为把
    ///   [rbx+0x290] 当成条目基址偏移; 但 rbx = 条目0 + 0x8, 所以它实际是
    ///   条目 + 0x290, **并没有越过 stride 0x298**(0x290 < 0x298)。
    ///   偏移是对的, 之前的结论是基址换算错了一步。
    ///
    /// 判读: 1 = P5 为 0 ⇒ 等粗细; 0 = 有锥形。
    /// ★ 但**不要用它当唯一判据** —— 直接看 +0x278 是否为 0 更直接,
    ///   而且这个标志只说明"不做锥形", 不含宽度数值。
    /// </summary>
    public const int L_NoWidth = 0x290;              // u8   1 = P5 为 0(等粗细)

    /// <summary>宽度值域命中扫描的区间(诊断用): [L6] 段扫这个范围。</summary>
    public const int WidthScanBegin = 0x260;
    public const int WidthScanEnd = 0x290;

    /// <summary>激光半宽**兜底**值 —— 手动模式下同时作为起点/终点的初值。</summary>
    public const float LaserHalfWidthDefault = 6.0f;

    // ---- 表 8　道具条目内部偏移 (stride 0x160) ----
    //
    // 这一张表已经不是"文档抄来的"了, 是拿 th06nc.exe 静态反汇编实读确认的
    // (48630a42… 这个构建, .text 里 27 处 lea 直接引用道具池, 4 处生成函数逐字段写值):
    //   mov byte ptr [item], 1            → +0x00 就是"槽在用"标志
    //   cmp byte ptr [item], 0 / je       → 分配槽位时就是找 +0x00 == 0 的空槽
    //   movsd [item+0x10], xmm0           → 坐标 X/Y 在 +0x10 / +0x14
    //   mov   [item+0x18], eax            → 坐标 Z 在 +0x18
    //   mov byte ptr [item+0x34], 6       → 类型是 **1 字节**, 不是 i32
    //   lea rcx, [item+0x38]              → Sprite VM 起点
    //   mov [rcx+0xF8], r8                → VM 脚本指针 (item+0x130)
    //   imul rdi, rax, 0x160  /  cmp eax, 0x400  → stride 0x160, 1024 槽
    public const int I_Alive = 0x00;                 // u8  0 = 空槽  ★反汇编确认
    public const int I_PosX = 0x10;                  // float
    public const int I_PosY = 0x14;
    public const int I_PosZ = 0x18;
    public const int I_Type = 0x34;                  // **u8** ★反汇编确认 (旧表里写成 i32 是错的)
    public const int I_VmPosX = 0x100;               // float  VM 绘制坐标 (0x38+0xC8)
    public const int I_VmPosY = 0x104;
    public const int I_Script = 0x130;               // ptr   VM 脚本指针 (VM+0xF8), 不是占用标志

    /// <summary>
    /// ★★ 2026-10-06 16:05 **定案**: 道具**贴图尺寸**的描述符指针。
    ///
    /// 来源: item_desc_scan.py 全池运行时统计 (1024 槽) ——
    ///   条目+0x138 上有 201 个指针, 逐个跟到目标结构读 +0x1C/+0x20,
    ///   **200 个给出 (16, 16)**, 剩下 1 个读不到。一致性 200/201。
    ///   目标结构 float 视图: +0x14=36 +0x18=36 +0x1C=16 +0x20=16
    ///                        +0x28=1024 +0x2C=1024 (图集尺寸) +0x30..=UV 矩形
    ///   ⇒ +0x1C/+0x20 就是 (w,h), 与**弹幕描述符**的 Nc.D_Width/D_Height 完全同一套偏移。
    ///
    /// ★ 用户要求: 道具直接按**贴图大小**显示, 而且是**正方形** —— 16×16 正好是正方形。
    ///
    /// ⚠ 对照: +0x130 是 VM 脚本指针(已知), 不是描述符; 实测 +0x130/+0x140/+0x148/+0x150
    ///   上**一个指针都没有**, 只有 +0x138 有。别选错。
    /// </summary>
    public const int I_Desc = 0x138;                 // ptr ★道具贴图描述符 (目标 +0x1C/+0x20 = w/h)

    /// <summary>贴图尺寸读不到时的兜底边长(全宽)。实测道具贴图恒为 16×16。</summary>
    public const float ItemTexFallback = 16f;

    // ---- 表 8.1　道具类型号语义 (2026-10-05 反汇编 + ECL 全量统计 双实锤) ----
    // 生成函数 .text 0x43FA0 (真入口; 0x44070 是它内联的中段):
    //   mov byte ptr [rbx+0x34], r8b      → 类型号 r8b 原样写入, 无任何变换
    //   mov eax, 0x215                    → 贴图 id 基址 533
    //   add eax, r8d                      → 贴图 id = 533 + 类型号
    //   mov r8, [r9 + rax*8 + 0x24138]    → 再用类型号索引 ANM 脚本指针表 (运行时构建)
    //
    // ★类型号的**来源有两路** (0x39600 段):
    //   (A) 0x39613  movsx r8d, byte [rbx+0x10a8]   ← 敌人模板字段, 由 ECL 指定
    //   (B) 0x39683  movzx r8d, byte [rax+0x34e0a8] ← .rdata 轮转表(值域仅 {0,1,2})
    //   +0x10A8 的写入点只有 4 个:
    //       0x37B27  = al   ← al 来自 [rsp+0x60], 即 ECL "生成敌机" 指令的**第 6 个参数**
    //       0x37CC5  = 0xFE (-2)   0x382C8 = 0xFF (-1)   0x386DF = 0xFF (-1)
    //   负数 = "此敌机不掉落道具"。与 ECL 里 -1(920处) / -2(14处) 精确对应。
    //
    // ★★ ECL 全量统计 (2213 条 ins_0/ins_2/ins_4 生成指令, 参数#6 的值域):
    //        0 : 434 处   得分
    //        1 : 718 处   小 P
    //        2 : 126 处   大 P   ★全部集中在 ecldata4(第4关), 血量 1200~4000 的高血量精英
    //       -1 : 920 处   不掉落
    //       -2 :  14 处   不掉落(特殊)
    //        5 :   1 处   命 / 1UP  (全作仅 1 条, 极稀有)
    //   另有两条硬编码路径: 0x6A3D4 写死 4(自机死亡撒道具), 0xF630 写死 6(弹幕消除转道具)
    public const int ItemTypePoint = 0;    // 得分 (红)
    public const int ItemTypePower = 1;    // 小 P (蓝)
    public const int ItemTypeBigPower = 2; // 大 P (绿) ★与 1 必须分开显示
    public const int ItemTypeDeath = 4;    // 死亡掉落 (灰白) —— 0x6A3D4 写死
    public const int ItemTypeLife = 5;     // 命 / 1UP (紫) —— ECL 全作仅 1 条
    public const int ItemTypeBomb = 6;     // 弹幕消除转道具 (金) —— 0xF630 写死

    // ---- 表 11　判定公式　★2026-10-05 全链路反汇编实锤 ----
    // 全部由 th06nc.exe (48630a42…) 的碰撞函数逐条读出, 详见地址表表 11。
    //
    // 中弹致死 (敌弹 -> 自机, 函数 0x6C090 的 r9b==0 分支):
    //   弹盒 = [x ∓ w*0.5, y ∓ h*0.5]      (0x6C09E / 0x6C0D9 都乘 0.5)
    //   把自机点裁剪进弹盒, 再比 dist² <= 自机半径²   (0x6C166 / 0x6C1A4)
    //   —— 等价于"弹是 w×h 的盒子, 自机是半径 1.25 的点"
    //
    // 致死 (自机弹 -> 敌, 同函数 r9b!=0 分支):
    //   dist <= min(w,h) * 0.5 + 自机半径        (0x6C122 minss / 0x6C139 ×0.5 / 0x6C145 addss)
    //
    // 擦弹 (函数 0x6C2B0, 写计数器 0x549D20 在 0x6C576):
    //   函数参数: rdx = 弹坐标结构, r8 = 弹尺寸结构(宽高在 [0]/[+4]),
    //             r9 = 自机盒参数, [rsp+0xF8] = "宽松擦弹"开关
    //   ① 先由 [r9] + 全局 0x551720/24 + sin/cos 旋转, 算出**自机盒**边界 (xmm3/xmm2)
    //   ② 弹盒 = [弹x ∓ size*0.5, 弹y ∓ size*0.5]                (0x6C36A 的 0.5)
    //   ③ 阶段1(精确): 弹盒 ∩ 自机盒 裁剪后 dist² <= 阈值² (阈值 = 全局 0x55173C)
    //        命中 -> 生成特效(0x6C428/0x6C447), **不**计擦弹
    //   ④ 阶段2(宽松): 上面未命中时, **弹盒四边各向外扩 GrazeExtra**(0x6C4EF~0x6C4FC),
    //        再与自机盒裁剪判 dist² <= 阈值²
    //        命中 -> 写 0x549D1C(本面) 与 0x549D20(总) 擦弹计数器  ★这才是"擦弹"
    //   —— 扩边加在**弹盒**上, 不是自机盒; 且 **与弹尺寸 w 无关**
    //      (旧文档 "w*0.5 + 20" 两边都错: 系数用途错、常量 20 也错)
    //
    // 前置: 弹 state == 1
    public const float FactorHalf = 0.5f;     // ★0.5 实锤: .rdata 0x352A44, 被 4 处引用
    public const float GrazeExtra = 48.0f;    // ★擦弹扩边量实锤: .rdata 0x352C20, 加在弹盒上 (旧值 20.0 是错的)

    // ---- 场地 (局部坐标, 原点 0,0) ----
    public const float FieldW = 384f;
    public const float FieldH = 448f;

    // 读不到描述符尺寸时的兜底半径 (画成空心灰圈, 与"读到尺寸"的实心红圆区分)
    public const float FallbackRadius = 6.0f;

    public const string GameExe = "th06nc.exe";
}
