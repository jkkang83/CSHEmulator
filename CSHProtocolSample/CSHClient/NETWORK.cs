using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSHClient
{
    // ================================================================
    // C# 7.3 호환 클라이언트 네트워크 코어 (UI 비의존)
    // - 자동 재연결 루프
    // - 서버와 동일한 프레이밍 파서(DefaultFrameParser)
    // - 스레드 안전 송신(퍼-클라이언트 락)
    // - 이벤트: LogEvented(string), RecieveEvened(byte[])
    // ================================================================

    #region 프레임 파서 (서버와 동일 규칙)
    public interface IFrameParser
    {
        bool TryExtract(List<byte> src, out byte[] frame);
    }

    public sealed class DefaultFrameParser : IFrameParser
    {
        public bool TryExtract(List<byte> src, out byte[] frame)
        {
            frame = null;
            if (src.Count < 2) return false;

            int crlf = IndexOfCRLF(src, 0);
            if (crlf < 0) return false;

            int at1 = IndexOfByte(src, (byte)'@', 0, crlf);
            if (at1 < 0)
            {
                frame = Take(src, crlf + 2);
                return true;
            }

            string cmd = GetAscii(src, 0, at1);
            if (cmd == "A_D" || cmd == "A_R")
            {
                int at2 = IndexOfByte(src, (byte)'@', at1 + 1, src.Count);
                if (at2 < 0) return false;

                string numStr = GetAscii(src, at1 + 1, at2);
                int token;
                if (!int.TryParse(numStr, out token) || token < 0)
                {
                    if (src.Count > 0) src.RemoveAt(0);
                    return false;
                }

                int dataStart = at2 + 1;
                int dataLen = (cmd == "A_D") ? token * 8 : 44 + token * 8 * 6;
                int afterData = dataStart + dataLen;

                if (src.Count < afterData + 3) return false;
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
                frame = Take(src, crlf + 2);
                return true;
            }
        }

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

    public class NetworkClient : IDisposable
    {
        // ===== 이벤트 =====
        public event EventHandler<string> LogEvented;       // 로그 전달
        public event EventHandler<byte[]> RecieveEvened;    // 프레임 수신 (서버와 호환: CRLF/토큰/바이너리)

        // ===== 상태/소켓 =====
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _sendLock = new object();

        private CancellationTokenSource _runnerCts;
        private Task _runnerTask;
        private Task _readTask;

        private readonly IFrameParser _parser;
        private readonly TimeSpan _readTimeout;

        private volatile bool _isConnected;
        public bool IsConnected { get { return _isConnected; } }

        public NetworkClient(int readTimeoutSeconds = 30, IFrameParser parser = null)
        {
            _readTimeout = TimeSpan.FromSeconds(Math.Max(1, readTimeoutSeconds));
            _parser = parser ?? new DefaultFrameParser();
        }

        // ===== 공개 API =====
        public async Task StartClientAsync(string host, int port)
        {
            if (_runnerTask != null) return;

            _runnerCts = new CancellationTokenSource();
            var ct = _runnerCts.Token;

            _runnerTask = Task.Run(async () =>
            {
                int backoffMs = 500; // 초기 재시도 간격
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        OnLog($"[Client] Connecting to {host}:{port}...");
                        var cli = new TcpClient();
                        await cli.ConnectAsync(host, port).ConfigureAwait(false);
                        cli.NoDelay = true;
                        lock (_sendLock)
                        {
                            _client = cli;
                            _stream = cli.GetStream();
                        }
                        _isConnected = true;
                        OnLog("[Client] Connected\r\n");

                        // 수신 루프 시작
                        _readTask = Task.Run(() => ReadLoopAsync(ct));
                        // 수신 루프 종료 대기
                        await _readTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        OnLog("[Client] Connect/Read error: " + ex.Message + "\r\n");
                    }
                    finally
                    {
                        // 정리
                        _isConnected = false;
                        lock (_sendLock)
                        {
                            try { if (_stream != null) _stream.Close(); } catch { }
                            try { if (_client != null) _client.Close(); } catch { }
                            _stream = null; _client = null;
                        }
                    }

                    // 재연결 대기
                    if (ct.IsCancellationRequested) break;
                    await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                    backoffMs = Math.Min(backoffMs * 2, 5000); // 최대 5초 백오프
                }
            }, ct);

            await Task.Yield();
        }

        public async Task StopClientAsync()
        {
            try
            {
                if (_runnerCts != null)
                {
                    _runnerCts.Cancel();
                }
                if (_runnerTask != null)
                {
                    try { await _runnerTask.ConfigureAwait(false); } catch { }
                    _runnerTask = null;
                }
            }
            finally
            {
                lock (_sendLock)
                {
                    try { if (_stream != null) _stream.Close(); } catch { }
                    try { if (_client != null) _client.Close(); } catch { }
                    _stream = null; _client = null; _isConnected = false;
                }
            }
        }

        // ===== 송신 =====
        public Task SendCmdAsync(string cmd)
        {
            if (cmd == null) cmd = string.Empty;

            // 규칙: 항상 @로 끝나도록 보장
            if (!cmd.EndsWith("@"))
                cmd += "@";

            // CRLF 붙이기
            var bytes = Encoding.ASCII.GetBytes(cmd.EndsWith("\r\n") ? cmd : cmd + "\r\n");
            return SendRawAsync(bytes);
        }

        public Task SendAsciiAsync(string cmd, params string[] args)
        {
            var sb = new StringBuilder(cmd ?? string.Empty);
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    sb.Append('@').Append(args[i] ?? string.Empty);
                }
            }
            sb.Append("\r\n");
            return SendRawAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        }

        public Task SendRawAsync(byte[] data)
        {
            try
            {
                lock (_sendLock)
                {
                    if (_stream == null || !_isConnected) throw new InvalidOperationException("Not connected");
                    _stream.Write(data, 0, data.Length);
                }
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                OnLog("[TX] Send error: " + ex.Message + "\r\n");
                return Task.FromException(ex);
            }
        }

        // ===== 내부 수신 루프 =====
        // ===== 내부 수신 루프 =====
        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var ring = new List<byte>(64 * 1024);
            var buf = new byte[8192];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read = 0;

                    // 현재 스트림 스냅샷 (await 전에 잠깐만 락)
                    NetworkStream localStream;
                    lock (_sendLock)
                    {
                        if (_stream == null) break;              // 아직/이미 끊김
                        localStream = _stream;                   // 참조만 복사
                    }

                    using (var tcs = new CancellationTokenSource(_readTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(tcs.Token, ct))
                    {
                        try
                        {
                            // 비동기 읽기 + 타임아웃/취소 토큰
                            read = await localStream
                                .ReadAsync(buf, 0, buf.Length, linked.Token)
                                .ConfigureAwait(false);

                            if (read == 0) break; // 원격 종료
                        }
                        catch (OperationCanceledException)
                        {
                            OnLog("[RX] timeout → reconnect\r\n");
                            break;
                        }
                    }

                    ring.AddRange(new ArraySegment<byte>(buf, 0, read));

                    int guard = 0;
                    byte[] frame;
                    while (_parser.TryExtract(ring, out frame))
                    {
                        try { var h = RecieveEvened; if (h != null) h(this, frame); } catch { }
                        if (++guard > 1000) break; // 안전 가드
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog("[RX] Exception: " + ex.Message + "\r\n");
            }
        }


        private void OnLog(string s)
        {
            var h = LogEvented; if (h != null) h(this, s);
        }

        public void Dispose()
        {
            try { StopClientAsync().GetAwaiter().GetResult(); } catch { }
        }
    }
}
