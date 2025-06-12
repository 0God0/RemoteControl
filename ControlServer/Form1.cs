using Microsoft.VisualBasic.Devices;
using NetWork;
using System.Diagnostics;
using System.Text;

namespace ControlServer {
    public partial class Form1 : Form {

        TcpServer server;

        public Form1() {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e) {
            CheckForIllegalCrossThreadCalls = false;
            server = new NetWork.TcpServer(ex => infoBox.Text += ($"Server error: {ex.Message}"));
            server.onDisconnect += (client => {
                infoBox.Text += ($"Client disconnected: {client.RemoteEndPoint}" + Environment.NewLine);
                Debug.WriteLine($"Client disconnected: {client.RemoteEndPoint}" + Environment.NewLine);
            });

            // 设置AES加密
            var key = Encoding.UTF8.GetBytes("1234567891234567");
            server.SetEncryption(
                data => CAes.AesEncrypt(data, key),
                data => CAes.AesDecrypt(data, key)
            );

            server.onConnect += (client => {
                infoBox.Text += ($"Client connected: {client.RemoteEndPoint}" + Environment.NewLine);
                Debug.WriteLine($"Client connected: {client.RemoteEndPoint}" + Environment.NewLine);
            });


            server.RegisterHandler("MSG", (client, marker, payload) => {
                //infoBox.Text+=($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine);
                Debug.WriteLine($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine);
                server.SendToClient(client.Id, "REPLY", $"Echo: {payload}", false);
            });

            server.EnableFileReceiver(@"D:\Incoming");     // 保存到 D:\Incoming
            server.FileReceived += (c, p) => Console.WriteLine("已存: " + p);

            server.Start(11451);


        }

        private void StartServer_Click(object sender, EventArgs e) {

            //Console.WriteLine("Server started. Press any key to stop...");
            //Console.ReadKey();
        }

        private void infoBox_TextChanged(object sender, EventArgs e) {
            infoBox.SelectionStart = infoBox.Text.Length;

            infoBox.ScrollToCaret();
        }
    }
}
