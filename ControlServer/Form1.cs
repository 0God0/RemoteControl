using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.Logging;
using NetWork;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ControlServer {
    public partial class Form1 : Form {

        TcpServer server;

        ControlWindow cw = null;

        public Form1() {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e) {
            server = new TcpServer(ex => infoBox.Invoke(() => $"Server error: {ex.Message}"));

            server.onConnect += (client => {
                //infoBox.Invoke(() => infoBox.Text += ($"Client connected: {client.RemoteEndPoint}" + Environment.NewLine));
                Debug.WriteLine($"Client connected: {client.RemoteEndPoint}" + Environment.NewLine);
                connectList.Invoke(() => connectList.Rows.Add(client.Id.ToString(), client.RemoteEndPoint.Address.ToString(), "点击开启窗口", "点击开启窗口"));
            });

            server.onDisconnect += (client => {
                //infoBox.Invoke(() => infoBox.Text += ($"Client disconnected: {client.RemoteEndPoint}" + Environment.NewLine));

                connectList.Invoke(() => {
                    for (int i = connectList.Rows.Count - 1; i >= 0; i--) {
                        var cell = connectList.Rows[i].Cells[0].Value;
                        if (cell != null && cell.ToString() == client.Id.ToString()) {
                            connectList.Rows.RemoveAt(i);
                        }
                    }
                });
                Debug.WriteLine($"Client disconnected: {client.RemoteEndPoint}" + Environment.NewLine);
            });



            // 设置AES加密
            var key = Encoding.UTF8.GetBytes("1234567891234567");
            server.SetEncryption(
                data => CAes.AesEncrypt(data, key),
                data => CAes.AesDecrypt(data, key)
            );

            server.RegisterHandler("ECHO", (client, marker, payload) => {;
                infoBox.Invoke(()=>infoBox.Text += ($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine));
                Debug.WriteLine($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine);
            });

            server.RegisterHandler("MSG", (client, marker, payload) => {
                infoBox.Invoke(()=>infoBox.Text += ($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine));
                Debug.WriteLine($"Received from {client.RemoteEndPoint}: {payload}" + Environment.NewLine);
                server.SendToClient(client.Id, "REPLY", $"Echo: {payload}", false);
            });

            RunAsync();
            server.Start(11451);


        }

        private static void RunAsync() {
            /* ---------- 通用回调 ---------- */
            void Log(string m) => Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
            void Err(Exception ex) => Debug.WriteLine($"[ERR] {ex.Message}");

            FileTransfer.ProgressCallback ShowProgress(string tag) =>
                (cur, total) => Debug.WriteLine($"{tag,-3} {cur,12:N0}/{total,12:N0}  {cur * 100 / Math.Max(total, 1),3}%");

            /* ---------- 1. 启动服务器 ---------- */
            var server = new FileTransfer.TransferServer(
                port: 11452,
                filePath: null,
                logCallback: Log,
                errorCallback: Err,
                progressCallback: ShowProgress("SV"));

            server.Start();
            Log("Server started. Waiting for uploads…");
        }

        private void infoBox_TextChanged(object sender, EventArgs e) {
            infoBox.SelectionStart = infoBox.Text.Length;
            infoBox.ScrollToCaret();
        }



        private void connectList_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e) {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 2) {

                cw ??= new ControlWindow(server);
                if (cw.IsDisposed) cw = new ControlWindow(server);

            } else if (e.ColumnIndex == 3) Debug.WriteLine(e.ColumnIndex + "  " + e.RowIndex);
            Debug.WriteLine(e.ColumnIndex + "  " + e.RowIndex);
        }
    }
}
