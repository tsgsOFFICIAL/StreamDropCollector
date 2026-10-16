using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Core.Logging;

namespace UI.Diagnostics
{
    /// <summary>
    /// Local HTTP <c>CONNECT</c> proxy that relays WebView traffic unchanged and counts the bytes in each direction.
    /// </summary>
    /// <remarks>
    /// Counts TLS payload bytes per TCP connection, so TCP/IP headers and UDP (such as DNS) are not included.
    /// Plain HTTP requests are refused.
    /// </remarks>
    public sealed class NetworkMeter : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private long _receivedBytes;
        private long _sentBytes;
        private long _startedTicks = DateTime.UtcNow.Ticks;
        private readonly ConcurrentDictionary<string, HostCounter> _hosts = new(StringComparer.OrdinalIgnoreCase);

        private sealed class HostCounter
        {
            public long Down;
            public long Up;
        }

        /// <summary>
        /// Gets the bytes received so far per remote host name.
        /// </summary>
        public Dictionary<string, long> SnapshotBytesReceivedByHost() =>
            _hosts.ToDictionary(pair => pair.Key, pair => Interlocked.Read(ref pair.Value.Down), StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the loopback port the proxy listens on.</summary>
        public int Port { get; }

        /// <summary>Gets when the meter started counting (the last reset).</summary>
        public DateTime StartedAtUtc => new(Interlocked.Read(ref _startedTicks), DateTimeKind.Utc);

        /// <summary>Gets the total bytes received from the network (downloaded) so far.</summary>
        public long BytesReceived => Interlocked.Read(ref _receivedBytes);

        /// <summary>Gets the total bytes sent to the network (uploaded) so far.</summary>
        public long BytesSent => Interlocked.Read(ref _sentBytes);

        /// <summary>Starts listening on a free loopback port.</summary>
        public NetworkMeter()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync(_cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                    _ = HandleAsync(client, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Warn("NetMeter", $"Accept loop ended: {ex.Message}");
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    NetworkStream clientStream = client.GetStream();

                    byte[] buffer = new byte[16 * 1024];
                    int length = 0;
                    int headerEnd;
                    while ((headerEnd = FindHeaderEnd(buffer, length)) < 0)
                    {
                        if (length == buffer.Length)
                            return;

                        int read = await clientStream.ReadAsync(buffer.AsMemory(length), ct);
                        if (read == 0)
                            return;

                        length += read;
                    }

                    string requestLine = Encoding.ASCII.GetString(buffer, 0, Array.IndexOf(buffer, (byte)'\r'));
                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2 || !parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                    {
                        await clientStream.WriteAsync("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n"u8.ToArray(), ct);
                        return;
                    }

                    int colon = parts[1].LastIndexOf(':');
                    if (colon <= 0 || !int.TryParse(parts[1].AsSpan(colon + 1), out int port))
                        return;

                    string host = parts[1][..colon].Trim('[', ']');

                    HostCounter hostCounter = _hosts.GetOrAdd(host, _ => new HostCounter());

                    using TcpClient upstream = new() { NoDelay = true };
                    await upstream.ConnectAsync(host, port, ct);
                    NetworkStream upstreamStream = upstream.GetStream();

                    await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct);

                    // Anything the client sent after the request header already belongs to the tunnel.
                    int extra = length - (headerEnd + 4);
                    if (extra > 0)
                    {
                        await upstreamStream.WriteAsync(buffer.AsMemory(headerEnd + 4, extra), ct);
                        Interlocked.Add(ref _sentBytes, extra);
                        Interlocked.Add(ref hostCounter.Up, extra);
                    }

                    Task up = PumpAsync(clientStream, upstreamStream, hostCounter, countAsReceived: false, ct);
                    Task down = PumpAsync(upstreamStream, clientStream, hostCounter, countAsReceived: true, ct);
                    await Task.WhenAny(up, down);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                {
                    // A tunnel closing is normal.
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("NetMeter", $"Tunnel failed: {ex.Message}");
                }
            }
        }

        private async Task PumpAsync(NetworkStream from, NetworkStream to, HostCounter hostCounter, bool countAsReceived, CancellationToken ct)
        {
            byte[] buffer = new byte[32 * 1024];
            try
            {
                while (true)
                {
                    int read = await from.ReadAsync(buffer, ct);
                    if (read == 0)
                        return;

                    Interlocked.Add(ref countAsReceived ? ref _receivedBytes : ref _sentBytes, read);
                    Interlocked.Add(ref countAsReceived ? ref hostCounter.Down : ref hostCounter.Up, read);
                    await to.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Sets both byte totals to zero and restarts the clock. Open connections keep being counted.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _receivedBytes, 0);
            Interlocked.Exchange(ref _sentBytes, 0);

            // Zero in place: open tunnels keep a reference to their counter, so clearing the table would orphan them.
            foreach (HostCounter counter in _hosts.Values)
            {
                Interlocked.Exchange(ref counter.Down, 0);
                Interlocked.Exchange(ref counter.Up, 0);
            }

            Interlocked.Exchange(ref _startedTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>Stops the proxy.</summary>
        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
