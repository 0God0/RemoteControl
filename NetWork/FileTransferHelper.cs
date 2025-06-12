using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace NetWork {

    static class FtpMarker {
        public const string META  = "FILE_META";
        public const string CHUNK = "FILE_CHUNK";
        public const string END   = "FILE_END";
    }

    /* ————————————— ① TcpClient 侧扩展 ————————————— */
    public static class TcpClientFileExtensions {

        /// <param name="path">
        ///   绝对或相对文件路径。例如：
        ///   <br>• <c>@\"D:\\Uploads\\big.iso\"</c>
        ///   <br>• <c>Path.Combine(Environment.CurrentDirectory, \"test.bin\")</c>
        /// </param>
        public static async Task SendFileAsync(
            this TcpClient cli,
            string path,
            int chunkSize = 64 * 1024,
            bool encrypt  = false,
            CancellationToken ct = default) {

            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            /* —— 1) SHA-256 —— */
            string sha;
            using (var sha256 = SHA256.Create())
            using (var fs = File.OpenRead(path)) {
                var hash = await sha256.ComputeHashAsync(fs, ct);
                sha = BitConverter.ToString(hash).Replace("-", ""); // HEX
            }

            /* —— 2) META —— */
            var fi = new FileInfo(path);
            var meta = new StringBuilder()
                .AppendLine($"NAME={fi.Name}")
                .AppendLine($"SIZE={fi.Length}")
                .AppendLine($"CHUNK={chunkSize}")
                .AppendLine($"SHA={sha}")
                .AppendLine();                      // 尾部空行
            cli.SendPacket(FtpMarker.META, meta.ToString(), encrypt);

            /* —— 3) DATA —— */
            var buffer = new byte[chunkSize];
            using (var fs = File.OpenRead(path)) {
                int read;
                while ((read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), ct)) > 0) {
                    byte[] slice = read == chunkSize ? buffer : buffer[..read];
                    cli.SendBytes(FtpMarker.CHUNK, slice, encrypt);
                }
            }

            /* —— 4) END —— */
            cli.SendPacket(FtpMarker.END, string.Empty, encrypt);
        }
    }

    /* ————————————— ② TcpServer 侧扩展 ————————————— */
    public static class TcpServerFileExtensions {

        /// <param name="saveDir">
        ///   接收端保存目录。  
        ///   例：<c>\"Received\"</c>（相对）或 <c>@\"D:\\Downloads\"</c>（绝对）。  
        ///   若为空串 = 当前工作目录。
        /// </param>
        public static void EnableFileReceiver(
            this TcpServer srv,
            string saveDir = "",
            Action<ClientInfo,string> finished = null) {

            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = Environment.CurrentDirectory;
            Directory.CreateDirectory(saveDir);

            var dict = new ConcurrentDictionary<string, State>();

            /* ---- META ---- */
            srv.RegisterHandler(FtpMarker.META, (c, _, txt) => {
                // 解析 NAME / SIZE / CHUNK / SHA
                var lines = txt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var s = new State();
                foreach (var l in lines) {
                    var kv = l.Split('=', 2);
                    if (kv.Length < 2) continue;
                    switch (kv[0]) {
                        case "NAME":  s.Name = kv[1].Trim(); break;
                        case "SIZE":  s.Size = long.Parse(kv[1]); break;
                        case "CHUNK": s.Chunk = int.Parse(kv[1]); break;
                        case "SHA":   s.Sha = kv[1].Trim(); break;
                    }
                }

                s.Path = Path.Combine(saveDir, s.Name);
                s.Fs   = File.Create(s.Path);
                dict[c.Id] = s;
                Console.WriteLine($"[S] 开始接收 {s.Name} ({s.Size} bytes)");
            });

            /* ---- CHUNK ---- */
            srv.RegisterBytesHandler(FtpMarker.CHUNK, (c, _, data) => {
                if (!dict.TryGetValue(c.Id, out var s)) return;
                s.Fs.Write(data);
                s.Received += data.Length;
            });

            /* ---- END ---- */
            srv.RegisterHandler(FtpMarker.END, (c, _, __) => {
                if (!dict.TryRemove(c.Id, out var s)) return;
                s.Fs.Close();

                // ✔ 校验
                bool ok = VerifySha256(s.Path, s.Sha);
                Console.WriteLine(ok
                    ? $"[S] ✅ {s.Name} OK"
                    : $"[S] ⚠️  {s.Name} Hash mismatch");

                if (ok) finished?.Invoke(c, s.Path);
                srv.RaiseFileReceived(c, s.Path);
            });

            /* —— helpers —— */
            static bool VerifySha256(string file, string expect) {
                using var sha256 = SHA256.Create();
                using var fs = File.OpenRead(file);
                var hash = sha256.ComputeHash(fs);
                var local = BitConverter.ToString(hash).Replace("-", "");
                return string.Equals(local, expect, StringComparison.OrdinalIgnoreCase);
            }
        }

        /* —— 用于存放接收状态 —— */
        class State {
            public string Name;
            public string Path;
            public long   Size;
            public int    Chunk;
            public string Sha;
            public FileStream Fs;
            public long   Received;
        }
    }
}
