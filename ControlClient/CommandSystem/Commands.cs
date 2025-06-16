using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlClient.CommandSystem {

    /// <summary>
    /// cd 命令：切换工作目录，支持 "cd D:" 形式切换盘符。
    /// </summary>
    public sealed class CdCommand : ICommand {
        public string Name => "cd";
        public string Description => "切换工作目录 (cd [/d] <目录>)";

        public Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) {
            // 无参数 → 输出当前目录
            if (args.Length == 0)
                return Task.FromResult<IEnumerable<string>>(new[] { ctx.CurrentDirectory });

            // 兼容 Windows CMD 的 "/d" 参数
            string pathArg = args.Length == 2 && args[0].Equals("/d", StringComparison.OrdinalIgnoreCase)
                ? args[1]
                : args.Length == 1
                    ? args[0]
                    : throw new ArgumentException("用法: cd [/d] <目录>");

            // 单斜杠或反斜杠 → 当前驱动器根目录
            if (pathArg is "\\" or "/")
                pathArg = Path.GetPathRoot(ctx.CurrentDirectory) ?? "/";

            // Windows 切盘："cd D:" → 转为 "D:\\"
            if (pathArg.Length == 2 && pathArg[1] == ':')
                pathArg += Path.DirectorySeparatorChar;

            string target = Path.IsPathFullyQualified(pathArg)
                ? pathArg
                : Path.GetFullPath(Path.Combine(ctx.CurrentDirectory, pathArg));

            if (!Directory.Exists(target))
                throw new DirectoryNotFoundException($"目录不存在: {target}");

            ctx.CurrentDirectory = target;
            return Task.FromResult<IEnumerable<string>>(new[] { $"已切换到: {ctx.CurrentDirectory}" });
        }
    }

    /// <summary>
    /// ls 命令：列出目录内容。-a / --all / /a 显示完整路径。
    /// </summary>
    public sealed class LsCommand : ICommand {
        public string Name => "ls";
        public string Description => "列出目录内容 (ls [-a|/a|--all])";

        public Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) {
            bool showFull = args.Any(a => a is "-a" or "/a" or "--all");
            var opt = new EnumerationOptions { IgnoreInaccessible = true };

            IEnumerable<string> items = Directory.EnumerateFileSystemEntries(ctx.CurrentDirectory, "*", opt)
                .Select(p => FormatEntry(p, showFull));

            return Task.FromResult(items);

            static string FormatEntry(string path, bool full) {
                bool isDir = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
                string name = full ? path : Path.GetFileName(path);
                return (isDir ? "<DIR> " : "      ") + name;
            }
        }
    }

    /*────────────────────── 其他示例命令 ─────────────────────*/

    /// <summary>
    /// echo 命令：将输入参数原样输出。
    /// </summary>
    public sealed class EchoCommand : ICommand {
        public string Name => "echo";
        public string Description => "回显文本";
        public Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) =>
            Task.FromResult<IEnumerable<string>>(new[] { string.Join(' ', args) });
    }

    /// <summary>
    /// time 命令：显示当前系统时间。
    /// </summary>
    public sealed class TimeCommand : ICommand {
        public string Name => "time";
        public string Description => "显示当前时间";
        public Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) =>
            Task.FromResult<IEnumerable<string>>(new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
    }

    /// <summary>
    /// run 命令：启动外部程序并捕获其标准输出与错误输出。
    /// 用法：run <程序> [参数...] 例如：run ping -n 1 127.0.0.1
    /// </summary>
    public sealed class RunCommand : ICommand {
        public string Name => "run";
        public string Description => "执行外部程序并捕获输出";

        public async Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) {
            if (args.Length == 0)
                throw new ArgumentException("用法: run <程序> [参数]");

            string fileName = args[0];
            string arguments = string.Join(' ', args.Skip(1));

            var info = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = ctx.CurrentDirectory
            };

            using var process = Process.Start(info);
            if (process == null)
                throw new InvalidOperationException("进程启动失败。");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(stdout)) lines.Add(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr)) lines.Add("[stderr] " + stderr.TrimEnd());
            if (lines.Count == 0) lines.Add($"进程退出码 {process.ExitCode}，无输出。");
            return lines;
        }
    }

}
