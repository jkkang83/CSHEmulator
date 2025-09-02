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
        /// Handle received frames (text or binary) like the server-side pattern.
        /// Frames include trailing CRLF.
        /// </summary>
        private void Client_RecieveEvened(object sender, byte[] frame)
        {
            UI(() =>
            {
                // Extract command token (before first '@') or until CRLF for pure text
                int firstAt = Array.IndexOf(frame, (byte)'@');
                int crlf = -1;
                for (int i = 0; i < frame.Length - 1; i++)
                {
                    if (frame[i] == 0x0D && frame[i + 1] == 0x0A) { crlf = i; break; }
                }

                // Pure text line: "CMD\r\n"
                if (firstAt < 0 && crlf >= 0)
                {
                    string sline = Encoding.ASCII.GetString(frame, 0, crlf);
                    AddViewLog("RX: " + sline + "\r\n");
                    return;
                }

                if (firstAt < 0)
                {
                    AddViewLog("[WARN] Unknown frame (no '@') len=" + frame.Length + "\r\n");
                    return;
                }

                string cmd = Encoding.ASCII.GetString(frame, 0, firstAt);

                // A_D@len@<bytes>@\r\n
                if (cmd == "A_D")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_D incomplete header\r\n"); return; }

                    string lenStr = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    int dataLen;
                    if (!int.TryParse(lenStr, out dataLen) || dataLen < 0)
                    {
                        AddViewLog("[WARN] A_D len parse fail\r\n");
                        return;
                    }

                    int dataStart = secondAt + 1;
                    int tailStart = dataStart + dataLen; // '@' before CRLF
                    if (frame.Length < tailStart + 3 ||
                        frame[tailStart] != (byte)'@' || frame[tailStart + 1] != 0x0D || frame[tailStart + 2] != 0x0A)
                    {
                        AddViewLog("[WARN] A_D tail invalid\r\n");
                        return;
                    }

                    var payload = new byte[dataLen];
                    Buffer.BlockCopy(frame, dataStart, payload, 0, dataLen);

                    // Interpret payload as doubles (if multiple of 8)
                    if (dataLen % 8 != 0)
                    {
                        AddViewLog($"[WARN] A_D payload size({dataLen}) is not multiple of 8\r\n");
                    }

                    int count = dataLen / 8;
                    double[] values = new double[count];
                    for (int i = 0; i < count; i++)
                        values[i] = BitConverter.ToDouble(payload, i * 8);

                    AddViewLog($"A_D Recieve (len={dataLen}, doubles={count})\r\n");
                    if (count == 6)
                    {
                        AddViewLog(
                            $"  X={values[0]:0.000}, Y={values[1]:0.000}, Z={values[2]:0.000}, " +
                            $"TX={values[3]:0.000}, TY={values[4]:0.000}, TZ={values[5]:0.000}\r\n"
                        );
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            AddViewLog($"  [{i}] {values[i]:0.000}\r\n");
                    }
                    return;
                }

                // A_R@frmCnt@<bytes>@\r\n  (bytes len = 44 + frmCnt*8*6)
                // Header (44B): Int64 sTime, int frameCount, double fps, ledLeft, ledRight, testTime
                if (cmd == "A_R")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_R incomplete header\r\n"); return; }

                    string cntStr = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    int frmCnt;
                    if (!int.TryParse(cntStr, out frmCnt) || frmCnt < 0)
                    {
                        AddViewLog("[WARN] A_R frmCnt parse fail\r\n");
                        return;
                    }

                    int dataLen = 44 + frmCnt * 8 * 6;
                    int dataStart = secondAt + 1;
                    int tailStart = dataStart + dataLen;
                    if (frame.Length < tailStart + 3 ||
                        frame[tailStart] != (byte)'@' || frame[tailStart + 1] != 0x0D || frame[tailStart + 2] != 0x0A)
                    {
                        AddViewLog("[WARN] A_R tail invalid\r\n");
                        return;
                    }

                    var payload = new byte[dataLen];
                    Buffer.BlockCopy(frame, dataStart, payload, 0, dataLen);

                    int off = 0;
                    long sTime = ReadInt64LE(payload, ref off);
                    int frameCnt = ReadInt32LE(payload, ref off);
                    double fps = ReadDoubleLE(payload, ref off);
                    double ledL = ReadDoubleLE(payload, ref off);
                    double ledR = ReadDoubleLE(payload, ref off);
                    double ttime = ReadDoubleLE(payload, ref off);

                    Func<int, double[]> ReadDoubles = (n) =>
                    {
                        var arr = new double[n];
                        for (int i = 0; i < n; i++) arr[i] = ReadDoubleLE(payload, ref off);
                        return arr;
                    };

                    var X = ReadDoubles(frmCnt);
                    var Y = ReadDoubles(frmCnt);
                    var Z = ReadDoubles(frmCnt);
                    var TX = ReadDoubles(frmCnt);
                    var TY = ReadDoubles(frmCnt);
                    var TZ = ReadDoubles(frmCnt);

                    AddViewLog(
                        $"A_R Recieve (frmCnt={frmCnt}, bytes={dataLen})  " +
                        $"fps={fps:0.##}, LED(L/R)={ledL:0.##}/{ledR:0.##}, testTime={ttime}\r\n"
                    );

                    // Dump all array values
                    for (int i = 0; i < frmCnt; i++)
                    {
                        AddViewLog(
                            $"  [{i}] X={X[i]:0.00}, Y={Y[i]:0.00}, Z={Z[i]:0.00}, " +
                            $"TX={TX[i]:0.00}, TY={TY[i]:0.00}, TZ={TZ[i]:0.00}\r\n"
                        );
                    }
                    return;
                }

                // A_M@<id>@\r\n
                if (cmd == "A_M")
                {
                    int secondAt = Array.IndexOf(frame, (byte)'@', firstAt + 1);
                    if (secondAt < 0) { AddViewLog("[WARN] A_M missing 2nd '@'\r\n"); return; }

                    string markId = Encoding.ASCII.GetString(frame, firstAt + 1, secondAt - (firstAt + 1));
                    AddViewLog($"A_M Recieve (Mark ID = {markId})\r\n");
                    return;
                }

                // Fallback: print the raw ASCII line
                string line = (crlf >= 0) ? Encoding.ASCII.GetString(frame, 0, crlf)
                                          : Encoding.ASCII.GetString(frame);
                AddViewLog("RX: " + line.TrimEnd('\r', '\n') + "\r\n");
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
