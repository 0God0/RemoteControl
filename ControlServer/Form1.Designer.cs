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
            infoBox = new TextBox();
            fileSystemWatcher1 = new FileSystemWatcher();
            connectList = new DataGridView();
            guid = new DataGridViewTextBoxColumn();
            ipAddress = new DataGridViewTextBoxColumn();
            startControlWindowButton = new DataGridViewButtonColumn();
            startRemoteButton = new DataGridViewButtonColumn();
            ((System.ComponentModel.ISupportInitialize)fileSystemWatcher1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)connectList).BeginInit();
            SuspendLayout();
            // 
            // infoBox
            // 
            infoBox.Location = new Point(627, 11);
            infoBox.Margin = new Padding(2);
            infoBox.Multiline = true;
            infoBox.Name = "infoBox";
            infoBox.Size = new Size(546, 939);
            infoBox.TabIndex = 1;
            infoBox.TextChanged += infoBox_TextChanged;
            // 
            // fileSystemWatcher1
            // 
            fileSystemWatcher1.EnableRaisingEvents = true;
            fileSystemWatcher1.SynchronizingObject = this;
            // 
            // connectList
            // 
            connectList.AllowUserToAddRows = false;
            connectList.AllowUserToDeleteRows = false;
            connectList.AllowUserToResizeColumns = false;
            connectList.AllowUserToResizeRows = false;
            connectList.BackgroundColor = SystemColors.Window;
            connectList.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            connectList.Columns.AddRange(new DataGridViewColumn[] { guid, ipAddress, startControlWindowButton, startRemoteButton });
            connectList.Location = new Point(6, 11);
            connectList.Name = "connectList";
            connectList.ReadOnly = true;
            connectList.RowHeadersVisible = false;
            connectList.ScrollBars = ScrollBars.Vertical;
            connectList.Size = new Size(570, 470);
            connectList.TabIndex = 7;
            connectList.CellMouseClick += connectList_CellMouseClick;
            // 
            // guid
            // 
            guid.HeaderText = "主机标识";
            guid.MinimumWidth = 160;
            guid.Name = "guid";
            guid.ReadOnly = true;
            guid.Width = 160;
            // 
            // ipAddress
            // 
            ipAddress.HeaderText = "IP地址";
            ipAddress.MinimumWidth = 170;
            ipAddress.Name = "ipAddress";
            ipAddress.ReadOnly = true;
            ipAddress.Width = 170;
            // 
            // startControlWindowButton
            // 
            startControlWindowButton.HeaderText = "启用控制窗";
            startControlWindowButton.MinimumWidth = 118;
            startControlWindowButton.Name = "startControlWindowButton";
            startControlWindowButton.ReadOnly = true;
            startControlWindowButton.Resizable = DataGridViewTriState.False;
            startControlWindowButton.SortMode = DataGridViewColumnSortMode.Automatic;
            startControlWindowButton.Width = 118;
            // 
            // startRemoteButton
            // 
            startRemoteButton.HeaderText = "启用远控";
            startRemoteButton.MinimumWidth = 120;
            startRemoteButton.Name = "startRemoteButton";
            startRemoteButton.ReadOnly = true;
            startRemoteButton.Resizable = DataGridViewTriState.False;
            startRemoteButton.SortMode = DataGridViewColumnSortMode.Automatic;
            startRemoteButton.Width = 120;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1184, 961);
            Controls.Add(connectList);
            Controls.Add(infoBox);
            Margin = new Padding(2);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)fileSystemWatcher1).EndInit();
            ((System.ComponentModel.ISupportInitialize)connectList).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private TextBox infoBox;
        private FileSystemWatcher fileSystemWatcher1;
        private DataGridView connectList;
        private DataGridViewTextBoxColumn guid;
        private DataGridViewTextBoxColumn ipAddress;
        private DataGridViewButtonColumn startControlWindowButton;
        private DataGridViewButtonColumn startRemoteButton;
    }
}
