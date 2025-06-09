using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetWork {
    public delegate void PacketHandler(ClientInfo client, string packetMarker, string payload);
    public delegate void BytesPacketHandler(ClientInfo client, string packetMarker, byte[] data);
    public delegate void ConnectionHandler(ClientInfo client);
    public delegate void DisconnectionHandler(ClientInfo client);
    public delegate void ErrorHandler(Exception ex);
    public delegate byte[] EncryptionHandler(byte[] data);
    public delegate byte[] DecryptionHandler(byte[] data);

    public class ClientInfo {
        public string Id { get; }
        public IPEndPoint RemoteEndPoint { get; }
        public object Tag { get; set; } // 用于存储自定义数据

        public ClientInfo(string id, IPEndPoint remoteEndPoint) {
            Id = id;
            RemoteEndPoint = remoteEndPoint;
        }
    }

    public class TcpClient {
        private TcpClientInternal client;
        private ErrorHandler errorHandler;

        public TcpClient(ErrorHandler errorHandler) {
            this.errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            this.client = new TcpClientInternal(errorHandler);
        }

        public void Connect(string ipAddress, int port) {
            client.Connect(ipAddress, port);
        }

        public void Disconnect() {
            client.Disconnect();
        }

        public void SendPacket(string packetMarker, string payload, bool encrypt = false) {
            client.SendPacket(packetMarker, payload, encrypt);
        }

        public void SendBytes(string packetMarker, byte[] data, bool encrypt = false) {
            client.SendBytes(packetMarker, data, encrypt);
        }

        public void RegisterHandler(string packetMarker, PacketHandler handler) {
            client.RegisterHandler(packetMarker, handler);
        }

        public void RegisterBytesHandler(string packetMarker, BytesPacketHandler handler) {
            client.RegisterBytesHandler(packetMarker, handler);
        }

        public void UnregisterHandler(string packetMarker) {
            client.UnregisterHandler(packetMarker);
        }

        public void UnregisterBytesHandler(string packetMarker) {
            client.UnregisterBytesHandler(packetMarker);
        }

        public void SetEncryption(EncryptionHandler encryptor, DecryptionHandler decryptor) {
            client.SetEncryption(encryptor, decryptor);
        }

        private class TcpClientInternal {
            private System.Net.Sockets.TcpClient socket;
            private NetworkStream stream;
            private Thread receiveThread;
            private bool isConnected;
            private readonly ErrorHandler errorHandler;
            private readonly Dictionary<string, PacketHandler> packetHandlers;
            private readonly Dictionary<string, BytesPacketHandler> bytesHandlers;
            private ClientInfo clientInfo;
            private EncryptionHandler encryptor;
            private DecryptionHandler decryptor;

            public TcpClientInternal(ErrorHandler errorHandler) {
                this.errorHandler = errorHandler;
                packetHandlers = new Dictionary<string, PacketHandler>();
                bytesHandlers = new Dictionary<string, BytesPacketHandler>();
            }

            public void Connect(string ipAddress, int port) {
                try {
                    socket = new System.Net.Sockets.TcpClient();
                    socket.Connect(ipAddress, port);
                    stream = socket.GetStream();
                    isConnected = true;

                    // 创建客户端信息
                    clientInfo = new ClientInfo(
                        Guid.NewGuid().ToString(),
                        (IPEndPoint)socket.Client.RemoteEndPoint
                    );

                    receiveThread = new Thread(new ThreadStart(ReceiveData));
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                } catch (Exception ex) {
                    errorHandler(ex);
                    Disconnect();
                }
            }

            public void Disconnect() {
                isConnected = false;
                try {
                    stream?.Close();
                    socket?.Close();
                    receiveThread?.Join(1000);
                } catch { }
            }

            public void SendPacket(string packetMarker, string payload, bool encrypt) {
                if (!isConnected) return;

                try {
                    // 将字符串转换为字节数组
                    byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                    SendInternal(packetMarker, payloadBytes, encrypt, false);
                } catch (Exception ex) {
                    errorHandler(ex);
                    Disconnect();
                }
            }

            public void SendBytes(string packetMarker, byte[] data, bool encrypt) {
                if (!isConnected) return;

                try {
                    SendInternal(packetMarker, data, encrypt, true);
                } catch (Exception ex) {
                    errorHandler(ex);
                    Disconnect();
                }
            }

            private void SendInternal(string packetMarker, byte[] payloadData, bool encrypt, bool isBinary) {
                // 协议格式: [1字节类型][1字节加密标志][2字节标记长度][标记][4字节负载长度][负载]
                // 类型: 0=字符串, 1=二进制

                byte[] markerBytes = Encoding.UTF8.GetBytes(packetMarker);
                byte[] payload = payloadData;

                // 如果需要加密
                if (encrypt && encryptor != null) {
                    payload = encryptor(payloadData);
                }

                // 创建数据包
                using (MemoryStream ms = new MemoryStream()) {
                    // 类型 (1字节)
                    ms.WriteByte(isBinary ? (byte)1 : (byte)0);

                    // 加密标志 (1字节)
                    ms.WriteByte(encrypt ? (byte)1 : (byte)0);

                    // 标记长度 (2字节)
                    byte[] markerLength = BitConverter.GetBytes((ushort)markerBytes.Length);
                    ms.Write(markerLength, 0, 2);

                    // 标记
                    ms.Write(markerBytes, 0, markerBytes.Length);

                    // 负载长度 (4字节)
                    byte[] payloadLength = BitConverter.GetBytes(payload.Length);
                    ms.Write(payloadLength, 0, 4);

                    // 负载
                    ms.Write(payload, 0, payload.Length);

                    // 发送数据
                    byte[] packetData = ms.ToArray();
                    stream.Write(packetData, 0, packetData.Length);
                }
            }

            public void RegisterHandler(string packetMarker, PacketHandler handler) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers[packetMarker] += handler;
                    } else {
                        packetHandlers.Add(packetMarker, handler);
                    }
                }
            }

            public void RegisterBytesHandler(string packetMarker, BytesPacketHandler handler) {
                lock (bytesHandlers) {
                    if (bytesHandlers.ContainsKey(packetMarker)) {
                        bytesHandlers[packetMarker] += handler;
                    } else {
                        bytesHandlers.Add(packetMarker, handler);
                    }
                }
            }

            public void UnregisterHandler(string packetMarker) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers.Remove(packetMarker);
                    }
                }
            }

            public void UnregisterBytesHandler(string packetMarker) {
                lock (bytesHandlers) {
                    if (bytesHandlers.ContainsKey(packetMarker)) {
                        bytesHandlers.Remove(packetMarker);
                    }
                }
            }

            public void SetEncryption(EncryptionHandler encryptor, DecryptionHandler decryptor) {
                this.encryptor = encryptor;
                this.decryptor = decryptor;
            }

            private void ReceiveData() {
                byte[] buffer = new byte[8192]; // 8KB缓冲区
                List<byte> receivedData = new List<byte>();
                int bytesRead;

                while (isConnected) {
                    try {
                        // 读取数据
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) {
                            Disconnect();
                            break;
                        }

                        // 添加到接收缓冲区
                        receivedData.AddRange(new ArraySegment<byte>(buffer, 0, bytesRead));

                        // 处理接收到的数据
                        ProcessReceivedData(receivedData);
                    } catch (Exception ex) {
                        errorHandler(ex);
                        Disconnect();
                        break;
                    }
                }
            }

            private void ProcessReceivedData(List<byte> receivedData) {
                // 需要至少8字节才能解析头部 (1+1+2+4=8字节)
                while (receivedData.Count >= 8) {
                    // 解析数据包类型
                    byte packetType = receivedData[0];
                    bool isBinary = packetType == 1;

                    // 解析加密标志
                    bool isEncrypted = receivedData[1] == 1;

                    // 解析标记长度
                    ushort markerLength = BitConverter.ToUInt16(receivedData.ToArray(), 2);

                    // 解析负载长度
                    int payloadLength = BitConverter.ToInt32(receivedData.ToArray(), 4);

                    // 计算整个数据包长度 (8字节头部 + 标记长度 + 负载长度)
                    int totalLength = 8 + markerLength + payloadLength;

                    // 如果数据不足，等待更多数据
                    if (receivedData.Count < totalLength) {
                        return;
                    }

                    // 提取标记
                    byte[] markerBytes = new byte[markerLength];
                    receivedData.CopyTo(4, markerBytes, 0, markerLength);
                    string packetMarker = Encoding.UTF8.GetString(markerBytes);

                    // 提取负载
                    byte[] payload = new byte[payloadLength];
                    receivedData.CopyTo(8 + markerLength, payload, 0, payloadLength);

                    // 如果需要解密
                    if (isEncrypted && decryptor != null) {
                        try {
                            payload = decryptor(payload);
                        } catch (Exception ex) {
                            errorHandler(new Exception("Decryption failed", ex));
                            // 移除已处理的数据
                            receivedData.RemoveRange(0, totalLength);
                            continue;
                        }
                    }

                    // 移除已处理的数据
                    receivedData.RemoveRange(0, totalLength);

                    // 根据类型分发处理
                    if (isBinary) {
                        BytesPacketHandler handler = null;
                        lock (bytesHandlers) {
                            bytesHandlers.TryGetValue(packetMarker, out handler);
                        }

                        if (handler != null) {
                            // 使用线程池处理二进制数据
                            Task.Run(() => {
                                try {
                                    handler.Invoke(clientInfo, packetMarker, payload);
                                } catch (Exception ex) {
                                    errorHandler(ex);
                                }
                            });
                        }
                    } else {
                        PacketHandler handler = null;
                        lock (packetHandlers) {
                            packetHandlers.TryGetValue(packetMarker, out handler);
                        }

                        if (handler != null) {
                            // 将字节数组转换为字符串
                            string payloadStr = Encoding.UTF8.GetString(payload);

                            // 使用线程池处理字符串数据
                            Task.Run(() => {
                                try {
                                    handler.Invoke(clientInfo, packetMarker, payloadStr);
                                } catch (Exception ex) {
                                    errorHandler(ex);
                                }
                            });
                        }
                    }
                }
            }
        }
    }

    public class TcpServer {
        private TcpServerInternal server;
        private ErrorHandler errorHandler;

        public TcpServer(ErrorHandler errorHandler) {
            this.errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            this.server = new TcpServerInternal(errorHandler);
        }

        public void Start(int port) {
            server.Start(port);
        }

        public void Stop() {
            server.Stop();
        }

        public void BroadcastPacket(string packetMarker, string payload, bool encrypt = false) {
            server.BroadcastPacket(packetMarker, payload, encrypt);
        }

        public void BroadcastBytes(string packetMarker, byte[] data, bool encrypt = false) {
            server.BroadcastBytes(packetMarker, data, encrypt);
        }

        public void SendToClient(string clientId, string packetMarker, string payload, bool encrypt = false) {
            server.SendToClient(clientId, packetMarker, payload, encrypt);
        }

        public void SendBytesToClient(string clientId, string packetMarker, byte[] data, bool encrypt = false) {
            server.SendBytesToClient(clientId, packetMarker, data, encrypt);
        }

        public void RegisterHandler(string packetMarker, PacketHandler handler) {
            server.RegisterHandler(packetMarker, handler);
        }

        public void RegisterBytesHandler(string packetMarker, BytesPacketHandler handler) {
            server.RegisterBytesHandler(packetMarker, handler);
        }

        public void UnregisterHandler(string packetMarker) {
            server.UnregisterHandler(packetMarker);
        }

        public void UnregisterBytesHandler(string packetMarker) {
            server.UnregisterBytesHandler(packetMarker);
        }

        public void RegisterConnectionHandler(ConnectionHandler handler) {
            server.RegisterConnectionHandler(handler);
        }

        public void RegisterDisconnectionHandler(DisconnectionHandler handler) {
            server.RegisterDisconnectionHandler(handler);
        }

        public void SetEncryption(EncryptionHandler encryptor, DecryptionHandler decryptor) {
            server.SetEncryption(encryptor, decryptor);
        }

        private class TcpServerInternal {
            private TcpListener listener;
            private Thread acceptThread;
            private bool isRunning;
            private readonly ErrorHandler errorHandler;
            private readonly Dictionary<string, PacketHandler> packetHandlers;
            private readonly Dictionary<string, BytesPacketHandler> bytesHandlers;
            private readonly ConcurrentDictionary<string, ClientHandler> clients;
            private ConnectionHandler connectionHandler;
            private DisconnectionHandler disconnectionHandler;
            private EncryptionHandler encryptor;
            private DecryptionHandler decryptor;

            public TcpServerInternal(ErrorHandler errorHandler) {
                this.errorHandler = errorHandler;
                packetHandlers = new Dictionary<string, PacketHandler>();
                bytesHandlers = new Dictionary<string, BytesPacketHandler>();
                clients = new ConcurrentDictionary<string, ClientHandler>();
            }

            public void Start(int port) {
                try {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    isRunning = true;

                    acceptThread = new Thread(new ThreadStart(AcceptClients));
                    acceptThread.IsBackground = true;
                    acceptThread.Start();
                } catch (Exception ex) {
                    errorHandler(ex);
                    Stop();
                }
            }

            public void Stop() {
                isRunning = false;
                listener?.Stop();

                foreach (var client in clients.Values) {
                    client.Disconnect();
                }
                clients.Clear();

                acceptThread?.Join();
            }

            public void BroadcastPacket(string packetMarker, string payload, bool encrypt) {
                foreach (var client in clients.Values) {
                    client.SendPacket(packetMarker, payload, encrypt);
                }
            }

            public void BroadcastBytes(string packetMarker, byte[] data, bool encrypt) {
                foreach (var client in clients.Values) {
                    client.SendBytes(packetMarker, data, encrypt);
                }
            }

            public void SendToClient(string clientId, string packetMarker, string payload, bool encrypt) {
                if (clients.TryGetValue(clientId, out ClientHandler client)) {
                    client.SendPacket(packetMarker, payload, encrypt);
                }
            }

            public void SendBytesToClient(string clientId, string packetMarker, byte[] data, bool encrypt) {
                if (clients.TryGetValue(clientId, out ClientHandler client)) {
                    client.SendBytes(packetMarker, data, encrypt);
                }
            }

            public void RegisterHandler(string packetMarker, PacketHandler handler) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers[packetMarker] += handler;
                    } else {
                        packetHandlers.Add(packetMarker, handler);
                    }
                }
            }

            public void RegisterBytesHandler(string packetMarker, BytesPacketHandler handler) {
                lock (bytesHandlers) {
                    if (bytesHandlers.ContainsKey(packetMarker)) {
                        bytesHandlers[packetMarker] += handler;
                    } else {
                        bytesHandlers.Add(packetMarker, handler);
                    }
                }
            }

            public void UnregisterHandler(string packetMarker) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers.Remove(packetMarker);
                    }
                }
            }

            public void UnregisterBytesHandler(string packetMarker) {
                lock (bytesHandlers) {
                    if (bytesHandlers.ContainsKey(packetMarker)) {
                        bytesHandlers.Remove(packetMarker);
                    }
                }
            }

            public void RegisterConnectionHandler(ConnectionHandler handler) {
                connectionHandler += handler;
            }

            public void RegisterDisconnectionHandler(DisconnectionHandler handler) {
                disconnectionHandler += handler;
            }

            public void SetEncryption(EncryptionHandler encryptor, DecryptionHandler decryptor) {
                this.encryptor = encryptor;
                this.decryptor = decryptor;
            }

            private void AcceptClients() {
                while (isRunning) {
                    try {
                        System.Net.Sockets.TcpClient clientSocket = listener.AcceptTcpClient();
                        var clientHandler = new ClientHandler(
                            clientSocket,
                            packetHandlers,
                            bytesHandlers,
                            errorHandler,
                            OnClientDisconnected,
                            encryptor,
                            decryptor
                        );

                        // 添加客户端到列表
                        clients.TryAdd(clientHandler.ClientInfo.Id, clientHandler);

                        // 触发连接事件
                        connectionHandler?.Invoke(clientHandler.ClientInfo);
                    } catch (Exception ex) {
                        if (isRunning) {
                            errorHandler(ex);
                        }
                    }
                }
            }

            private void OnClientDisconnected(ClientHandler client) {
                // 从客户端列表移除
                clients.TryRemove(client.ClientInfo.Id, out _);

                // 触发断开连接事件
                disconnectionHandler?.Invoke(client.ClientInfo);
            }

            private class ClientHandler {
                private readonly System.Net.Sockets.TcpClient socket;
                private readonly NetworkStream stream;
                private readonly Thread receiveThread;
                private bool isConnected;
                private readonly Dictionary<string, PacketHandler> packetHandlers;
                private readonly Dictionary<string, BytesPacketHandler> bytesHandlers;
                private readonly ErrorHandler errorHandler;
                private readonly Action<ClientHandler> disconnectCallback;
                private readonly EncryptionHandler encryptor;
                private readonly DecryptionHandler decryptor;

                public ClientInfo ClientInfo { get; }

                public ClientHandler(
                    System.Net.Sockets.TcpClient socket,
                    Dictionary<string, PacketHandler> packetHandlers,
                    Dictionary<string, BytesPacketHandler> bytesHandlers,
                    ErrorHandler errorHandler,
                    Action<ClientHandler> disconnectCallback,
                    EncryptionHandler encryptor,
                    DecryptionHandler decryptor) {
                    this.socket = socket;
                    this.stream = socket.GetStream();
                    this.packetHandlers = packetHandlers;
                    this.bytesHandlers = bytesHandlers;
                    this.errorHandler = errorHandler;
                    this.disconnectCallback = disconnectCallback;
                    this.encryptor = encryptor;
                    this.decryptor = decryptor;
                    isConnected = true;

                    // 创建客户端信息
                    ClientInfo = new ClientInfo(
                        Guid.NewGuid().ToString(),
                        (IPEndPoint)socket.Client.RemoteEndPoint
                    );

                    receiveThread = new Thread(new ThreadStart(ReceiveData));
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }

                public void Disconnect() {
                    if (!isConnected) return;

                    isConnected = false;
                    try {
                        stream?.Close();
                        socket?.Close();
                        receiveThread?.Join(1000);
                    } catch { }

                    disconnectCallback?.Invoke(this);
                }

                public void SendPacket(string packetMarker, string payload, bool encrypt) {
                    if (!isConnected) return;

                    try {
                        // 将字符串转换为字节数组
                        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                        SendInternal(packetMarker, payloadBytes, encrypt, false);
                    } catch (Exception ex) {
                        errorHandler(ex);
                        Disconnect();
                    }
                }

                public void SendBytes(string packetMarker, byte[] data, bool encrypt) {
                    if (!isConnected) return;

                    try {
                        SendInternal(packetMarker, data, encrypt, true);
                    } catch (Exception ex) {
                        errorHandler(ex);
                        Disconnect();
                    }
                }

                private void SendInternal(string packetMarker, byte[] payloadData, bool encrypt, bool isBinary) {
                    // 协议格式: [1字节类型][1字节加密标志][2字节标记长度][标记][4字节负载长度][负载]
                    // 类型: 0=字符串, 1=二进制

                    byte[] markerBytes = Encoding.UTF8.GetBytes(packetMarker);
                    byte[] payload = payloadData;

                    // 如果需要加密
                    if (encrypt && encryptor != null) {
                        payload = encryptor(payloadData);
                    }

                    // 创建数据包
                    using (MemoryStream ms = new MemoryStream()) {
                        // 类型 (1字节)
                        ms.WriteByte(isBinary ? (byte)1 : (byte)0);

                        // 加密标志 (1字节)
                        ms.WriteByte(encrypt ? (byte)1 : (byte)0);

                        // 标记长度 (2字节)
                        byte[] markerLength = BitConverter.GetBytes((ushort)markerBytes.Length);
                        ms.Write(markerLength, 0, 2);

                        // 标记
                        ms.Write(markerBytes, 0, markerBytes.Length);

                        // 负载长度 (4字节)
                        byte[] payloadLength = BitConverter.GetBytes(payload.Length);
                        ms.Write(payloadLength, 0, 4);

                        // 负载
                        ms.Write(payload, 0, payload.Length);

                        // 发送数据
                        byte[] packetData = ms.ToArray();
                        stream.Write(packetData, 0, packetData.Length);
                    }
                }

                private void ReceiveData() {
                    byte[] buffer = new byte[8192]; // 8KB缓冲区
                    List<byte> receivedData = new List<byte>();
                    int bytesRead = 0;

                    while (isConnected) {
                        try {
                            Debug.WriteLine($"1 {bytesRead}");
                            // 读取数据
                            bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) {
                                Disconnect();
                                break;
                            }
                            Debug.WriteLine($"2 {bytesRead}");

                            // 添加到接收缓冲区
                            receivedData.AddRange(new ArraySegment<byte>(buffer, 0, bytesRead));

                            // 处理接收到的数据
                            ProcessReceivedData(receivedData);
                        } catch (Exception ex) {
                            errorHandler(ex);
                            Disconnect();
                            break;
                        }
                    }
                }

                private void ProcessReceivedData(List<byte> receivedData) {
                    // 需要至少8字节才能解析头部 (1+1+2+4=8字节)
                    while (receivedData.Count >= 8) {
                        // 解析数据包类型
                        byte packetType = receivedData[0];
                        bool isBinary = packetType == 1;

                        // 解析加密标志
                        bool isEncrypted = receivedData[1] == 1;

                        // 解析标记长度
                        ushort markerLength = BitConverter.ToUInt16(receivedData.ToArray(), 2);

                        // 解析负载长度
                        int payloadLength = BitConverter.ToInt32(receivedData.ToArray(), 4);

                        // 计算整个数据包长度 (8字节头部 + 标记长度 + 负载长度)
                        int totalLength = 8 + markerLength + payloadLength;

                        // 如果数据不足，等待更多数据
                        if (receivedData.Count < totalLength) {
                            return;
                        }

                        // 提取标记
                        byte[] markerBytes = new byte[markerLength];
                        receivedData.CopyTo(4, markerBytes, 0, markerLength);
                        string packetMarker = Encoding.UTF8.GetString(markerBytes);

                        // 提取负载
                        byte[] payload = new byte[payloadLength];
                        receivedData.CopyTo(8 + markerLength, payload, 0, payloadLength);

                        // 如果需要解密
                        if (isEncrypted && decryptor != null) {
                            try {
                                payload = decryptor(payload);
                            } catch (Exception ex) {
                                errorHandler(new Exception("Decryption failed", ex));
                                // 移除已处理的数据
                                receivedData.RemoveRange(0, totalLength);
                                continue;
                            }
                        }

                        // 移除已处理的数据
                        receivedData.RemoveRange(0, totalLength);

                        // 根据类型分发处理
                        if (isBinary) {
                            BytesPacketHandler handler = null;
                            lock (bytesHandlers) {
                                bytesHandlers.TryGetValue(packetMarker, out handler);
                            }

                            if (handler != null) {
                                // 使用线程池处理二进制数据
                                Task.Run(() => {
                                    try {
                                        handler.Invoke(ClientInfo, packetMarker, payload);
                                    } catch (Exception ex) {
                                        errorHandler(ex);
                                    }
                                });
                            }
                        } else {
                            PacketHandler handler = null;
                            lock (packetHandlers) {
                                packetHandlers.TryGetValue(packetMarker, out handler);
                            }

                            if (handler != null) {
                                // 将字节数组转换为字符串
                                string payloadStr = Encoding.UTF8.GetString(payload);

                                // 使用线程池处理字符串数据
                                Task.Run(() => {
                                    try {
                                        handler.Invoke(ClientInfo, packetMarker, payloadStr);
                                    } catch (Exception ex) {
                                        errorHandler(ex);
                                    }
                                });
                            }
                        }
                    }
                }
            }
        }
    }
}