using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NetWork {
    public delegate void PacketHandler(string packetMarker, string payload);
    public delegate void ErrorHandler(Exception ex);

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
                if (packetHandlers.ContainsKey(packetMarker)) {
                    packetHandlers[packetMarker] += handler;
                } else {
                    packetHandlers.Add(packetMarker, handler);
                }
            }

            public void UnregisterHandler(string packetMarker) {
                if (packetHandlers.ContainsKey(packetMarker)) {
                    packetHandlers.Remove(packetMarker);
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

                        if (packetHandlers.TryGetValue(packetMarker, out PacketHandler handler)) {
                            try {
                                handler.Invoke(packetMarker, payload);
                            } catch (Exception ex) {
                                errorHandler(ex);
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

        public void RegisterHandler(string packetMarker, PacketHandler handler) {
            server.RegisterHandler(packetMarker, handler);
        }

        public void UnregisterHandler(string packetMarker) {
            server.UnregisterHandler(packetMarker);
        }

        private class TcpServerInternal {
            private TcpListener listener;
            private Thread acceptThread;
            private bool isRunning;
            private readonly ErrorHandler errorHandler;
            private readonly Dictionary<string, PacketHandler> packetHandlers;
            private readonly List<ClientHandler> clients;

            public TcpServerInternal(ErrorHandler errorHandler) {
                this.errorHandler = errorHandler;
                packetHandlers = new Dictionary<string, PacketHandler>();
                clients = new List<ClientHandler>();
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

                lock (clients) {
                    foreach (var client in clients) {
                        client.Disconnect();
                    }
                    clients.Clear();
                }

                acceptThread?.Join();
            }

            public void BroadcastPacket(string packetMarker, string payload) {
                lock (clients) {
                    foreach (var client in clients) {
                        client.SendPacket(packetMarker, payload);
                    }
                }
            }

            public void RegisterHandler(string packetMarker, PacketHandler handler) {
                if (packetHandlers.ContainsKey(packetMarker)) {
                    packetHandlers[packetMarker] += handler;
                } else {
                    packetHandlers.Add(packetMarker, handler);
                }
            }

            public void UnregisterHandler(string packetMarker) {
                if (packetHandlers.ContainsKey(packetMarker)) {
                    packetHandlers.Remove(packetMarker);
                }
            }

            private void AcceptClients() {
                while (isRunning) {
                    try {
                        System.Net.Sockets.TcpClient clientSocket = listener.AcceptTcpClient();
                        ClientHandler clientHandler = new ClientHandler(clientSocket, packetHandlers, errorHandler);

                        lock (clients) {
                            clients.Add(clientHandler);
                        }
                    } catch (Exception ex) {
                        if (isRunning) {
                            errorHandler(ex);
                        }
                    }
                }
            }

            private class ClientHandler {
                private readonly System.Net.Sockets.TcpClient socket;
                private readonly NetworkStream stream;
                private readonly Thread receiveThread;
                private bool isConnected;
                private readonly Dictionary<string, PacketHandler> packetHandlers;
                private readonly ErrorHandler errorHandler;

                public ClientHandler(System.Net.Sockets.TcpClient socket, Dictionary<string, PacketHandler> packetHandlers, ErrorHandler errorHandler) {
                    this.socket = socket;
                    this.stream = socket.GetStream();
                    this.packetHandlers = packetHandlers;
                    this.errorHandler = errorHandler;
                    isConnected = true;

                    receiveThread = new Thread(new ThreadStart(ReceiveData));
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
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

                            if (packetHandlers.TryGetValue(packetMarker, out PacketHandler handler)) {
                                try {
                                    handler.Invoke(packetMarker, payload);
                                } catch (Exception ex) {
                                    errorHandler(ex);
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
