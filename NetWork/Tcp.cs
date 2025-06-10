// ------------------------------------------------------------
//  网络通信框架（精简版）
//
//  已为所有公开类型与成员补充中文 XML 文档注释，
//  便于生成 API 帮助手册与后续维护。
// ------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using STcpClient = System.Net.Sockets.TcpClient;

namespace NetWork {
    #region 委托与数据模型
    /// <summary>
    /// 处理字符串数据包的回调委托。
    /// </summary>
    /// <param name="client">来源客户端信息。</param>
    /// <param name="packetMarker">数据包标记。</param>
    /// <param name="payload">字符串负载。</param>
    public delegate void PacketHandler(ClientInfo client, string packetMarker, string payload);

    /// <summary>
    /// 处理二进制数据包的回调委托。
    /// </summary>
    /// <param name="client">来源客户端信息。</param>
    /// <param name="packetMarker">数据包标记。</param>
    /// <param name="data">二进制负载。</param>
    public delegate void BytesPacketHandler(ClientInfo client, string packetMarker, byte[] data);

    /// <summary>
    /// 客户端连接成功时触发。
    /// </summary>
    /// <param name="client">客户端信息。</param>
    public delegate void ConnectionHandler(ClientInfo client);

    /// <summary>
    /// 客户端断开连接时触发。
    /// </summary>
    /// <param name="client">客户端信息。</param>
    public delegate void DisconnectionHandler(ClientInfo client);

    /// <summary>
    /// 框架内部异常统一回调委托。
    /// </summary>
    /// <param name="ex">异常实例。</param>
    public delegate void ErrorHandler(Exception ex);

    /// <summary>
    /// 自定义加密处理委托。
    /// </summary>
    /// <param name="data">待加密数据。</param>
    /// <returns>加密后的数据。</returns>
    public delegate byte[] EncryptionHandler(byte[] data);

    /// <summary>
    /// 自定义解密处理委托。
    /// </summary>
    /// <param name="data">待解密数据。</param>
    /// <returns>解密后的数据。</returns>
    public delegate byte[] DecryptionHandler(byte[] data);

    /// <summary>
    /// 表示一个客户端的基本信息（线程安全只读）。
    /// </summary>
    public class ClientInfo {
        /// <summary>
        /// 服务器为每个连接生成的唯一标识符。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 远程终结点（IP + 端口）。
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// 可用于存放业务相关的自定义对象。
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        /// 初始化 <see cref="ClientInfo"/> 实例。
        /// </summary>
        /// <param name="id">唯一标识。</param>
        /// <param name="remoteEndPoint">远程终结点。</param>
        public ClientInfo(string id, IPEndPoint remoteEndPoint) {
            Id = id;
            RemoteEndPoint = remoteEndPoint;
        }
    }
    #endregion

    #region 数据包封装/拆解辅助类
    /// <summary>
    /// 提供数据包封装与拆解的静态工具方法。
    /// </summary>
    internal static class PacketFraming {
        /// <summary>
        /// 固定包头长度（字节）。<br/>
        /// 组成：类型(1) + 加密标记(1) + 标记长度(2) + 负载长度(4)
        /// </summary>
        private const int HeaderSize = 8;

        /// <summary>
        /// 构建数据包。
        /// </summary>
        /// <param name="marker">业务标记。</param>
        /// <param name="data">负载数据。</param>
        /// <param name="binary">是否为二进制包。</param>
        /// <param name="encrypted">是否加密。</param>
        /// <param name="enc">加密委托（可为空）。</param>
        /// <returns>完整数据包字节数组。</returns>
        public static byte[] Build(string marker, byte[] data, bool binary, bool encrypted, EncryptionHandler enc) {
            var markerBytes = Encoding.UTF8.GetBytes(marker);
            var payload = encrypted && enc != null ? enc(data) : data;

            // 按最大长度预分配，提高写入效率
            using var ms = new MemoryStream(HeaderSize + markerBytes.Length + payload.Length);
            ms.WriteByte(binary ? (byte)1 : (byte)0);                         // 类型
            ms.WriteByte(encrypted ? (byte)1 : (byte)0);                      // 加密标记
            ms.Write(BitConverter.GetBytes((ushort)markerBytes.Length), 0, 2);// 标记长度
            ms.Write(markerBytes, 0, markerBytes.Length);                    // 标记
            ms.Write(BitConverter.GetBytes(payload.Length), 0, 4);           // 负载长度
            ms.Write(payload, 0, payload.Length);                             // 负载
            return ms.ToArray();
        }

        /// <summary>
        /// 尝试从接收缓存中解析出一个完整数据包。
        /// </summary>
        /// <param name="buf">接收缓存。</param>
        /// <param name="binary">输出：是否二进制。</param>
        /// <param name="encrypted">输出：是否加密。</param>
        /// <param name="marker">输出：数据包标记。</param>
        /// <param name="payload">输出：负载数据。</param>
        /// <returns>若成功解析返回 <c>true</c>，否则 <c>false</c>。</returns>
        public static bool TryParse(List<byte> buf, out bool binary, out bool encrypted, out string marker, out byte[] payload) {
            marker = null; payload = null; binary = encrypted = false;
            if (buf.Count < HeaderSize) return false;

            // 依次读取固定头字段
            binary = buf[0] == 1;
            encrypted = buf[1] == 1;
            ushort markerLen = BitConverter.ToUInt16(buf.GetRange(2, 2).ToArray(), 0);

            int payloadLenOffset = 4 + markerLen;
            if (buf.Count < payloadLenOffset + 4) return false;

            int payloadLen = BitConverter.ToInt32(buf.GetRange(payloadLenOffset, 4).ToArray(), 0);
            int total = HeaderSize + markerLen + payloadLen;
            if (buf.Count < total) return false; // 数据尚未接收完整

            // 解析标记与负载
            marker = Encoding.UTF8.GetString(buf.GetRange(4, markerLen).ToArray());
            payload = buf.GetRange(payloadLenOffset + 4, payloadLen).ToArray();

            // 从缓存中移除已消费部分
            buf.RemoveRange(0, total);
            return true;
        }
    }
    #endregion

    #region 连接封装类（客户端/服务器共用）
    /// <summary>
    /// 表示一个 TCP 连接，负责收发数据包、解析、加解密、回调分发等逻辑。
    /// </summary>
    internal sealed class Connection {
        private readonly STcpClient socket;
        private readonly NetworkStream stream;
        private readonly Dictionary<string, PacketHandler> textHandlers;
        private readonly Dictionary<string, BytesPacketHandler> binHandlers;
        private readonly ErrorHandler onError;
        private readonly EncryptionHandler encrypt;
        private readonly DecryptionHandler decrypt;

        private readonly List<byte> recvBuf = new(8192); // 接收缓存
        private readonly byte[] readBuf = new byte[8192];// 临时读缓冲
        private volatile bool connected = true;
        private readonly Thread rxThread;

        /// <summary>
        /// 当前连接对应的 <see cref="ClientInfo"/>。
        /// </summary>
        public ClientInfo Info { get; }

        /// <summary>
        /// 连接断开事件（内部使用）。
        /// </summary>
        public event Action<Connection> Disconnected;

        /// <summary>
        /// 创建 <see cref="Connection"/> 实例。
        /// </summary>
        /// <param name="sock">底层 <see cref="STcpClient"/>。</param>
        /// <param name="txt">字符串包处理器集合。</param>
        /// <param name="bin">二进制包处理器集合。</param>
        /// <param name="err">错误回调。</param>
        /// <param name="enc">加密委托。</param>
        /// <param name="dec">解密委托。</param>
        public Connection(STcpClient sock,
                          Dictionary<string, PacketHandler> txt,
                          Dictionary<string, BytesPacketHandler> bin,
                          ErrorHandler err,
                          EncryptionHandler enc,
                          DecryptionHandler dec) {
            socket = sock;
            stream = socket.GetStream();
            textHandlers = txt;
            binHandlers = bin;
            onError = err;
            encrypt = enc;
            decrypt = dec;

            // 生成客户端信息
            Info = new ClientInfo(Guid.NewGuid().ToString(),
                                  (IPEndPoint)socket.Client.RemoteEndPoint);

            // 启动接收线程
            rxThread = new Thread(ReceiveLoop) { IsBackground = true };
            rxThread.Start();
        }

        #region 发送方法
        /// <summary>
        /// 发送字符串数据包。
        /// </summary>
        public void Send(string marker, string payload, bool enc = false)
            => Send(marker, Encoding.UTF8.GetBytes(payload), enc, binary: false);

        /// <summary>
        /// 发送二进制数据包。
        /// </summary>
        public void Send(string marker, byte[] data, bool enc = false)
            => Send(marker, data, enc, binary: true);

        private void Send(string marker, byte[] data, bool enc, bool binary) {
            if (!connected) return;
            try {
                var packet = PacketFraming.Build(marker, data, binary, enc, encrypt);
                stream.Write(packet, 0, packet.Length);
            } catch (Exception ex) {
                HandleError(ex);
                Disconnect();
            }
        }
        #endregion

        #region 接收循环
        private void ReceiveLoop() {
            try {
                while (connected) {
                    int read = stream.Read(readBuf, 0, readBuf.Length);
                    if (read == 0) break; // 远端关闭连接

                    // 将新读取的数据追加到缓存
                    recvBuf.AddRange(new ArraySegment<byte>(readBuf, 0, read));

                    // 尝试解析所有完整数据包
                    while (PacketFraming.TryParse(recvBuf,
                                                  out bool bin,
                                                  out bool enc,
                                                  out string marker,
                                                  out byte[] payload)) {
                        // 如需解密
                        if (enc && decrypt != null) {
                            try {
                                payload = decrypt(payload);
                            } catch (Exception ex) {
                                HandleError(new Exception("Decryption failed", ex));
                                continue; // 跳过本包
                            }
                        }

                        // 分发到对应处理器（多线程）
                        if (bin)
                            Dispatch(binHandlers, marker,
                                     h => h?.Invoke(Info, marker, payload));
                        else
                            Dispatch(textHandlers, marker,
                                     h => h?.Invoke(Info, marker,
                                                    Encoding.UTF8.GetString(payload)));
                    }
                }
            } catch (Exception ex) {
                HandleError(ex);
            } finally {
                Disconnect();
            }
        }
        #endregion

        #region 私有辅助
        private static void Dispatch<T>(Dictionary<string, T> dict,
                                        string key,
                                        Action<T> inv) where T : Delegate {
            if (!dict.TryGetValue(key, out var h) || h == null) return;
            _ = Task.Run(() => {
                try { inv(h); } catch { /* 用户代码异常忽略 */ }
            });
        }

        private void HandleError(Exception ex) => onError?.Invoke(ex);
        #endregion

        /// <summary>
        /// 主动或被动断开连接。
        /// </summary>
        public void Disconnect() {
            if (!connected) return;

            connected = false;
            try {
                stream.Close();
                socket.Close();
            } catch { /* 忽略关闭异常 */ }

            // 通知上层
            Disconnected?.Invoke(this);
        }
    }
    #endregion

    #region TcpClient API
    /// <summary>
    /// 轻量级 TCP 客户端封装（同步收包线程 + 线程池分发）。
    /// </summary>
    public class TcpClient {
        private readonly ErrorHandler onError;
        private readonly Dictionary<string, PacketHandler> textHandlers = new();
        private readonly Dictionary<string, BytesPacketHandler> binHandlers = new();
        private Connection conn;
        private EncryptionHandler encrypt;
        private DecryptionHandler decrypt;

        /// <summary>
        /// 创建 <see cref="TcpClient"/>。
        /// </summary>
        /// <param name="errorHandler">统一错误回调。</param>
        public TcpClient(ErrorHandler errorHandler)
            => onError = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));

        /// <summary>
        /// 连接到服务器。
        /// </summary>
        public void Connect(string host, int port) {
            var sock = new STcpClient();
            sock.Connect(host, port);
            conn = new Connection(sock, textHandlers, binHandlers, onError, encrypt, decrypt);
        }

        /// <summary>
        /// 断开连接。
        /// </summary>
        public void Disconnect() => conn?.Disconnect();

        /// <summary>
        /// 发送字符串数据包。
        /// </summary>
        public void SendPacket(string marker, string payload, bool enc = false)
            => conn?.Send(marker, payload, enc);

        /// <summary>
        /// 发送二进制数据包。
        /// </summary>
        public void SendBytes(string marker, byte[] data, bool enc = false)
            => conn?.Send(marker, data, enc);

        /// <summary>
        /// 注册字符串包处理器（可叠加）。
        /// </summary>
        public void RegisterHandler(string marker, PacketHandler h) => Add(textHandlers, marker, h);

        /// <summary>
        /// 注册二进制包处理器（可叠加）。
        /// </summary>
        public void RegisterBytesHandler(string marker, BytesPacketHandler h) => Add(binHandlers, marker, h);

        /// <summary>
        /// 取消指定标记的字符串包处理器。
        /// </summary>
        public void UnregisterHandler(string marker) => textHandlers.Remove(marker);

        /// <summary>
        /// 取消指定标记的二进制包处理器。
        /// </summary>
        public void UnregisterBytesHandler(string marker) => binHandlers.Remove(marker);

        /// <summary>
        /// 配置加解密回调。
        /// </summary>
        public void SetEncryption(EncryptionHandler enc, DecryptionHandler dec) { encrypt = enc; decrypt = dec; }

        private static void Add<T>(IDictionary<string, T> dict, string key, T h) where T : Delegate
            => dict[key] = dict.TryGetValue(key, out var exist) ? (T)Delegate.Combine(exist, h) : h;
    }
    #endregion

    #region TcpServer API
    /// <summary>
    /// 轻量级 TCP 服务器封装（每连接 1 线程，同步读，线程池分发）。
    /// </summary>
    public class TcpServer {
        private readonly ErrorHandler onError;
        private readonly Dictionary<string, PacketHandler> textHandlers = new();
        private readonly Dictionary<string, BytesPacketHandler> binHandlers = new();
        private readonly ConcurrentDictionary<string, Connection> clients = new();
        private TcpListener listener;
        private Thread acceptThread;
        private EncryptionHandler encrypt;
        private DecryptionHandler decrypt;
        private volatile bool running;
        private ConnectionHandler onConnect;
        private DisconnectionHandler onDisconnect;

        /// <summary>
        /// 初始化 <see cref="TcpServer"/>。
        /// </summary>
        /// <param name="errorHandler">统一错误回调。</param>
        public TcpServer(ErrorHandler errorHandler)
            => onError = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));

        /// <summary>
        /// 启动服务器并监听指定端口。
        /// </summary>
        public void Start(int port) {
            if (running) return;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            running = true;

            acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            acceptThread.Start();
        }

        /// <summary>
        /// 停止服务器并断开所有客户端。
        /// </summary>
        public void Stop() {
            running = false;
            try { listener?.Stop(); } catch { }

            foreach (var c in clients.Values) c.Disconnect();
            clients.Clear();
            acceptThread?.Join();
        }

        // 接受循环：阻塞 Accept，创建 Connection
        private void AcceptLoop() {
            while (running) {
                try {
                    var sock = listener.AcceptTcpClient();
                    var conn = new Connection(sock, textHandlers, binHandlers, onError, encrypt, decrypt);
                    conn.Disconnected += OnClientDisconnected;

                    clients.TryAdd(conn.Info.Id, conn);
                    onConnect?.Invoke(conn.Info);
                } catch (SocketException) {
                    if (!running) break; // Stop() 已调用
                } catch (Exception ex) {
                    if (running) onError?.Invoke(ex);
                }
            }
        }

        // 客户端断开回调
        private void OnClientDisconnected(Connection c) {
            clients.TryRemove(c.Info.Id, out _);
            onDisconnect?.Invoke(c.Info);
        }

        /// <summary>
        /// 向所有客户端广播字符串数据包。
        /// </summary>
        public void BroadcastPacket(string marker, string payload, bool enc = false)
            => Parallel.ForEach(clients.Values, c => c.Send(marker, payload, enc));

        /// <summary>
        /// 向所有客户端广播二进制数据包。
        /// </summary>
        public void BroadcastBytes(string marker, byte[] data, bool enc = false)
            => Parallel.ForEach(clients.Values, c => c.Send(marker, data, enc));

        /// <summary>
        /// 向指定客户端发送字符串数据包。
        /// </summary>
        public void SendToClient(string id, string marker, string payload, bool enc = false) {
            if (clients.TryGetValue(id, out var c)) c.Send(marker, payload, enc);
        }

        /// <summary>
        /// 向指定客户端发送二进制数据包。
        /// </summary>
        public void SendBytesToClient(string id, string marker, byte[] data, bool enc = false) {
            if (clients.TryGetValue(id, out var c)) c.Send(marker, data, enc);
        }

        /// <summary>
        /// 注册字符串包处理器（可叠加）。
        /// </summary>
        public void RegisterHandler(string marker, PacketHandler h) => Add(textHandlers, marker, h);

        /// <summary>
        /// 注册二进制包处理器（可叠加）。
        /// </summary>
        public void RegisterBytesHandler(string marker, BytesPacketHandler h) => Add(binHandlers, marker, h);

        /// <summary>
        /// 取消指定标记的字符串包处理器。
        /// </summary>
        public void UnregisterHandler(string marker) => textHandlers.Remove(marker);

        /// <summary>
        /// 取消指定标记的二进制包处理器。
        /// </summary>
        public void UnregisterBytesHandler(string marker) => binHandlers.Remove(marker);

        /// <summary>
        /// 注册客户端连接事件处理器（可叠加）。
        /// </summary>
        public void RegisterConnectionHandler(ConnectionHandler h) => onConnect += h;

        /// <summary>
        /// 注册客户端断开事件处理器（可叠加）。
        /// </summary>
        public void RegisterDisconnectionHandler(DisconnectionHandler h) => onDisconnect += h;

        /// <summary>
        /// 配置加解密回调。
        /// </summary>
        public void SetEncryption(EncryptionHandler enc, DecryptionHandler dec) { encrypt = enc; decrypt = dec; }

        private static void Add<T>(IDictionary<string, T> dict, string key, T h) where T : Delegate
            => dict[key] = dict.TryGetValue(key, out var exist) ? (T)Delegate.Combine(exist, h) : h;
    }
    #endregion
}
