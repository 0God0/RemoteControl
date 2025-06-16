using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer {
    #region 公共回调
    public delegate void LogCallback(string message);
    public delegate void ErrorCallback(Exception ex);
    public delegate void ProgressCallback(long transferredBytes, long totalBytes);
    #endregion

    /// <summary>内部工具</summary>
    internal static class TransferUtil {
        public const ushort Magic = 0xABCD;
        private const int HeaderSize = 53;                // 2+1+2+8+8+32
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static byte[] ComputeSha256(string path) {
            using FileStream fs = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(fs);
        }

        public static async Task<byte[]> ReadExactAsync(NetworkStream ns, int count, CancellationToken ct) {
            byte[] buf = new byte[count];
            int off = 0;
            while (off < count) {
                int r = await ns.ReadAsync(buf.AsMemory(off, count - off), ct);
                if (r == 0) throw new IOException("Remote closed unexpectedly.");
                off += r;
            }
            return buf;
        }

        public static async Task WriteAsync(NetworkStream ns, byte[] data, CancellationToken ct) =>
            await ns.WriteAsync(data.AsMemory(), ct);

        public static byte[] BuildHeader(byte cmd, string name, long fileLen, long offset, byte[] sha256) {
            byte[] nameBytes = Utf8.GetBytes(name);
            using MemoryStream ms = new();
            using BinaryWriter bw = new(ms);

            bw.Write(Magic);
            bw.Write(cmd);
            bw.Write((ushort)nameBytes.Length);
            bw.Write(fileLen);
            bw.Write(offset);
            bw.Write(sha256);
            bw.Write(nameBytes);
            return ms.ToArray();
        }

        public static async Task<(byte cmd, string name, long len, long off, byte[] sha)>
            ParseHeaderAsync(NetworkStream ns, CancellationToken ct) {
            byte[] head = await ReadExactAsync(ns, HeaderSize, ct);
            using MemoryStream ms = new(head);
            using BinaryReader br = new(ms);

            if (br.ReadUInt16() != Magic) throw new InvalidDataException("Magic mismatch");
            byte cmd = br.ReadByte();
            ushort nLen = br.ReadUInt16();
            long len = br.ReadInt64();
            long off = br.ReadInt64();
            byte[] sha = br.ReadBytes(32);
            byte[] nameBuf = await ReadExactAsync(ns, nLen, ct);
            string name = Utf8.GetString(nameBuf);
            return (cmd, name, len, off, sha);
        }
    }

    /// <summary>端点基类</summary>
    public abstract class TransferEndpoint : IDisposable {
        protected readonly LogCallback log;
        protected readonly ErrorCallback err;
        protected readonly ProgressCallback prog;
        protected readonly CancellationTokenSource cts = new();

        protected TransferEndpoint(LogCallback l, ErrorCallback e, ProgressCallback p) {
            log = l ?? (_ => { });
            err = e ?? (_ => { });
            prog = p ?? ((_, _) => { });
        }
        public void Dispose() => cts.Cancel();
    }

    /// <summary>文件服务器：可选推送 + 永远接收客户端上传</summary>
    public sealed class TransferServer : TransferEndpoint {
        private readonly int port;
        private readonly bool pushEnabled;
        private readonly string filePath;
        private readonly string fileName;
        private readonly long fileLen;
        private readonly byte[] sha256;
        private TcpListener listener;

        public TransferServer(
            int port,
            string filePath = null,                     // 传 null ⇒ 只接收上传，不推送
            LogCallback logCallback = null,
            ErrorCallback errorCallback = null,
            ProgressCallback progressCallback = null)
            : base(logCallback, errorCallback, progressCallback) {
            this.port = port;
            pushEnabled = !string.IsNullOrEmpty(filePath);
            this.filePath = filePath;

            if (pushEnabled) {
                fileName = Path.GetFileName(filePath);
                fileLen = new FileInfo(filePath).Length;
                sha256 = TransferUtil.ComputeSha256(filePath);
            }
        }

        public void Start() {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            log($"Server listening on {port}");
            _ = AcceptLoopAsync();
        }

        public void Stop() => cts.Cancel();

        private async Task AcceptLoopAsync() {
            try {
                while (!cts.IsCancellationRequested) {
                    TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);
                    _ = HandleClientAsync(client);
                }
            } catch (OperationCanceledException) { } catch (Exception ex) { err(ex); }
        }

        private async Task HandleClientAsync(TcpClient client) {
            string remote = client.Client.RemoteEndPoint.ToString();
            log($"Client {remote} connected.");

            NetworkStream ns = client.GetStream();
            var tasks = new List<Task>();

            try {
                /* ---------- 1. 先把自己头发出去 ---------- */
                byte[] headerOut = pushEnabled
                    ? TransferUtil.BuildHeader(0x01, fileName, fileLen, 0, sha256)
                    : TransferUtil.BuildHeader(0x00, string.Empty, 0, 0, new byte[32]);
                await TransferUtil.WriteAsync(ns, headerOut, cts.Token);

                /* ---------- 2. 解析客户端头 ---------- */
                var (cmd, name, len, off, peerSha) = await TransferUtil.ParseHeaderAsync(ns, cts.Token);

                if (cmd == 0x01) // 客户端要上传
                    tasks.Add(ReceiveFileAsync(ns, name, len, peerSha));

                if (pushEnabled && off < fileLen) // 客户端可能稍后下载
                    tasks.Add(SendFileAsync(ns, off));

                await Task.WhenAll(tasks); // 等全部收发完成
            } catch (Exception ex) { err(ex); } finally {
                ns.Close();
                client.Close();
                log($"Client {remote} disconnected.");
            }
        }

        private async Task SendFileAsync(NetworkStream ns, long offset) {
            try {
                using FileStream fs = File.OpenRead(filePath);
                fs.Seek(offset, SeekOrigin.Begin);
                long sent = offset;
                byte[] buf = new byte[81920];
                int r;
                while ((r = await fs.ReadAsync(buf, cts.Token)) > 0) {
                    await ns.WriteAsync(buf.AsMemory(0, r), cts.Token);
                    sent += r;
                    prog(sent, fileLen);
                }
            } catch (Exception ex) { err(ex); }
        }

        private async Task ReceiveFileAsync(NetworkStream ns, string name, long len, byte[] sha) {
            string save = $"recv_{DateTime.Now:yyyyMMddHHmmss}_{name}";
            long recved = 0;
            try {
                using FileStream fs = new(save, FileMode.Create, FileAccess.Write);
                byte[] buf = new byte[81920];
                int r;
                while (recved < len && (r = await ns.ReadAsync(buf, cts.Token)) > 0) {
                    await fs.WriteAsync(buf.AsMemory(0, r), cts.Token);
                    recved += r;
                    prog(recved, len);
                }
                fs.Close();

                if (!sha.AsSpan().SequenceEqual(TransferUtil.ComputeSha256(save)))
                    throw new InvalidDataException("Checksum mismatch");
                log($"Receive OK: {save}");
            } catch (Exception ex) { err(ex); }
        }
    }

    /// <summary>客户端：上传 / 下载 / 双端互传</summary>
    public sealed class TransferClient : TransferEndpoint {
        private readonly string host;
        private readonly int port;
        private readonly string upPath;
        private readonly string downPath;
        private readonly string upName;
        private readonly long upLen;
        private readonly byte[] upSha;

        public TransferClient(
            string host, int port,
            string localFileToUpload,       // null = 不上传
            string downloadSavePath,        // null = 不下载
            LogCallback logCallback = null,
            ErrorCallback errorCallback = null,
            ProgressCallback progressCallback = null)
            : base(logCallback, errorCallback, progressCallback) {
            try {
                this.host = host;
                this.port = port;
                upPath = localFileToUpload;
                downPath = downloadSavePath;

                if (upPath is not null) {
                    upName = Path.GetFileName(upPath);
                    upLen = new FileInfo(upPath).Length;
                    upSha = TransferUtil.ComputeSha256(upPath);
                }
            }catch (Exception ex) { err(ex); }
            
        }

        public async Task StartAsync() {
            using TcpClient tcp = new();
            NetworkStream ns = null;

            var tasks = new List<Task>();
            try {
                await tcp.ConnectAsync(host, port, cts.Token);
                log($"Connected {host}:{port}");
                ns = tcp.GetStream();
                /* ---------- 1. 先把自己头发出去 ---------- */
                byte cmdOut = upPath is null ? (byte)0x02 : (byte)0x01;
                long resume = 0;

                if (cmdOut == 0x02 && downPath is not null && File.Exists(downPath)) {
                    resume = new FileInfo(downPath).Length;
                    log($"Resume download from {resume}");
                }

                byte[] headOut = TransferUtil.BuildHeader(
                    cmdOut, upName ?? string.Empty, upLen, resume, upSha ?? new byte[32]);
                await TransferUtil.WriteAsync(ns, headOut, cts.Token);

                /* ---------- 2. 解析服务器头 ---------- */
                var (svCmd, svName, svLen, svOff, svSha) = await TransferUtil.ParseHeaderAsync(ns, cts.Token);

                /* ---------- 3. 启动上传/下载 ---------- */
                if (upPath is not null) tasks.Add(UploadAsync(ns));
                if (svCmd == 0x01 && downPath is not null)               // 需要下载
                    tasks.Add(DownloadAsync(ns, svName, svLen, svSha, svOff));

                await Task.WhenAll(tasks); // 等全部完成
            } catch (Exception ex) { err(ex); } finally {
                ns?.Close();
                tcp?.Close();
            }
        }

        private async Task UploadAsync(NetworkStream ns) {
            try {
                using FileStream fs = File.OpenRead(upPath);
                long sent = 0;
                byte[] buf = new byte[81920];
                int r;
                while ((r = await fs.ReadAsync(buf, cts.Token)) > 0) {
                    await ns.WriteAsync(buf.AsMemory(0, r), cts.Token);
                    sent += r;
                    prog(sent, upLen);
                }
                log("Upload complete.");
            } catch (Exception ex) { err(ex); }
        }

        private async Task DownloadAsync(NetworkStream ns, string name, long len, byte[] sha, long offset) {
            string save = downPath ?? $"download_{DateTime.Now:yyyyMMddHHmmss}_{name}";
            FileMode mode = offset > 0 ? FileMode.Append : FileMode.Create;
            long recved = offset;
            try {
                using FileStream fs = new(save, mode, FileAccess.Write);
                if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);

                byte[] buf = new byte[81920];
                int r;
                while (recved < len && (r = await ns.ReadAsync(buf, cts.Token)) > 0) {
                    await fs.WriteAsync(buf.AsMemory(0, r), cts.Token);
                    recved += r;
                    prog(recved, len);
                }
                fs.Close();

                if (!sha.AsSpan().SequenceEqual(TransferUtil.ComputeSha256(save)))
                    throw new InvalidDataException("Checksum mismatch");
                log($"Download OK: {save}");
            } catch (Exception ex) { err(ex); }
        }
    }
}
