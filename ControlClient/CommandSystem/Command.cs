using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ControlClient.CommandSystem {

    /*────────────────────── 执行环境 ─────────────────────*/

    /// <summary>
    /// 所有命令共享的运行时上下文。目前仅保存工作目录，可根据需要扩展，例如环境变量、用户偏好等。
    /// </summary>
    public sealed class ExecutionContext {
        /// <summary>当前工作目录。</summary>
        public string CurrentDirectory { get; set; } = Environment.CurrentDirectory;
    }

    /*────────────────────── 命令抽象 ─────────────────────*/

    /// <summary>
    /// Shell 命令接口。所有命令必须实现 Name、Description 与异步执行方法。
    /// </summary>
    public interface ICommand {
        /// <summary>命令名称（大小写不敏感）。</summary>
        string Name { get; }

        /// <summary>显示在 help 列表中的简要描述。</summary>
        string Description { get; }

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="args">命令参数（不含命令本身）。</param>
        /// <param name="context">运行时上下文。</param>
        /// <returns>输出行集合，框架将直接显示给用户。</returns>
        Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext context);
    }

    /*────────────────────── 命令行解析器 ─────────────────────*/

    /// <summary>
    /// 轻量级命令行解析器，支持双引号包裹含空格参数。
    /// </summary>
    public static class CommandParser {
        private static readonly Regex TokenPattern = new("[\"]+?.+?[\"]|[^ ]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 将一行输入拆分为 Token，并移除最外层引号。
        /// </summary>
        public static List<string> Tokenize(string line) {
            var tokens = new List<string>();
            foreach (Match m in TokenPattern.Matches(line)) {
                string token = m.Value.Trim();
                if (token.Length > 0)
                    tokens.Add(token.Trim('"'));
            }
            return tokens;
        }
    }

    /*────────────────────── 命令分发器 ─────────────────────*/

    /// <summary>
    /// 负责注册、查找并执行命令。所有异常通过 <see cref="ContextExceptionCallback"/> 回调，不向调用者抛出。
    /// </summary>
    public sealed class CommandDispatcher {
        private readonly Dictionary<string, ICommand> commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly Common.ContextExceptionCallback onError;

        public CommandDispatcher(Common.ContextExceptionCallback? onError = null) {
            this.onError = onError ?? ((c, e) => Console.WriteLine($"[ERR] {c}: {e.Message}"));
        }

        /// <summary>注册（或覆盖）一个命令。</summary>
        public void Register(ICommand command) => commands[command.Name] = command;

        /// <summary>
        /// 执行输入行。如遇异常通过回调处理并返回空集合。
        /// </summary>
        public Task<IEnumerable<string>> ExecuteAsync(string commandLine, ExecutionContext context) {
            if (string.IsNullOrWhiteSpace(commandLine))
                return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

            var tokens = CommandParser.Tokenize(commandLine);
            var name = tokens[0];
            var args = tokens.Skip(1).ToArray();

            if (!commands.TryGetValue(name, out var cmd)) {
                onError("命令解析", new NotSupportedException($"未知命令: {name}"));
                return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
            }

            return SafeAsync(() => cmd.ExecuteAsync(args, context), $"执行命令 \"{name}\"");
        }

        /// <summary>返回所有已注册命令的帮助信息。</summary>
        public IEnumerable<(string Name, string Description)> Help() =>
            commands.Values.Select(c => (c.Name, c.Description));

        /// <summary>
        /// 执行一行命令并将所有输出整合为单个字符串（方便网络层一次性发送）。
        /// </summary>
        /// <param name="commandLine">完整命令行文本。</param>
        /// <param name="context">运行时上下文。</param>
        /// <returns>多行输出用 Environment.NewLine 拼接；若无输出返回空字符串。</returns>
        public async Task<string> ExecuteAsStringAsync(string commandLine, ExecutionContext context) {
            IEnumerable<string> lines = await ExecuteAsync(commandLine, context)
                                             .ConfigureAwait(false);
            return string.Join(Environment.NewLine, lines);
        }

        /*────────────── 内部安全包装 ─────────────*/
        private async Task<IEnumerable<string>> SafeAsync(Func<Task<IEnumerable<string>>> work, string ctx) {
            try { return await work(); } catch (Exception ex) { onError(ctx, ex); return Array.Empty<string>(); }
        }
    }

    /// <summary>
    /// 远程命令执行服务。
    /// —— 由上层网络层调用：收到一行命令文本后，调用 <see cref="ExecuteAsync"/> 获取结果字符串。
    /// —— 网络传输、连接管理、权限校验等不在此类职责范围内。
    /// </summary>
    public sealed class RemoteCommandService {

    private readonly CommandDispatcher dispatcher;
    private readonly ExecutionContext context;

    /// <param name="dispatcher">复用已初始化并注册好命令的分发器。</param>
    /// <param name="context">共享的执行上下文；可根据会话拆分多个实例。</param>
    public RemoteCommandService(CommandDispatcher dispatcher, ExecutionContext context) {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// 执行来自远程的命令行并将所有输出整合为单一字符串返回。
    /// </summary>
    /// <param name="commandLine">完整命令行文本，已由网络层解码为 UTF-8 字符串。</param>
    /// <returns>
    /// 若命令产生多行输出，则用 <see cref="Environment.NewLine"/> 进行拼接；
    /// 若没有任何输出，则返回空字符串。
    /// </returns>
    public async Task<string> ExecuteAsync(string commandLine) {
        // 调用现有分发器执行业务逻辑
        IEnumerable<string> lines = await dispatcher.ExecuteAsync(commandLine, context)
                                                    .ConfigureAwait(false);

        // 统一组装为可直接通过 TCP/WebSocket 发送的文本
        return string.Join(Environment.NewLine, lines);
    }
}

/*────────────────────── Lambda 快速命令 ─────────────────────*/

/// <summary>
/// 辅助类：通过 Lambda 快速创建 ICommand 实例。
/// </summary>
public record class LambdaCommand : ICommand {
    public string Name { get; }
    public string Description { get; }
    private readonly Func<string[], ExecutionContext, Task<IEnumerable<string>>> impl;

    public LambdaCommand(string name, string description, Func<string[], ExecutionContext, Task<IEnumerable<string>>> impl) {
        Name = name;
        Description = description;
        this.impl = impl;
    }

    public Task<IEnumerable<string>> ExecuteAsync(string[] args, ExecutionContext ctx) => impl(args, ctx);
}

/*────────────────────── 主程序示例 ─────────────────────*/

internal static class Program {
    private static async Task Main() {
        // 构建调度器并注册异常回调，可接入 UI 日志。
        var dispatcher = new CommandDispatcher((ctx, ex) => {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERR] {ctx}: {ex.Message}");
            Console.ResetColor();
        });

        // 注册内置命令
        dispatcher.Register(new CdCommand());
        dispatcher.Register(new LsCommand());
        dispatcher.Register(new EchoCommand());
        dispatcher.Register(new TimeCommand());
        dispatcher.Register(new RunCommand());

        // 示例：动态注册一个 puts 命令
        dispatcher.Register(new LambdaCommand("puts", "打印 Hello", (a, _) =>
            Task.FromResult<IEnumerable<string>>(new[] { "Hello " + string.Join(' ', a) })));

        var context = new ExecutionContext();
        Console.WriteLine("通用 Shell。输入 help 查看命令，exit 退出。\n");

        while (true) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{context.CurrentDirectory}> ");
            Console.ResetColor();
            string? line = Console.ReadLine();
            if (line is null) break;        // Ctrl+D
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            if (line.Equals("help", StringComparison.OrdinalIgnoreCase)) {
                foreach (var (name, desc) in dispatcher.Help())
                    Console.WriteLine($"{name,-8} {desc}");
                continue;
            }

            IEnumerable<string> output = await dispatcher.ExecuteAsync(line, context);
            foreach (var o in output) Console.WriteLine(o);
        }
    }
}
}
