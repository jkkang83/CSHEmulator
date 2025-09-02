using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSHServer
{
    public partial class Form1 : Form
    {
        public Network Network = new Network();
        public int RunNum = 1;

        private System.Windows.Forms.Timer _netTimer;

        private void InitNetworkLamp()
        {
            _netTimer = new System.Windows.Forms.Timer();
            _netTimer.Interval = 1000;
            _netTimer.Tick += (s, e) =>
            {
                if (IsDisposed || !IsHandleCreated) return;

                bool anyAlive = false;
                try
                {
                    var clients = Network.GetConnectedClients();
                    foreach (var c in clients)
                    {
                        if (Network.IsClientAlive(c)) { anyAlive = true; break; }
                    }
                }
                catch { /* ignore */ }

                lblNetwork.BackColor = anyAlive ? Color.Blue : Color.Red;
            };
            _netTimer.Start();
        }

        public Form1()
        {
            InitializeComponent();

            // 상태등 타이머
            InitNetworkLamp();

            // 서버 이벤트 먼저 연결 (패치된 이벤트명)
            Network.FrameReceived += Network_FrameReceived;
            Network.LogEmitted += Network_LogEmitted;

            // 포트 텍스트 준비 시점이 애매하면 Load에서 StartServer 호출을 권장
            if (!int.TryParse(ServerPort.Text, out var _))
                ServerPort.Text = "5000";
        }

        private void Network_LogEmitted(object sender, string e)
        {
            AddViewLog($"{e}");
        }

        const int MaxLogChars = 200_000; // 필요에 맞게 조정

        public void AddViewLog(string lstr)
        {
            void write()
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                if (txtLog.TextLength > MaxLogChars) txtLog.Clear();
                txtLog.AppendText($"{ts}, {lstr}{(lstr.EndsWith("\r\n") ? "" : "\r\n")}");
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }

            if (InvokeRequired) BeginInvoke((MethodInvoker)(() => write()));
            else write();
        }

        public void ClearAllLog()
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => txtLog.Text = ""));
            }
            else
            {
                txtLog.Text = "";
            }
        }

        // 안전 파싱: "CMD@arg1@arg2@...@\r\n" → string[]
        private string[] ParseFrame(byte[] raw)
        {
            var text = Encoding.ASCII.GetString(raw).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.Split('@'); // 빈 토큰 허용
        }

        // UI 호출 래퍼
        private void UI(Action a)
        {
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            int port = 5000;
            int.TryParse(ServerPort.Text, out port);

            try
            {
                Network.StartServer(port);
                AddViewLog($"Server Listen Start (Port: {port})");
            }
            catch (Exception ex)
            {
                AddViewLog("[ERR] StartServer: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                if (_netTimer != null)
                {
                    _netTimer.Stop();
                    _netTimer.Dispose();
                    _netTimer = null;
                }

                // 이벤트 해제(후행 콜백 방지) — 패치된 이벤트명
                Network.FrameReceived -= Network_FrameReceived;
                Network.LogEmitted -= Network_LogEmitted;

                Network.StopServer();
            }
            catch { }
        }

        public class sSaveResultBin
        {
            public Int64 sTime;
            public int frameCount;
            public double fps;
            public double ledLeft;
            public double ledRight;
            public double testTime;
            public double[] X;
            public double[] Y;
            public double[] Z;
            public double[] TX;
            public double[] TY;
            public double[] TZ;
        }

        public byte[] MakeSaveResult(int framCnt)
        {
            int structCnt = 44;
            byte[] dataBuf = new byte[structCnt + framCnt * 8 * 6];

            double umscale = 5.5 / 0.30;
            double minscale = 180 / Math.PI * 60;
            int i = 0;

            sSaveResultBin sResult = new sSaveResultBin();
            int curCount = 0;
            byte[] data;

            DateTime startDateTime = DateTime.Now;
            DateTimeOffset datetimeOffset = new DateTimeOffset(startDateTime);
            long unixTime = datetimeOffset.ToUnixTimeSeconds();
            sResult.sTime = unixTime;
            data = BitConverter.GetBytes(sResult.sTime);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.frameCount = framCnt;
            data = BitConverter.GetBytes(sResult.frameCount);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.fps = 1000;
            data = BitConverter.GetBytes(sResult.fps);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.ledLeft = 2.7;
            data = BitConverter.GetBytes(sResult.ledLeft);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.ledRight = 2.7;
            data = BitConverter.GetBytes(sResult.ledRight);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.testTime = 10;
            data = BitConverter.GetBytes(sResult.testTime);
            Array.Copy(data, 0, dataBuf, curCount, data.Length);
            curCount += data.Length;

            sResult.X = new double[framCnt];
            sResult.Y = new double[framCnt];
            sResult.Z = new double[framCnt];
            sResult.TX = new double[framCnt];
            sResult.TY = new double[framCnt];
            sResult.TZ = new double[framCnt];

            for (i = 0; i < framCnt; i++)
            {
                sResult.X[i] = 1 * i * umscale;
                sResult.Y[i] = 2 * i * umscale;
                sResult.Z[i] = 3 * i * umscale;
                sResult.TX[i] = 0.1 * i * minscale;
                sResult.TY[i] = 0.2 * i * minscale;
                sResult.TZ[i] = 0.3 * i * minscale;
            }

            DateTime dt = DateTime.Now;
            string sLotDir = "C:\\CSHTest\\Data\\" + dt.Year + "\\" + dt.Month + "\\" + dt.Day + "\\" + "A_RData\\";
            if (!Directory.Exists(sLotDir))
                Directory.CreateDirectory(sLotDir);

            sLotDir += string.Format("ActroRawData_{0}_{1}.csv", framCnt, DateTime.Now.ToString("HHmmss"));

            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.X[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }
            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.Y[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }
            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.Z[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }
            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.TX[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }
            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.TY[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }
            for (i = 0; i < framCnt; i++)
            {
                data = BitConverter.GetBytes(sResult.TZ[i]);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }

            using (var wr = new StreamWriter(sLotDir))
            {
                wr.WriteLine("X,Y,Z,TX,TY,TZ");
                for (i = 0; i < framCnt; i++)
                    wr.WriteLine(string.Format("{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.00}",
                        sResult.X[i], sResult.Y[i], sResult.Z[i], sResult.TX[i], sResult.TY[i], sResult.TZ[i]));
            }

            return dataBuf;
        }

        public byte[] MakeMarkShift()
        {
            byte[] dataBuf = new byte[8 * 6];
            int curCount = 0;

            void put(double v)
            {
                var data = BitConverter.GetBytes(v);
                Array.Copy(data, 0, dataBuf, curCount, data.Length);
                curCount += data.Length;
            }

            put(1.1); put(2.2); put(3.3); put(4.4); put(5.5); put(6.6);
            return dataBuf;
        }

        // === 패치된 수신 핸들러 (FrameReceivedEventArgs 사용) ===
        private void Network_FrameReceived(object sender, Network.FrameReceivedEventArgs e)
        {
            var arry = ParseFrame(e.Frame);
            if (arry.Length == 0) return;

            string cmd = arry[0];

            switch (cmd)
            {
                case "P_S":
                    UI(() => AddViewLog("P_S Recieve"));
                    break;

                case "R_S": // Request Inspection
                    {
                        if (arry.Length < 2)
                        {
                            UI(() => AddViewLog("[WARN] R_S missing arg"));
                            break;
                        }
                        if (!int.TryParse(arry[1], out var frmCnt))
                        {
                            UI(() => AddViewLog("[WARN] R_S arg parse fail"));
                            break;
                        }

                        UI(() => AddViewLog($"[{RunNum++}] - R_S Recieve, trg :{frmCnt}"));

                        byte[] sDatabuffer = MakeSaveResult(frmCnt);
                        var head = Encoding.ASCII.GetBytes($"A_R@{frmCnt}@");
                        var tail = Encoding.ASCII.GetBytes("@\r\n");
                        var sendBuf = new byte[head.Length + sDatabuffer.Length + tail.Length];

                        Array.Copy(head, 0, sendBuf, 0, head.Length);
                        Array.Copy(sDatabuffer, 0, sendBuf, head.Length, sDatabuffer.Length);
                        Array.Copy(tail, 0, sendBuf, head.Length + sDatabuffer.Length, tail.Length);

                        Network.SendData(sendBuf);
                        break;
                    }

                case "R_C": // Request Continuous Inspection
                    {
                        if (arry.Length < 2)
                        {
                            UI(() => AddViewLog("[WARN] R_C missing arg"));
                            break;
                        }
                        if (!int.TryParse(arry[1], out var frmCnt))
                        {
                            UI(() => AddViewLog("[WARN] R_C arg parse fail"));
                            break;
                        }

                        Thread.Sleep(1);
                        UI(() => AddViewLog($"[{RunNum++}] - R_C Recieve, trg :{frmCnt}"));

                        byte[] sDatabuffer = MakeSaveResult(frmCnt);
                        var head = Encoding.ASCII.GetBytes($"A_R@{frmCnt}@");
                        var tail = Encoding.ASCII.GetBytes("@\r\n");
                        var sendBuf = new byte[head.Length + sDatabuffer.Length + tail.Length];

                        Array.Copy(head, 0, sendBuf, 0, head.Length);
                        Array.Copy(sDatabuffer, 0, sendBuf, head.Length, sDatabuffer.Length);
                        Array.Copy(tail, 0, sendBuf, head.Length + sDatabuffer.Length, tail.Length);

                        Network.SendData(sendBuf);
                        break;
                    }

                case "D_S": // Detect Shift Length -> A_D@<len>@<data>@\r\n
                    {
                        UI(() => AddViewLog("D_S Recieve=="));

                        byte[] data = MakeMarkShift();
                        var head = Encoding.ASCII.GetBytes("A_D@6@");
                        var tail = Encoding.ASCII.GetBytes("@\r\n");
                        var sendBuf = new byte[head.Length + data.Length + tail.Length];

                        Array.Copy(head, 0, sendBuf, 0, head.Length);
                        Array.Copy(data, 0, sendBuf, head.Length, data.Length);
                        Array.Copy(tail, 0, sendBuf, head.Length + data.Length, tail.Length);

                        UI(() => AddViewLog("A_D Send"));
                        Network.SendData(sendBuf);
                        break;
                    }

                case "M_S":
                    {
                        UI(() =>
                        {
                            ClearAllLog();
                            RunNum = 0;
                            byte[] sDatabuffer = Encoding.ASCII.GetBytes("A_M@" + "3" + "@\r\n");
                            AddViewLog("Mark ID : 3 Send");
                            Network.SendData(sDatabuffer);
                        });
                        break;
                    }

                case "B_U":
                    {
                        if (arry.Length >= 2 && int.TryParse(arry[1], out var val))
                            UI(() => AddViewLog(val == 0 ? "Base Down" : "Base Up"));
                        break;
                    }

                case "P_L":
                case "S_L":
                case "C_L":
                case "D_L":
                    {
                        if (arry.Length >= 2 && int.TryParse(arry[1], out var val))
                        {
                            string msg =
                                cmd == "P_L" ? (val == 0 ? "Pogo Pin Unload" : "Pogo Pin load") :
                                cmd == "S_L" ? (val == 0 ? "Side Push Unload" : "Side Push load") :
                                cmd == "C_L" ? (val == 0 ? "Cam Side Push Unload" : "Cam Side Push load") :
                                               (val == 0 ? "All Dln Unload" : "All Dln load");
                            UI(() => AddViewLog(msg));
                        }
                        break;
                    }

                case "A_P":
                    UI(() => AddViewLog("A_P Recieve,"));
                    break;

                case "A_C":
                case "A_H":
                case "A_G":
                    // TODO: 필요 시 동일 패턴으로 TryParse & 처리 추가
                    break;
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            ClearAllLog();
        }
    }
}
