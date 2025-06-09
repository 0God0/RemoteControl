using Microsoft.VisualBasic.Devices;
using NetWork;

namespace ControlServer
{
    public partial class Form1 : Form {

        TcpServer server;

        public Form1() {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e) {
            server = new NetWork.TcpServer(e => infoBox.Text += $"Server error: {e.Message + Environment.NewLine}");

            server.RegisterHandler("MSG", (marker, payload) => {
                //Console.WriteLine($"Received message: {payload}");
                server.BroadcastPacket("MSG", $"Echo: {payload}");
            });

            
        }

        private void StartServer_Click(object sender, EventArgs e) {
            server.Start(8080);

            //Console.WriteLine("Server started. Press any key to stop...");
            //Console.ReadKey();

            server.Stop();
        }
    }
}
