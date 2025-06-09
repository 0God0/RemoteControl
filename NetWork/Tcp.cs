using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetWork {
    public delegate void PacketHandler(ClientInfo client, string packetMarker, string payload);
    public delegate void ConnectionHandler(ClientInfo client);
    public delegate void ErrorHandler(Exception ex);

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

        public void SendPacket(string packetMarker, string payload) {
            client.SendPacket(packetMarker, payload);
        }

        public void RegisterHandler(string packetMarker, PacketHandler handler) {
            client.RegisterHandler(packetMarker, handler);
        }

        public void UnregisterHandler(string packetMarker) {
            client.UnregisterHandler(packetMarker);
        }

        private class TcpClientInternal {
            private System.Net.Sockets.TcpClient socket;
            private NetworkStream stream;
            private Thread receiveThread;
            private bool isConnected;
            private readonly ErrorHandler errorHandler;
            private readonly Dictionary<string, PacketHandler> packetHandlers;
            private ClientInfo clientInfo;

            public TcpClientInternal(ErrorHandler errorHandler) {
                this.errorHandler = errorHandler;
                packetHandlers = new Dictionary<string, PacketHandler>();
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
                stream?.Close();
                socket?.Close();
                receiveThread?.Join();
            }

            public void SendPacket(string packetMarker, string payload) {
                if (!isConnected) return;

                try {
                    string formattedPacket = $"{packetMarker}|{payload}\n";
                    byte[] data = Encoding.UTF8.GetBytes(formattedPacket);
                    stream.Write(data, 0, data.Length);
                } catch (Exception ex) {
                    errorHandler(ex);
                    Disconnect();
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

            public void UnregisterHandler(string packetMarker) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers.Remove(packetMarker);
                    }
                }
            }

            private void ReceiveData() {
                byte[] buffer = new byte[4096];
                StringBuilder receivedData = new StringBuilder();

                while (isConnected) {
                    try {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) {
                            Disconnect();
                            break;
                        }

                        receivedData.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                        ProcessReceivedData(receivedData);
                    } catch (Exception ex) {
                        errorHandler(ex);
                        Disconnect();
                    }
                }
            }

            private void ProcessReceivedData(StringBuilder receivedData) {
                string dataStr = receivedData.ToString();
                int newLineIndex;

                while ((newLineIndex = dataStr.IndexOf('\n')) >= 0) {
                    string packet = dataStr.Substring(0, newLineIndex);
                    dataStr = dataStr.Substring(newLineIndex + 1);

                    int separatorIndex = packet.IndexOf('|');
                    if (separatorIndex >= 0) {
                        string packetMarker = packet.Substring(0, separatorIndex);
                        string payload = packet.Substring(separatorIndex + 1);

                        PacketHandler handler = null;
                        lock (packetHandlers) {
                            if (packetHandlers.TryGetValue(packetMarker, out handler)) {
                                // 使用线程池非阻塞处理
                                Task.Run(() => {
                                    try {
                                        handler.Invoke(clientInfo, packetMarker, payload);
                                    } catch (Exception ex) {
                                        errorHandler(ex);
                                    }
                                });
                            }
                        }
                    }
                }

                receivedData.Clear();
                receivedData.Append(dataStr);
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

        public void BroadcastPacket(string packetMarker, string payload) {
            server.BroadcastPacket(packetMarker, payload);
        }

        public void SendToClient(string clientId, string packetMarker, string payload) {
            server.SendToClient(clientId, packetMarker, payload);
        }

        public void RegisterHandler(string packetMarker, PacketHandler handler) {
            server.RegisterHandler(packetMarker, handler);
        }

        public void UnregisterHandler(string packetMarker) {
            server.UnregisterHandler(packetMarker);
        }

        public void RegisterConnectionHandler(ConnectionHandler handler) {
            server.RegisterConnectionHandler(handler);
        }

        public void RegisterDisconnectionHandler(ConnectionHandler handler) {
            server.RegisterDisconnectionHandler(handler);
        }

        private class TcpServerInternal {
            private TcpListener listener;
            private Thread acceptThread;
            private bool isRunning;
            private readonly ErrorHandler errorHandler;
            private readonly Dictionary<string, PacketHandler> packetHandlers;
            private readonly ConcurrentDictionary<string, ClientHandler> clients;
            private ConnectionHandler connectionHandler;
            private ConnectionHandler disconnectionHandler;

            public TcpServerInternal(ErrorHandler errorHandler) {
                this.errorHandler = errorHandler;
                packetHandlers = new Dictionary<string, PacketHandler>();
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

            public void BroadcastPacket(string packetMarker, string payload) {
                foreach (var client in clients.Values) {
                    client.SendPacket(packetMarker, payload);
                }
            }

            public void SendToClient(string clientId, string packetMarker, string payload) {
                if (clients.TryGetValue(clientId, out ClientHandler client)) {
                    client.SendPacket(packetMarker, payload);
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

            public void UnregisterHandler(string packetMarker) {
                lock (packetHandlers) {
                    if (packetHandlers.ContainsKey(packetMarker)) {
                        packetHandlers.Remove(packetMarker);
                    }
                }
            }

            public void RegisterConnectionHandler(ConnectionHandler handler) {
                connectionHandler += handler;
            }

            public void RegisterDisconnectionHandler(ConnectionHandler handler) {
                disconnectionHandler += handler;
            }

            private void AcceptClients() {
                while (isRunning) {
                    try {
                        System.Net.Sockets.TcpClient clientSocket = listener.AcceptTcpClient();
                        var clientHandler = new ClientHandler(
                            clientSocket,
                            packetHandlers,
                            errorHandler,
                            OnClientDisconnected
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
                clients.TryRemove(client.ClientInfo.Id, out _);

                disconnectionHandler?.Invoke(client.ClientInfo);
            }

            private class ClientHandler {
                private readonly System.Net.Sockets.TcpClient socket;
                private readonly NetworkStream stream;
                private readonly Thread receiveThread;
                private bool isConnected;
                private readonly Dictionary<string, PacketHandler> packetHandlers;
                private readonly ErrorHandler errorHandler;
                private readonly Action<ClientHandler> disconnectCallback;

                public ClientInfo ClientInfo { get; }

                public ClientHandler(
                    System.Net.Sockets.TcpClient socket,
                    Dictionary<string, PacketHandler> packetHandlers,
                    ErrorHandler errorHandler,
                    Action<ClientHandler> disconnectCallback) {
                    this.socket = socket;
                    this.stream = socket.GetStream();
                    this.packetHandlers = packetHandlers;
                    this.errorHandler = errorHandler;
                    this.disconnectCallback = disconnectCallback;
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
                    stream?.Close();
                    socket?.Close();

                    try {
                        receiveThread?.Join(1000);
                    } catch { }

                    disconnectCallback?.Invoke(this);
                }

                public void SendPacket(string packetMarker, string payload) {
                    if (!isConnected) return;

                    try {
                        string formattedPacket = $"{packetMarker}|{payload}\n";
                        byte[] data = Encoding.UTF8.GetBytes(formattedPacket);
                        stream.Write(data, 0, data.Length);
                    } catch (Exception ex) {
                        errorHandler(ex);
                        Disconnect();
                    }
                }

                private void ReceiveData() {
                    byte[] buffer = new byte[4096];
                    StringBuilder receivedData = new StringBuilder();

                    while (isConnected) {
                        try {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) {
                                Disconnect();
                                break;
                            }

                            receivedData.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                            ProcessReceivedData(receivedData);
                        } catch (Exception ex) {
                            errorHandler(ex);
                            Disconnect();
                            break;
                        }
                    }
                }

                private void ProcessReceivedData(StringBuilder receivedData) {
                    string dataStr = receivedData.ToString();
                    int newLineIndex;

                    while ((newLineIndex = dataStr.IndexOf('\n')) >= 0) {
                        string packet = dataStr.Substring(0, newLineIndex);
                        dataStr = dataStr.Substring(newLineIndex + 1);

                        int separatorIndex = packet.IndexOf('|');
                        if (separatorIndex >= 0) {
                            string packetMarker = packet.Substring(0, separatorIndex);
                            string payload = packet.Substring(separatorIndex + 1);

                            PacketHandler handler = null;
                            lock (packetHandlers) {
                                if (packetHandlers.TryGetValue(packetMarker, out handler)) {
                                    Task.Run(() => {
                                        try {
                                            handler.Invoke(ClientInfo, packetMarker, payload);
                                        } catch (Exception ex) {
                                            errorHandler(ex);
                                        }
                                    });
                                }
                            }
                        }
                    }

                    receivedData.Clear();
                    receivedData.Append(dataStr);
                }
            }
        }
    }
}