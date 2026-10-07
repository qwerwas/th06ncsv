using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TH06NCTools;

/// <summary>
/// 附加到 th06nc.exe (x64) 并读写其内存。
///
/// 与 2002 原版最大的差别: 新典是 PE32+ 且开了 ASLR —— 基址每次启动都不同,
/// 必须先拿到**主模块基址**, 再用 基址 + RVA 访问。
///
/// 取基址走两级回退, 因为只靠一种方法会在某些机器上直接找不到游戏:
///   1) Toolhelp32 快照 (顺带拿到模块名与映像大小);
///   2) 读目标进程 PEB 的 ImageBaseAddress —— 只要 OpenProcess 成功就一定拿得到,
///      Toolhelp 被权限/杀软挡住时靠它兜底。
/// 全程不碰 Process.Modules: 枚举模块列表在部分进程上会长时间阻塞, 曾把附加卡死。
///
/// 默认只读。写权限顺手申请, 申请不到就退回只读 (游戏以管理员运行而本工具不是时的典型表现)。
/// </summary>
internal sealed class NcMemory
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, IntPtr dwSize, out IntPtr lpNumberOfBytesRead);

    /// <summary>同上, 但能指定往数组的第 offset 个字节开始写 —— 分块读大池时用。
    /// 另一个重载的 lpBuffer 只能从数组头部开始, 无法读子区间。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemoryAt(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, int offset, IntPtr dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, int th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr hProcess, int infoClass,
        out PROCESS_BASIC_INFORMATION pbi, int size, out int returned);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private IntPtr _hProcess = IntPtr.Zero;

    /// <summary>
    /// 只有**完整走完附加流程**才置位。
    /// 不能直接用 "_hProcess != 0" 当 Attached: TryAttach 一上来就要 OpenProcess 拿句柄,
    /// 之后还有 Toolhelp 快照 / PEB / PE 校验 / 文件哈希, 中间要几百毫秒 ——
    /// 这期间 UI 线程采样到句柄已存在就会显示"已连接", 下一帧校验失败又变"未连接",
    /// 表现就是先开工具再开游戏时"已连接/未连接"疯狂跳。
    /// </summary>
    private bool _ready;

    // ---- 对外状态 ----
    public bool Attached => _ready && _hProcess != IntPtr.Zero;
    public bool CanWrite { get; private set; }
    public ulong Base { get; private set; }
    public int ProcessId { get; private set; }
    public string ProcessName { get; private set; } = "";
    public uint ImageSize { get; private set; }
    public uint TimeDateStamp { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string AttachStatus { get; private set; } = "未连接";
    public string VersionWarning { get; private set; } = "";

    /// <summary>主模块在磁盘上的完整路径 —— 版本指纹要拿文件算, 不能拿内存映像算。</summary>
    public string ExePath { get; private set; } = "";

    /// <summary>0 = 指纹匹配; 1 = 提醒 (哈希不同但结构多半没变 / 无法校验); 2 = 严重 (映像大小都对不上)。</summary>
    public int VersionWarnLevel { get; private set; }

    /// <summary>附加过程诊断 / 提示, 显示在右侧信息面板 —— 找不到游戏时看这里就知道卡在哪一步。</summary>
    public string Hint { get; private set; } = "";

    /// <summary>最近一次读取失败的原因 (地址 + Win32 错误码), 显示在右栏 —— 读不到数据时看这里。</summary>
    public string LastError { get; private set; } = "";

    /// <summary>目标进程还活着吗 (STILL_ACTIVE = 259)。用来区分"进程没了"和"暂时读不动"。</summary>
    public bool IsProcessAlive()
    {
        if (_hProcess == IntPtr.Zero) return false;
        return GetExitCodeProcess(_hProcess, out uint code) && code == 259;
    }

    /// <summary>把这个进程拉黑若干毫秒。用于"连上了但一直读不动"的情况 —— 多半是
    /// 抓到了错误的基址 (游戏还在初始化时的 PEB 首选基址), 冷却一下再拿新基址重试。</summary>
    public void Cooldown(int ms)
    {
        if (ProcessId != 0) _skipUntil[ProcessId] = Environment.TickCount64 + ms;
    }

    public void Detach()
    {
        _ready = false;
        if (_hProcess != IntPtr.Zero)
        {
            CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }
        // ★ 换进程了, 坏页记录必须作废 —— 否则新进程里那页明明可读却被跳过
        _skipPages.Clear();
        _badPageStreak.Clear();
        LastSkippedPages = 0;
        CanWrite = false;
        Base = 0;
        ProcessId = 0;
        ProcessName = "";
        Sha256 = "";
        ImageSize = 0;
        TimeDateStamp = 0;
        AttachStatus = "未连接";
        VersionWarning = "";
        VersionWarnLevel = 0;
        ExePath = "";
    }

    public string DigestShort => Sha256.Length >= 8 ? Sha256[..8] : "?";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags,
        System.Text.StringBuilder lpExeName, ref int lpdwSize);

    // ---------------- 附加 ----------------

    /// <summary>找 th06nc 进程并附加。成功返回 true; 失败时 <see cref="Hint"/> 里是逐步诊断。</summary>
    public bool TryAttach()
    {
        var diag = new StringBuilder();
        // 注意: 这里**不要**先把 AttachStatus 改成"正在寻找…"。TryAttach 每次重试都会跑一遍,
        // 而枚举进程要几百毫秒 —— UI 以 10Hz 采样就会在"正在寻找"和"等待游戏进程"之间来回跳,
        // 表现就是未连接时界面一闪一闪。中间过程只记在 diag 里, 结尾统一写状态。
        int candidates = 0;

        try
        {
            // 先把候选进程收集起来、按优先级排序, 再依次尝试。
            // 为什么必须排序: Steam 启动器 / 前置 splash 进程同样可能是合法 PE32+
            // (能过 PE 头校验、映像也够大), 但地址表对它完全不适用 ——
            // 枚举顺序先撞上它就会一直附在错进程上反复失败,
            // 表现正是"已连接 / 未连接"无限抖, 而且永远读不到游戏。
            var list = new List<Process>();
            int myPid = Process.GetCurrentProcess().Id;
            string myName = Process.GetCurrentProcess().ProcessName;
            foreach (var p in Process.GetProcesses())
            {
                string nm;
                try { nm = p.ProcessName; }
                catch (Exception) { p.Dispose(); continue; }

                // 先把自己和同名进程 (本工具的另一个实例) 踢出去:
                // 工具叫 TH06NCSV.exe, 进程名里恰好带 "th06nc", 模糊匹配会把自己当成游戏
                if (p.Id == myPid || nm.Equals(myName, StringComparison.OrdinalIgnoreCase))
                {
                    p.Dispose();
                    continue;
                }

                if (!IsCandidate(nm)) { p.Dispose(); continue; }
                list.Add(p);
            }
            candidates = list.Count;
            list.Sort((a, b) => Rank(b).CompareTo(Rank(a)));


            foreach (var p in list)
            {
                try
                {
                    string name = p.ProcessName;

                    // 刚被创建出来的进程还没初始化完: Toolhelp 快照可能还列不出模块,
                    // PEB.ImageBaseAddress 也可能还停在 PE 头里的"首选基址"而不是 ASLR 后的真实基址。
                    // 这时候附加上去拿到的 Base 是错的 —— 先放它一马, 下次扫描再来。
                    if (_skipUntil.TryGetValue(p.Id, out long skipTo) && Environment.TickCount64 < skipTo)
                    {
                        diag.Append($"{name}({p.Id}): 冷却中; ");
                        continue;
                    }
                    if (!IsReady(p))
                    {
                        diag.Append($"{name}({p.Id}): 刚启动, 等它初始化; ");
                        continue;
                    }

                    IntPtr h = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION
                                        | PROCESS_QUERY_INFORMATION, false, p.Id);
                    bool canWrite = h != IntPtr.Zero;
                    if (h == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
                        if (h == IntPtr.Zero)
                        {
                            int err2 = Marshal.GetLastWin32Error();
                            diag.Append($"{name}({p.Id}): 打开进程失败 err={err2}" +
                                        (err2 == 5 ? " (权限不足, 请用管理员运行本工具)" : "") + "; ");
                            continue;
                        }
                        diag.Append($"{name}({p.Id}): 只读, 无写权限; ");
                    }

                    // 暂时接管句柄, 后面所有 Read 都用它
                    _hProcess = h;

                    ulong baseAddr = 0;
                    uint modSize = 0;
                    string modName = "";
                    string exePath = "";
                    if (TryGetMainModule(p.Id, out baseAddr, out modSize, out modName, out exePath))
                        diag.Append($"{name}({p.Id}): 模块 {modName}; ");
                    else
                        diag.Append($"{name}({p.Id}): Toolhelp 取模块失败; ");

                    if (baseAddr == 0)
                    {
                        baseAddr = GetImageBaseViaPeb();
                        diag.Append(baseAddr != 0 ? "基址来自 PEB; " : "PEB 取基址也失败; ");
                    }

                    // Toolhelp 没给路径 (或走了 PEB 路线) 时, 用句柄直接问进程要
                    if (exePath.Length == 0) exePath = GetProcessPath();

                    if (baseAddr == 0)
                    {
                        Detach();
                        continue;
                    }

                    Base = baseAddr;
                    ImageSize = modSize;
                    ProcessId = p.Id;
                    ProcessName = modName.Length > 0 ? modName : name + ".exe";
                    ExePath = exePath;
                    CanWrite = canWrite;

                    if (!VerifyPeHeader())
                    {
                        diag.Append($"PE 头校验失败 (基址 0x{baseAddr:X}); ");
                        Detach();
                        continue;
                    }

                    // 基址对了不代表游戏能用: 再探两个已知地址, 读不动就说明这个进程
                    // 还没准备好 (或者根本不是游戏本体) —— 冷却 1.2 秒后重试, 别当场判"已连接"
                    if (!ProbeData())
                    {
                        diag.Append($"{name}({p.Id}): 数据探针失败 ({LastError}); ");
                        _skipUntil[p.Id] = Environment.TickCount64 + 1200;
                        Detach();
                        continue;
                    }

                    ComputeFileSha256();

                    // 分级提示: 只有映像大小都对不上才是真错位; 大小一致而哈希不同,
                    // 通常只是 Steam 又推了个小更新 (结构布局没变, 功能正常就是证据)
                    bool sizeOk = ImageSize == Nc.KnownImageSize;
                    bool shaOk = Sha256.Length > 0
                                && Sha256.Equals(Nc.KnownSha256, StringComparison.OrdinalIgnoreCase);

                    if (shaOk && sizeOk)
                    {
                        VersionWarnLevel = 0;
                        VersionWarning = "";
                        Hint = "";
                    }
                    else if (Sha256.Length == 0)
                    {
                        VersionWarnLevel = 1;
                        VersionWarning = "拿不到 exe 文件路径, 无法校验版本指纹";
                        Hint = "";
                    }
                    else if (sizeOk)
                    {
                        VersionWarnLevel = 1;
                        VersionWarning = $"映像大小一致但文件哈希不同 ({DigestShort}) —— 多半是 Steam 又小更了一次";
                        Hint = "功能与判定都正常的话, 说明结构布局没变, 可以照常用";
                    }
                    else
                    {
                        //★★ 2026-10-06 还原为 **只提示不拦截**。
                        //   (上一版曾改成 level 3 硬拦截 + Detach, 结果把thprac 下
                        //    明明显示正常的练习用法也一并挡掉了 —— 过度反应。)
                        //
                        //   事实是: thprac 虽然是另一个构建(32 位重编版), 但实测
                        //   位置/角度/束长这些字段读出来是能对上的, 画面显示正常。
                        //   所以这里只把话说清楚: **显示可以参考, 但不要拿它抓数据**
                        //   (宽度等未定位字段在别的构建上会是垃圾, 例如读出 6.3e-05)。
                        VersionWarnLevel = 2;
                        VersionWarning =
                            $"映像大小 0x{ImageSize:X} 与已知 0x{Nc.KnownImageSize:X} 不符 "
                          + "—— 这是另一个构建(thprac 等), 结构布局可能不同";
                        Hint = "★ 显示可以看, 但**别拿这个进程的数据去推断字段含义**: "
                            + "未定位的字段(激光宽度等)在别的构建上读出来是垃圾值。"
                            + "要抓数据请用 Steam 版 th06nc.exe。";
                    }
                    // 全部校验走完才真的算"已连接" —— 中途任何一步失败都不该让 UI 显示已连接
                    _ready = true;
                    LastError = "";
                    AttachStatus = $"已连接: {ProcessName} (PID {p.Id})" + (canWrite ? "" : " [只读]");
                    return true;
                }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            AttachStatus = "查找进程出错: " + ex.Message;
            Hint = AttachStatus;
            return false;
        }

        Hint = candidates == 0
            ? "没找到名字像 th06nc 的进程。先启动游戏, 再点【选项 → 重新附加进程】。"
            : "找到候选进程但附加失败: " + diag.ToString().TrimEnd(' ', ';');
        AttachStatus = $"等待游戏进程… (请启动 {Nc.GameExe})";
        return false;
    }

    /// <summary>附加失败的 PID → 冷却到什么时候 (TickCount64 ms), 期间不再重复试。</summary>
    private static readonly Dictionary<int, long> _skipUntil = new();

    /// <summary>
    /// 进程是否"已经初始化得差不多"。判据: 已有主窗口, 或者已存活 ≥ 1.2 秒。
    /// 拿不到启动时间时不拦 (宁可错试, 也不能永远不试)。
    /// </summary>
    private static bool IsReady(Process p)
    {
        try
        {
            if (p.MainWindowHandle != IntPtr.Zero) return true;
            double ageMs = (DateTime.Now - p.StartTime).TotalMilliseconds;
            if (ageMs < 1200) return false;
        }
        catch (Exception)
        {
            return true;
        }
        return true;
    }

    /// <summary>候选优先级: 名字就是 th06nc 的排最前, 其次名字里含 th06nc, 其余模糊匹配垫底。</summary>
    private static int Rank(Process p)
    {
        try
        {
            string n = p.ProcessName;
            if (n.Equals("th06nc", StringComparison.OrdinalIgnoreCase)) return 2;
            if (n.Contains("th06nc", StringComparison.OrdinalIgnoreCase)) return 1;
        }
        catch (Exception)
        {
            return 0;
        }
        return 0;
    }

/// <summary>
    /// 进程名是否像东方红魔乡新典。
    ///
    /// ★ 2026-10-06 还原为模糊匹配 `Contains("th06nc")`。
    ///   （中间试过精确白名单，想挡掉 thprac —— 但thprac 下画面显示是正常的，
    ///   精确白名单反而把练习用法一起挡了，属于过度反应。）
    ///
    ///   注意: 工具自己叫 `TH06NCSV.exe`, 也含 "th06nc"。
    ///   所以**匹配前必须先按进程名/PID 排除自己**（见 TryAttach 里的那段），
    ///   那一半是真实 bug 的修复，必须保留。
    /// </summary>
    private static bool IsCandidate(string name)
    {
        if (GameExeNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return name.Contains("th06nc", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>已知的游戏主模块文件名（精确匹配优先于模糊匹配）。</summary>
    private static readonly string[] GameExeNames =
        { "th06nc.exe", "th06.exe", "touhou6.exe", "touhou6_.exe" };

    // ---------------- 取主模块基址 ----------------

    private static bool TryGetMainModule(int pid, out ulong baseAddr, out uint size,
                                        out string name, out string exePath)
    {
        baseAddr = 0; size = 0; name = ""; exePath = "";
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snap == IntPtr.Zero || snap.ToInt64() == -1) return false;

        try
        {
            var me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
            if (!Module32First(snap, ref me)) return false;

            do
            {
                string mod = me.szModule ?? "";
                if (mod.Length == 0 || me.modBaseAddr == IntPtr.Zero) continue;
                baseAddr = (ulong)me.modBaseAddr.ToInt64();
                size = me.modBaseSize;
                name = mod;
                exePath = me.szExePath ?? "";
                return true;
            }
            while (Module32Next(snap, ref me));
        }
        finally
        {
            CloseHandle(snap);
        }
        return false;
    }

    /// <summary>兜底路线: 读目标进程 PEB 的 ImageBaseAddress (x64: PEB+0x10)。
    /// 只要 OpenProcess 成功就能读到, 不受 Toolhelp 的枚举权限限制。</summary>
    private ulong GetImageBaseViaPeb()
    {
        try
        {
            int status = NtQueryInformationProcess(_hProcess, 0, out var pbi,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return 0;

            byte[] buf = new byte[8];
            if (!ReadProcessMemory(_hProcess, pbi.PebBaseAddress + 0x10, buf, (IntPtr)8, out IntPtr n)
                || n.ToInt64() != 8) return 0;
            return BitConverter.ToUInt64(buf, 0);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ---------------- 版本校验 ----------------

    private readonly byte[] _hdr = new byte[0x400];

    private bool VerifyPeHeader()
    {
        if (!Read(Base, _hdr, _hdr.Length)) return false;
        if (_hdr[0] != 'M' || _hdr[1] != 'Z') return false;

        int pe = BitConverter.ToInt32(_hdr, 0x3C);
        if (pe < 0x40 || pe + 0x180 > _hdr.Length) return false;
        if (_hdr[pe] != 'P' || _hdr[pe + 1] != 'E' || _hdr[pe + 2] != 0 || _hdr[pe + 3] != 0) return false;

        int opt = pe + 0x18;
        ushort magic = BitConverter.ToUInt16(_hdr, opt);
        if (magic != 0x20B) return false;   // 必须是 PE32+ (x64)

        TimeDateStamp = BitConverter.ToUInt32(_hdr, pe + 8);
        ImageSize = BitConverter.ToUInt32(_hdr, opt + 0x38);   // 以 PE 头为准, 比 Toolhelp 的更可信
        return true;
    }

    private readonly byte[] _probe = new byte[8];

    /// <summary>
    /// 拿两个已知地址试读一下: 擦弹计数 (表 2) 与自机结构首部。
    /// PE 头能对上只说明"这是个 PE32+ 映像", 不代表地址表适用 —— 游戏还在初始化时
    /// 这些 .data 页可能没提交, 读会失败; 探一次就能把"还没准备好"和"基址错了"分开。
    /// </summary>
    private bool ProbeData()
    {
        return Read(Base + Nc.RVA_Graze, _probe, 4)
            && Read(Base + Nc.RVA_Player, _probe, 8);
    }

    /// <summary>用句柄直接问进程要主模块路径 (Toolhelp 拿不到时的兜底)。</summary>
    private string GetProcessPath()
    {
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(_hProcess, 0, sb, ref size) ? sb.ToString(0, size) : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// 版本指纹必须拿**磁盘上的 exe 文件**算, 不能拿进程内存映像算:
    /// 内存里的 .data 已被游戏改写、IAT 已填充、还有重定位 —— 跟文件哈希必然不一样,
    /// 拿内存比对的结果永远是"版本对不上"。地址表里的 SHA256 是文件哈希。
    /// </summary>
    private void ComputeFileSha256()
    {
        Sha256 = "";
        if (ExePath.Length == 0 || !File.Exists(ExePath)) return;

        try
        {
            using var fs = new FileStream(ExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();
            Sha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch (Exception)
        {
            Sha256 = "";
        }
    }

    // ---------------- 读写 ----------------

    public bool Read(ulong addr, byte[] buffer, int count)
    {
        if (_hProcess == IntPtr.Zero || addr == 0) { LastError = "句柄/地址为空"; return false; }
        if (count > buffer.Length) { LastError = "缓冲区太小"; return false; }

        if (ReadProcessMemory(_hProcess, (IntPtr)addr, buffer, (IntPtr)count, out IntPtr n)
            && n.ToInt64() == count)
        {
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        LastError = $"读 0x{addr:X}+{count} 失败 err={err}" +
                    (err == 299 ? " (只读出一部分, 内存页未提交)" :
                    err == 5 ? " (拒绝访问)" :
                    err == 6 ? " (句柄失效, 进程可能已退出)" : "");
        return false;
    }

    /// <summary>
    /// 分块读取, **允许部分失败**。
    /// 大池 (敌弹 640×0x620=396KB、道具 1024×0x160=256KB) 往往横跨很多页, 只要末尾
    /// 有一页还没提交, ReadProcessMemory 就整块失败 (err=299 部分成功)。而实际有用的
    /// 条目全在前面 —— 所以这里按 4KB 页逐块读, 失败的页清零后继续, 只要有一页读回来
    /// 就返回 true。没这个方法的话, 池尾一页没提交就会让整类物件显示不出来。
    /// </summary>
    /// <summary>连续失败多少次才把该页标记为"永久读不出来"并跳过。
    /// 30 次 ≈ 0.5 秒(按 60Hz 轮询) —— 足够区分"偶发抖动"与"页根本没提交"。</summary>
    private const int BadPageSkipThreshold = 30;

    /// <summary>已被判定读不出来的页(页基址)。重连时清空, 见 Detach。</summary>
    private readonly HashSet<ulong> _skipPages = new();

    /// <summary>每页当前的连续失败次数(成功即清零)。</summary>
    private readonly Dictionary<ulong, int> _badPageStreak = new();

    /// <summary>最近一次 ReadChunked 跳过了多少页(诊断用; 恒 >0 说明尾部有未提交页)。</summary>
    public int LastSkippedPages { get; private set; }

    public bool ReadChunked(ulong addr, byte[] buffer, int count, out int bytesRead)
    {
        bytesRead = 0;
        if (_hProcess == IntPtr.Zero || addr == 0) { LastError = "句柄/地址为空"; return false; }
        if (count > buffer.Length) { LastError = "缓冲区太小"; return false; }

        // 整块先试一次 —— 绝大多数情况一次就成, 不走分块路径
        if (ReadProcessMemory(_hProcess, (IntPtr)addr, buffer, (IntPtr)count, out IntPtr n)
            && n.ToInt64() == count)
        {
            bytesRead = count;
            return true;
        }

        const int Page = 0x1000;
        int got = 0;
        int lastErr = 0;
        int skipped = 0;

        for (int off = 0; off < count; off += Page)
        {
            int len = Math.Min(Page, count - off);
            // ReadProcessMemoryAt(hProcess, addr+off, buffer, off, len, &read)
            IntPtr srcAddr = (IntPtr)(addr + (ulong)off);
            ulong pg = (addr + (ulong)off) & ~(ulong)(Page - 1);

            // ★ 2026-10-06 14:50 已知读不出来的页**不再每帧重试**。
            //   敌人池 1MB 跨 268 页, 弹幕 396KB 跨 97 页 —— 只要尾部有未提交的页,
            //   整块读就永远失败, 于是每帧都要走 268+97 次 ReadProcessMemory,
            //   白白把轮询频率拖下去(表现就是"画面对不上/没及时刷新")。
            //   ⚠ 必须**连续**失败到阈值才跳过: 偶发一次失败就永久跳过的话,
            //   那页后来真的提交了也读不回来了, 会永久丢数据。
            if (_skipPages.Contains(pg))
            {
                Array.Clear(buffer, off, len);
                skipped++;
                continue;
            }

            if (ReadProcessMemoryAt(_hProcess, srcAddr, buffer, off, (IntPtr)len, out IntPtr got1)
                && got1.ToInt64() == len)
            {
                got += len;
                _badPageStreak.Remove(pg);
            }
            else
            {
                lastErr = Marshal.GetLastWin32Error();
                // 读不出来的页清零, 免得残留上一帧的旧数据被当成"这一帧的道具"
                Array.Clear(buffer, off, len);

                int streak = _badPageStreak.TryGetValue(pg, out int s) ? s + 1 : 1;
                _badPageStreak[pg] = streak;
                if (streak >= BadPageSkipThreshold) _skipPages.Add(pg);
            }
        }

        LastSkippedPages = skipped;

        if (got > 0)
        {
            bytesRead = got;
            LastError = $"部分成功: {got}/{count} 字节 (err={lastErr})";
            return true;
        }

        LastError = $"分块读 0x{addr:X}+{count} 全失败 err={lastErr}";
        return false;
    }

    public bool Write(ulong addr, byte[] buffer, int count)
    {
        if (_hProcess == IntPtr.Zero || addr == 0 || !CanWrite) return false;
        return WriteProcessMemory(_hProcess, (IntPtr)addr, buffer, (IntPtr)count, out IntPtr n)
            && n.ToInt64() == count;
    }
}
