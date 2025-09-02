using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSHServer
{
    public class Network
    {
        public event EventHandler<string> LogEvented = null;
        public event EventHandler<byte[]> RecieveEvened = null;

        private TcpListener Listener;
        private List<TcpClient> ConnectedClients = new List<TcpClient>();
        private Dictionary<TcpClient, NetworkStream> ClientStreams = new Dictionary<TcpClient, NetworkStream>();
        private Dictionary<TcpClient, bool> ClientAliveStates = new Dictionary<TcpClient, bool>();
        private readonly object clientLock = new object();

        public bool IsMonitorStart = true;

        public void StartServer(int port)
        {
            Listener = new TcpListener(IPAddress.Any, port);
            Listener.Start();
            Task.Run(() => AcceptLoop());
        }
        private async Task AcceptLoop()
        {
            while (true)
            {
                try
                {
                    TcpClient client = await Listener.AcceptTcpClientAsync();
                    NetworkStream stream = client.GetStream();

                    // 기존 클라이언트 모두 정리 (단일 클라이언트 구조 기준)
                    lock (clientLock)
                    {
                        foreach (var oldClient in ConnectedClients)
                        {
                            oldClient.Close();
                        }

                        ConnectedClients.Clear();
                        ClientStreams.Clear();
                        ClientAliveStates.Clear();

                        ConnectedClients.Add(client);
                        ClientStreams[client] = stream;
                        ClientAliveStates[client] = true;
                    }

                    if (LogEvented != null) LogEvented(this, "Client Connected: " + client.Client.RemoteEndPoint);

                    _ = HandleClientAsync(client, stream);
                }
                catch (Exception ex)
                {
                    if (LogEvented != null) LogEvented(this, "Accept Error: " + ex.Message);
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, NetworkStream stream)
        {
            //NetWorkMonitor(client, stream);
            await ReciveDataAsync(client, stream);

            lock (clientLock)
            {
                ConnectedClients.Remove(client);
                ClientStreams.Remove(client);
                ClientAliveStates.Remove(client);
            }

            if (LogEvented != null) LogEvented(this, "Client Disconnected: " + client.Client.RemoteEndPoint);
            client.Close();
        }

        private async Task ReciveDataAsync(TcpClient client, NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            StringBuilder sb = new StringBuilder();

            while (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead == 0) break;

                    string received = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    sb.Append(received);

                    // 기준으로 메시지를 하나씩 분리
                    while (sb.ToString().Contains("\r\n"))
                    {
                        string fullBuffer = sb.ToString();
                        int splitIndex = fullBuffer.IndexOf("\r\n");

                        string messageWithTerminator = fullBuffer.Substring(0, splitIndex + 2);
                        sb.Remove(0, splitIndex + 2);

                        // 처리 이벤트 호출
                        if (RecieveEvened != null)
                        {
                            byte[] msgBytes = Encoding.ASCII.GetBytes(messageWithTerminator);
                            RecieveEvened(this, msgBytes);
                        }
                    }
                }
                catch (OperationCanceledException ex)
                {
                    LogEvented?.Invoke(this, "Timeout network " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    LogEvented?.Invoke(this, "Receive Error: " + ex.Message);
                    break;
                }
                finally
                {
                    cts.Dispose();
                }
            }

            client.Close();
            LogEvented?.Invoke(this, "Client Disconnected==");
        }

        public void SendDataToClient(TcpClient client, byte[] data)
        {
            try
            {
                lock (clientLock)
                {
                    if (ClientStreams.ContainsKey(client) && client.Connected)
                    {
                        NetworkStream stream = ClientStreams[client];
                        stream.Write(data, 0, data.Length);
                        if (LogEvented != null) LogEvented(this, "Sent to " + client.Client.RemoteEndPoint);
                    }
                }
            }
            catch (Exception ex)
            {
                if (LogEvented != null) LogEvented(this, "Send Error: " + ex.Message);
            }
        }

        public void SendData(byte[] data)
        {
            BroadcastData(data);
        }

        public void BroadcastData(byte[] data)
        {
            lock (clientLock)
            {
                foreach (TcpClient client in ConnectedClients)
                {
                    if (client.Connected)
                    {
                        try
                        {
                            ClientStreams[client].Write(data, 0, data.Length);
                        }
                        catch { }
                    }
                }
            }
        }

        public void NetWorkMonitor(TcpClient client, NetworkStream stream)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(5000);

                    try
                    {
                        if (IsMonitorStart && client.Connected)
                        {
                            byte[] ping = Encoding.ASCII.GetBytes("<PING>");
                            await stream.WriteAsync(ping, 0, ping.Length);

                            bool disconnected = client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0;
                            if (disconnected)
                            {
                                lock (clientLock) ClientAliveStates[client] = false;
                                LogEvented?.Invoke(this, "클라이언트 Poll 감지 → 연결 끊김");
                                client.Close();
                                break;
                            }
                            else
                            {
                                lock (clientLock) ClientAliveStates[client] = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (clientLock) ClientAliveStates[client] = false;
                        LogEvented?.Invoke(this, "모니터링 중 예외 발생: " + ex.Message);

                        client.Close();
                        break;
                    }
                }
            });
        }

        public List<TcpClient> GetConnectedClients()
        {
            lock (clientLock)
            {
                return new List<TcpClient>(ConnectedClients);
            }
        }

        public bool IsClientAlive(TcpClient client)
        {
            lock (clientLock)
            {
                return ClientAliveStates.ContainsKey(client) && ClientAliveStates[client];
            }
        }

        public void StopServer()
        {
            try
            {
                if (Listener != null) Listener.Stop();
                lock (clientLock)
                {
                    foreach (TcpClient client in ConnectedClients)
                    {
                        client.Close();
                    }
                    ConnectedClients.Clear();
                    ClientStreams.Clear();
                    ClientAliveStates.Clear();
                }
                if (LogEvented != null) LogEvented(this, "서버 종료 완료");
            }
            catch (Exception ex)
            {
                if (LogEvented != null) LogEvented(this, "서버 종료 오류: " + ex.Message);
            }
        }
    }
}
