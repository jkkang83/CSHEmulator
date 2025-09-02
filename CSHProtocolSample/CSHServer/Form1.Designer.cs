namespace CSHServer
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
            this.lblNetwork = new System.Windows.Forms.Label();
            this.ServerPort = new System.Windows.Forms.TextBox();
            this.label11 = new System.Windows.Forms.Label();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtMsaterNuma = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.btnClear = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblNetwork
            // 
            this.lblNetwork.BackColor = System.Drawing.Color.Red;
            this.lblNetwork.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblNetwork.ForeColor = System.Drawing.SystemColors.ControlText;
            this.lblNetwork.Location = new System.Drawing.Point(763, 10);
            this.lblNetwork.Name = "lblNetwork";
            this.lblNetwork.Size = new System.Drawing.Size(31, 23);
            this.lblNetwork.TabIndex = 191;
            // 
            // ServerPort
            // 
            this.ServerPort.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(236)))));
            this.ServerPort.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.ServerPort.ForeColor = System.Drawing.Color.Red;
            this.ServerPort.Location = new System.Drawing.Point(704, 9);
            this.ServerPort.Name = "ServerPort";
            this.ServerPort.Size = new System.Drawing.Size(42, 23);
            this.ServerPort.TabIndex = 190;
            this.ServerPort.Text = "5000";
            this.ServerPort.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label11.Location = new System.Drawing.Point(653, 11);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(41, 16);
            this.label11.TabIndex = 189;
            this.label11.Text = "Port :";
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
            this.txtLog.TabIndex = 192;
            // 
            // txtMsaterNuma
            // 
            this.txtMsaterNuma.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(236)))));
            this.txtMsaterNuma.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtMsaterNuma.ForeColor = System.Drawing.Color.Red;
            this.txtMsaterNuma.Location = new System.Drawing.Point(599, 11);
            this.txtMsaterNuma.Name = "txtMsaterNuma";
            this.txtMsaterNuma.Size = new System.Drawing.Size(42, 23);
            this.txtMsaterNuma.TabIndex = 194;
            this.txtMsaterNuma.Text = "0";
            this.txtMsaterNuma.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label10.Location = new System.Drawing.Point(514, 11);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(76, 16);
            this.label10.TabIndex = 193;
            this.label10.Text = "Last Mark :";
            // 
            // btnClear
            // 
            this.btnClear.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnClear.Location = new System.Drawing.Point(178, 8);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(180, 43);
            this.btnClear.TabIndex = 207;
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
            this.Controls.Add(this.txtMsaterNuma);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.lblNetwork);
            this.Controls.Add(this.ServerPort);
            this.Controls.Add(this.label11);
            this.Name = "Form1";
            this.Text = "CSHEmul_090３01";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblNetwork;
        private System.Windows.Forms.TextBox ServerPort;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.TextBox txtMsaterNuma;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Button btnClear;
    }
}

