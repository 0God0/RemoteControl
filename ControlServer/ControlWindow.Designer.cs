namespace ControlServer {
    partial class ControlWindow {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            CMDTextBox = new TextBox();
            doCMDButton = new Button();
            getFileButton = new Button();
            fileNameBox = new TextBox();
            commandBox = new TextBox();
            sendCommandButton = new Button();
            SuspendLayout();
            // 
            // CMDTextBox
            // 
            CMDTextBox.Location = new Point(116, 101);
            CMDTextBox.Margin = new Padding(2);
            CMDTextBox.Name = "CMDTextBox";
            CMDTextBox.Size = new Size(440, 23);
            CMDTextBox.TabIndex = 12;
            // 
            // doCMDButton
            // 
            doCMDButton.Location = new Point(11, 97);
            doCMDButton.Margin = new Padding(2);
            doCMDButton.Name = "doCMDButton";
            doCMDButton.Size = new Size(90, 30);
            doCMDButton.TabIndex = 11;
            doCMDButton.Text = "CMD命令";
            doCMDButton.UseVisualStyleBackColor = true;
            doCMDButton.Click += doCMDButton_Click;
            // 
            // getFileButton
            // 
            getFileButton.Location = new Point(11, 55);
            getFileButton.Margin = new Padding(2);
            getFileButton.Name = "getFileButton";
            getFileButton.Size = new Size(90, 30);
            getFileButton.TabIndex = 10;
            getFileButton.Text = "获取远程文件";
            getFileButton.UseVisualStyleBackColor = true;
            getFileButton.Click += getFileButton_Click;
            // 
            // fileNameBox
            // 
            fileNameBox.Location = new Point(116, 59);
            fileNameBox.Margin = new Padding(2);
            fileNameBox.Name = "fileNameBox";
            fileNameBox.Size = new Size(440, 23);
            fileNameBox.TabIndex = 9;
            // 
            // commandBox
            // 
            commandBox.Location = new Point(116, 15);
            commandBox.Margin = new Padding(2);
            commandBox.Name = "commandBox";
            commandBox.Size = new Size(440, 23);
            commandBox.TabIndex = 8;
            // 
            // sendCommandButton
            // 
            sendCommandButton.Location = new Point(11, 11);
            sendCommandButton.Margin = new Padding(2);
            sendCommandButton.Name = "sendCommandButton";
            sendCommandButton.Size = new Size(90, 30);
            sendCommandButton.TabIndex = 7;
            sendCommandButton.Text = "发送命令";
            sendCommandButton.UseVisualStyleBackColor = true;
            sendCommandButton.Click += sendCommandButton_Click;
            // 
            // ControlWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(583, 151);
            Controls.Add(CMDTextBox);
            Controls.Add(doCMDButton);
            Controls.Add(getFileButton);
            Controls.Add(fileNameBox);
            Controls.Add(commandBox);
            Controls.Add(sendCommandButton);
            Name = "ControlWindow";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ControlWindow";
            FormClosing += ControlWindow_FormClosing;
            Load += ControlWindow_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox CMDTextBox;
        private Button doCMDButton;
        private Button getFileButton;
        private TextBox fileNameBox;
        private TextBox commandBox;
        private Button sendCommandButton;
    }
}