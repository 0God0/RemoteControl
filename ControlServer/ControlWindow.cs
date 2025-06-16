using NetWork;


namespace ControlServer {
    public partial class ControlWindow : Form {
        private readonly TcpServer server;
        public ControlWindow(TcpServer server) {
            this.server = server;
            InitializeComponent();
            Show();
        }


        private void sendCommandButton_Click(object sender, EventArgs e) =>
            server.BroadcastPacket("COMMAND", commandBox.Text);

        private void getFileButton_Click(object sender, EventArgs e) =>
            server.BroadcastPacket("GETFILE", fileNameBox.Text);

        private void doCMDButton_Click(object sender, EventArgs e) =>
            server.BroadcastPacket("DOCMD", CMDTextBox.Text);

        private void ControlWindow_FormClosing(object sender, FormClosingEventArgs e) => Dispose();

        private void ControlWindow_Load(object sender, EventArgs e) {

        }
    }
}
