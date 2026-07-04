using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CampusNetworkLogin.Services;

/// <summary>
/// Dr.COM 校园网认证核心服务 - 移植自 Python 版本
/// </summary>
public class DrcomAuthService : IDisposable
{
    private Socket? _socket;
    private readonly int _localPort = 61440;
    private readonly int _serverPort = 61440;
    private string _serverIp = "";
    private string _username = "";
    private string _password = "";
    private string _hostIp = "";
    private long _mac = 0x888888888888;
    private string _hostName = "";
    private string _hostOs = "Windows 10";
    private string _primaryDns = "10.10.10.10";
    private string _dhcpServer = "0.0.0.0";
    private bool _running = false;
    private bool _loggedIn = false;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private TaskCompletionSource<bool>? _initialLoginTcs;

    // 协议常量
    private const byte ControlCheckStatus = 0x20;
    private const byte AdapterNum = 0x03;
    private const byte IpDog = 0x01;
    private static readonly byte[] AuthVersion = [0x68, 0x00];
    private static readonly byte[] KeepAliveVersion = [0xDC, 0x02];

    // 运行时状态
    private byte[] _salt = [];
    private byte[] _tail = [];

    public bool IsLoggedIn => _loggedIn;
    public bool IsRunning => _running;

    public event Action<string>? OnLog;
    public event Action<bool>? OnConnectionChanged;

    private void LogMessage(string message) => OnLog?.Invoke(message);

    public void UpdateConfig(
        string server, string username, string password,
        string hostIp, string mac, string hostName,
        string hostOs, string primaryDns, string dhcpServer)
    {
        _serverIp = server;
        _username = username;
        _password = password;
        _hostIp = hostIp;
        _hostName = hostName;
        _hostOs = hostOs;
        _primaryDns = primaryDns;
        _dhcpServer = dhcpServer;

        try
        {
            if (mac.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                _mac = Convert.ToInt64(mac, 16);
            else if (mac.Contains(':'))
                _mac = long.Parse(mac.Replace(":", ""), System.Globalization.NumberStyles.HexNumber);
            else
                _mac = Convert.ToInt64(mac, 16);
        }
        catch
        {
            _mac = 0x888888888888;
        }
    }

    public async Task<bool> StartLoginAsync()
    {
        if (_running)
        {
            if (_loggedIn) return true;
            if (_initialLoginTcs != null)
                return await _initialLoginTcs.Task.ConfigureAwait(false);
            return false;
        }

        _running = true;
        _loggedIn = false;
        _cts = new CancellationTokenSource();
        _initialLoginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnConnectionChanged?.Invoke(false);

        _workerTask = Task.Run(() => MainLoop(_cts.Token), _cts.Token);
        _ = _workerTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
                LogMessage($"认证服务异常退出：{task.Exception?.GetBaseException().Message}");
            _initialLoginTcs?.TrySetResult(false);
        }, TaskScheduler.Default);

        try
        {
            var loginResult = await _initialLoginTcs.Task
                .WaitAsync(TimeSpan.FromSeconds(30), _cts.Token)
                .ConfigureAwait(false);
            _initialLoginTcs = null;

            if (!loginResult)
                Stop();

            return loginResult;
        }
        catch (TimeoutException)
        {
            LogMessage("首次登录超时，请检查账号、密码或网络环境");
            _initialLoginTcs = null;
            Stop();
            return false;
        }
        catch (OperationCanceledException)
        {
            _initialLoginTcs = null;
            return false;
        }
    }

    public void Stop()
    {
        _running = false;
        _loggedIn = false;
        _cts?.Cancel();
        _initialLoginTcs?.TrySetResult(false);
        CloseSocket();
        OnConnectionChanged?.Invoke(false);
    }

    private void MainLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                CreateSocket();
                LogMessage("正在获取认证挑战...");
                var tail = Login(_username, _password, _serverIp, token);
                if (token.IsCancellationRequested) break;

                _tail = tail;
                _loggedIn = true;
                _initialLoginTcs?.TrySetResult(true);
                OnConnectionChanged?.Invoke(true);
                LogMessage("登录成功，进入保活阶段");

                EmptySocketBuffer();
                KeepAlive1(_salt, _tail, _password, _serverIp, token);
                if (token.IsCancellationRequested) break;

                KeepAlive2(_salt, _tail, _password, _serverIp, token);
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException ex)
            {
                LogMessage($"连接超时：{ex.Message}");
                if (!_loggedIn)
                    _initialLoginTcs?.TrySetResult(false);
                _loggedIn = false;
                OnConnectionChanged?.Invoke(false);
                CloseSocket();
                DelayWithCancellation(3000, token);
            }
            catch (Exception ex)
            {
                LogMessage($"登录失败：{ex.Message}");
                if (!_loggedIn)
                    _initialLoginTcs?.TrySetResult(false);
                _loggedIn = false;
                OnConnectionChanged?.Invoke(false);
                CloseSocket();
                DelayWithCancellation(3000, token);
            }
        }

        _running = false;
    }

    #region 协议核心方法

    private byte[] Challenge(string server, CancellationToken token)
    {
        var random = new Random();
        while (!token.IsCancellationRequested)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var ranVal = timestamp + random.Next(0xF, 0xFF);
            var ran = (ushort)(ranVal % 0xFFFF);
            var t = BitConverter.GetBytes(ran); // 小端序，Python "<H" 也是小端序
            var packet = new byte[20];
            packet[0] = 0x01;
            packet[1] = 0x02;
            packet[2] = t[0];
            packet[3] = t[1];
            packet[4] = 0x09;




                        try
            {
                SendTo(packet, server);
                var (data, _) = ReceiveFrom(token);

                if (data.Length >= 8 && data[0] == 0x02)
                {
                    var salt = new byte[4];
                    Array.Copy(data, 4, salt, 0, 4);
                    LogMessage("挑战码获取成功");
                    return salt;
                }
                LogMessage("挑战码响应格式异常，重试中...");
            }
            catch (TimeoutException)
            {
                LogMessage("挑战码请求超时，重试中...");
            }
            catch (Exception ex)
            {
                LogMessage($"挑战码请求异常：{ex.Message}");
            }
        }
        throw new OperationCanceledException();
    }

    private byte[] Login(string username, string password, string server, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var salt = Challenge(server, token);
            _salt = salt;

            var packet = BuildLoginPacket(salt, username, password, _mac);

                        try
            {
                SendTo(packet, server);
                var (data, _) = ReceiveFrom(token);

                if (data.Length > 0 && data[0] == 0x04)
                {
                    var tail = new byte[16];
                    if (data.Length >= 39)
                        Array.Copy(data, 23, tail, 0, 16);
                    else if (data.Length >= 22)
                        Array.Copy(data, data.Length - 22, tail, 0, Math.Min(16, data.Length - 22));
                    LogMessage("登录认证响应已接收");
                    return tail;
                }
                var code = data.Length > 0 ? $"0x{data[0]:X2}" : "empty";
                LogMessage($"登录认证响应异常 (data[0]={code})，3秒后重试...");
                DelayWithCancellation(3000, token);
            }
            catch (TimeoutException)
            {
                LogMessage("登录认证请求超时，3秒后重试...");
                DelayWithCancellation(3000, token);
            }
            catch (Exception ex)
            {
                LogMessage($"登录认证异常：{ex.Message}");
                DelayWithCancellation(3000, token);
            }
        }
        throw new OperationCanceledException();
    }

    private void KeepAlive1(byte[] salt, byte[] tail, string password, string server, CancellationToken token)
    {
        var foo = BitConverter.GetBytes((ushort)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 0xFFFF));
        if (BitConverter.IsLittleEndian) Array.Reverse(foo);

        var md5Input = new byte[] { 0x03, 0x01 };
        md5Input = [.. md5Input, .. salt, .. Encoding.ASCII.GetBytes(password)];
        var md51 = MD5.HashData(md5Input);

        var packet = new byte[1 + 16 + 3 + tail.Length + foo.Length + 4];
        int pos = 0;
        packet[pos++] = 0xFF;
        Array.Copy(md51, 0, packet, pos, 16); pos += 16;
        packet[pos++] = 0x00; packet[pos++] = 0x00; packet[pos++] = 0x00;
        Array.Copy(tail, 0, packet, pos, tail.Length); pos += tail.Length;
        Array.Copy(foo, 0, packet, pos, foo.Length); pos += foo.Length;

        SendTo(packet, server);

        while (!token.IsCancellationRequested)
        {
            var (data, _) = ReceiveFrom(token);
            if (data.Length > 0 && data[0] == 0x07)
                break;
        }
    }

    private void KeepAlive2(byte[] salt, byte[] tail, string password, string server, CancellationToken token)
    {
        var random = new Random();
        var ran = random.Next(0, 0xFFFF);
        ran += random.Next(1, 10);
        var svrNum = 0;
        var currentTail = new byte[4];

        // Step 1
        while (!token.IsCancellationRequested)
        {
            var packet = BuildKeepAlivePacket(svrNum, (ushort)(ran % 0xFFFF), currentTail, 1, svrNum == 0);
            SendTo(packet, server);

            var (data, _) = ReceiveFrom(token);

            if (data.Length >= 4 && data[0] == 0x07 && (data[1] == svrNum || data[1] == 0x00) && data[2] == 0x28)
                break;
            if (data.Length >= 3 && data[0] == 0x07 && data[2] == 0x10)
            {
                svrNum++;
                packet = BuildKeepAlivePacket(svrNum, (ushort)(ran % 0xFFFF), currentTail, 1, false);
            }
        }

        // Step 2
        if (token.IsCancellationRequested) return;

        ran += random.Next(1, 10);
        var packet2 = BuildKeepAlivePacket(svrNum, (ushort)(ran % 0xFFFF), currentTail, 1, false);

        SendTo(packet2, server);

        byte[] recvData2 = [];
        while (!token.IsCancellationRequested)
        {
            (recvData2, _) = ReceiveFrom(token);

            if (recvData2.Length > 0 && recvData2[0] == 0x07)
            {
                svrNum++;
                break;
            }
        }


        currentTail = new byte[4];
        if (recvData2.Length >= 20)
            Array.Copy(recvData2, 16, currentTail, 0, 4);


        // Step 3
        if (token.IsCancellationRequested) return;

        ran += random.Next(1, 10);
        var packet3 = BuildKeepAlivePacket(svrNum, (ushort)(ran % 0xFFFF), currentTail, 3, false);

        SendTo(packet3, server);

        byte[] recvData3 = [];
        while (!token.IsCancellationRequested)
        {
            (recvData3, _) = ReceiveFrom(token);

            if (recvData3.Length > 0 && recvData3[0] == 0x07)
            {
                svrNum++;
                break;
            }
        }


        currentTail = new byte[4];
        if (recvData3.Length >= 20)
            Array.Copy(recvData3, 16, currentTail, 0, 4);


        // 持续保活循环

        var i = svrNum;
        int failureCount = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                ran += random.Next(1, 10);
                var pkt = BuildKeepAlivePacket(i, (ushort)(ran % 0xFFFF), currentTail, 1, false);

                SendTo(pkt, server);

                byte[] rcvData;
                (rcvData, _) = ReceiveFrom(token);

                currentTail = new byte[4];
                if (rcvData.Length >= 20)
                    Array.Copy(rcvData, 16, currentTail, 0, 4);

                ran += random.Next(1, 10);
                var pkt2 = BuildKeepAlivePacket(i + 1, (ushort)(ran % 0xFFFF), currentTail, 3, false);

                SendTo(pkt2, server);

                byte[] rcvData2;
                (rcvData2, _) = ReceiveFrom(token);

                currentTail = new byte[4];
                if (rcvData2.Length >= 20)
                    Array.Copy(rcvData2, 16, currentTail, 0, 4);

                i = (i + 2) % 0xFF;

                for (int w = 0; w < 20 && !token.IsCancellationRequested; w++)
                    DelayWithCancellation(1000, token);

                if (!token.IsCancellationRequested)
                    KeepAlive1(salt, tail, password, server, token);
                    
                failureCount = 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                failureCount++;
                if (failureCount > 3) throw;

                DelayWithCancellation(1000, token);
            }
        }
    }

    #endregion

    #region 封包构建

    private byte[] BuildLoginPacket(byte[] salt, string username, string password, long mac)
    {



        var usrBytes = Encoding.ASCII.GetBytes(username);
        var pwdBytes = Encoding.ASCII.GetBytes(password);

        // ===== MD51 = MD5(0x03 + 0x01 + salt + password) =====
        var md5Input1 = new byte[] { 0x03, 0x01 };
        md5Input1 = [.. md5Input1, .. salt, .. pwdBytes];

        var md51 = MD5.HashData(md5Input1);


        // ===== MAC XOR = dump(int(data[4:10].encode('hex'),16) ^ mac) =====
        // Python data[4:10] -> data 刚添加完 md51 后，data[4:10] = md51[0:6]
        var md51Part = md51[0..6]; // 取 md51 前 6 字节
        long md51Val = 0;
        for (int j = 0; j < 6; j++)
            md51Val = (md51Val << 8) | md51Part[j];
        long xorVal = md51Val ^ (mac & 0xFFFFFFFFFFFF);



        var xorResult = new byte[6];
        for (int j = 5; j >= 0; j--)
        {
            xorResult[j] = (byte)(xorVal & 0xFF);
            xorVal >>= 8;
        }


        // ===== MD52 = MD5(0x01 + password + salt + 0x00*4) =====
        var md5Input2 = new byte[] { 0x01 };
        md5Input2 = [.. md5Input2, .. pwdBytes, .. salt, 0x00, 0x00, 0x00, 0x00];

        var md52 = MD5.HashData(md5Input2);


        var ipBytes = ParseIp(_hostIp);


        // ===== 开始构建数据包 =====
        var data = new List<byte>
        {
            0x03, 0x01, 0x00,
            (byte)(usrBytes.Length + 20)
        };


        data.AddRange(md51);


        var usrPadded = new byte[36];
        Array.Copy(usrBytes, usrPadded, Math.Min(usrBytes.Length, 36));
        data.AddRange(usrPadded);


        data.Add(ControlCheckStatus); data.Add(AdapterNum);


        data.AddRange(xorResult);


        data.AddRange(md52);


        data.Add(0x01);


        data.AddRange(ipBytes);
        data.AddRange(new byte[12]);


        // ===== MD53 = MD5(data + 0x14 0x00 0x07 0x0b)[:8] =====
        byte[] md5Input3 = [.. data, 0x14, 0x00, 0x07, 0x0b];

        var md53 = MD5.HashData(md5Input3);

        data.AddRange(md53.Take(8));


        data.Add(IpDog);
        data.AddRange(new byte[4]);


        var hostBytes = Encoding.ASCII.GetBytes(_hostName);
        var hostPadded = new byte[32];
        Array.Copy(hostBytes, hostPadded, Math.Min(hostBytes.Length, 32));
        data.AddRange(hostPadded);


        data.AddRange(ParseIp(_primaryDns));
        data.AddRange(ParseIp(_dhcpServer));
        data.AddRange(new byte[4]);
        data.AddRange(new byte[8]);


        data.AddRange([0x94, 0x00, 0x00, 0x00]);
        data.AddRange([0x06, 0x00, 0x00, 0x00]);
        data.AddRange([0x02, 0x00, 0x00, 0x00]);
        data.AddRange([0xF0, 0x23, 0x00, 0x00]);
        data.AddRange([0x02, 0x00, 0x00, 0x00]);
        data.AddRange([0x44, 0x72, 0x43, 0x4F, 0x4D, 0x00, 0xCF, 0x07, 0x68]);
        data.AddRange(new byte[55]);
        data.AddRange(Encoding.ASCII.GetBytes("3dc79f5212e8170acfa9ec95f1d749916542be7b1"));
        data.AddRange(new byte[24]);


        data.AddRange(AuthVersion);


        data.Add(0x00);
        data.Add((byte)pwdBytes.Length);


        var rorResult = Ror(md51, pwdBytes);

        data.AddRange(rorResult);


        data.AddRange([0x02, 0x0c]);


        // ===== Checksum =====
        var checkData = new List<byte>();
        checkData.AddRange(data);
        checkData.AddRange([0x01, 0x26, 0x07, 0x11, 0x00, 0x00]);
        var macDump = DumpLong(mac);

        checkData.AddRange(macDump);


        var cs = Checksum([.. checkData]);

        data.AddRange(cs);


        data.AddRange([0x00, 0x00]);
        data.AddRange(DumpLong(mac));


        // 密码长度填充：Python: if (len(pwd)/4) != 4: data += '\x00' * (len(pwd)/4)
        // 只有密码长度不为 16 时才填充
        if (pwdBytes.Length / 4 != 4)
        {
            var pwdPaddingLen = pwdBytes.Length / 4;
            data.AddRange(new byte[pwdPaddingLen]);

        }

        data.AddRange([0x60, 0xA2]);
        data.AddRange(new byte[28]);


        return [.. data];
    }

    private byte[] BuildKeepAlivePacket(int number, ushort random, byte[] tail, int type, bool first)
    {
        var data = new List<byte>
        {
            0x07,
            (byte)number,
            0x28,
            0x00,
            0x0B,
            (byte)type
        };

        if (first)
            data.AddRange([0x0F, 0x27]);
        else
            data.AddRange(KeepAliveVersion);

        data.AddRange([0x2F, 0x12]);
        data.AddRange(new byte[6]);
        data.AddRange(tail);
        data.AddRange(new byte[4]);

        if (type == 3)
        {
            var foo = ParseIp(_hostIp);
            data.AddRange(new byte[4]); // CRC placeholder
            data.AddRange(foo);
            data.AddRange(new byte[8]);
        }
        else
        {
            data.AddRange(new byte[16]);
        }

        return [.. data];
    }

    #endregion

    #region 辅助方法

    private void CreateSocket()
    {
        CloseSocket();
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(IPAddress.Any, _localPort));
        _socket.ReceiveTimeout = 3000;
        _socket.SendTimeout = 3000;

    }

    private void CloseSocket()
    {
        try { _socket?.Close(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
    }

    private void SendTo(byte[] data, string server)
    {
        if (_socket == null || !_socket.IsBound)
            CreateSocket();
        var endpoint = new IPEndPoint(IPAddress.Parse(server), _serverPort);
        _socket!.SendTo(data, endpoint);
    }

    private (byte[] data, IPEndPoint remote) ReceiveFrom(CancellationToken token)
    {
        if (_socket == null)
            throw new InvalidOperationException("Socket not initialized");

        var remoteEp = new IPEndPoint(IPAddress.Any, 0);
        var buffer = new byte[1024];

        while (!token.IsCancellationRequested)
        {
            try
            {
                EndPoint ep = remoteEp;
                var len = _socket.ReceiveFrom(buffer, ref ep);
                var result = new byte[len];
                Array.Copy(buffer, result, len);
                return (result, (IPEndPoint)ep);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                throw new TimeoutException("Socket receive timeout");
            }
            catch (ObjectDisposedException)
            {
                throw new OperationCanceledException();
            }
        }
        throw new OperationCanceledException();
    }

    private void EmptySocketBuffer()
    {

        try
        {
            if (_socket != null) _socket.ReceiveTimeout = 500;
            while (true)
            {
                var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                EndPoint ep = remoteEp;
                var buffer = new byte[1024];
                if (_socket != null)
                {
                    _socket.ReceiveFrom(buffer, ref ep);
                }
                else break;
            }
        }
        catch { }
        finally
        {
            if (_socket != null) _socket.ReceiveTimeout = 3000;
        }

    }

    private static byte[] DumpLong(long val)
    {
        // Python dump(): val -> hex string -> decode('hex')，大端序
        var hex = val.ToString("x");
        if (hex.Length % 2 == 1) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static byte[] ParseIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
            return [0, 0, 0, 0];

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 ? bytes : [0, 0, 0, 0];
    }

    private static void DelayWithCancellation(int milliseconds, CancellationToken token)
    {
        if (milliseconds <= 0 || token.IsCancellationRequested)
            token.ThrowIfCancellationRequested();

        if (token.WaitHandle.WaitOne(milliseconds))
            throw new OperationCanceledException(token);
    }

    private static byte[] Ror(byte[] md5Data, byte[] pwd)
    {
        // Python ror(): for i in len(pwd): x = md5[i] ^ pwd[i]; chr(((x<<3)&0xFF) + (x>>5))
        var ret = new byte[pwd.Length];
        for (int i = 0; i < pwd.Length; i++)
        {
            var x = md5Data[i] ^ pwd[i];
            ret[i] = (byte)(((x << 3) & 0xFF) + (x >> 5));
        }
        return ret;
    }

    private static byte[] Checksum(byte[] data)
    {
        // Python:
        // ret = 1234
        // for i in re.findall('....', s): 每 4 字节一组
        //     ret ^= int(i[::-1].encode('hex'), 16) # 反转字节序后按大端解析
        // ret = (1968 * ret) & 0xffffffff
        // return struct.pack('<I', ret) # 返回小端序
        uint ret = 1234;
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            var chunk = new byte[4];
            Array.Copy(data, i, chunk, 0, 4);
            Array.Reverse(chunk);  // i[::-1]
            uint val = (uint)((chunk[0] << 24) | (chunk[1] << 16) | (chunk[2] << 8) | chunk[3]);
            ret ^= val;
        }
        ret = (1968 * ret) & 0xFFFFFFFF;
        var result = BitConverter.GetBytes(ret);
        if (!BitConverter.IsLittleEndian) Array.Reverse(result);
        return result;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    #endregion
}
