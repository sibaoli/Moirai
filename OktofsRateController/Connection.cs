//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading;

namespace OktofsRateControllerNamespace
{
    public interface IConnectionCallback
    {
        /// <summary>
        /// Called by a connection when it has received an intact and complete message in wire-format.
        /// Parses the supplied byte-array to generate a typed message for processing.
        /// On return from this routine the connection is free to overwrite the buffer contents.
        /// /// </summary>
        /// <param name="conn">Connection (think TCP controller<-->agent) on which message arrived.</param>
        /// <param name="buff">Buffer encoding the message.</param>
        /// <param name="offset">Offset to start of message in the supplied buffer.</param>
        /// <param name="length">Length of message encoding in supplied buffer</param>
        void ReceiveMessage(Connection conn, MessageTypes messageType, byte[] buff, int offset, int length);

        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when TCP has closed the connection.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        void ReceiveClose(Connection conn);

        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when it encounters a TCP receive error.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        void ReceiveError(Connection conn, int errNo);

        /// <summary>
        /// Callback invoked by a connection catches a socket exception.
        /// </summary>
        /// <param name="conn">Connection that caught the socket exception.</param>
        /// <param name="sockEx">The socket exception.</param>
        void CatchSocketException(Connection conn, SocketException sockEx);
  
    }


    /// <summary>
    /// Implements the TCP connection from a Rate Controller to a specific network agent.
    /// </summary>
    public class Connection
    {
        public const int AgentPort = Parameters.IOFLOWAGENT_TCP_PORT_NUMBER;
        // Member variables initialised at construction
        private readonly IConnectionCallback callbacks;
        private Socket sock = null;
        private readonly IPEndPoint locEndPoint;
        private readonly int remPort;
        private readonly int locPort;
        internal readonly string hostName;
        private readonly object LockReceive = new object();
        private CallbackContext receiveContext;
        private readonly MessageHeader messageHeader = null;    // Reused, current incoming msg header.
        internal readonly List<OktoQueue> ListQueues;           // Ordered on FlowId by construction.
        internal readonly List<RAP> ListRap;                    // Ordered list inter-server raps.
        internal readonly Dictionary<uint,Flow> DictIoFlows;    // Added for IoFlow-specific handling.
        internal readonly MessageAck MessageAck = new MessageAck();
        private readonly Socket sockListen = null;
        private List<Socket> ListSock = new List<Socket>();
        private readonly object LockListSock = new object();

        // Configuration constants
        const int SOCK_SEND_BUFFER_SIZE = Parameters.SOCK_BUFFER_SIZE;
        const bool SOCK_NO_DELAY = true;
        const int SOCK_RCV_BUFFER_SIZE = Parameters.SOCK_BUFFER_SIZE;

        // Properties
        public int RemPort { get { return remPort; } }
        public string HostName { get { return hostName; } }
        public IPEndPoint RemoteEndPoint { get { return (IPEndPoint)sock.RemoteEndPoint; } }

        // Private variables
        private int currOffset = 0;
        private int bytesExpected = 0;
        private int bytesReceived = 0;
        private int DbgBeginReceivePending = 0;

        // Public state variables manipulated directly by other classes!
        public byte[] sendBuffer;
        public byte[] receiveBuffer;


        /// <summary>
        /// Connection through which rate controller conects to network agent socket app.
        /// </summary>
        /// <param name="callbacks"></param>
        /// <param name="hostName"></param>
        /// <param name="locPort"></param>
        /// <param name="remPort"></param>
        public Connection(IConnectionCallback callbacks, string hostName, int locPort, int remPort)
        {
            this.callbacks = callbacks;
            ListQueues = new List<OktoQueue>();
            ListRap = new List<RAP>();
            DictIoFlows = new Dictionary<uint, Flow>();
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            receiveContext = new CallbackContext(sock);
            this.hostName = hostName;
            this.locPort = locPort;
            this.remPort = remPort;
            sendBuffer = new byte[SOCK_SEND_BUFFER_SIZE];
            locEndPoint = new IPEndPoint(IPAddress.Any, locPort);
            receiveBuffer = new byte[SOCK_RCV_BUFFER_SIZE];
            messageHeader = new MessageHeader();

            // Avoid winsock buffering and Nagle delays on small TCP sends.
            // Ref http://msdn.microsoft.com/en-us/library/ms817942.aspx
#if gregos // ToDo: one or both sockopts sometimes stalling master->agent connection 
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 0);
            sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.Linger, false);
#endif
            sock.Connect(hostName, remPort);
        }

        /// <summary>
        /// Connection on which an IoFlowAgent listens.
        /// </summary>
        /// <param name="callbacks"></param>
        public Connection(IConnectionCallback callbacks)
        {
            this.callbacks = callbacks;
            receiveContext = new CallbackContext(sock);
            this.locPort = AgentPort;
            sendBuffer = new byte[SOCK_SEND_BUFFER_SIZE];
            locEndPoint = new IPEndPoint(IPAddress.Any, locPort);
            receiveBuffer = new byte[SOCK_RCV_BUFFER_SIZE];
            messageHeader = new MessageHeader();
            sockListen = new Socket(locEndPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            ListSock.Add(sockListen);
            sockListen.Bind(locEndPoint);
            const int LISTEN_BACKLOG = 256;
            sockListen.Listen(LISTEN_BACKLOG);
            sockListen.BeginAccept(new AsyncCallback(acceptComplete), new CallbackContext(sockListen));
        }

        private void acceptComplete(IAsyncResult ar)
        {
            try
            {
                CallbackContext callbackContext = (CallbackContext)ar.AsyncState;
                Socket listenSocket = callbackContext.socket;
                Socket newSocket = listenSocket.EndAccept(ar);
                if (newSocket.Connected == false)
                    throw new ApplicationException(String.Format("Socket connect failed on locPort {0}", ((IPEndPoint)listenSocket.LocalEndPoint).Port));
                newSocket.ReceiveBufferSize = SOCK_RCV_BUFFER_SIZE;
                LingerOption ImmediateHardClose = new LingerOption(true, 0);
                newSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, ImmediateHardClose);
                int locPort = ((IPEndPoint)newSocket.LocalEndPoint).Port;
                int remPort = ((IPEndPoint)newSocket.RemoteEndPoint).Port;
                Console.WriteLine("Accept Tcp port {0} accepted new conn from ({0},{1}) on port {2}", newSocket.RemoteEndPoint, remPort, locPort);
                lock (LockListSock)
                {
                    ListSock.Add(newSocket);
                }
                sock = newSocket;
                receiveContext = new CallbackContext(newSocket);
                BeginReceive();
                listenSocket.BeginAccept(acceptComplete, callbackContext);
            }
            catch (ObjectDisposedException ode)
            {
                if (!ode.Message.StartsWith("Cannot access a disposed object"))
                    throw; // Not just shutting down.
            }
        }

        public int Send(byte[] buff, int offset, int length)
        {
            int bytesSent = 0;
            bytesSent = sock.Send(buff, offset, length, SocketFlags.None);
            return bytesSent;
        }

        public int Recv(byte[] buff, int offset, int length)
        {
            int BytesReceived = 0;
            if (buff.Length < length + offset)
                throw new ArgumentException("Connection.Recv err buffer len+offset.");
            while (BytesReceived < length)
            {
                int recvCount = sock.Receive(buff, offset, length - BytesReceived, SocketFlags.None);
                if (recvCount == 0)
                {
                    Close();
                    return 0;
                }
                offset += recvCount;
                BytesReceived += recvCount;
            }
            return BytesReceived;
        }

        private class CallbackContext
        {
            public readonly Socket socket;
            public int offset = 0;
            public CallbackContext(Socket socket) { this.socket = socket; }
        }

        public void BeginReceive()
        {
            lock (LockReceive)
            {
                currOffset = 0;
                bytesExpected = Message.SIZEOF_MESSAGE_HEADER;
                bytesReceived = 0;
                BeginReceive(receiveBuffer, currOffset, bytesExpected, receiveComplete);
            }
        }

        /// <summary>
        /// Continue attempt to receive a complete message in wire format.
        /// Caller must hold LockReceive.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        private void BeginReceive(byte[] buffer, int offset, int length, AsyncCallback funcComplete)
        {
            try
            {
                Debug.Assert(Interlocked.Increment(ref DbgBeginReceivePending) == 1);
                receiveContext.offset = offset;
                sock.BeginReceive(buffer, offset, length, SocketFlags.None, receiveComplete, receiveContext);
            }
            catch (SocketException e)
            {
                const int E_CONNECTION_RESET = 10054;
                if (e.ErrorCode == E_CONNECTION_RESET)
                {
                    Console.WriteLine(e.Message);
                    callbacks.ReceiveError(this, e.ErrorCode);
                    return;
                }
                throw;
            }
        }

        /// <summary>
        /// Handles incoming network traffic on this (Master,Agent) connection.
        /// Initially keeps going until we have a complete message header. When it has a complete
        /// header it determines the message length and keeps going until it has the entire message.
        /// Once it has a complete message it calls passes it to the Rate Controller for parsing.
        /// </summary>
        /// <param name="ar"></param>
        public void receiveComplete(IAsyncResult ar)
        {
            int bytesIn = 0;
            int offset = 0;
            try
            {
                lock (LockReceive)
                {
                    Debug.Assert(Interlocked.Decrement(ref DbgBeginReceivePending) == 0);
                    CallbackContext callbackContext = (CallbackContext)ar.AsyncState;
                    Socket socket = callbackContext.socket;
                    bytesIn = socket.EndReceive(ar);
                    offset = callbackContext.offset;
                    bytesReceived = offset + bytesIn;
                    currOffset += bytesIn;
                    if (bytesReceived == 0)
                    {
                        callbacks.ReceiveClose(this);
                    }
                    else if (bytesReceived < Message.SIZEOF_MESSAGE_HEADER)
                    {
                        // At outset must get enough bytes to extract message size from header.
                        int bytesNeeded = Message.SIZEOF_MESSAGE_HEADER - bytesReceived;
                        BeginReceive(receiveBuffer, currOffset, bytesExpected, receiveComplete);
                    }
                    else if (bytesReceived == Message.SIZEOF_MESSAGE_HEADER)
                    {
                        // We now have a complete message header and can determine message size.
                        messageHeader.InitFromNetBytes(receiveBuffer, 0);
                        int msgLen = messageHeader.GetLength();
                        bytesExpected += msgLen;
                        if (msgLen == 0 || bytesReceived == bytesExpected)
                        {
                            // The message has a zero-length body.
                            callbacks.ReceiveMessage(this, messageHeader.GetMessageType(), receiveBuffer, 0, bytesReceived);
                        }
                        else if (bytesReceived < bytesExpected)
                        {
                            int bytesNeeded = bytesExpected - bytesReceived;
                            BeginReceive(receiveBuffer, bytesReceived, bytesNeeded, receiveComplete);
                        }
                        else
                        {
                            // Should never get here.
                            throw new ApplicationException("Conn: received too many bytes (a).");
                        }
                    }
                    else if (bytesReceived < bytesExpected)
                    {
                        // Received an incomplete message and must obtain the missing (trailing) bytes.
                        int bytesNeeded = bytesExpected - bytesReceived;
                        BeginReceive(receiveBuffer, bytesReceived, bytesNeeded, receiveComplete);
                    }
                    else if (bytesReceived == bytesExpected)
                    {
                        // We have a complete message. Hand it to the Rate Controller for parsing.
                        callbacks.ReceiveMessage(this, messageHeader.GetMessageType(), receiveBuffer, 0, bytesReceived);
                    }
                    else
                    {
                        // Should never get here.
                        throw new ApplicationException("Conn: received too many bytes (b).");
                    }
                }
            }
            catch (SocketException sockEx)
            {
                callbacks.CatchSocketException(this, sockEx);
            }
            catch (ObjectDisposedException odex)
            {
                Console.WriteLine("receiveComplete caught ObjectDisposedException {0}", odex.Message);
            }
        }

        public void Close()
        {
            lock (LockListSock)
            {
                foreach (Socket sock in ListSock)
                {
                    sock.Shutdown(SocketShutdown.Both);
                    sock.Close();
                }
                ListSock.Clear();
                ListSock = null;
            }
        }


    }
}
