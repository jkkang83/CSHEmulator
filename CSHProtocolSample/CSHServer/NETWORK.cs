using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSHServer
{
    // =====================================================================
    // 재사용 가능한 네트워크 코어 (UI 비의존)
    // - 프레임 파서 플러그인 구조(IFrameParser)
    // - 클라이언트별 송신 락으로 안전한 동시 전송
    // - 단일/다중 클라이언트 모드, Ping 모니터 옵션
    // - WinForms/WPF/콘솔/서비스 어디서든 그대로 사용 가능
    // =====================================================================

    #region 프레임 파서 추상화
    /// <summary>
    /// 링버퍼(src)에서 1개의 완성 프레임을 추출하는 인터페이스
    /// - 프레임을 찾으면 src에서 해당 바이트를 제거하고 frame에 반환
    /// - 충분한 데이터가 없으면 false
    /// </summary>
    public interface IFrameParser
    {
        bool TryExtract(List<byte> src, out byte[] frame);
    }

    /// <summary>
    /// 기본 프레임 파서
    /// - 텍스트 라인: "...\r\n"
    /// - 바이너리 포함 토큰형:
    ///     * A_D@count@<payload>@\r\n  (payload = count * 8)
    ///     * A_R@framCnt@<payload>@\r\n (payload = 44 + framCnt * 8 * 6)
    /// </summary>
    public sealed class DefaultFrameParser : IFrameParser
    {
        public bool TryExtract(List<byte> src, out byte[] frame)
        {
            frame = null;
            if (src.Count < 2) return false; // CRLF 최소 길이 미만

            int crlf = IndexOfCRLF(src, 0);
            if (crlf < 0) return false; // 프레임 경계(\r\n) 대기

            // CRLF 이전에 '@'가 없다면 → 순수 텍스트 라인으로 처리
            int at1 = IndexOfByte(src, (byte)'@', 0, crlf);
            if (at1 < 0)
            {
                frame = Take(src, crlf + 2);
                return true;
            }

            // 토큰형: 명령 파트 확인
            string cmd = GetAscii(src, 0, at1);
            if (cmd == "A_D" || cmd == "A_R")
            {
                // 두 번째 '@'까지 숫자 토큰 추출
                int at2 = IndexOfByte(src, (byte)'@', at1 + 1, src.Count);
                if (at2 < 0) return false; // 더 읽어야 함

                string numStr = GetAscii(src, at1 + 1, at2);
                if (!int.TryParse(numStr, out int token) || token < 0)
                {
                    // 헤더 이상 시 1바이트 드롭(재동기화)
                    if (src.Count > 0) src.RemoveAt(0);
                    return false;
                }

                int dataStart = at2 + 1;
                int dataLen = (cmd == "A_D") ? token * 8 : 44 + token * 8 * 6;
                int afterData = dataStart + dataLen;

                // 테일 "@\r\n" 확인 (바이너리 내부의 '@'와 구분)
                if (src.Count < afterData + 3) return false; // 더 읽어야 함
                if (src[afterData] != (byte)'@' || src[afterData + 1] != 0x0D || src[afterData + 2] != 0x0A)
                {
                    if (src.Count > 0) src.RemoveAt(0); // 재동기화
                    return false;
                }

                frame = Take(src, afterData + 3);
                return true;
            }
            else
            {
                // 기타 토큰형도 CRLF까지 프레임으로 간주
                frame = Take(src, crlf + 2);
                return true;
            }
        }

        // ---- 파서 내부 유틸 ----
        private static int IndexOfCRLF(List<byte> s, int start)
        {
            for (int i = start; i < s.Count - 1; i++)
                if (s[i] == 0x0D && s[i + 1] == 0x0A) return i;
            return -1;
        }
        private static int IndexOfByte(List<byte> s, byte b, int start, int endEx)
        {
            int end = Math.Min(endEx, s.Count);
            for (int i = start; i < end; i++) if (s[i] == b) return i; return -1;
        }
        private static string GetAscii(List<byte> s, int start, int endEx)
        {
            int len = endEx - start; if (len <= 0) return string.Empty;
            return Encoding.ASCII.GetString(s.GetRange(start, len).ToArray());
        }
        private static byte[] Take(List<byte> s, int count)
        {
            var r = s.GetRange(0, count).ToArray(); s.RemoveRange(0, count); return r;
        }
    }
    #endregion

    // =====================================================================
    // 네트워크 서버 본체 (재사용 가능한 코어)
    // =====================================================================
    public class Network : IDisposable
    {
        // ----- 이벤트 (상위 앱에서 구독) -----
        public event EventHandler<string> LogEmitted; // 로그 문자열 전파
        public sealed class FrameReceivedEventArgs : EventArgs
        {
            public TcpClient Client { get; }
            public byte[] Frame { get; }
            public FrameReceivedEventArgs(TcpClient client, byte[] frame) { Client = client; Frame = frame; }
        }
        public event EventHandler<FrameReceivedEventArgs> FrameReceived; // 프레임 수신 이벤트

        // ----- 소켓/상태 -----
        private TcpListener _listener;
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private readonly Dictionary<TcpClient, NetworkStream> _streams = new Dictionary<TcpClient, NetworkStream>();
        private readonly Dictionary<TcpClient, bool> _alive = new Dictionary<TcpClient, bool>();
        private readonly Dictionary<TcpClient, object> _sendLocks = new Dictionary<TcpClient, object>();
        private readonly object _lock = new object();

        private CancellationTokenSource _acceptCts;
        private Task _acceptTask;

        private readonly TimeSpan _readTimeout;
        public bool SingleClientMode { get; set; } = true;  // true면 새 연결 시 기존 모두 종료
        public bool EnableMonitorPing { get; set; } = false; // true면 PING 주기 전송 + 연결 상태 확인

        private readonly IFrameParser _parser;              // 플러그인 파서
        public bool IsRunning => _listener != null;

        // ----- 생성자 -----
        /// <param name="readTimeoutSeconds">수신 Read 타임아웃(초)</param>
        /// <param name="parser">프레임 파서(미지정 시 기본 파서)</param>
        public Network(int readTimeoutSeconds = 30, IFrameParser parser = null)
        {
            _readTimeout = TimeSpan.FromSeconds(Math.Max(1, readTimeoutSeconds));
            _parser = parser ?? new DefaultFrameParser();
        }

        // ----- 공개 API -----
        /// <summary>서버 시작</summary>
        public void StartServer(int port)
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _acceptCts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_acceptCts.Token));
            Log($"[Server] Listen start on *:{port}");
        }

        /// <summary>서버 정지(모든 연결 종료)</summary>
        public void StopServer()
        {
            try
            {
                _acceptCts?.Cancel();
                try { _acceptTask?.Wait(1000); } catch { /* ignore */ }

                List<TcpClient> snap;
                lock (_lock) snap = new List<TcpClient>(_clients);
                foreach (var c in snap) RemoveClient(c);

                _listener?.Stop();
                _listener = null;
                Log("[Server] 서버 종료 완료");
            }
            catch (Exception ex) { Log("[Server] 서버 종료 오류: " + ex.Message); }
        }

        public void Dispose() => StopServer();

        /// <summary>현재 연결된 클라이언트 스냅샷</summary>
        public List<TcpClient> GetConnectedClients() { lock (_lock) return new List<TcpClient>(_clients); }
        /// <summary>클라이언트 생존 플래그(모니터링 기준)</summary>
        public bool IsClientAlive(TcpClient client) { lock (_lock) return _alive.ContainsKey(client) && _alive[client]; }

        /// <summary>브로드캐스트 전송</summary>
        public void SendData(byte[] data) => BroadcastData(data);
        /// <summary>특정 클라이언트 전송</summary>
        public void SendDataToClient(TcpClient client, byte[] data)
        {
            NetworkStream ns; object sl;
            lock (_lock)
            {
                if (!_streams.TryGetValue(client, out ns) || !client.Connected) return;
                if (!_sendLocks.TryGetValue(client, out sl)) { sl = new object(); _sendLocks[client] = sl; }
            }
            try { lock (sl) ns.Write(data, 0, data.Length); Log("[TX] Sent to " + SafeEP(client)); }
            catch (Exception ex) { Log("[TX] Send Error: " + ex.Message); }
        }
        /// <summary>전체 브로드캐스트</summary>
        public void BroadcastData(byte[] data)
        {
            List<(NetworkStream ns, object sl)> targets;
            lock (_lock)
            {
                targets = new List<(NetworkStream ns, object sl)>(_clients.Count);
                foreach (var c in _clients)
                    if (c.Connected && _streams.TryGetValue(c, out var ns))
                    {
                        if (!_sendLocks.TryGetValue(c, out var sl)) { sl = new object(); _sendLocks[c] = sl; }
                        targets.Add((ns, sl));
                    }
            }
            foreach (var t in targets)
            {
                var ns = t.ns;
                var sl = t.sl;
                try { lock (sl) ns.Write(data, 0, data.Length); }
                catch { /* ignore */ }
            }
        }

        // ----- 내부 루프 -----
        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;
                    var stream = client.GetStream();

                    lock (_lock)
                    {
                        if (SingleClientMode)
                        {
                            var olds = new List<TcpClient>(_clients);
                            foreach (var old in olds) RemoveClient(old); // 기존 연결 종료 후 교체
                        }
                        _clients.Add(client);
                        _streams[client] = stream;
                        _alive[client] = true;
                        if (!_sendLocks.ContainsKey(client)) _sendLocks[client] = new object();
                    }

                    Log("[ACCEPT] " + SafeEP(client));
                    _ = HandleClientAsync(client, stream, ct); // 수신 루프 시작(분리 실행)
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log("[ACCEPT] Error: " + ex.Message); try { await Task.Delay(200, ct); } catch { } }
            }
            Log("[Server] Accept loop end");
        }

        /// <summary>
        /// 개별 클라이언트 수신 루프
        /// - Read 타임아웃, 프레임 파서, 이벤트 호출
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, NetworkStream stream, CancellationToken outerCt)
        {
            var ring = new List<byte>(64 * 1024); // 간단한 리스트 기반 링버퍼
            var buffer = new byte[8192];

            if (EnableMonitorPing)
                _ = MonitorPingAsync(client, stream, outerCt);

            try
            {
                while (!outerCt.IsCancellationRequested && client.Connected)
                {
                    int read = 0;
                    using (var tcs = new CancellationTokenSource(_readTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(tcs.Token, outerCt))
                    {
                        try { read = await stream.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false); if (read == 0) break; }
                        catch (OperationCanceledException) { Log("[RX] timeout → disconnect"); break; }
                    }

                    // 수신 바이트를 링버퍼 뒤에 추가
                    ring.AddRange(new ArraySegment<byte>(buffer, 0, read));
                    SetAlive(client, true);

                    // 파서가 프레임을 추출할 수 있을 때까지 반복 호출
                    int guard = 0;
                    while (_parser.TryExtract(ring, out var frame))
                    {
                        FrameReceived?.Invoke(this, new FrameReceivedEventArgs(client, frame));
                        if (++guard > 1000) break; // 프레임 폭주 방지 가드
                    }
                }
            }
            catch (Exception ex) { Log("[RX] Exception: " + ex.Message); }
            finally { RemoveClient(client); Log("[DISCONNECT] " + SafeEP(client)); }
        }

        /// <summary>
        /// (옵션) 5초 주기 Ping 전송 + Poll 기반 연결 확인
        /// - 송신은 클라이언트별 락으로 보호
        /// </summary>
        private async Task MonitorPingAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
        {
            var ping = Encoding.ASCII.GetBytes("PING\r\n");
            object sl; lock (_lock) { if (!_sendLocks.TryGetValue(client, out sl)) { sl = new object(); _sendLocks[client] = sl; } }

            while (!ct.IsCancellationRequested && client.Connected)
            {
                try
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                    lock (sl) stream.Write(ping, 0, ping.Length);

                    bool disconnected = client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0;
                    SetAlive(client, !disconnected);
                    if (disconnected) break;
                }
                catch { break; }
            }
        }

        // ----- 유틸 -----
        private void SetAlive(TcpClient c, bool alive) { lock (_lock) { if (_alive.ContainsKey(c)) _alive[c] = alive; } }
        private void RemoveClient(TcpClient c)
        {
            lock (_lock)
            {
                _clients.Remove(c);
                _streams.Remove(c);
                _alive.Remove(c);
                _sendLocks.Remove(c);
            }
            TryClose(c);
        }
        private static void TryClose(TcpClient c) { try { c.Close(); } catch { /* ignore */ } }
        private static string SafeEP(TcpClient c) { try { return c?.Client?.RemoteEndPoint?.ToString() ?? "(null)"; } catch { return "(unknown)"; } }
        private void Log(string s) { LogEmitted?.Invoke(this, s.EndsWith("\r\n") ? s : s + "\r\n"); }

        // ----- 편의 송신 API -----
        /// <summary>CRLF 라인 전송</summary>
        public void SendLineToClient(TcpClient c, string line)
        { var bytes = Encoding.ASCII.GetBytes((line ?? string.Empty).EndsWith("\r\n") ? line : line + "\r\n"); SendDataToClient(c, bytes); }
        /// <summary>토큰형 전송: CMD@arg1@arg2@...@\r\n</summary>
        public void SendTokensToClient(TcpClient c, string cmd, params string[] args)
        { var sb = new StringBuilder(cmd ?? string.Empty); if (args != null) foreach (var a in args) sb.Append('@').Append(a ?? string.Empty); sb.Append("\r\n"); SendDataToClient(c, Encoding.ASCII.GetBytes(sb.ToString())); }
    }
}
