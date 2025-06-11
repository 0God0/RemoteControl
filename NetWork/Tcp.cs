using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
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
    /// 提供根据“新协议”进行数据包封装与拆解的静态方法。
    /// 约定：
    ///   ● 未加密包格式保持旧版：Type + EncFlag + MarkerLen + Marker + PayloadLen + Payload
    ///   ● 加密包格式：Type + EncFlag + BodyLen + 〔EncryptedBlock〕
    ///       EncryptedBlock = MarkerLen + Marker + PayloadLen + Payload （整体加密）
    /// </summary>
    internal static class PacketFraming {
        /*************  头部固定长度常量  *************/
        /// <summary>未加密包固定头长度：1(Type)+1(EncFlag)+2(MarkerLen)+4(PayloadLen)=8</summary>
        private const int PlainHeaderSize  = 8;

        /// <summary>加密包固定头长度：1(Type)+1(EncFlag)+4(BodyLen)=6</summary>
        private const int CryptoHeaderSize = 6;

        #region 封  包  Build
        /// <summary>
        /// 将 marker 与 data 按“新协议”封装成可直接发送的字节数组
        /// </summary>
        /// <param name="marker">业务标记字符串</param>
        /// <param name="data">负载（明文）</param>
        /// <param name="binary">true→二进制包；false→文本包</param>
        /// <param name="encrypted">true→按照加密包格式封装</param>
        /// <param name="enc">加密委托（允许空；为空或 encrypted=false 时不加密）</param>
        public static byte[] Build(string marker,
                                   byte[] data,
                                   bool binary,
                                   bool encrypted,
                                   EncryptionHandler enc) {
            var markerBytes = Encoding.UTF8.GetBytes(marker);

            /************* 未加密分支：沿用旧格式 *************/
            if (!encrypted || enc == null) {
                // 预估整体大小，避免多次扩容
                using var ms = new MemoryStream(PlainHeaderSize + markerBytes.Length + data.Length);

                ms.WriteByte(binary ? (byte)1 : (byte)0);                // Type：0=文本 1=二进制
                ms.WriteByte(0);                                         // EncFlag：未加密=0
                ms.Write(BitConverter.GetBytes((ushort)markerBytes.Length)); // MarkerLen(2)
                ms.Write(markerBytes);                                   // Marker（n）
                ms.Write(BitConverter.GetBytes(data.Length));            // PayloadLen(4)
                ms.Write(data);                                          // Payload（m）
                return ms.ToArray();
            }

            /************* 加密分支：先拼内部块 → 加密 → 拼外部头 *************/

            /* === 第 1 步：拼装“内部块”(Marker+Payload) === */
            using var inner = new MemoryStream(2 + markerBytes.Length + 4 + data.Length);
            inner.Write(BitConverter.GetBytes((ushort)markerBytes.Length)); // MarkerLen(2)
            inner.Write(markerBytes);                                       // Marker
            inner.Write(BitConverter.GetBytes(data.Length));                // PayloadLen(4)
            inner.Write(data);                                              // Payload
            var cipher = enc(inner.ToArray());                              // 整块加密

            /* === 第 2 步：拼装带明文头部的“外部包” === */
            using var outer = new MemoryStream(CryptoHeaderSize + cipher.Length);
            outer.WriteByte(binary ? (byte)1 : (byte)0);        // Type
            outer.WriteByte(1);                                 // EncFlag = 1(加密)
            outer.Write(BitConverter.GetBytes(cipher.Length));  // BodyLen(4)：整体密文长度
            outer.Write(cipher);                                // EncryptedBlock
            return outer.ToArray();
        }
        #endregion

        #region 拆  包  TryParse
        /// <summary>
        /// 试图从连续接收缓冲 <paramref name="buf"/> 中拆出一个完整 外部包。
        /// 解析完毕后会把对应数据段从缓冲区移除。
        /// </summary>
        /// <param name="buf">连续的接收缓冲（可能含多个包或半包）</param>
        /// <param name="binary">输出：是否为二进制包</param>
        /// <param name="encrypted">输出：是否为加密包 (EncFlag)</param>
        /// <param name="marker">
        ///     输出：若未加密包→解析出标记并赋值；
        ///          若加密包→此处保持 null，待 Connection 解密后再解析
        /// </param>
        /// <param name="payloadOrCipher">
        ///     输出：
        ///       • 未加密包 → 真实业务负载
        ///       • 加密包   → 整个密文块( MarkerLen+Marker+PayloadLen+Payload )
        /// </param>
        /// <returns>true→已成功拆出一个完整包；false→数据不足需继续接收</returns>
        public static bool TryParse(List<byte> buf,
                                    out bool binary,
                                    out bool encrypted,
                                    out string marker,
                                    out byte[] payloadOrCipher) {
            // —— 初始化输出值，防止编译器警告 ——
            marker = null;
            payloadOrCipher = null;
            binary = encrypted = false;

            /* ========== 预判：至少要有 Type + EncFlag = 2 字节 ========== */
            if (buf.Count < 2) return false;

            /* ========== 读取最前面两个一字节字段 ========== */
            binary = buf[0] == 1;
            encrypted = buf[1] == 1;

            /*********************** ① 加密包 ***********************/
            if (encrypted) {
                /* ---- 判断头部是否完整 (Type+EncFlag+BodyLen) ---- */
                if (buf.Count < CryptoHeaderSize) return false;

                /* ---- 解析 BodyLen(4) ---- */
                int bodyLen = BitConverter.ToInt32(buf.GetRange(2, 4).ToArray(), 0);
                int total   = CryptoHeaderSize + bodyLen;            // 加密包总长度

                /* ---- 整包是否已完整接收？ ---- */
                if (buf.Count < total) return false;

                /* ---- 拷贝密文块并裁剪缓冲区 ---- */
                payloadOrCipher = buf.GetRange(CryptoHeaderSize, bodyLen).ToArray();
                buf.RemoveRange(0, total);
                return true;     // marker 保持 null，后续由 Connection 解密再解析
            }

            /*********************** ② 未加密包 ***********************/
            /* ---- 头部固定 8 字节: 1+1+2+4 ---- */
            if (buf.Count < PlainHeaderSize) return false;

            ushort markerLen = BitConverter.ToUInt16(buf.GetRange(2, 2).ToArray(), 0);
            int payloadLenOffset = 4 + markerLen;                  // MarkerLen 字节后是 PayloadLen(4)

            /* ---- 判断 PayloadLen 字段是否已完整 ---- */
            if (buf.Count < payloadLenOffset + 4) return false;

            int payloadLen = BitConverter.ToInt32(buf.GetRange(payloadLenOffset, 4).ToArray(), 0);
            int totalPlain = PlainHeaderSize + markerLen + payloadLen;

            /* ---- 判断整个包是否收全 ---- */
            if (buf.Count < totalPlain) return false;

            /* ---- 解析 marker 与 payload ---- */
            marker = Encoding.UTF8.GetString(buf.GetRange(4, markerLen).ToArray());
            payloadOrCipher = buf.GetRange(payloadLenOffset + 4, payloadLen).ToArray();

            buf.RemoveRange(0, totalPlain);                       // 从缓冲区移除已消费字节
            return true;
        }
        #endregion
    }
    #endregion

    #region 连接封装类（客户端/服务器共用）
    /// <summary>
    /// <para>表示与单个客户端的长连接。</para>
    /// <para>核心职责：</para>
    /// <list type="bullet">
    ///   <item>维护底层 <see cref="STcpClient"/> 与 <see cref="NetworkStream"/>。</item>
    ///   <item>负责「收 → 解析 → 分发」的完整链路。</item>
    ///   <item>通过 <b>Channel 串行队列</b>，确保业务层回调顺序绝对一致，同步执行。</item>
    /// </list>
    /// <para>✧ 线程模型：</para>
    /// <list type="number">
    ///   <item><b>1× 接收线程</b>：阻塞式 Read，完成协议解析并把回调封装成 <see cref="Action"/> 写入 Channel。</item>
    ///   <item><b>1× 消费协程</b>：单线程读取 Channel，顺序执行业务回调，不阻塞接收线程。</item>
    /// </list>
    /// </summary>
    internal sealed class Connection {

        #region ------------ 依赖注入 & 对外暴露 ------------
        private readonly STcpClient socket;                     // TCP 客户端套接字
        private readonly NetworkStream stream;                  // 网络流
        private readonly Dictionary<string, PacketHandler> textHandlers;   // 文本包→处理器
        private readonly Dictionary<string, BytesPacketHandler> binHandlers; // 二进制包→处理器
        private readonly ErrorHandler onError;                  // 错误通知回调
        private readonly EncryptionHandler encrypt;             // 上行加密（Send 时）
        private readonly DecryptionHandler decrypt;             // 下行解密（Receive 时）
        #endregion

        #region ------------ 与接收相关的字段 ------------
        private readonly List<byte> recvBuf = new(8192);        // 动态接收缓存，专属于 rxThread
        private readonly byte[]     readBuf = new byte[8192];   // 承接 NetworkStream.Read 的临时缓冲
        private readonly Thread     rxThread;                   // 接收线程
        private volatile bool       connected = true;           // 连接状态标记（多线程可见）
        #endregion

        #region ------------ 串行业务队列相关字段 ------------
        /// <summary>
        /// 业务调用队列。<br/>
        /// ✧ SingleReader=true ⇒ 同一时间只会有一个消费者 ⇒ 顺序保证。<br/>
        /// ✧ Unbounded ⇒ 无上限，保证接收线程永不因满队列阻塞。
        /// </summary>
        private readonly Channel<Action> workQueue =
            Channel.CreateUnbounded<Action>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false
            });

        private readonly Task workerTask;                       // 消费协程入口（绑定到线程池）
        #endregion

        #region ------------ 发送相关字段 ------------
        /// <summary>
        /// 写流互斥锁。防止多线程并发调用 <see cref="Send(string,string,bool)"/> 时导致底层流写入交叉。
        /// </summary>
        private readonly object writeLock = new();
        #endregion

        /// <summary>此连接对应的客户端信息。</summary>
        public ClientInfo Info { get; }

        /// <summary>连接断开（Dispose）时触发，内部用以自清理。</summary>
        public event Action<Connection> Disconnected;

        #region ====== 构造 & 启动 ======
        /// <summary>
        /// 创建并立即启动 <see cref="Connection"/>。  
        /// <paramref name="txt"/> / <paramref name="bin"/> 可为空，内部自动判空。
        /// </summary>
        public Connection(
            STcpClient sock,
            Dictionary<string, PacketHandler> txt,
            Dictionary<string, BytesPacketHandler> bin,
            ErrorHandler err,
            EncryptionHandler enc,
            DecryptionHandler dec) {

            socket = sock ?? throw new ArgumentNullException(nameof(sock));
            stream = socket.GetStream();
            textHandlers = txt ?? new();
            binHandlers = bin ?? new();
            onError = err;
            encrypt = enc;
            decrypt = dec;

            // 生成客户端标识
            Info = new ClientInfo(Guid.NewGuid().ToString(),
                                  (IPEndPoint)socket.Client.RemoteEndPoint);

            /* ---------- 1) 启动接收线程 ---------- */
            rxThread = new Thread(ReceiveLoop) {
                IsBackground = true,
                Name = $"TCP-Rx-{Info.RemoteEndPoint.Address.ToString() + Info.Id}"
            };
            rxThread.Start();

            /* ---------- 2) 启动业务消费协程 ---------- */
            workerTask = Task.Run(ProcessQueueAsync);
        }
        #endregion

        #region ====== 发送 API ======
        /// <summary>发送 UTF-8 字符串数据包。</summary>
        /// <param name="marker">业务标记。</param>
        /// <param name="payload">正文字符串。</param>
        /// <param name="enc">是否对包体加密。</param>
        public void Send(string marker, string payload, bool enc = false) =>
            Send(marker, Encoding.UTF8.GetBytes(payload), enc, binary: false);

        /// <summary>发送二进制数据包。</summary>
        public void Send(string marker, byte[] data, bool enc = false) =>
            Send(marker, data, enc, binary: true);

        /// <summary>
        /// 实际发送实现：构帧 →（可选）加密 → 写入网络流。<br/>
        /// 采用 <see cref="lock"/> 保证多线程调用时字节序列不交叉。
        /// </summary>
        private void Send(string marker, byte[] data, bool enc, bool binary) {
            if (!connected) return;

            try {
                var packet = PacketFraming.Build(marker, data, binary, enc, encrypt);

                lock (writeLock) {                    // ✧ 临界区：写网络流
                    stream.Write(packet, 0, packet.Length);
                }
            } catch (Exception ex) {
                HandleError(ex);
                Disconnect();                         // ⚠️ 异常 ⇒ 主动断开
            }
        }
        #endregion

        #region ====== 接收循环 ======
        /// <summary>
        /// ① 阻塞式 <see cref="NetworkStream.Read"/> 把字节流写入 <see cref="recvBuf"/>  
        /// ② 调用 <c>PacketFraming.TryParse</c> 连续拆出完整包  
        /// ③ 针对每个包 <b>只做轻量工作</b>：解密、编码转换、封装 Action 并 <c>TryWrite</c> 到 <see cref="workQueue"/>  
        /// </summary>
        private void ReceiveLoop() {
            try {
                while (connected) {
                    int read = stream.Read(readBuf, 0, readBuf.Length);
                    if (read == 0) break;                 // 远端优雅关闭（FIN）

                    // 1️ 累加到接收缓存
                    recvBuf.AddRange(new ArraySegment<byte>(readBuf, 0, read));

                    // 2️ 循环解析，直到缓冲区不足一个完整包
                    while (PacketFraming.TryParse(
                               recvBuf,
                               out bool bin,
                               out bool enc,
                               out string marker,
                               out byte[] payload)) {

                        /* ---------- 解密（若需要） ---------- */
                        if (enc && decrypt != null) {
                            try { payload = decrypt(payload); } catch (Exception ex) {
                                HandleError(new Exception("Decryption failed", ex));
                                continue;                // 跳过此包
                            }

                            // 重新拆 Marker + Payload（自定义内部格式：2 + mLen + 4 + pLen）
                            if (payload.Length < 2 + 4) continue;      // 数据异常

                            ushort mLen = BitConverter.ToUInt16(payload, 0);
                            int    pLen = BitConverter.ToInt32(payload, 2 + mLen);
                            if (payload.Length < 2 + mLen + 4 + pLen) continue;

                            marker = Encoding.UTF8.GetString(payload, 2, mLen);
                            var real = new byte[pLen];
                            Buffer.BlockCopy(payload, 2 + mLen + 4, real, 0, pLen);
                            payload = real;
                        }

                        /* ---------- 3 入队（串行执行） ---------- */
                        EnqueueHandler(bin, marker, payload);
                    }
                }
            } catch (Exception ex) {
                HandleError(ex);
            } finally {
                Disconnect();     // 退出循环 ⇒ 确保资源释放
            }
        }
        #endregion

        #region ====== 业务入队 / 消费 ======
        /// <summary>
        /// 根据包类型获取对应处理器，封装为 <see cref="Action"/> 并写入 <see cref="workQueue"/>。
        /// </summary>
        private void EnqueueHandler(bool bin, string marker, byte[] payload) {
            Action action;

            if (bin) {
                if (!binHandlers.TryGetValue(marker, out var h) || h == null) return;
                action = () => {
                    try { 
                        h.Invoke(Info, marker, payload); 
                    } catch { 
                    
                    }
                };
            } else {
                if (!textHandlers.TryGetValue(marker, out var h) || h == null) return;
                string text = Encoding.UTF8.GetString(payload);
                action = () => {
                    try { 
                        h.Invoke(Info, marker, text); 
                    } catch {
                    
                    }
                };
            }

            workQueue.Writer.TryWrite(action);   // Writer 完成时（连接断开）返回 false
        }

        /// <summary>
        /// 单消费者协程：<c>await</c> + <c>TryRead</c>，确保严格顺序执行队列中的回调。
        /// </summary>
        private async Task ProcessQueueAsync() {
            try {
                var reader = workQueue.Reader;

                // WaitToReadAsync() 可中断于 Complete
                while (await reader.WaitToReadAsync()) {
                    while (reader.TryRead(out var action)) {
                        action();                     // ⚠️ 同步执行，阻塞直到业务结束
                    }
                }
            } catch (Exception ex) {
                HandleError(ex);
            }
        }
        #endregion

        #region ====== 断开与错误处理 ======
        /// <summary>
        /// 主动 / 被动断开时调用。线程安全，幂等。
        /// </summary>
        public void Disconnect() {
            if (!connected) return;
            connected = false;
    
            try {
                socket?.Close();// 关闭网络资源
                workQueue.Writer.TryComplete();// 通知 Channel：不再写入，令消费协程优雅退出
                Disconnected?.Invoke(this);// 触发外部事件
            } catch { }

        }

        /// <summary>对错误做统一包装转发，避免 try/catch 重复。</summary>
        private void HandleError(Exception ex) {
            try { onError?.Invoke(ex); } catch { /* 避免递归异常 */ }
        }
        #endregion
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
        public bool Connect(string host, int port) {
            var sock = new STcpClient();
            try {
                sock.Connect(host, port);
            } catch (Exception ex) {
                onError?.Invoke(ex);
                return false;
            }
            conn = new Connection(sock, textHandlers, binHandlers, onError, encrypt, decrypt);
            return true;
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