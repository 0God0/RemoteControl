using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlClient {
    /// <summary>
    /// 启动并维护一个持久化 cmd.exe 会话，
    /// 允许多次发送命令并获取其标准输出。
    /// </summary>
    public sealed class CommandSession : IDisposable {
        private Common.ContextExceptionCallback onError;
        private readonly Process shellProcess;
        private readonly object syncRoot = new();

        /// <param name="shellExe">要启动的外壳程序，默认 cmd.exe</param>
        /// <param name="shellArgs">外壳参数，默认 /K（启动后保持运行）</param>
        public CommandSession(Common.ContextExceptionCallback onErr, string shellExe = "cmd.exe", string shellArgs = "/K") {
            onError = onErr ?? ((t, e) => Debug.WriteLine(t + e.Message));
            try {
                var info = new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = "/u /k",        // /u = Unicode 输出，/k = 保持会话
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode,
                    StandardErrorEncoding = Encoding.Unicode

                };

                shellProcess = new Process { StartInfo = info };
                shellProcess.Start();
            } catch (Exception e) { onError.Invoke("[Error] CommandSession.Init ", e); }

        }

        /// <summary>
        /// 向持久化 shell 发送一条命令并同步等待结果。
        /// </summary>
        /// <param name="command">Shell 内要执行的指令文本</param>
        /// <param name="timeoutMillis">超时毫秒数，0 表示不限制</param>
        /// <returns>标准输出（末尾自动换行）</returns>
        public string Execute(string command, int timeoutMillis = 30000) {
            var output = new StringBuilder();
            var watch = Stopwatch.StartNew();
            try {
                lock (syncRoot) {
                    var sentinel = $"CMDDONE{Guid.NewGuid():N}";

                    // 写入命令与结束标记
                    shellProcess.StandardInput.WriteLine(command);
                    shellProcess.StandardInput.WriteLine($"echo {sentinel}");
                    shellProcess.StandardInput.Flush();

                    while (true) {
                        if (timeoutMillis > 0 && watch.ElapsedMilliseconds > timeoutMillis)
                            throw new TimeoutException($"Command timeout after {timeoutMillis} ms.");

                        var line = shellProcess.StandardOutput.ReadLine();
                        if (line == null)
                            throw new InvalidOperationException("Shell terminated unexpectedly.");
                        if (line.Equals(sentinel, StringComparison.Ordinal))
                            break;

                        output.AppendLine(line);
                    }
                }
            } catch (Exception e) { onError.Invoke("[Error] CommandSession.Execute ", e); }

            return output.ToString();

        }

        /// <summary>
        /// 关闭 shell。多次调用安全。
        /// </summary>
        public void Close() {
            try {
                if (shellProcess.HasExited) return;

                // 让 cmd 正常退出；若两秒内未退出则强杀
                shellProcess.StandardInput.WriteLine("exit");
                if (!shellProcess.WaitForExit(2000))
                    shellProcess.Kill(true);
            } catch (Exception e) { onError.Invoke("[Error] CommandSession.Close ", e); }
        }

        public void Dispose() {
            Close();
            shellProcess.Dispose();
        }
    }

}
