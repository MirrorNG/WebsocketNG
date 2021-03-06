﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mirage.Websocket.Server
{
    using UniTaskChannel = Cysharp.Threading.Tasks.Channel;

    /// <summary>
    /// Connection to a client
    /// </summary>
    public class Connection : IConnection
    {
        private readonly BlockingCollection<MemoryStream> sendQueue = new BlockingCollection<MemoryStream>();
        private readonly Channel<MemoryStream> receiveQueue = UniTaskChannel.CreateSingleConsumerUnbounded<MemoryStream>();

        readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private readonly TcpClient client;
        readonly Stream stream;

        public Connection(TcpClient client, X509Certificate2 certificate)
        {
            this.client = client;
            stream = GetStream(client, certificate);
        }

        private Stream GetStream(TcpClient client, X509Certificate2 certificate)
        {
            if (certificate is null)
                return client.GetStream();

            var sslStream = new SslStream(client.GetStream(), false, ServerCertValidation);
            sslStream.AuthenticateAsServer(certificate);
            return sslStream;
        }

        // Server does not need to validate client or server certificate
        // only the client needs to validate server cert
        private bool ServerCertValidation(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;

        public void Handshake()
        {
            ServerHandshake.Handshake(stream);
        }

        public void SendAndReceive()
        {
            try
            {

                var sendThread = new Thread(() =>
                {
                    SendLoop.Loop(sendQueue, stream, cancellationTokenSource.Token);
                })
                {
                    IsBackground = true
                };
                sendThread.Start();

                while (true)
                {
                    MemoryStream message = Parser.ReadOneMessage(stream);
                    receiveQueue.Writer.TryWrite(message);
                }
            }
            catch (EndOfStreamException)
            {
                receiveQueue.Writer.TryComplete();
            }
            finally
            {
                cancellationTokenSource.Cancel();
                stream?.Close();
                client?.Close();
            }
        }
        
        public void Disconnect()
        {
            stream?.Close();
            client?.Close();
        }

        public EndPoint GetEndPointAddress()
        {
            return client.Client.RemoteEndPoint;
        }

        public async UniTask<int> ReceiveAsync(MemoryStream buffer)
        {
            try
            {
                MemoryStream receiveMsg = await receiveQueue.Reader.ReadAsync(cancellationTokenSource.Token);
                buffer.SetLength(0);
                receiveMsg.WriteTo(buffer);
                return 0;
            }
            catch (OperationCanceledException)
            {
                throw new EndOfStreamException();
            }
            catch (ChannelClosedException)
            {
                throw new EndOfStreamException();
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }
        }

        public UniTask SendAsync(ArraySegment<byte> segment, int channel = 0)
        {
            MemoryStream stream = SendLoop.PackageMessage(segment, false);

            sendQueue.Add(stream);

            return UniTask.CompletedTask;
        }
    }
}