using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSHClient
{
    public class NetworkClient : IDisposable
    {
        public event EventHandler<string> LogEvented;
        public event EventHandler<byte[]> RecieveEvened;

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _readCts;   // for Receive loop cancel
        private readonly List<byte> _buf = new List<byte>(64 * 1024);

        private string _host;
        private int _port;
        private volatile bool _wantRun;             // runner loop on/off
        private Task _runnerTask;

        private readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(30);
        private readonly double _maxBackoffSec = 10.0;

        public bool IsConnected => _client != null && _client.Connected;

        // ============== Public API ==============

        public Task StartClientAsync(string host, int port, CancellationToken ct = default(CancellationToken))
        {
            _host = host;
            _port = port;
            _wantRun = true;

            if (_runnerTask == null || _runnerTask.IsCompleted)
                _runnerTask = Task.Run(() => RunnerLoop(ct));

            return Task.CompletedTask;
        }

        public async Task StopClientAsync()
        {
            _wantRun = false;
            Cleanup(); // close immediately
            if (_runnerTask != null)
            {
                try { await _runnerTask.ConfigureAwait(false); } catch { }
                _runnerTask = null;
            }
        }

        public void Dispose()
        {
            try { StopClientAsync().GetAwaiter().GetResult(); } catch { }
        }

        // ============== Runner Loop (reconnect FSM) ==============

        private async Task RunnerLoop(CancellationToken outerCt)
        {
            double backoff = 1.0;

            RaiseLog("[Runner] started");

            while (_wantRun && !outerCt.IsCancellationRequested)
            {
                try
                {
                    // 1) connect
                    if (!await ConnectOnceAsync(outerCt).ConfigureAwait(false))
                    {
                        // failed to connect → backoff
                        if (!_wantRun) break;
                        RaiseLog($"[Runner] connect failed, retry in {backoff:0.0}s");
                        try { await Task.Delay(TimeSpan.FromSeconds(backoff), outerCt).ConfigureAwait(false); }
                        catch { break; }
                        backoff = Math.Min(backoff * 2.0, _maxBackoffSec);
                        continue;
                    }

                    // connected ok
                    backoff = 1.0;

                    // 2) receive loop (blocks until disconnected/timeout/error)
                    await RecvLoopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // swallow any error to keep the runner alive
                    RaiseLog("[Runner] exception: " + ex.Message);
                }
                finally
                {
                    // 3) always cleanup before next attempt
                    Cleanup();
                }

                if (!_wantRun || outerCt.IsCancellationRequested) break;

                // 4) backoff before reconnect
                RaiseLog($"[Runner] disconnected, retry in {backoff:0.0}s");
                try { await Task.Delay(TimeSpan.FromSeconds(backoff), outerCt).ConfigureAwait(false); }
                catch { break; }
                backoff = Math.Min(backoff * 2.0, _maxBackoffSec);
            }

            RaiseLog("[Runner] stopped");
        }

        // Try to connect once; return true if connected, false otherwise
        private async Task<bool> ConnectOnceAsync(CancellationToken outerCt)
        {
            Cleanup(); // ensure clean state

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_host, _port).ConfigureAwait(false);
                if (outerCt.IsCancellationRequested) return false;

                _client.NoDelay = true;
                _stream = _client.GetStream();
                _readCts = new CancellationTokenSource();

                RaiseLog($"Connected to {_host}:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                RaiseLog("Connect error: " + ex.Message);
                Cleanup();
                return false;
            }
        }

        // ============== Receive Loop ==============

        private async Task RecvLoopAsync()
        {
            if (_stream == null) return;

            var buffer = new byte[8192];
            _buf.Clear();

            while (_wantRun && IsConnected)
            {
                int read = 0;
                try
                {
                    using (var tcs = CancellationTokenSource.CreateLinkedTokenSource(_readCts.Token))
                    {
                        tcs.CancelAfter(_readTimeout);
                        read = await _stream.ReadAsync(buffer, 0, buffer.Length, tcs.Token).ConfigureAwait(false);
                    }

                    if (read == 0) // graceful close
                    {
                        RaiseLog("Remote closed");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    RaiseLog("Receive timeout");
                    break; // let runner reconnect
                }
                catch (Exception ex)
                {
                    RaiseLog("Receive error: " + ex.Message);
                    break; // let runner reconnect
                }

                // append
                for (int i = 0; i < read; i++) _buf.Add(buffer[i]);

                // extract frames
                byte[] frame;
                int guard = 0; // avoid infinite loop if parser misbehaves
                while (TryExtractFrame(_buf, out frame))
                {
                    RecieveEvened?.Invoke(this, frame);
                    guard++;
                    if (guard > 1000) break;
                }
            }
        }

        // ============== Send ==============

        public async Task SendCmdAsync(string cmd)
        {
            await SendRawAsync(Encoding.ASCII.GetBytes(cmd + "\r\n")).ConfigureAwait(false);
        }

        public async Task SendAsciiAsync(params string[] parts)
        {
            await SendRawAsync(Encoding.ASCII.GetBytes(string.Join("@", parts) + "\r\n")).ConfigureAwait(false);
        }

        public void SendData(byte[] data) // sync variant to mirror server
        {
            try
            {
                if (_stream == null || !IsConnected) { RaiseLog("Send failed: not connected"); return; }
                _stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                RaiseLog("Send error (sync): " + ex.Message);
            }
        }

        private async Task SendRawAsync(byte[] data)
        {
            try
            {
                if (_stream == null || !IsConnected) { RaiseLog("Send failed: not connected"); return; }
                await _stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseLog("Send error: " + ex.Message);
            }
        }

        // ============== Framing Parser ==============

        // Text: "CMD\r\n" or "CMD@...@\r\n"
        // Binary: "A_D@len@<bytes>@\r\n" or "A_R@frmCnt@<bytes>@\r\n"
        private static bool TryExtractFrame(List<byte> src, out byte[] frame)
        {
            frame = null;
            if (src.Count == 0) return false;

            int crlfIdx = IndexOfCrlf(src, 0);
            int atIdx = IndexOf(src, (byte)'@', 0);

            // pure text line first
            if (crlfIdx >= 0 && (atIdx < 0 || crlfIdx < atIdx))
            {
                frame = Take(src, crlfIdx + 2);
                return true;
            }

            if (atIdx >= 0)
            {
                string cmd = GetAscii(src, 0, atIdx);

                // "A_D@len@...@\r\n"  or  "A_R@frmCnt@...@\r\n"
                if (cmd == "A_D" || cmd == "A_R")
                {
                    int secondAt = IndexOf(src, (byte)'@', atIdx + 1);
                    if (secondAt < 0) return false; // wait more

                    string lenStr = GetAscii(src, atIdx + 1, secondAt);
                    int token;
                    if (!int.TryParse(lenStr, out token) || token < 0)
                    {
                        // corrupt header → drop 1 byte and reparse next time
                        if (src.Count > 0) src.RemoveAt(0);
                        return false;
                    }

                    int dataStart = secondAt + 1;
                    int dataLen = (cmd == "A_D") ? token : (44 + token * 8 * 6);
                    int afterData = dataStart + dataLen;

                    if (src.Count < afterData + 3) return false; // wait more

                    // tail "@\r\n"
                    if (src[afterData] != (byte)'@' || src[afterData + 1] != 0x0D || src[afterData + 2] != 0x0A)
                    {
                        if (src.Count > 0) src.RemoveAt(0);
                        return false;
                    }

                    frame = Take(src, afterData + 3);
                    return true;
                }
                else
                {
                    // generic "CMD@...@\r\n" → need CRLF
                    if (crlfIdx < 0) return false;
                    frame = Take(src, crlfIdx + 2);
                    return true;
                }
            }

            return false;
        }

        // ============== Byte helpers ==============

        private static int IndexOf(List<byte> s, byte b, int start)
        {
            for (int i = start; i < s.Count; i++) if (s[i] == b) return i;
            return -1;
        }

        private static int IndexOfCrlf(List<byte> s, int start)
        {
            for (int i = start; i < s.Count - 1; i++)
                if (s[i] == 0x0D && s[i + 1] == 0x0A) return i;
            return -1;
        }

        private static string GetAscii(List<byte> s, int start, int endEx)
        {
            int len = endEx - start;
            if (len <= 0) return string.Empty;
            return Encoding.ASCII.GetString(s.GetRange(start, len).ToArray());
        }

        private static byte[] Take(List<byte> s, int count)
        {
            var r = s.GetRange(0, count).ToArray();
            s.RemoveRange(0, count);
            return r;
        }

        // ============== Cleanup / Log ==============

        private void Cleanup()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }

            _readCts = null;
            _stream = null;
            _client = null;
        }

        private void RaiseLog(string s)
        {
            var h = LogEvented;
            if (h != null) h(this, s + (s.EndsWith("\r\n") ? "" : "\r\n"));
        }
    }
}
