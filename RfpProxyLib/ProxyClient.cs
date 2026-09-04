using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RfpProxyLib.Messages;

namespace RfpProxyLib
{
    public abstract class ProxyClient : IDisposable
    {
        private readonly Socket _socket;
        private readonly string _socketPath;
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        protected ProxyClient(string socket)
        {
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            _socketPath = socket;
        }

        /// <summary>
        /// Adds a subscription using the given masked filters, which listens to messages.
        /// Listening to messages means getting a copy of each message, but not intercepting it.
        /// No action has to be performed in these handlers, messages will always be forwarded.
        /// These handlers can still inject additional messages using <see cref="WriteAsync" />.
        /// </summary>
        /// <param name="mac">The MAC filter.</param>
        /// <param name="macMask">The mask to apply the MAC filter with.</param>
        /// <param name="filter">The message filter.</param>
        /// <param name="filterMask">The mask to apply the message filter with.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken" /> that can be used to end the subscription.</param>
        public Task AddListenAsync(string mac, string macMask, string filter, string filterMask, CancellationToken cancellationToken)
        {
            var subscription = new Subscribe
            {
                Type = SubscriptionType.Listen
            };
            return AddSubscriptionAsync(subscription, mac, macMask, filter, filterMask, cancellationToken);
        }

        /// <summary>
        /// Adds a subscription using the given masked filters, which handles messages.
        /// Handling messages means intercepting each message and deciding if and how it is forwarded.
        /// Each message must be manually forwarded using <see cref="WriteAsync" />.
        /// </summary>
        /// <param name="priority">The priority of the subscription. Subscriptions are processed in <b>ascending</b> priority.</param>
        /// <param name="mac">The MAC filter.</param>
        /// <param name="macMask">The mask to apply the MAC filter with.</param>
        /// <param name="filter">The message filter.</param>
        /// <param name="filterMask">The mask to apply the message filter with.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken" /> that can be used to end the subscription.</param>
        public Task AddHandlerAsync(byte priority, string mac, string macMask, string filter, string filterMask, CancellationToken cancellationToken)
        {
            var subscription = new Subscribe
            {
                Type = SubscriptionType.Handle,
                Priority = priority
            };
            return AddSubscriptionAsync(subscription, mac, macMask, filter, filterMask, cancellationToken);
        }

        public async Task FinishHandshakeAsync(CancellationToken cancellationToken)
        {
            await InitAsync(cancellationToken).ConfigureAwait(false);
            if (_finished)
                return;
            var eos = new Subscribe
            {
                Type = SubscriptionType.End
            };
            await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_finished)
                    return;
                _finished = true;
                await using var stream = new NetworkStream(_socket, false);
                await using var writer = new StreamWriter(stream);
                using var reader = new StreamReader(stream);
                
                var msg = JsonConvert.SerializeObject(eos);
                await writer.WriteLineAsync(msg).ConfigureAwait(false);
                LogWritten(msg);
                cancellationToken.ThrowIfCancellationRequested();
                await writer.FlushAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                msg = await reader.ReadLineAsync().ConfigureAwait(false);
                LogRead(msg);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                _readLock.Release();
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await FinishHandshakeAsync(cancellationToken).ConfigureAwait(false);
            await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var length = new byte[4];
                while (!cancellationToken.IsCancellationRequested)
                {
                    var success = await FillBufferAsync(_socket, length, cancellationToken).ConfigureAwait(false);
                    if (!success) return;

                    var msgLength = BinaryPrimitives.ReadUInt32BigEndian(length);
                    var msg = new byte[msgLength];

                    success = await FillBufferAsync(_socket, msg, cancellationToken).ConfigureAwait(false);
                    if (!success) return;

                    await OnMessageAsync(msg, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _readLock.Release();
            }
        }

        public virtual async Task WriteAsync(MessageDirection direction, uint messageId, RfpIdentifier rfp, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            var header = new byte[4 + 1 + 4 + RfpIdentifier.Length];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint) (1+4+RfpIdentifier.Length + data.Length));
            header[4] = (byte) direction;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5), messageId);
            rfp.CopyTo(header.AsSpan(4 + 1 + 4));
            await _socket.SendAsync(header, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            await _socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }

        public void Stop()
        {
            _socket.Close();
        }

        private Task OnMessageAsync(byte[] message, CancellationToken cancellationToken)
        {
            var direction = (MessageDirection) message[0];
            var messageId = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(1));
            var rfp = new RfpIdentifier(message.AsMemory(5, 6));
            return OnMessageAsync(direction, messageId, rfp, message.AsMemory(5)[RfpIdentifier.Length..], cancellationToken);
        }

        protected abstract Task OnMessageAsync(MessageDirection direction, uint messageId, RfpIdentifier rfp, Memory<byte> data, CancellationToken cancellationToken);

        private static async Task<bool> FillBufferAsync(Socket socket, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            while (buffer.Length > 0)
            {
                var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) return false;
                buffer = buffer[bytesRead..];
            }
            return true;
        }

        private bool _initialized;
        private bool _finished;

        private async Task InitAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
                return;
            await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                    return;
                await _socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _initialized = true;
                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using var stream = new NetworkStream(_socket, false);
                    using var reader = new StreamReader(stream);
                    
                    var init = await reader.ReadLineAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    LogRead(init);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            finally
            {
                _readLock.Release();
            }
        }

        private async Task AddSubscriptionAsync(Subscribe subscription, string mac, string macMask, string filter,
            string filterMask, CancellationToken cancellationToken)
        {
            subscription.Rfp = new SubscriptionFilter
            {
                Filter = mac.Replace(" ", string.Empty),
                Mask = macMask.Replace(" ", string.Empty)
            };
            subscription.Message = new SubscriptionFilter
            {
                Filter = filter.Replace(" ", string.Empty),
                Mask = filterMask.Replace(" ", string.Empty)
            };
            await InitAsync(cancellationToken).ConfigureAwait(false);
            var msg = JsonConvert.SerializeObject(subscription);
            await using var stream = new NetworkStream(_socket, false);
            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync(msg).ConfigureAwait(false);
            LogWritten(msg);
            cancellationToken.ThrowIfCancellationRequested();
            await writer.FlushAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public event EventHandler<LogEventArgs> Log;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            _socket.Dispose();
            _readLock.Dispose();
            _writeLock.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void LogRead(string message)
        {
            Log?.Invoke(this, new LogEventArgs(LogDirection.Read, message));
        }

        private void LogWritten(string message)
        {
            Log?.Invoke(this, new LogEventArgs(LogDirection.Written, message));
        }
    }
}