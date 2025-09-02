using CSHClient; // Make sure this namespace contains NetworkClient with reconnect runner
using System;
using System.Text;
using System.Windows.Forms;

namespace CSHClient
{
    public partial class Form1 : Form
    {
        private NetworkClient _client = null;
        private System.Windows.Forms.Timer _lampTimer;
        private const int MaxLogChars = 200_000;

        public Form1()
        {
            InitializeComponent();

            // Default text if blanks
            if (string.IsNullOrWhiteSpace(txtHost.Text)) txtHost.Text = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(txtPort.Text)) txtPort.Text = "5000";

            InitLamp(); // network status indicator timer
        }

        /// <summary>
        /// UI helper to marshal calls to UI thread safely.
        /// </summary>
        private void UI(Action a)
        {
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        /// <summary>
        /// Append a line to log textbox with timestamp and auto-scroll.
        /// </summary>
        public void AddViewLog(string lstr)
        {
            Action write = () =>
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                if (txtLog.TextLength > MaxLogChars) txtLog.Clear();
                txtLog.AppendText($"{ts}, {lstr}");
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            };

            if (InvokeRequired) BeginInvoke((MethodInvoker)(() => write()));
            else write();
        }

        /// <summary>
        /// Simple status lamp that shows connection state (Blue=Connected, Red=Disconnected).
        /// </summary>
        private void InitLamp()
        {
            _lampTimer = new System.Windows.Forms.Timer();
            _lampTimer.Interval = 500;
            _lampTimer.Tick += (s, e) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                bool connected = (_client != null && _client.IsConnected);
                lblNetwork.BackColor = connected ? System.Drawing.Color.Blue : System.Drawing.Color.Red;
            };
            _lampTimer.Start();
        }

        /// <summary>
        /// Start the reconnecting client once the form is loaded.
        /// </summary>
        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _client = new NetworkClient();
            _client.LogEvented += (s, msg) => UI(() => AddViewLog(msg + (msg.EndsWith("\r\n") ? "" : "\r\n")));
            _client.RecieveEvened += Client_RecieveEvened;

            int port;
            if (!int.TryParse(txtPort.Text, out port)) port = 5000;

            try
            {
                // Reconnecting runner starts inside StartClientAsync (from the version I provided)
                await _client.StartClientAsync(txtHost.Text.Trim(), port);
            }
            catch (Exception ex)
            {
                AddViewLog("[ERR] Connect start: " + ex.Message + "\r\n");
            }
        }

        /// <summary>
        /// Stop runner/timer cleanly on closing.
        /// </summary>
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                if (_lampTimer != null)
                {
                    _lampTimer.Stop();
                    _lampTimer.Dispose();
                    _lampTimer = null;
                }

                if (_client != null)
                {
                    await _client.StopClientAsync();
                    _client = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Handle one complete frame (frame already includes trailing "@\r\n").
        /// Matches server protocol:
        ///   - A_D@<count>@<payload>@\r\n   (payload = count * double, LE)
        ///   - A_R@<framCnt>@<payload>@\r\n (payload = 44B header + 6 arrays * framCnt, LE)
        ///   - A_M@<id>@\r\n                (ASCII only)
        /// </summary>
        private void Client_RecieveEvened(object sender, byte[] frame)
        {
            UI(() =>
            {
                // Find CRLF once (for fallback ASCII dump)
                int crlf = -1;
                for (int i = 0; i < frame.Length - 1; i++)
                {
                    if (frame[i] == 0x0D && frame[i + 1] == 0x0A) { crlf = i; break; }
                }

                // First '@' separates CMD from the rest
                int firstAt = Array.IndexOf(frame, (byte)'@');
                if (firstAt <= 0)
                {
                    if (crlf >= 0)
                    {
                        string sline = Encoding.ASCII.GetString(frame, 0, crlf);
                        AddViewLog("RX: " + sline + "\r\n");
                    }
                    else
                    {
                        AddViewLog("[WARN] Invalid frame (no '@' after CMD)\r\n");
                    }
                    return;
                }

                string cmd = Encoding.ASCII.GetString(frame, 0, firstAt);

                // ----------------- A_D -----------------
                if (cmd == "A_D")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_D header incomplete\r\n"); return; }

                    // <count>
                    string sCount = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    if (!int.TryParse(sCount, out int count) || count < 0)
                    {
                        AddViewLog("[WARN] A_D count parse fail\r\n");
                        return;
                    }

                    // Tail '@' is the char right before CRLF
                    int tailIdx = frame.Length - 3;
                    if (tailIdx < 0 || frame[tailIdx] != (byte)'@' || frame[tailIdx + 1] != 0x0D || frame[tailIdx + 2] != 0x0A)
                    {
                        AddViewLog("[WARN] A_D tail invalid\r\n");
                        return;
                    }

                    int dataStart = secondAt + 1;
                    int payloadLen = tailIdx - dataStart;
                    if (payloadLen <= 0) { AddViewLog("[WARN] A_D empty payload\r\n"); return; }

                    var payload = new byte[payloadLen];
                    Buffer.BlockCopy(frame, dataStart, payload, 0, payloadLen);

                    if (payloadLen % 8 != 0)
                        AddViewLog($"[WARN] A_D payload size({payloadLen}) not multiple of 8\r\n");

                    int actual = payloadLen / 8;
                    if (actual != count)
                        AddViewLog($"[WARN] A_D count mismatch header({count}) != actual({actual})\r\n");

                    var values = new double[actual];
                    for (int i = 0; i < actual; i++)
                        values[i] = BitConverter.ToDouble(payload, i * 8); // LE

                    AddViewLog($"A_D Receive (count={count}, doubles={actual})\r\n");
                    if (actual == 6)
                    {
                        AddViewLog($"  X={values[0]:0.000}, Y={values[1]:0.000}, Z={values[2]:0.000}, " +
                                   $"TX={values[3]:0.000}, TY={values[4]:0.000}, TZ={values[5]:0.000}\r\n");
                    }
                    else
                    {
                        for (int i = 0; i < actual; i++)
                            AddViewLog($"  [{i}] {values[i]:0.000}\r\n");
                    }
                    return;
                }

                // ----------------- A_R -----------------
                if (cmd == "A_R")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_R header incomplete\r\n"); return; }

                    string cntStr = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    if (!int.TryParse(cntStr, out int frmCnt) || frmCnt < 0)
                    {
                        AddViewLog("[WARN] A_R framCnt parse fail\r\n");
                        return;
                    }

                    int tailIdx = frame.Length - 3;
                    if (tailIdx < 0 || frame[tailIdx] != (byte)'@' || frame[tailIdx + 1] != 0x0D || frame[tailIdx + 2] != 0x0A)
                    {
                        AddViewLog("[WARN] A_R tail invalid\r\n");
                        return;
                    }

                    int dataStart = secondAt + 1;
                    int payloadLen = tailIdx - dataStart;
                    if (payloadLen < 44) { AddViewLog("[WARN] A_R payload too small\r\n"); return; }

                    var p = new byte[payloadLen];
                    Buffer.BlockCopy(frame, dataStart, p, 0, payloadLen);

                    int off = 0;
                    long sTime = ReadInt64LE(p, ref off);
                    int frameCt = ReadInt32LE(p, ref off);
                    double fps = ReadDoubleLE(p, ref off);
                    double ledL = ReadDoubleLE(p, ref off);
                    double ledR = ReadDoubleLE(p, ref off);
                    double ttime = ReadDoubleLE(p, ref off);

                    if (frameCt != frmCnt)
                        AddViewLog($"[WARN] A_R frameCount mismatch header({frmCnt}) != payload({frameCt})\r\n");

                    double[] ReadArr(int n)
                    {
                        var arr = new double[n];
                        for (int i = 0; i < n; i++) arr[i] = ReadDoubleLE(p, ref off);
                        return arr;
                    }

                    var X = ReadArr(frmCnt);
                    var Y = ReadArr(frmCnt);
                    var Z = ReadArr(frmCnt);
                    var TX = ReadArr(frmCnt);
                    var TY = ReadArr(frmCnt);
                    var TZ = ReadArr(frmCnt);

                    AddViewLog($"A_R Receive (frames={frmCnt}, fps={fps:0.##}, test={ttime:0.##}s)\r\n");
                    for (int i = 0; i < frmCnt; i++)
                    {
                        AddViewLog($"  [{i}] X={X[i]:0.00}, Y={Y[i]:0.00}, Z={Z[i]:0.00}, " +
                                   $"TX={TX[i]:0.00}, TY={TY[i]:0.00}, TZ={TZ[i]:0.00}\r\n");
                    }
                    return;
                }

                // ----------------- A_M -----------------
                if (cmd == "A_M")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_M missing 2nd '@'\r\n"); return; }

                    string markId = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    AddViewLog($"A_M Receive (Mark ID = {markId})\r\n");
                    return;
                }

                // ----------------- Fallback ASCII -----------------
                if (crlf >= 0)
                {
                    string line = Encoding.ASCII.GetString(frame, 0, crlf);
                    AddViewLog("RX: " + line + "\r\n");
                }
                else
                {
                    AddViewLog("[WARN] Unknown frame (no CRLF)\r\n");
                }
            });
        }





        private static int ReadInt32LE(byte[] buf, ref int offset)
        {
            int v = BitConverter.ToInt32(buf, offset);
            offset += 4;
            return v;
        }

        private static long ReadInt64LE(byte[] buf, ref int offset)
        {
            long v = BitConverter.ToInt64(buf, offset);
            offset += 8;
            return v;
        }

        private static double ReadDoubleLE(byte[] buf, ref int offset)
        {
            double v = BitConverter.ToDouble(buf, offset);
            offset += 8;
            return v;
        }

        // ===== Buttons =====

        private async void btnSendPS_Click(object sender, EventArgs e)
        {
            if (_client != null) await _client.SendCmdAsync("P_S");
        }

        private async void btnSendRS_Click(object sender, EventArgs e)
        {
            if (_client == null) return;
            var n = txtFrame.Text.Trim();
            if (string.IsNullOrEmpty(n)) n = "1";
            await _client.SendAsciiAsync("R_S", n);
        }

        private async void btnSendRC_Click(object sender, EventArgs e)
        {
            if (_client == null) return;
            var n = txtFrame.Text.Trim();
            if (string.IsNullOrEmpty(n)) n = "1";
            await _client.SendAsciiAsync("R_C", n);
        }

        private async void btnSendDS_Click(object sender, EventArgs e)
        {
            if (_client != null) await _client.SendCmdAsync("D_S");
        }

        private async void btnSendMS_Click(object sender, EventArgs e)
        {
            if (_client != null) await _client.SendCmdAsync("M_S");
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            UI(() => txtLog.Clear());
        }
    }
}
