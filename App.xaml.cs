using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace XenoStudio
{
    /// <summary>
    /// One XenoStudio per install folder. A second launch hands its command line (e.g. --send "text") to the
    /// running window over a named pipe and exits, so double-clicking the exe just brings the app to the front.
    /// </summary>
    public partial class App : Application
    {
        // keyed on the install folder, so a separate copy (XenoDuo, a test build) can still run alongside
        static readonly string Key = "XenoStudio-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToLowerInvariant())))[..16];

        Mutex single;

        protected override void OnStartup(StartupEventArgs e)
        {
            // XenoStudio.exe --setup-selftest out.json [mcpId…]: runs the setup scan (and test-starts the named catalog MCP servers),
            // writes the result and exits. Read-only: installs nothing, opens no window.
            int st = Array.IndexOf(e.Args, "--setup-selftest");
            if (st >= 0 && st + 1 < e.Args.Length)
            {
                var outFile = e.Args[st + 1];
                var mcpIds = e.Args.Skip(st + 2).ToList();
                var result = Task.Run(async () =>
                {
                    var s = new Settings();
                    var items = await Setup.Scan(s, new ClaudeAgent().ResolveExe(s), new CodexAgent().ResolveExe(s));
                    var mcp = new System.Collections.Generic.List<object>();
                    foreach (var c in Setup.Catalog(s).Where(c => mcpIds.Contains(c.Id)))
                        try { mcp.Add(new { c.Id, tools = await Setup.TestMcp(c.Make(), _ => { }) }); }
                        catch (Exception ex) { mcp.Add(new { c.Id, error = ex.Message }); }
                    return new { items, mcp, winget = Setup.HasWinget };
                }).GetAwaiter().GetResult();
                File.WriteAllText(outFile, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                Shutdown();
                return;
            }

            single = new Mutex(true, @"Local\" + Key, out bool first);
            if (!first)
            {
                HandOff(e.Args);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, ex) =>
            {
                CrashLog.Write("ui", ex.Exception);
                MessageBox.Show("XenoStudio hit an unexpected error and kept running.\n\n" + ex.Exception.Message +
                                "\n\nDetails: " + CrashLog.File, "XenoStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                ex.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (_, ex) => { CrashLog.Write("task", ex.Exception); ex.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => CrashLog.Write("fatal", ex.ExceptionObject as Exception);

            base.OnStartup(e);
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            _ = Listen(win);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { single?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        static void HandOff(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Key, PipeDirection.Out);
                pipe.Connect(3000);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args));
                pipe.Write(bytes, 0, bytes.Length);
            }
            catch { /* the running window may be closing; nothing useful to do */ }
        }

        static async Task Listen(MainWindow win)
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Key, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync();
                    using var ms = new MemoryStream();
                    await pipe.CopyToAsync(ms);
                    var args = JsonSerializer.Deserialize<string[]>(ms.ToArray()) ?? Array.Empty<string>();
                    await win.Dispatcher.InvokeAsync(() => win.OnSecondLaunch(args));
                }
                catch (Exception ex) { CrashLog.Write("pipe", ex); await Task.Delay(1000); }
            }
        }
    }

    public static class CrashLog
    {
        public static readonly string File = Path.Combine(Paths.Data, "crash.log");

        public static void Write(string where, Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Paths.Data);
                var fi = new FileInfo(File);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();   // keep it from growing forever
                System.IO.File.AppendAllText(File, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\n\n");
            }
            catch { }
        }
    }
}
