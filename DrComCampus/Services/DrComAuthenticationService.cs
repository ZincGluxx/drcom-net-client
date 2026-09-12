using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DrComCampus.Services;

internal enum ConnectionStatus { Disconnected, Connecting, Connected }

internal enum LoginStep { None, Challenge, Authenticating, KeepAlive, Connected }

internal sealed class AuthServerUnreachableException : Exception
{
    public AuthServerUnreachableException(string message) : base(message) { }
    public AuthServerUnreachableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Dr.COM 校园网认证核心服务 - 移植自 Python 版本
/// </summary>
internal sealed class DrComAuthenticationService : IDisposable
{
    private const int LocalPort = 61440;
    private const int ServerPort = 61440;
    private const long DefaultMacAddressValue = 0x888888888888;

    private Socket? _socket;
    private IPEndPoint? _serverEndpoint;
    private string _serverAddress = "";
    private string _username = "";
    private string _password = "";
    private string _hostIpv4Address = "";
    private long _macAddressValue = DefaultMacAddressValue;
    private string _hostName = "";
    private string _primaryDns = "10.10.10.10";
    private string _dhcpServer = "0.0.0.0";
    private volatile bool _running;
    private volatile bool _loggedIn;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private TaskCompletionSource<bool>? _initialLoginTcs;
    private readonly byte[] _receiveBuffer = new byte[1024];

    // 协议常量
    private const byte ControlCheckStatus = 0x20;
    private const byte AdapterNum = 0x03;
    private const byte IpDog = 0x01;
    private static readonly byte[] s_authVersion = [0x68, 0x00];
    private static readonly byte[] s_keepAliveVersion = [0xDC, 0x02];

    // 运行时状态
    private byte[] _salt = [];
    private byte[] _tail = [];
    private bool _wasConnected;
    private bool _isReconnecting;
    private int _reconnectAttempts;
    private const int BaseReconnectDelayMs = 3000;
    private const int MaxReconnectDelayMs = 60000;
    private const int KeepAliveIntervalMs = 20000;
    private const int KeepAliveRetryLimit = 3;

    public bool IsLoggedIn => _loggedIn;
    public bool IsRunning => _running;
    public DateTime? ConnectedSince { get; private set; }

    public event Action<ConnectionStatus>? ConnectionStatusChanged;
    public event Action<LoginStep>? LoginStepChanged;
    public event Action<string>? ErrorOccurred;

    private void ReportError(string message) => ErrorOccurred?.Invoke(message);

    public void UpdateConfig(
        string serverAddress, string username, string password,
        string hostIpv4Address, string macAddress, string hostName,
        string primaryDns, string dhcpServer)
    {
        if (!string.Equals(_serverAddress, serverAddress, StringComparison.OrdinalIgnoreCase))
        {
            _serverEndpoint = null;
        }

        _serverAddress = serverAddress;
        _username = username;
        _password = password;
        _hostIpv4Address = hostIpv4Address;
        _hostName = hostName;
        _primaryDns = primaryDns;
        _dhcpServer = dhcpServer;

        var normalizedMacAddress = macAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? macAddress[2..]
            : macAddress
                .Replace(":", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal);

        if (!long.TryParse(
                normalizedMacAddress,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out _macAddressValue))
        {
            _macAddressValue = DefaultMacAddressValue;
        }
    }

    public async Task<bool> StartLoginAsync()
    {
        if (_running)
        {
            if (_loggedIn)
            {
                return true;
            }

            if (_initialLoginTcs != null)
            {
                return await _initialLoginTcs.Task.ConfigureAwait(false);
            }

            return false;
        }

        _running = true;
        _loggedIn = false;
        _wasConnected = false;
        _isReconnecting = false;
        _reconnectAttempts = 0;
        ConnectedSince = null;
        var cts = new CancellationTokenSource();
        var initialLoginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cts = cts;
        _initialLoginTcs = initialLoginTcs;
        ConnectionStatusChanged?.Invoke(ConnectionStatus.Connecting);

        _workerTask = Task.Run(() => MainLoop(initialLoginTcs, cts.Token), cts.Token);
        _ = _workerTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                ReportError($"认证服务异常退出：{task.Exception?.GetBaseException().Message}");
            }

            initialLoginTcs.TrySetResult(false);
            Interlocked.CompareExchange(ref _cts, null, cts);
            Interlocked.CompareExchange(ref _initialLoginTcs, null, initialLoginTcs);
            cts.Dispose();
        }, TaskScheduler.Default);

        try
        {
            var loginResult = await initialLoginTcs.Task
                .WaitAsync(TimeSpan.FromSeconds(30), cts.Token)
                .ConfigureAwait(false);
            Interlocked.CompareExchange(ref _initialLoginTcs, null, initialLoginTcs);

            if (!loginResult)
            {
                Stop();
            }

            return loginResult;
        }
        catch (TimeoutException)
        {
            ReportError("首次登录超时，请检查账号、密码或网络环境");
            Interlocked.CompareExchange(ref _initialLoginTcs, null, initialLoginTcs);
            Stop();
            return false;
        }
        catch (OperationCanceledException)
        {
            Interlocked.CompareExchange(ref _initialLoginTcs, null, initialLoginTcs);
            return false;
        }
    }

    public void Stop()
    {
        _running = false;
        _loggedIn = false;
        _wasConnected = false;
        _isReconnecting = false;
        _reconnectAttempts = 0;
        ConnectedSince = null;
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _initialLoginTcs?.TrySetResult(false);
        CloseSocket();
        ConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
    }

    private void MainLoop(TaskCompletionSource<bool> initialLoginTcs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_isReconnecting)
            {
                // 原版会持续恢复连接。指数只用于前几次，之后固定每分钟尝试，避免忙循环。
                var exponent = Math.Min(_reconnectAttempts, 4);
                var delay = Math.Min(BaseReconnectDelayMs * (1 << exponent), MaxReconnectDelayMs);
                try { DelayWithCancellation(delay, token); }
                catch (OperationCanceledException) { break; }
                _reconnectAttempts = Math.Min(_reconnectAttempts + 1, 5);
            }

            try
            {
                OpenSocket();
                LoginStepChanged?.Invoke(LoginStep.Challenge);
                LoginStepChanged?.Invoke(LoginStep.Authenticating);
                var tail = Authenticate(_username, _password, token);
                if (token.IsCancellationRequested)
                {
                    break;
                }

                _tail = tail;
                _loggedIn = true;
                _wasConnected = true;
                _isReconnecting = false;
                _reconnectAttempts = 0;
                ConnectedSince = DateTime.Now;
                initialLoginTcs.TrySetResult(true);
                ConnectionStatusChanged?.Invoke(ConnectionStatus.Connected);
                LoginStepChanged?.Invoke(LoginStep.Connected);

                DrainSocketReceiveBuffer();
                LoginStepChanged?.Invoke(LoginStep.KeepAlive);
                RunKeepAliveLoop(_salt, _tail, _password, token);
            }
            catch (OperationCanceledException) { break; }
            catch (AuthServerUnreachableException ex)
            {
                ReportError($"服务器不可达：{ex.Message}");
                if (!_loggedIn)
                {
                    initialLoginTcs.TrySetResult(false);
                }

                HandleDisconnect();
                CloseSocket();
            }
            catch (TimeoutException ex)
            {
                ReportError($"连接超时：{ex.Message}");
                if (!_loggedIn)
                {
                    initialLoginTcs.TrySetResult(false);
                }

                HandleDisconnect();
                CloseSocket();
            }
            catch (Exception ex)
            {
                ReportError($"登录失败：{ex.Message}");
                if (!_loggedIn)
                {
                    initialLoginTcs.TrySetResult(false);
                }

                HandleDisconnect();
                CloseSocket();
            }
        }

        _running = false;
    }

    private void HandleDisconnect()
    {
        _loggedIn = false;
        ConnectedSince = null;
        LoginStepChanged?.Invoke(LoginStep.None);

        if (_wasConnected)
        {
            _isReconnecting = true;
            _reconnectAttempts = 0;
            ConnectionStatusChanged?.Invoke(ConnectionStatus.Connecting);
        }
        else
        {
            ConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }
    }

    #region 协议核心方法

    private byte[] RequestChallenge(CancellationToken token)
    {
        var consecutiveFailures = 0;
        while (!token.IsCancellationRequested)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var randomValue = timestamp + RandomNumberGenerator.GetInt32(0x0F, 0xFF);
            var challengeId = (ushort)(randomValue % ushort.MaxValue);
            var challengeIdBytes = BitConverter.GetBytes(challengeId);
            var packet = new byte[20];
            packet[0] = 0x01;
            packet[1] = 0x02;
            packet[2] = challengeIdBytes[0];
            packet[3] = challengeIdBytes[1];
            packet[4] = 0x09;

            try
            {
                SendPacket(packet);
                var length = ReceivePacket(token);

                if (length >= 8 && _receiveBuffer[0] == 0x02)
                {
                    var salt = new byte[4];
                    Array.Copy(_receiveBuffer, 4, salt, 0, 4);
                    return salt;
                }
            }
            catch (TimeoutException)
            {
                consecutiveFailures++;
            }
            catch (SocketException ex)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 5)
                {
                    throw new AuthServerUnreachableException("认证服务器不可达", ex);
                }
            }
            catch (Exception)
            {
                consecutiveFailures++;
            }
        }
        throw new OperationCanceledException();
    }

    private byte[] Authenticate(string username, string password, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var salt = RequestChallenge(token);
            _salt = salt;

            var packet = BuildLoginPacket(salt, username, password, _macAddressValue);

            try
            {
                SendPacket(packet);
                var length = ReceivePacket(token);

                if (length > 0 && _receiveBuffer[0] == 0x04)
                {
                    var tail = new byte[16];
                    if (length >= 39)
                    {
                        Array.Copy(_receiveBuffer, 23, tail, 0, 16);
                    }
                    else if (length >= 22)
                    {
                        Array.Copy(_receiveBuffer, length - 22, tail, 0, Math.Min(16, length - 22));
                    }

                    return tail;
                }
                DelayWithCancellation(3000, token);
            }
            catch (TimeoutException)
            {
                DelayWithCancellation(3000, token);
            }
            catch (Exception)
            {
                DelayWithCancellation(3000, token);
            }
        }
        throw new OperationCanceledException();
    }

    private void SendPrimaryKeepAlive(byte[] salt, byte[] tail, string password, CancellationToken token)
    {
        for (var attempt = 0; attempt < KeepAliveRetryLimit; attempt++)
        {
            var foo = BitConverter.GetBytes((ushort)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 0xFFFF));
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(foo);
            }

            var md5Input = new byte[] { 0x03, 0x01 };
            md5Input = [.. md5Input, .. salt, .. Encoding.ASCII.GetBytes(password)];
            var md51 = MD5.HashData(md5Input);

            var packet = new byte[1 + 16 + 3 + tail.Length + foo.Length + 4];
            var pos = 0;
            packet[pos++] = 0xFF;
            Array.Copy(md51, 0, packet, pos, 16); pos += 16;
            packet[pos++] = 0x00; packet[pos++] = 0x00; packet[pos++] = 0x00;
            Array.Copy(tail, 0, packet, pos, tail.Length); pos += tail.Length;
            Array.Copy(foo, 0, packet, pos, foo.Length);

            SendPacket(packet);
            if (TryReceiveExpected(static (buffer, length) => length > 0 && buffer[0] == 0x07, out _, token))
            {
                return;
            }
        }

        throw new TimeoutException("主保活连续无响应");
    }

    private void RunKeepAliveLoop(byte[] salt, byte[] tail, string password, CancellationToken token)
    {
        var restartCount = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                RunKeepAliveSession(salt, tail, password, () => restartCount = 0, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                if (++restartCount >= KeepAliveRetryLimit)
                {
                    throw;
                }

                // 原版客户端会从主保活重新同步，而不是沿用可能错位的阶段继续发送。
                DrainSocketReceiveBuffer();
                DelayWithCancellation(1000, token);
            }
        }
    }

    private void RunKeepAliveSession(
        byte[] salt,
        byte[] sessionTail,
        string password,
        Action reportHealthyCycle,
        CancellationToken token)
    {
        var sequence = 0;
        var keepAliveTail = new byte[4];

        SendPrimaryKeepAlive(salt, sessionTail, password, token);

        _ = SendKeepAliveAndReceive(sequence, keepAliveTail, 1, true, true, token);
        if (_receiveBuffer[2] == 0x10)
        {
            sequence = NextSequence(sequence);
        }

        var type1Length = SendKeepAliveAndReceive(sequence, keepAliveTail, 1, false, false, token);
        sequence = NextSequence(sequence);
        UpdateKeepAliveTail(keepAliveTail, type1Length);

        var type3Length = SendKeepAliveAndReceive(sequence, keepAliveTail, 3, false, false, token);
        UpdateKeepAliveTail(keepAliveTail, type3Length);
        reportHealthyCycle();

        while (!token.IsCancellationRequested)
        {
            DelayWithCancellation(KeepAliveIntervalMs, token);
            SendPrimaryKeepAlive(salt, sessionTail, password, token);

            // Dr.COM 在主保活确认后推进序号，并用登录会话尾码启动下一组 type2 保活。
            sequence = NextSequence(sequence);
            type1Length = SendKeepAliveAndReceive(sequence, sessionTail, 1, false, false, token);
            sequence = NextSequence(sequence);
            UpdateKeepAliveTail(keepAliveTail, type1Length);

            type3Length = SendKeepAliveAndReceive(sequence, keepAliveTail, 3, false, false, token);
            UpdateKeepAliveTail(keepAliveTail, type3Length);
            reportHealthyCycle();
        }
    }

    private int SendKeepAliveAndReceive(
        int sequence,
        byte[] packetTail,
        int packetType,
        bool isFirst,
        bool requireHandshakeHeader,
        CancellationToken token)
    {
        var packet = BuildKeepAlivePacket(sequence, packetTail, packetType, isFirst);
        for (var attempt = 0; attempt < KeepAliveRetryLimit; attempt++)
        {
            SendPacket(packet);
            if (TryReceiveExpected((buffer, length) =>
                length >= (requireHandshakeHeader ? 4 : 20) &&
                buffer[0] == 0x07 &&
                (!requireHandshakeHeader ||
                 (buffer[2] == 0x10 ||
                  (buffer[2] == 0x28 && (buffer[1] == (byte)sequence || buffer[1] == 0x00)))), out var length, token))
            {
                return length;
            }
        }

        throw new TimeoutException($"保活阶段 type{packetType} 连续无响应");
    }

    private static int NextSequence(int sequence) => (sequence + 1) & 0xFF;

    private void UpdateKeepAliveTail(byte[] destination, int responseLength)
    {
        // 未收到完整响应时保留上一个有效尾码，禁止以全零尾码污染后续保活。
        if (responseLength >= 20)
        {
            Array.Copy(_receiveBuffer, 16, destination, 0, 4);
        }
    }

    #endregion

    #region 封包构建

    private byte[] BuildLoginPacket(byte[] salt, string username, string password, long mac)
    {



        var usrBytes = Encoding.ASCII.GetBytes(username);
        var pwdBytes = Encoding.ASCII.GetBytes(password);

        // ===== MD51 = MD5(0x03 + 0x01 + salt + password) =====
        var md5Input1 = new byte[2 + salt.Length + pwdBytes.Length];
        md5Input1[0] = 0x03;
        md5Input1[1] = 0x01;
        salt.CopyTo(md5Input1, 2);
        pwdBytes.CopyTo(md5Input1, 2 + salt.Length);

        var md51 = MD5.HashData(md5Input1);


        // ===== MAC XOR = dump(int(data[4:10].encode('hex'),16) ^ mac) =====
        // Python data[4:10] -> data 刚添加完 md51 后，data[4:10] = md51[0:6]
        long md51Val = 0;
        for (int j = 0; j < 6; j++)
        {
            md51Val = (md51Val << 8) | md51[j];
        }

        long xorVal = md51Val ^ (mac & 0xFFFFFFFFFFFF);



        var xorResult = new byte[6];
        for (int j = 5; j >= 0; j--)
        {
            xorResult[j] = (byte)(xorVal & 0xFF);
            xorVal >>= 8;
        }


        // ===== MD52 = MD5(0x01 + password + salt + 0x00*4) =====
        var md5Input2 = new byte[1 + pwdBytes.Length + salt.Length + 4];
        md5Input2[0] = 0x01;
        pwdBytes.CopyTo(md5Input2, 1);
        salt.CopyTo(md5Input2, 1 + pwdBytes.Length);

        var md52 = MD5.HashData(md5Input2);


        var ipBytes = ParseIpv4Address(_hostIpv4Address);


        // ===== 开始构建数据包 =====
        var data = new List<byte>(384)
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

        for (var i = 0; i < 8; i++)
        {
            data.Add(md53[i]);
        }

        data.Add(IpDog);
        data.AddRange(new byte[4]);


        var hostBytes = Encoding.ASCII.GetBytes(_hostName);
        var hostPadded = new byte[32];
        Array.Copy(hostBytes, hostPadded, Math.Min(hostBytes.Length, 32));
        data.AddRange(hostPadded);


        data.AddRange(ParseIpv4Address(_primaryDns));
        data.AddRange(ParseIpv4Address(_dhcpServer));
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


        data.AddRange(s_authVersion);


        data.Add(0x00);
        data.Add((byte)pwdBytes.Length);


        var rorResult = RotatePasswordBytes(md51, pwdBytes);

        data.AddRange(rorResult);


        data.AddRange([0x02, 0x0c]);


        // ===== Checksum =====
        var checkData = new List<byte>(data.Count + 14);
        checkData.AddRange(data);
        checkData.AddRange([0x01, 0x26, 0x07, 0x11, 0x00, 0x00]);
        var macDump = ToMinimalBigEndianBytes(mac);

        checkData.AddRange(macDump);


        var cs = ComputeChecksum([.. checkData]);

        data.AddRange(cs);


        data.AddRange([0x00, 0x00]);
        data.AddRange(ToMinimalBigEndianBytes(mac));


        // 密码填充：与 Python 版保持一致，添加 (pwdLen / 4) 个零字节
        var pwdPaddingCount = pwdBytes.Length / 4;
        if (pwdPaddingCount > 0)
        {
            data.AddRange(new byte[pwdPaddingCount]);
        }

        data.AddRange([0x60, 0xA2]);
        data.AddRange(new byte[28]);


        return [.. data];
    }

    private byte[] BuildKeepAlivePacket(int sequenceNumber, byte[] tail, int packetType, bool isFirst)
    {
        var data = new byte[40];
        data[0] = 0x07;
        data[1] = (byte)sequenceNumber;
        data[2] = 0x28;
        data[4] = 0x0B;
        data[5] = (byte)packetType;
        data[6] = isFirst ? (byte)0x0F : s_keepAliveVersion[0];
        data[7] = isFirst ? (byte)0x27 : s_keepAliveVersion[1];
        data[8] = 0x2F;
        data[9] = 0x12;
        Array.Copy(tail, 0, data, 16, Math.Min(4, tail.Length));

        if (packetType == 3)
        {
            var ip = ParseIpv4Address(_hostIpv4Address);
            Array.Copy(ip, 0, data, 28, 4);
        }

        return data;
    }

    #endregion

    #region 辅助方法

    private void OpenSocket()
    {
        CloseSocket();
        _serverEndpoint ??= new IPEndPoint(ResolveServerIp(_serverAddress), ServerPort);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(GetLocalBindAddress(), LocalPort));
        _socket.ReceiveTimeout = 3000;
        _socket.SendTimeout = 3000;

    }

    private void CloseSocket()
    {
        try { _socket?.Close(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
    }

    private void SendPacket(byte[] data)
    {
        if (_socket == null || !_socket.IsBound)
        {
            OpenSocket();
        }

        _socket!.SendTo(data, _serverEndpoint!);
    }

    private int ReceivePacket(CancellationToken token)
    {
        if (_socket == null)
        {
            throw new InvalidOperationException("Socket not initialized");
        }

        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                EndPoint ep = remoteEp;
                var length = _socket.ReceiveFrom(_receiveBuffer, ref ep);
                if (ep is IPEndPoint sender &&
                    _serverEndpoint != null &&
                    sender.Port == _serverEndpoint.Port &&
                    sender.Address.Equals(_serverEndpoint.Address))
                {
                    return length;
                }
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

    private void DrainSocketReceiveBuffer()
    {
        try
        {
            _socket?.ReceiveTimeout = 500;
            while (true)
            {
                var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                EndPoint ep = remoteEp;
                if (_socket != null)
                {
                    _socket.ReceiveFrom(_receiveBuffer, ref ep);
                }
                else
                {
                    break;
                }
            }
        }
        catch { }
        finally
        {
            _socket?.ReceiveTimeout = 3000;
        }
    }

    private static byte[] ToMinimalBigEndianBytes(long input)
    {
        // Python dump() 等价实现：最短长度的大端字节序，避免十六进制字符串与切片分配
        var value = unchecked((ulong)input);
        var byteCount = 1;
        for (var remaining = value >> 8; remaining != 0; remaining >>= 8)
        {
            byteCount++;
        }

        var bytes = new byte[byteCount];
        for (var i = byteCount - 1; i >= 0; i--)
        {
            bytes[i] = (byte)value;
            value >>= 8;
        }
        return bytes;
    }

    private static byte[] ParseIpv4Address(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return [0, 0, 0, 0];
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 ? bytes : [0, 0, 0, 0];
    }

    private static void DelayWithCancellation(int milliseconds, CancellationToken token)
    {
        if (milliseconds <= 0 || token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
        }

        if (token.WaitHandle.WaitOne(milliseconds))
        {
            throw new OperationCanceledException(token);
        }
    }

    private bool TryReceiveExpected(
        Func<byte[], int, bool> predicate,
        out int length,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                length = ReceivePacket(token);
                if (predicate(_receiveBuffer, length))
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
                length = 0;
                return false;
            }
        }

        throw new OperationCanceledException(token);
    }

    /// <summary>
    /// 解析认证服务器地址：支持 IP 或域名（取首个 IPv4）
    /// </summary>
    private static IPAddress ResolveServerIp(string serverAddress)
    {
        if (IPAddress.TryParse(serverAddress, out var address))
        {
            return address;
        }

        var addresses = Dns.GetHostAddresses(serverAddress);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new InvalidOperationException($"无法解析认证服务器地址: {serverAddress}");
    }

    private IPAddress GetLocalBindAddress()
    {
        // 与原版的接口绑定/认证服务器主机路由作用一致：认证流量只能从选中的真实网卡发出。
        return IPAddress.TryParse(_hostIpv4Address, out var address) &&
               address.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.Any.Equals(address)
            ? address
            : IPAddress.Any;
    }

    private static byte[] RotatePasswordBytes(byte[] md5Data, byte[] passwordBytes)
    {
        // Python ror(): for i in len(pwd): x = md5[i] ^ pwd[i]; chr(((x<<3)&0xFF) + (x>>5))
        var result = new byte[passwordBytes.Length];
        for (var i = 0; i < passwordBytes.Length; i++)
        {
            var value = md5Data[i] ^ passwordBytes[i];
            result[i] = (byte)(((value << 3) & 0xFF) + (value >> 5));
        }
        return result;
    }

    private static byte[] ComputeChecksum(ReadOnlySpan<byte> data)
    {
        // Python:
        // ret = 1234
        // for i in re.findall('....', s): 每 4 字节一组
        //     ret ^= int(i[::-1].encode('hex'), 16) # 反转字节序后按大端解析
        // ret = (1968 * ret) & 0xffffffff
        // return struct.pack('<I', ret) # 返回小端序
        uint checksum = 1234;
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            checksum ^= BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4));
        }

        checksum *= 1968;
        var result = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, checksum);
        return result;
    }

    public void Dispose()
    {
        Stop();
        // 等待后台任务彻底退出，避免 CancellationTokenSource 泄漏
        try { _workerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        Interlocked.Exchange(ref _cts, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
