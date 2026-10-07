using System.Runtime.InteropServices;

namespace TH06NCTools;

internal static class Program
{
    [DllImport("winmm.dll")]
    private static extern int timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern int timeEndPeriod(uint period);

    [STAThread]
    private static void Main()
    {
        // 系统定时器分辨率默认 ~15.6ms, 会把 Thread.Sleep 的精度拖到 30-40fps;
        // 提到 1ms, 限帧模式才能真正跑满目标帧率
        timeBeginPeriod(1);
        try
        {
            ApplicationConfiguration.Initialize();

            var form = new MainForm();
            form.Show();
            while (!form.IsDisposed)
            {
                Application.DoEvents();

                if (form.WindowState == FormWindowState.Minimized)
                {
                    Thread.Sleep(50);
                    continue;
                }

                form.RenderFrame();
            }
        }
        finally
        {
            timeEndPeriod(1);
        }
    }
}
