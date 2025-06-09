namespace ControlServer
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            StartServer = new Button();
            infoBox = new TextBox();
            SuspendLayout();
            // 
            // StartServer
            // 
            StartServer.Location = new Point(42, 33);
            StartServer.Name = "StartServer";
            StartServer.Size = new Size(169, 91);
            StartServer.TabIndex = 0;
            StartServer.Text = "button1";
            StartServer.UseVisualStyleBackColor = true;
            StartServer.Click += StartServer_Click;
            // 
            // infoBox
            // 
            infoBox.Location = new Point(42, 170);
            infoBox.Multiline = true;
            infoBox.Name = "infoBox";
            infoBox.Size = new Size(292, 76);
            infoBox.TabIndex = 1;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(14F, 31F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(infoBox);
            Controls.Add(StartServer);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button StartServer;
        private TextBox infoBox;
    }
}
