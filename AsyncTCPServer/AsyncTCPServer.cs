using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTCPServer
{
    public class AsyncTCPServer
    {
        public class ReceivedEventArgs : EventArgs
        {
            public Message[] Messages { get; private set; }
            public int ClientID { get; private set; }

            public ReceivedEventArgs(int clientID, Message[] messages)
            {
                Messages = messages;
                ClientID = clientID;
            }
        }

        public delegate void Received(object sender, ReceivedEventArgs e);
        public event Received OnReceived;

        private ManualResetEvent allDone = new ManualResetEvent(false);        
        private Dictionary<int, Connection> connections = new Dictionary<int, Connection>();
        private Socket listener;
        private IPEndPoint localEndPoint;
        private InputOutput io;
        private logging log;

        public AsyncTCPServer(int port) {
            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            localEndPoint = new IPEndPoint(ipAddress, port);

            // Create a TCP/IP socket.
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            File.Delete("log.txt");

            log = new logging("");
            io = new InputOutput(log);
        }

        public void Bind() {
            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    log.add_to_log(log_vrste.info, "Waiting for a connection...", "AsyncTCPServer.cs Bind()");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);

                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public void Send(int clientID, Message message) {
            Connection connection = connections[clientID];
            lock (connection.write_lock)
            {
                io.AddMessageToWriteBuffer(connection, message);

                if (connection.tmp_wbuffer.Length == 0)
                {
                    connection.CopyWBufferToTmp();
                    log.add_to_log(log_vrste.info, "BeginSend", "AsyncTCPServer.cs Send()");

                    byte[] tmp_wbuffer = connection.GetTmpWBuffer();

                    log.add_to_log(log_vrste.info, String.Format("Current buffer: {0}", io.ByteArrayToString(tmp_wbuffer)), "AsyncTCPServer.cs Send()");
                    connection.socket.BeginSend(tmp_wbuffer, 0, tmp_wbuffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), connection);
                }
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            log.add_to_log(log_vrste.info, "EndSend", "AsyncTCPServer.cs SendCallback()");
            // Retrieve the socket from the state object.
            Connection connection = (Connection)ar.AsyncState;

            // Complete sending the data to the remote device.
            int bytesSent = connection.socket.EndSend(ar);

            //lock write buffer to make sure no new messages are added while handling end write
            lock (connection.write_lock)
            {
                byte[] wbuffer;
                io.EndWrite(connection, bytesSent);
                wbuffer = connection.GetWBuffer();

                log.add_to_log(log_vrste.info, String.Format("Sent {0} bytes to client.", bytesSent), "AsyncTCPServer.cs SendCallback()");


                if (wbuffer.Length > 0)
                {
                    connection.CopyWBufferToTmp();
                    wbuffer = connection.GetTmpWBuffer();
                    log.add_to_log(log_vrste.info, String.Format("Current buffer: {0}", io.ByteArrayToString(wbuffer)), "AsyncTCPServer.cs SendCallback()");
                    connection.socket.BeginSend(wbuffer, 0, wbuffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), connection);
                }
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            Connection connection = new Connection(handler);
            connections.Add(connection.uid, connection);

            log.add_to_log(log_vrste.info, "AcceptCallback", "AsyncTCPServer.cs ReadCallback()");
            handler.BeginReceive(connection.tmp_rbuffer, 0, Connection.RBUFFER_SIZE, SocketFlags.None, new AsyncCallback(ReadCallback), connection);
        }

        public void ReadCallback(IAsyncResult ar)
        {
            log.add_to_log(log_vrste.info, "EndRead", "AsyncTCPServer.cs ReadCallback()");
            Connection connection = (Connection)ar.AsyncState;
            
            int bytesRead = connection.socket.EndReceive(ar);
            IOStatus status;
            List<Message> messages = io.EndRead(connection, bytesRead, out status);

            if (messages.Count > 0)
            {
                /*if (status == IOStatus.INCOMPLETE)
                {
                    connection.socket.BeginReceive(connection.bytes_read, 0, Connection.RBUFFER_SIZE, SocketFlags.None,
                        new AsyncCallback(ReadCallback), connection);
                }*/
                ReceivedEventArgs args = new ReceivedEventArgs(connection.uid, messages.ToArray());
                OnReceived?.Invoke(this, args);
            }
            //else {
            log.add_to_log(log_vrste.info, "BeginReceive", "AsyncTCPServer.cs ReadCallback()");
            connection.socket.BeginReceive(connection.tmp_rbuffer, 0, Connection.RBUFFER_SIZE, SocketFlags.None,
                    new AsyncCallback(ReadCallback), connection);
            //}
        }        

        static void Main(string[] args)
        {
            AsyncTCPServer server = new AsyncTCPServer(11000);
            server.OnReceived += server.Server_OnReceived;
            server.Bind();            

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        private void Server_OnReceived(object sender, ReceivedEventArgs e)
        {
            log.add_to_log(log_vrste.info, String.Format("Received {0}", e.Messages.Length), "AsyncTCPServer.cs Client_OnReceived()");
            AsyncTCPServer server = (AsyncTCPServer)sender;
            server.Send(e.ClientID, new Message(24, new byte[] { 1, 2, 3, 4 }));
            
        }
    }
}
