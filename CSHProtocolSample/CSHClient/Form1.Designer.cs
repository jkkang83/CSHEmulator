namespace CSHClient
{
    partial class Form1
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.txtLog = new System.Windows.Forms.TextBox();
            this.lblNetwork = new System.Windows.Forms.Label();
            this.txtHost = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.label11 = new System.Windows.Forms.Label();
            this.btnSendPS = new System.Windows.Forms.Button();
            this.btnSendRS = new System.Windows.Forms.Button();
            this.txtFrame = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnSendRC = new System.Windows.Forms.Button();
            this.btnSendDS = new System.Windows.Forms.Button();
            this.btnSendMS = new System.Windows.Forms.Button();
            this.btnClear = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(8)))));
            this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtLog.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtLog.ForeColor = System.Drawing.Color.LemonChiffon;
            this.txtLog.Location = new System.Drawing.Point(178, 57);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(636, 470);
            this.txtLog.TabIndex = 193;
            // 
            // lblNetwork
            // 
            this.lblNetwork.BackColor = System.Drawing.Color.Red;
            this.lblNetwork.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblNetwork.ForeColor = System.Drawing.SystemColors.ControlText;
            this.lblNetwork.Location = new System.Drawing.Point(760, 19);
            this.lblNetwork.Name = "lblNetwork";
            this.lblNetwork.Size = new System.Drawing.Size(31, 23);
            this.lblNetwork.TabIndex = 194;
            // 
            // txtHost
            // 
            this.txtHost.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(236)))));
            this.txtHost.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtHost.ForeColor = System.Drawing.Color.Red;
            this.txtHost.Location = new System.Drawing.Point(517, 19);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(106, 23);
            this.txtHost.TabIndex = 198;
            this.txtHost.Text = "127.0.0.1";
            this.txtHost.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label10.Location = new System.Drawing.Point(445, 19);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(66, 16);
            this.label10.TabIndex = 197;
            this.label10.Text = "IP Adress";
            // 
            // txtPort
            // 
            this.txtPort.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(236)))));
            this.txtPort.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtPort.ForeColor = System.Drawing.Color.Red;
            this.txtPort.Location = new System.Drawing.Point(686, 17);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(42, 23);
            this.txtPort.TabIndex = 196;
            this.txtPort.Text = "5000";
            this.txtPort.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label11.Location = new System.Drawing.Point(635, 19);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(41, 16);
            this.label11.TabIndex = 195;
            this.label11.Text = "Port :";
            // 
            // btnSendPS
            // 
            this.btnSendPS.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSendPS.Location = new System.Drawing.Point(12, 57);
            this.btnSendPS.Name = "btnSendPS";
            this.btnSendPS.Size = new System.Drawing.Size(146, 56);
            this.btnSendPS.TabIndex = 199;
            this.btnSendPS.Text = "P_S";
            this.btnSendPS.UseVisualStyleBackColor = true;
            this.btnSendPS.Click += new System.EventHandler(this.btnSendPS_Click);
            // 
            // btnSendRS
            // 
            this.btnSendRS.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSendRS.Location = new System.Drawing.Point(12, 243);
            this.btnSendRS.Name = "btnSendRS";
            this.btnSendRS.Size = new System.Drawing.Size(146, 56);
            this.btnSendRS.TabIndex = 200;
            this.btnSendRS.Text = "R_S";
            this.btnSendRS.UseVisualStyleBackColor = true;
            this.btnSendRS.Click += new System.EventHandler(this.btnSendRS_Click);
            // 
            // txtFrame
            // 
            this.txtFrame.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(236)))));
            this.txtFrame.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtFrame.ForeColor = System.Drawing.Color.Black;
            this.txtFrame.Location = new System.Drawing.Point(111, 132);
            this.txtFrame.Name = "txtFrame";
            this.txtFrame.Size = new System.Drawing.Size(42, 23);
            this.txtFrame.TabIndex = 202;
            this.txtFrame.Text = "1";
            this.txtFrame.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(13, 136);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(89, 16);
            this.label1.TabIndex = 201;
            this.label1.Text = "Frame Count";
            // 
            // btnSendRC
            // 
            this.btnSendRC.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSendRC.Location = new System.Drawing.Point(12, 171);
            this.btnSendRC.Name = "btnSendRC";
            this.btnSendRC.Size = new System.Drawing.Size(146, 56);
            this.btnSendRC.TabIndex = 203;
            this.btnSendRC.Text = "R_C";
            this.btnSendRC.UseVisualStyleBackColor = true;
            this.btnSendRC.Click += new System.EventHandler(this.btnSendRC_Click);
            // 
            // btnSendDS
            // 
            this.btnSendDS.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSendDS.Location = new System.Drawing.Point(12, 317);
            this.btnSendDS.Name = "btnSendDS";
            this.btnSendDS.Size = new System.Drawing.Size(146, 56);
            this.btnSendDS.TabIndex = 204;
            this.btnSendDS.Text = "D_S";
            this.btnSendDS.UseVisualStyleBackColor = true;
            this.btnSendDS.Click += new System.EventHandler(this.btnSendDS_Click);
            // 
            // btnSendMS
            // 
            this.btnSendMS.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSendMS.Location = new System.Drawing.Point(12, 398);
            this.btnSendMS.Name = "btnSendMS";
            this.btnSendMS.Size = new System.Drawing.Size(146, 56);
            this.btnSendMS.TabIndex = 205;
            this.btnSendMS.Text = "M_S";
            this.btnSendMS.UseVisualStyleBackColor = true;
            this.btnSendMS.Click += new System.EventHandler(this.btnSendMS_Click);
            // 
            // btnClear
            // 
            this.btnClear.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnClear.Location = new System.Drawing.Point(178, 6);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(180, 43);
            this.btnClear.TabIndex = 206;
            this.btnClear.Text = "ClearLog";
            this.btnClear.UseVisualStyleBackColor = true;
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(826, 555);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.btnSendMS);
            this.Controls.Add(this.btnSendDS);
            this.Controls.Add(this.btnSendRC);
            this.Controls.Add(this.txtFrame);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnSendRS);
            this.Controls.Add(this.btnSendPS);
            this.Controls.Add(this.txtHost);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.txtPort);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.lblNetwork);
            this.Controls.Add(this.txtLog);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Label lblNetwork;
        private System.Windows.Forms.TextBox txtHost;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Button btnSendPS;
        private System.Windows.Forms.Button btnSendRS;
        private System.Windows.Forms.TextBox txtFrame;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnSendRC;
        private System.Windows.Forms.Button btnSendDS;
        private System.Windows.Forms.Button btnSendMS;
        private System.Windows.Forms.Button btnClear;
    }
}

