﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UtilityBelt.Lib;
using System.ComponentModel;
using System.Windows.Forms;
using UBLoader.Lib.Settings;
using NetworkCommsDotNet;
using System.Collections.Concurrent;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections;
using UtilityBelt.Lib.Networking.Messages;
using NetworkCommsDotNet.DPSBase;
using System.Text.RegularExpressions;
using System.Threading;
using UtilityBelt.Views;

namespace UtilityBelt.Tools {
    [Name("Networking")]
    public class Networking : ToolBase {
        private ConnectionInfo connectionInfo;

        public event EventHandler OnPlayerUpdateMessage;
        public event EventHandler OnCastAttemptMessage;
        public event EventHandler OnCastSuccessMessage;

        private ConcurrentQueue<Action> GameThreadActionQueue = new ConcurrentQueue<Action>();
        private ConcurrentQueue<Action> SendQueue = new ConcurrentQueue<Action>();
        private TCPConnection connection;
        private double lastConnectionAttempt;
        private double connectionRetryTimeout = 0;

        private class ClientInfo {
            private static int __id = 0;

            public int Id { get; set; }
            public string Name { get; set; } = "Unknown";
            public bool IsAuthenticated { get; private set; }

            public Connection Connection { get; private set; }

            public ClientInfo(Connection connection) {
                Id = ++__id;
                Connection = connection;
            }

            public void Authenticate(string name) {
                Name = name;
                IsAuthenticated = true;
            }
        }

        internal struct MessageStatInfo {
            public double Time { get; set; }
            public int Size { get; set; }
        }

        internal class MessageTypeStatInfo {
            public string Type { get; set; }

            internal List<MessageStatInfo> LastMinuteRecvStats = new List<MessageStatInfo>();
            internal List<MessageStatInfo> Last5MinuteRecvStats = new List<MessageStatInfo>();
            internal List<MessageStatInfo> LastHourRecvStats = new List<MessageStatInfo>();

            internal List<MessageStatInfo> LastMinuteSentStats = new List<MessageStatInfo>();
            internal List<MessageStatInfo> Last5MinuteSentStats = new List<MessageStatInfo>();
            internal List<MessageStatInfo> LastHourSentStats = new List<MessageStatInfo>();
        }

        private ConcurrentDictionary<Connection, ClientInfo> ConnectedClients = new ConcurrentDictionary<Connection, ClientInfo>();
        private NetworkStatsView statsView;
        internal Dictionary<string, MessageTypeStatInfo> MessageStats = new Dictionary<string, MessageTypeStatInfo>();

        public BackgroundWorker BackgroundWorker { get; private set; }
        public bool IsServer { get; private set; }
        private double lastStatsCleanup;

        #region Config
        [Summary("Networking server host")]
        public readonly Setting<string> ServerHost = new Setting<string>("127.0.0.1");

        [Summary("Networking server port")]
        public readonly Setting<int> ServerPort = new Setting<int>(42163);

        [Summary("Show network stats (for debugging)")]
        public readonly Setting<bool> ShowNetworkStats = new Setting<bool>(false);

        [Summary("Main UB Window X position for this character (left is 0)")]
        public readonly CharacterState<int> StatsWindowPositionX = new CharacterState<int>(200);

        [Summary("Main UB Window Y position for this character (top is 0)")]
        public readonly CharacterState<int> StatsWindowPositionY = new CharacterState<int>(150);
        #endregion Config

        #region Commands
        #region /ub bc <millisecondDelay> <command>
        [Summary("Broadcasts a command to all open clients, with optional <millisecondDelay> inbetween each")]
        [Usage("/ub bc [millisecondDelay] <command>")]
        [Example("/ub bc 5000 /say hello", "Runs \"/say hello\" on every client, with a 5000ms delay between each")]
        [Example("/ub bc /say hello", "Runs \"/say hello\" on every client, with no delay")]
        [CommandPattern("bc", @"^(?<delay>\d*) ?(?<command>.*)$")]
        public void DoBroadcast(string _, Match args) {
            var command = args.Groups["command"].Value;
            int delay = 0;

            if (!string.IsNullOrEmpty(args.Groups["delay"].Value) && !int.TryParse(args.Groups["delay"].Value, out delay)) {
                Logger.Error($"Unable to broadcast command, invalid delay: {args.Groups["delay"].Value}");
                return;
            }
            if (delay < 0) {
                Logger.Error($"Delay must be greater than zero: {delay}");
                return;
            }

            Logger.WriteToChat($"Broadcasting command to all clients: \"{command}\" with delay inbetween of {delay}ms");

            SendObject("CommandBroadcastMessage", new CommandBroadcastMessage(command, delay));
        }
        #endregion /ub bc <millisecondDelay> <command>
        #endregion Commands

        public Networking(UtilityBeltPlugin ub, string name) : base(ub, name) {

        }

        public override void Init() {
            base.Init();

            StartBackgroundWorker();
            UB.Core.RenderFrame += Core_RenderFrame;
            ShowNetworkStats.Changed += ShowNetworkStats_Changed;
            TryEnableNetworkStatsHud();
        }

        private void ShowNetworkStats_Changed(object sender, SettingChangedEventArgs e) {
            TryEnableNetworkStatsHud();
        }

        public void SendObject(string packetType, object obj) {
            AddMessageStat(packetType, 1, true, false);
            SendQueue.Enqueue(() => connection?.SendObject(packetType, obj));
        }

        private void StartClient() {
            try {
                if (connection != null || UBHelper.Core.Uptime - lastConnectionAttempt < connectionRetryTimeout)
                    return;

                SendReceiveOptions customSendReceiveOptions = new SendReceiveOptions<ProtobufSerializer>();
                connectionInfo = new ConnectionInfo(ServerHost, ServerPort);
                connection = TCPConnection.GetConnection(connectionInfo, customSendReceiveOptions);

                connectionRetryTimeout += 2;
                lastConnectionAttempt = UBHelper.Core.Uptime;

                connection.EstablishConnection();
                connection.AppendShutdownHandler(Client_Shutdown);
                connection.AppendIncomingPacketHandler<PlayerUpdateMessage>("PlayerUpdateMessage", Client_PlayerUpdateMessage);
                connection.AppendIncomingPacketHandler<CastAttemptMessage>("CastAttemptMessage", Client_CastAttemptMessage);
                connection.AppendIncomingPacketHandler<CastSuccessMessage>("CastSuccessMessage", Client_CastSuccessMessage);
                connection.AppendIncomingPacketHandler<CommandBroadcastMessage>("CommandBroadcastMessage", Client_CommandBroadcastMessage);
                connection.SendObject("LoginMessage", new LoginMessage(UB.Core.CharacterFilter.Name));
            }
            catch (ConnectionSetupException ex) {
                if (connection != null) connection.Dispose();
                connection = null;
                // if the client cant connect, we attempt to start our own server
                StartServer();
            }
            catch (Exception ex) {
                RunOnGameThread(() => Logger.LogException(ex));
            }
        }

        private void StartServer() {
            try {
                if (IsServer)
                    return;
                Connection.StartListening(ConnectionType.TCP, new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ServerHost), ServerPort));
                NetworkComms.AppendGlobalConnectionEstablishHandler(ConnectionEstablishedHandler);
                NetworkComms.AppendGlobalConnectionCloseHandler(ConnectionClosedHandler);
                NetworkComms.AppendGlobalIncomingPacketHandler<LoginMessage>("LoginMessage", Server_HandleLoginMessage);
                NetworkComms.AppendGlobalIncomingPacketHandler<PlayerUpdateMessage>("PlayerUpdateMessage", Server_HandlePlayerUpdateMessage);
                NetworkComms.AppendGlobalIncomingPacketHandler<CastAttemptMessage>("CastAttemptMessage", Server_HandleCastAttemptMessage);
                NetworkComms.AppendGlobalIncomingPacketHandler<CastSuccessMessage>("CastSuccessMessage", Server_HandleCastSuccessMessage);
                NetworkComms.AppendGlobalIncomingPacketHandler<CommandBroadcastMessage>("CommandBroadcastMessage", Server_CommandBroadcastMessage);
                IsServer = true;
            }
            catch (CommsSetupShutdownException) {

            }
            catch (Exception ex) {
                Logger.LogException(ex);
            }
        }

        private void StopServer() {
            if (!IsServer)
                return;
            NetworkComms.RemoveGlobalConnectionEstablishHandler(ConnectionEstablishedHandler);
            NetworkComms.RemoveGlobalConnectionCloseHandler(ConnectionClosedHandler);
            NetworkComms.RemoveGlobalIncomingPacketHandler<LoginMessage>("LoginMessage", Server_HandleLoginMessage);
            NetworkComms.RemoveGlobalIncomingPacketHandler<PlayerUpdateMessage>("PlayerUpdateMessage", Server_HandlePlayerUpdateMessage);
            NetworkComms.RemoveGlobalIncomingPacketHandler<CastAttemptMessage>("CastAttemptMessage", Server_HandleCastAttemptMessage);
            NetworkComms.RemoveGlobalIncomingPacketHandler<CastSuccessMessage>("CastSuccessMessage", Server_HandleCastSuccessMessage);
            NetworkComms.RemoveGlobalIncomingPacketHandler<CommandBroadcastMessage>("CommandBroadcastMessage", Server_CommandBroadcastMessage);
            IsServer = false;
        }

        #region Client Events
        private void Client_PlayerUpdateMessage(PacketHeader packetHeader, Connection connection, PlayerUpdateMessage incomingObject) {
            RunOnGameThread(() => {
                if (ShowNetworkStats) AddMessageStat(packetHeader);
                OnPlayerUpdateMessage?.Invoke(incomingObject, EventArgs.Empty);
            });
        }

        private void Client_CastAttemptMessage(PacketHeader packetHeader, Connection connection, CastAttemptMessage incomingObject) {
            RunOnGameThread(() => {
                if (ShowNetworkStats) AddMessageStat(packetHeader);
                OnCastAttemptMessage?.Invoke(incomingObject, EventArgs.Empty);
            });
        }

        private void Client_CastSuccessMessage(PacketHeader packetHeader, Connection connection, CastSuccessMessage incomingObject) {
            RunOnGameThread(() => {
                if (ShowNetworkStats) AddMessageStat(packetHeader);
                OnCastSuccessMessage?.Invoke(incomingObject, EventArgs.Empty);
            });
        }

        private void Client_CommandBroadcastMessage(PacketHeader packetHeader, Connection connection, CommandBroadcastMessage incomingObject) {
            RunOnGameThread(() => {
                if (ShowNetworkStats) AddMessageStat(packetHeader);
                UB.Plugin.AddDelayedCommand(incomingObject.Command, incomingObject.DelayMsBetweenClients * incomingObject.ClientIndex);
            });
        }

        private void Client_Shutdown(Connection connection) {
            if (this.connection != null) {
                this.connection.RemoveIncomingPacketHandler("PlayerUpdateMessage");
                this.connection.RemoveIncomingPacketHandler("CastAttemptMessage");
                this.connection.RemoveIncomingPacketHandler("CastSuccessMessage");
                this.connection.RemoveIncomingPacketHandler("CommandBroadcastMessage");
                this.connection.RemoveShutdownHandler(Client_Shutdown);
                this.connection.Dispose();
                this.connection = null;
            }
        }
        #endregion Client Events

        #region Server Events
        private bool GetClient(Connection connection, out ClientInfo client) {
            if (!ConnectedClients.ContainsKey(connection)) {
                client = null;
                return false;
            }

            client = ConnectedClients[connection];
            return true;
        }

        private void Broadcast(string type, object message, ClientInfo sender = null) {
            foreach (var con in ConnectedClients.Keys) {
                if (!GetClient(con, out ClientInfo conClient) || !conClient.IsAuthenticated || (sender != null && conClient.Id == sender.Id))
                    continue;

                if (ShowNetworkStats) AddMessageStat(type, 0, true, true);
                con.SendObject(type, message);
            }
        }

        private void BroadCastClientMessage(Connection connection, string packetType, object obj) {
            if (!GetClient(connection, out ClientInfo client) || !client.IsAuthenticated)
                return;

            Broadcast(packetType, obj, client);
        }

        private void ConnectionEstablishedHandler(Connection connection) {
            try {
                if (!connection.ConnectionInfo.ServerSide)
                    return;

                var client = new ClientInfo(connection);
                ConnectedClients.TryAdd(connection, client);
            }
            catch (Exception ex) { RunOnGameThread(() => { Logger.LogException(ex); }); }
        }

        private void ConnectionClosedHandler(Connection connection) {
            try {
                if (ConnectedClients.TryRemove(connection, out ClientInfo client) && client != null) {
                    //RunOnGameThread(() => { Logger.WriteToChat($"Client disconnected: {client.Id}({client.Name})"); });
                }
            }
            catch (Exception ex) { RunOnGameThread(() => { Logger.LogException(ex); }); }
        }

        private void Server_HandleLoginMessage(PacketHeader packetHeader, Connection connection, LoginMessage incomingObject) {
            try {
                if (!GetClient(connection, out ClientInfo client))
                    return;
                if (ShowNetworkStats) AddMessageStat(packetHeader, false, true);
                client.Authenticate(incomingObject.Name);
            }
            catch (Exception ex) { RunOnGameThread(() => { Logger.LogException(ex); }); }
        }

        private void Server_HandlePlayerUpdateMessage(PacketHeader packetHeader, Connection connection, PlayerUpdateMessage incomingObject) {
            if (ShowNetworkStats) AddMessageStat(packetHeader, false, true);
            BroadCastClientMessage(connection, "PlayerUpdateMessage", incomingObject);
        }

        private void Server_HandleCastAttemptMessage(PacketHeader packetHeader, Connection connection, CastAttemptMessage incomingObject) {
            if (ShowNetworkStats) AddMessageStat(packetHeader, false, true);
            BroadCastClientMessage(connection, "CastAttemptMessage", incomingObject);
        }

        private void Server_HandleCastSuccessMessage(PacketHeader packetHeader, Connection connection, CastSuccessMessage incomingObject) {
            if (ShowNetworkStats) AddMessageStat(packetHeader, false, true);
            BroadCastClientMessage(connection, "CastSuccessMessage", incomingObject);
        }

        private void Server_CommandBroadcastMessage(PacketHeader packetHeader, Connection connection, CommandBroadcastMessage incomingObject) {
            if (!GetClient(connection, out ClientInfo client) || !client.IsAuthenticated)
                return;

            if (ShowNetworkStats) AddMessageStat(packetHeader, false, true);
            var index = 0;
            foreach (var con in ConnectedClients.Keys) {
                if (!GetClient(con, out ClientInfo conClient) || !conClient.IsAuthenticated)
                    continue;
                incomingObject.ClientIndex = index++;
                if (ShowNetworkStats) AddMessageStat("CommandBroadcastMessage", 0, true, true);
                con.SendObject("CommandBroadcastMessage", incomingObject);
            }
        }
        #endregion Server Events

        #region BackgroundWorker
        private void StartBackgroundWorker() {
            BackgroundWorker = new BackgroundWorker();
            BackgroundWorker.WorkerSupportsCancellation = true;
            BackgroundWorker.WorkerReportsProgress = false;
            BackgroundWorker.DoWork += BackgroundWorker_DoWork;
            BackgroundWorker.RunWorkerAsync();
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e) {
            BackgroundWorker worker = sender as BackgroundWorker;
            try {
                while (worker.CancellationPending != true) {
                    try {
                        if (connection == null) {
                            StartClient();
                            continue;
                        }

                        while (connection != null && SendQueue.TryDequeue(out Action action)) {
                            action.Invoke();
                        }
                    }
                    catch (Exception ex) { RunOnGameThread(() => Logger.LogException(ex)); }
                    Thread.Sleep(15);
                }
            }
            catch (Exception ex) { RunOnGameThread(() => Logger.LogException(ex)); }
        }
        #endregion BackgroundWorker

        #region Network Stats Hud
        private void TryEnableNetworkStatsHud() {
            if (statsView == null && ShowNetworkStats) {
                statsView = new NetworkStatsView(UB);
            }
            else if (statsView != null && !ShowNetworkStats) {
                MessageStats.Clear();
                statsView.Dispose();
                statsView = null;
            }
        }

        private void AddMessageStat(PacketHeader packetHeader, bool wasSent = false, bool isServer = false) {
            AddMessageStat(packetHeader.PacketType, packetHeader.TotalPayloadSize, wasSent, isServer);
        }
        private void AddMessageStat(string packetType, int size, bool wasSent, bool isServer) {
            var key = $"{(isServer ? "S:" : "C:")}{packetType}";
            if (!MessageStats.ContainsKey(key))
                MessageStats.Add(key, new MessageTypeStatInfo() { Type = packetType });
            
            var stat = new MessageStatInfo() {
                Size = size,
                Time = UBHelper.Core.Uptime
            };
            if (wasSent) {
                MessageStats[key].LastMinuteSentStats.Add(stat);
                MessageStats[key].Last5MinuteSentStats.Add(stat);
                MessageStats[key].LastHourSentStats.Add(stat);
            }
            else {
                MessageStats[key].LastMinuteRecvStats.Add(stat);
                MessageStats[key].Last5MinuteRecvStats.Add(stat);
                MessageStats[key].LastHourRecvStats.Add(stat);
            }
        }
        #endregion Network Stats Hud

        public void RunOnGameThread(Action action) {
            GameThreadActionQueue.Enqueue(action);
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (ShowNetworkStats && UBHelper.Core.Uptime - lastStatsCleanup >= 1) {
                    var now = UBHelper.Core.Uptime;
                    var keys = MessageStats.Keys.ToArray();
                    foreach (var k in keys) {
                        var v = MessageStats[k];
                        while (v.LastMinuteRecvStats.Count > 0 && now - v.LastMinuteRecvStats[0].Time > 60)
                            v.LastMinuteRecvStats.RemoveAt(0);
                        while (v.Last5MinuteRecvStats.Count > 0 && now - v.Last5MinuteRecvStats[0].Time > 60 * 5)
                            v.Last5MinuteRecvStats.RemoveAt(0);
                        while (v.LastHourRecvStats.Count > 0 && now - v.LastHourRecvStats[0].Time > 60 * 60)
                            v.LastHourRecvStats.RemoveAt(0);
                        while (v.LastMinuteSentStats.Count > 0 && now - v.LastMinuteSentStats[0].Time > 60)
                            v.LastMinuteSentStats.RemoveAt(0);
                        while (v.Last5MinuteSentStats.Count > 0 && now - v.Last5MinuteSentStats[0].Time > 60 * 5)
                            v.Last5MinuteSentStats.RemoveAt(0);
                        while (v.LastHourSentStats.Count > 0 && now - v.LastHourSentStats[0].Time > 60 * 60)
                            v.LastHourSentStats.RemoveAt(0);
                    }

                    lastStatsCleanup = UBHelper.Core.Uptime;
                    statsView.view.Title = $"UB Networking Stats: IsServer:{IsServer}";
                }
                //if (UBHelper.Core.Uptime - lastSpam > 5) {
                    //if (connection != null)
                    //    connection.SendObject("Message", $"Ping from {UB.Core.CharacterFilter.Name} ({lastSpam})");
                    //lastSpam = UBHelper.Core.Uptime;
                //}

                while (GameThreadActionQueue.TryDequeue(out Action action)) {
                    action.Invoke();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            statsView?.Dispose();
            UB.Core.RenderFrame -= Core_RenderFrame;

            if (BackgroundWorker != null) {
                BackgroundWorker.DoWork -= BackgroundWorker_DoWork;
                BackgroundWorker.CancelAsync();
                BackgroundWorker.Dispose();
            }
            if (IsServer) {
                StopServer();
            }

            NetworkComms.Shutdown();
        }
    }
}
