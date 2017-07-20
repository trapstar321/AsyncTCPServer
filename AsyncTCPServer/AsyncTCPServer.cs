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
        private ManualResetEvent allDone = new ManualResetEvent(false);
        private List<Connection> connections = new List<Connection>();
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

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            Connection connection = new Connection(handler);
            connections.Add(connection);
            
            handler.BeginReceive(connection.bytes_read, 0, Connection.RBUFFER_SIZE, SocketFlags.None, new AsyncCallback(ReadCallback), connection);
        }

        public void ReadCallback(IAsyncResult ar)
        {            
            Connection connection = (Connection)ar.AsyncState;
            
            int bytesRead = connection.socket.EndReceive(ar);
            IOStatus status;
            List<Message> messages = io.EndRead(connection, bytesRead, out status);

            if (messages.Count > 0)
            {
                foreach (Message message in messages)
                {
                    log.add_to_log(log_vrste.info, String.Format("Handler {0}: received opcode={1}, data={2}", connection.uid, message.opcode, io.ByteArrayToString(message.data)), "AsyncTCPServer.cs ReadCallback()");
                }

                io.AddMessageToWriteBuffer(connection, new Message(24, new byte[] { 1, 2, 3, 4 }));
                byte[] wbuffer = connection.wbuffer.ToArray();
                connection.socket.BeginSend(wbuffer, 0, wbuffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), connection);

                if (status == IOStatus.INCOMPLETE)
                {
                    connection.socket.BeginReceive(connection.bytes_read, 0, Connection.RBUFFER_SIZE, SocketFlags.None,
                        new AsyncCallback(ReadCallback), connection);
                }
            }
            else {
                connection.socket.BeginReceive(connection.bytes_read, 0, Connection.RBUFFER_SIZE, SocketFlags.None,
                    new AsyncCallback(ReadCallback), connection);
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            // Retrieve the socket from the state object.
            Connection connection = (Connection)ar.AsyncState;

            // Complete sending the data to the remote device.
            int bytesSent = connection.socket.EndSend(ar);
            IOStatus status = io.EndWrite(connection, bytesSent);
            log.add_to_log(log_vrste.info, String.Format("Sent {0} bytes to client.", bytesSent), "AsyncTCPServer.cs SendCallback()");

            if (status == IOStatus.INCOMPLETE)
            {
                byte[] wbuffer = connection.wbuffer.ToArray();
                connection.socket.BeginSend(wbuffer, 0, wbuffer.Length, SocketFlags.None, new AsyncCallback(ReadCallback), connection);
            }
            else
            {
                connection.socket.BeginReceive(connection.bytes_read, 0, Connection.RBUFFER_SIZE, SocketFlags.None,
                    new AsyncCallback(ReadCallback), connection);
            }            
        }

        static void Main(string[] args)
        {
            AsyncTCPServer server = new AsyncTCPServer(11000);
            server.Bind();

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }
    }
}
