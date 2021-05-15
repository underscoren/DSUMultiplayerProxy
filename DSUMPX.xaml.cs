using System;
using System.Linq;
using System.Windows;
using System.Net;
using System.Net.Sockets;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DSUMultiplayerProxy
{
    /// <summary>
    /// Interaction logic for DSUMPX.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // TODO: move networking stuff to it's own class

        private UdpClient client1;
        private UdpClient client2;
        private UdpClient client3;
        private UdpClient client4;

        // string to udpclient object
        private Dictionary<string, UdpClient> clientDictionary;

        // client number to controller index
        private Dictionary<int, int> slotMap;

        private UdpClient server;
        private IPEndPoint lastServerRequest;

        private uint clientID;

        public MainWindow()
        {
            InitializeComponent();
            Random rd = new Random();
            clientID = (uint)rd.Next(int.MinValue, int.MaxValue); // generate random id for this client/server

            client1 = new UdpClient();
            client2 = new UdpClient();
            client3 = new UdpClient();
            client4 = new UdpClient();

            clientDictionary = new Dictionary<string, UdpClient>
            {
                { "client1", client1 },
                { "client2", client2 },
                { "client3", client3 },
                { "client4", client4 }
            };

            slotMap = new Dictionary<int, int>();
            lastServerRequest = new IPEndPoint(IPAddress.Any, 0);
        }

        private void ConnectButtonClient_Click(object sender, RoutedEventArgs e)
        {
            // dynamically get elements by client number
            var connectButton = sender as Button;
            string clientNumber = connectButton.Name.Substring(connectButton.Name.Length - 1);
            var ipTextBox = (TextBox)FindName("ipTextBoxClient" + clientNumber);

            // get endpoint from textbox input
            IPAddress address;
            int port;
            IPEndPoint endPoint;

            string[] addressStrings = ipTextBox.Text.Split(':');
            try
            {
                if (addressStrings.Length == 1)
                    port = 26760; // default DSU port
                else
                    port = int.Parse(addressStrings[1]);

                address = IPAddress.Parse(addressStrings[0]);
                endPoint = new IPEndPoint(address, port);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Invalid IP address " + ipTextBox.Text + "\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            // get client and connect it to server
            UdpClient udpClient = clientDictionary["client" + clientNumber];
            udpClient.Connect(endPoint);

            // update UI
            Dispatcher.Invoke(() =>
            {
                connectButton.IsEnabled = false;
                connectButton.Content = "Connecting...";
                ipTextBox.IsEnabled = false;
            });

            // request controller data from server to see if it's online
            DSU.Header header = new DSU.Header
            {
                magic = new char[] { 'D', 'S', 'U', 'C' },
                protocol = 1001,
                packetsize = 12,
                id = clientID,
                messageType = (uint)DSU.MessageType.ControllerInfo
            };

            DSU.InfoRequest controllerInfoRequest = new DSU.InfoRequest
            {
                controllerCount = 1,
                controllerIndices = new byte[] { 0, 0, 0, 0 }
            };

            // form UDP packet from header and request
            byte[] packetData = DSU.FormPacket(header, controllerInfoRequest);

            udpClient.Send(packetData, packetData.Length);
            byte[] responsePacket;

            // wait for response (with timeout)

            var asyncResult = udpClient.BeginReceive(null, null);
            asyncResult.AsyncWaitHandle.WaitOne(5000); // 5s timeout

            if (asyncResult.IsCompleted)
            {
                try
                {
                    IPEndPoint responseEndPoint = null;
                    responsePacket = udpClient.EndReceive(asyncResult, ref responseEndPoint);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error receiving response from Client " + clientNumber + ":\n" + ex.Message,
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    // cleanup
                    Dispatcher.Invoke(() =>
                    {
                        connectButton.IsEnabled = true;
                        connectButton.Content = "Connect";
                        ipTextBox.IsEnabled = true;
                    });
                    return;
                }
            }
            else
            {
                MessageBox.Show("Client " + clientNumber + " did not get a response in 5 seconds. Check the connection / IP address and try again.",
                    "Connection Timeout", MessageBoxButton.OK, MessageBoxImage.Error);

                // cleanup
                Dispatcher.Invoke(() =>
                {
                    connectButton.IsEnabled = true;
                    connectButton.Content = "Connect";
                    ipTextBox.IsEnabled = true;
                });
                return;
            }

            Dispatcher.Invoke(() => { connectButton.Content = "Connected"; }); // display to the user that the client successfully connected

            DSU.Header respHeader = DSU.ByteArrayToStruct<DSU.Header>(responsePacket);
            if (respHeader.messageType != (uint)DSU.MessageType.ControllerInfo)
            {
                MessageBox.Show("Did not receive correct server response. Maybe server is using different protocol?",
                    "Incorrect Response", MessageBoxButton.OK, MessageBoxImage.Error);

                // cleanup
                Dispatcher.Invoke(() =>
                {
                    connectButton.IsEnabled = true;
                    connectButton.Content = "Connect";
                    ipTextBox.IsEnabled = true;
                });
                return;
            }

            DSU.InfoResponse respInfo = DSU.ByteArrayToStruct<DSU.InfoResponse>(responsePacket, DSU.HeaderLength);

            if (respInfo.slot != 0)
                MessageBox.Show("Only controller in slot 0 supported", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);

            // add client to ListBox to indicate it's position
            // TODO: Allow reordering of clients
            var listItem = new ListBoxItem
            {
                Content = "Client " + clientNumber
            };

            listBoxControllerMap.Items.Add(listItem);
            slotMap[int.Parse(clientNumber)] = slotMap.Count;
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int port = int.Parse(textBoxServerPort.Text);
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);
                server = new UdpClient(endpoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error starting server:\n" + ex.Message, "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // disable all controls
            startServerButton.IsEnabled = false;
            textBoxServerPort.IsEnabled = false;
            startServerButton.Content = "Started";

            for (int i = 1; i <= 4; i++)
            {
                ((TextBox)FindName("ipTextBoxClient" + i)).IsEnabled = false;
                ((Button)FindName("connectButtonClient" + i)).IsEnabled = false;
            }

            // setup server request handler
            Task.Run(() =>
            {
                bool warningSent = false;

                // TODO: allow stopping of server
                while (true)
                {
                    byte[] incomingRequestPacket;
                    try
                    {
                        incomingRequestPacket = server.Receive(ref lastServerRequest);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            startServerButton.Content = "ERROR";
                            var buttonMessageTooltip = new ToolTip { Content = ex.Message };
                            ToolTipService.SetShowOnDisabled(buttonMessageTooltip, true);
                            startServerButton.ToolTip = buttonMessageTooltip;
                        });
                        continue;
                    }

                    DSU.Header incomingRequestHeader = DSU.ByteArrayToStruct<DSU.Header>(incomingRequestPacket);
                    incomingRequestHeader.id = clientID;

                    if (incomingRequestHeader.messageType == (uint)DSU.MessageType.ControllerInfo)
                    {
                        DSU.InfoRequest incomingInfoRequest = DSU.ByteArrayToStruct<DSU.InfoRequest>(incomingRequestPacket, DSU.HeaderLength);

                        // query every connected client
                        foreach (var slot in slotMap)
                        {
                            int clientNum = slot.Key;
                            int slotNum = slot.Value;

                            // skip clients which are not requested
                            if (slotNum > incomingInfoRequest.controllerCount) continue;
                            if (!incomingInfoRequest.controllerIndices.Contains((byte)slotNum)) continue;

                            // send query to client
                            UdpClient client = clientDictionary["client" + clientNum];

                            DSU.InfoRequest outgoingInfoRequest = new DSU.InfoRequest
                            {
                                controllerCount = 1,
                                controllerIndices = new byte[] { 0, 0, 0, 0 }
                            };

                            byte[] outgoingRequestPacket = DSU.FormPacket(incomingRequestHeader, outgoingInfoRequest);
                            client.Send(outgoingRequestPacket, outgoingRequestPacket.Length);
                        }
                    }

                    if (incomingRequestHeader.messageType == (uint)DSU.MessageType.ControllerData)
                    {
                        DSU.DataRequest incomingDataRequest = DSU.ByteArrayToStruct<DSU.DataRequest>(incomingRequestPacket, DSU.HeaderLength);

                        if ((incomingDataRequest.flags & 0x2) > 0)
                        {
                            // only show warning once because the server can get spammed with requests
                            if (!warningSent)
                            {
                                MessageBox.Show("MAC Address based client selection is not supported yet", "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                warningSent = true;
                            }
                        }

                        if ((incomingDataRequest.flags & 0x1) > 0)
                        {
                            int clientNum = slotMap.FirstOrDefault(x => x.Value == incomingDataRequest.slot).Key;

                            if (clientNum > 0)
                            {
                                var client = clientDictionary["client" + clientNum];

                                incomingRequestHeader.id = clientID;

                                incomingDataRequest.flags = 1; // slot-based selection only
                                incomingDataRequest.slot = 0;

                                byte[] outgoingRequestPacket = DSU.FormPacket(incomingRequestHeader, incomingDataRequest);
                                client.Send(outgoingRequestPacket, outgoingRequestPacket.Length);
                            }
                        }
                    }
                }
            });

            // setup client response handlers
            foreach (var entry in slotMap)
            {
                int slotNum = entry.Value;
                int clientNum = entry.Key;

                UdpClient client = clientDictionary["client" + clientNum];
                Task.Run(() =>
                {
                    while (true)
                    {
                        IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] incomingResponsePacket;
                        try
                        {
                            incomingResponsePacket = client.Receive(ref clientEndPoint);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var connectButton = ((Button)FindName("connectButtonClient" + clientNum));
                                connectButton.Content = "ERROR";
                                var buttonMessageTooltip = new ToolTip { Content = ex.Message };
                                ToolTipService.SetShowOnDisabled(buttonMessageTooltip, true);
                                connectButton.ToolTip = buttonMessageTooltip;
                            });
                            continue;
                        }

                        DSU.Header incomingResponseHeader = DSU.ByteArrayToStruct<DSU.Header>(incomingResponsePacket);
                        incomingResponseHeader.id = clientID;

                        if (incomingResponseHeader.messageType == (uint)DSU.MessageType.ControllerInfo)
                        {
                            DSU.InfoResponse incomingResponseInfo = DSU.ByteArrayToStruct<DSU.InfoResponse>(incomingResponsePacket, DSU.HeaderLength);
                            incomingResponseInfo.slot = (byte)slotNum;

                            byte[] outgoingResponsePacket = DSU.FormPacket(incomingResponseHeader, incomingResponseInfo);

                            // TODO: handle request-response better instead of just blindly responding to the last client that requested data
                            server.Send(outgoingResponsePacket, outgoingResponsePacket.Length, lastServerRequest);
                        }

                        if (incomingResponseHeader.messageType == (uint)DSU.MessageType.ControllerData)
                        {
                            DSU.InfoResponse incomingResponseInfo = DSU.ByteArrayToStruct<DSU.InfoResponse>(incomingResponsePacket, DSU.HeaderLength);
                            DSU.DataResponse incomingResponseData = DSU.ByteArrayToStruct<DSU.DataResponse>(incomingResponsePacket, DSU.HeaderLength + DSU.InfoResponseLength);

                            incomingResponseInfo.slot = (byte)slotNum;

                            byte[] outgoingResponsePacket = DSU.FormPacket(incomingResponseHeader, incomingResponseInfo, incomingResponseData);
                            server.Send(outgoingResponsePacket, outgoingResponsePacket.Length, lastServerRequest);
                        }
                    }
                });
            }
        }
    }
}
