﻿using System;
using System.Collections.Generic;
using MultiWorldProtocol.Binary;
using MultiWorldProtocol.Messaging;
using MultiWorldProtocol.Messaging.Definitions.Messages;
using System.Net.Sockets;
using System.Threading;
using Modding;

namespace MultiWorldMod
{
    public class ClientConnection
    {
        private const int PING_INTERVAL = 10000;

        private readonly MWMessagePacker Packer = new MWMessagePacker(new BinaryMWMessageEncoder());
        private TcpClient _client;
        private Timer PingTimer;
        private ConnectionState State;
        private List<MWItemSendMessage> ItemSendQueue = new List<MWItemSendMessage>();
        private Thread ReadThread;

        public delegate void ItemReceiveEvent(string from, string itemName);

        public delegate void MessageReceiveEvent(string from, string message);

        public event ItemReceiveEvent ItemReceived;
        public event MessageReceiveEvent MessageReceived;

        private List<MWMessage> messageEventQueue = new List<MWMessage>();

        private readonly string _host;
        private readonly int _port;

        public ClientConnection(string host, int port, string Username)
        {
            State = new ConnectionState();
            State.UserName = Username;

            _host = host;
            _port = port;

            ModHooks.Instance.HeroUpdateHook += SynchronizeEvents;
        }

        public void Connect(string token = "")
        {
            State.Token = token;

            if (_client != null && _client.Connected)
            {
                Disconnect();
            }

            Reconnect();
        }

        private void Reconnect()
        {
            if (_client != null && _client.Connected)
            {
                return;
            }

            MultiWorldMod.Instance.Log("Attempting to connect to server");

            State.Uid = 0;
            State.LastPing = DateTime.Now;

            _client = new TcpClient
            {
                ReceiveTimeout = 2000,
                SendTimeout = 2000
            };

            _client.Connect(_host, _port);

            if (ReadThread != null && ReadThread.IsAlive)
            {
                ReadThread.Abort();
            }

            PingTimer = new Timer(DoPing, State, 1000, PING_INTERVAL);

            ReadThread = new Thread(ReadWorker);
            ReadThread.Start();

            SendMessage(new MWConnectMessage());
            MultiWorldMod.Instance.Log("Connected to server!");
        }

        private void Disconnect()
        {
            MultiWorldMod.Instance.Log("Disconnecting from server");
            PingTimer?.Dispose();

            try
            {
                byte[] buf = Packer.Pack(new MWDisconnectMessage {SenderUid = State.Uid}).Buffer;
                _client.GetStream().Write(buf, 0, buf.Length);
                _client.Close();
            }
            catch (Exception e)
            {
                MultiWorldMod.Instance.Log("Error disconnection:\n" + e);
            }
            finally
            {
                State.Connected = false;
                State.Joined = false;
                _client = null;
            }
        }

        private void SynchronizeEvents()
        {
            MWMessage message = null;

            lock (messageEventQueue)
            {
                if (messageEventQueue.Count > 0)
                {
                    message = messageEventQueue[0];
                    messageEventQueue.RemoveAt(0);
                }
            }

            if (message == null)
            {
                return;
            }

            switch (message)
            {
                case MWNotifyMessage notify:
                    MessageReceived?.Invoke(notify.From, notify.Message);
                    break;
                case MWItemReceiveMessage item:
                    GiveItem(item.From, item.Item);
                    break;
                default:
                    MultiWorldMod.Instance.Log("Unknown type in message queue: " + message.MessageType);
                    break;
            }
        }

        private void DoPing(object state)
        {
            if (_client == null || !_client.Connected)
            {
                if (State.Connected)
                {
                    State.Connected = false;
                    State.Joined = false;

                    MultiWorldMod.Instance.Log("Disconnected from server");
                }

                Reconnect();
            }

            if (State.Connected)
            {
                if (DateTime.Now - State.LastPing > TimeSpan.FromMilliseconds(PING_INTERVAL * 3.5))
                {
                    MultiWorldMod.Instance.Log("Connection timed out");

                    Disconnect();
                    Reconnect();
                }
                else
                {
                    SendMessage(new MWPingMessage());
                    //If there are items in the queue that the server hasn't confirmed yet
                    if (ItemSendQueue.Count > 0 && State.Joined)
                    {
                        ResendItemQueue();
                    }
                }
            }
        }

        private void SendMessage(MWMessage msg)
        {
            try
            {
                //Always set Uid in here, if uninitialized will be 0 as required.
                //Otherwise less work resuming session etc.
                msg.SenderUid = State.Uid;
                byte[] bytes = Packer.Pack(msg).Buffer;
                NetworkStream stream = _client.GetStream();
                stream.BeginWrite(bytes, 0, bytes.Length, WriteToServer, stream);
            }
            catch (Exception e)
            {
                MultiWorldMod.Instance.Log($"Failed to send message '{msg}' to server:\n{e}");
            }
        }

        private void ReadWorker()
        {
            NetworkStream stream = _client.GetStream();
            while(true)
            {
                var message = new MWPackedMessage(stream);
                ReadFromServer(message);
            }
        }

        private void WriteToServer(IAsyncResult res)
        {
            NetworkStream stream = (NetworkStream)res.AsyncState;
            stream.EndWrite(res);
        }

        private void ReadFromServer(MWPackedMessage packed)
        {
            MWMessage message;
            try
            {
                message = Packer.Unpack(packed);
            }
            catch (Exception e)
            {
                MultiWorldMod.Instance.Log(e);
                return;
            }

            switch (message.MessageType)
            {
                case MWMessageType.SharedCore:
                    break;
                case MWMessageType.ConnectMessage:
                    HandleConnect((MWConnectMessage)message);
                    break;
                case MWMessageType.ReconnectMessage:
                    break;
                case MWMessageType.DisconnectMessage:
                    HandleDisconnectMessage((MWDisconnectMessage)message);
                    break;
                case MWMessageType.JoinMessage:
                    break;
                case MWMessageType.JoinConfirmMessage:
                    HandleJoinConfirm((MWJoinConfirmMessage)message);
                    break;
                case MWMessageType.LeaveMessage:
                    HandleLeaveMessage((MWLeaveMessage)message);
                    break;
                case MWMessageType.ItemConfigurationMessage:
                    HandleItemConfiguration((MWItemConfigurationMessage)message);
                    break;
                case MWMessageType.ItemConfigurationConfirmMessage:
                    break;
                case MWMessageType.ItemReceiveMessage:
                    HandleItemReceive((MWItemReceiveMessage)message);
                    break;
                case MWMessageType.ItemReceiveConfirmMessage:
                    break;
                case MWMessageType.ItemSendMessage:
                    break;
                case MWMessageType.ItemSendConfirmMessage:
                    HandleItemSendConfirm((MWItemSendConfirmMessage)message);
                    break;
                case MWMessageType.NotifyMessage:
                    HandleNotify((MWNotifyMessage)message);
                    break;
                case MWMessageType.PingMessage:
                    State.LastPing = DateTime.Now;
                    break;
                case MWMessageType.InvalidMessage:
                default:
                    throw new InvalidOperationException("Received Invalid Message Type");
            }
        }

        private void ResendItemQueue()
        {
            foreach(MWItemSendMessage message in ItemSendQueue)
            {
                SendMessage(message);
            }
        }

        private void ClearFromSendQueue(uint playerId, string item)
        {
            for(int i=ItemSendQueue.Count-1; i>=0; i--)
            {
                var queueItem = ItemSendQueue[i];
                if (playerId == queueItem.To && item == queueItem.Item)
                    ItemSendQueue.RemoveAt(i);
            }
        }

        private void HandleConnect(MWConnectMessage message)
        {
            State.Uid = message.SenderUid;
            State.Connected = true;
            SendMessage(new MWJoinMessage { DisplayName = State.UserName, Token = State.Token ?? "" });
        }

        private void HandleJoinConfirm(MWJoinConfirmMessage message)
        {
            //Token is empty token if we connected for the first time
            if (string.IsNullOrEmpty(State.Token))
            {
                State.Token = message.Token;
                MultiWorldMod.Instance.Config.Token = message.Token;
                State.Joined = true;
                MultiWorldMod.Instance.Log("Joined");
                State.GameInfo = new GameInformation(message.PlayerId);
            }
            else
            {
                State.Token = message.Token;
                MultiWorldMod.Instance.Config.Token = message.Token;
                State.Joined = true;
                MultiWorldMod.Instance.Log("Rejoined");
                SendMessage(new MWItemConfigurationRequestMessage());
            }
        }

        private void HandleItemConfiguration(MWItemConfigurationMessage message)
        {
            MultiWorldMod.Instance.Log(message.Location + " is " + message.Item + " for player " + message.PlayerId);

            State.GameInfo.SetLocation(message.Location, message.Item, message.PlayerId);
            SendMessage(new MWItemConfigurationConfirmMessage { Location = message.Location, Item = message.Item, PlayerId = message.PlayerId });
        }

        private void HandleLeaveMessage(MWLeaveMessage message)
        {
            State.Joined = false;
        }

        private void HandleDisconnectMessage(MWDisconnectMessage message)
        {
            State.Connected = false;
            State.Joined = false;
        }

        private void HandleNotify(MWNotifyMessage message)
        {
            lock (messageEventQueue)
            {
                messageEventQueue.Add(message);
            }
        }

        private void HandleItemReceive(MWItemReceiveMessage message)
        {
            lock (messageEventQueue)
            {
                messageEventQueue.Add(message);
            }

            //Do whatever we want to do when we get an item here, then confirm
            SendMessage(new MWItemReceiveConfirmMessage { Item = message.Item, From = message.From });
        }

        private void HandleItemSendConfirm(MWItemSendConfirmMessage message)
        {
            ClearFromSendQueue(message.To, message.Item);
        }

        public void Say(string message)
        {
            SendMessage(new MWNotifyMessage { Message = message, To = "All", From = State.UserName });
        }

        public void SendItem(string loc, string item, uint playerId)
        {
            ItemSendQueue.Add(new MWItemSendMessage { Location = loc, Item = item, To = playerId });
        }

        public bool GetItemAtLocation(string loc, out PlayerItem item)
        {
            return State.GameInfo.ItemLocations.TryGetValue(loc, out item);
        }

        public (string, PlayerItem)[] GetItemsInShop(string shopName)
        {
            List<(string, PlayerItem)> items = new List<(string, PlayerItem)>();

            int i = 0;
            while (true)
            {
                string loc = shopName + "_" + (i++);
                if (!GetItemAtLocation(loc, out PlayerItem item))
                {
                    break;
                }

                items.Add((loc, item));
            }

            return items.ToArray();
        }

        public void ObtainItem(string loc)
        {
            if (!GetItemAtLocation(loc, out PlayerItem item))
            {
                MultiWorldMod.Instance.Log("Location " + loc + " not found");
                return;
            }

            if (item.PlayerId != State.GameInfo.PlayerID)
            {
                MultiWorldMod.Instance.Log("Giving item " + item.Item + " to player " + item.PlayerId);
                SendItem(loc, item.Item, item.PlayerId);
                return;
            }

            GiveItem(State.UserName, item.Item);
        }

        public void ObtainShopItem(string shopName, string itemName)
        {
            int i = 0;
            while (GetItemAtLocation(shopName + "_" + (i++), out PlayerItem item))
            {
                if (item.Item == itemName)
                {
                    ObtainItem(shopName + "_" + (i - 1));
                    break;
                }
            }
        }

        private void GiveItem(string from, string item)
        {
            ItemReceived?.Invoke(from, item);
        }

        public uint GetPID()
        {
            return State.GameInfo.PlayerID;
        }

        public string GetUserName()
        {
            return State.UserName;
        }

        public ConnectionStatus GetStatus()
        {
            if (!State.Connected)
            {
                return ConnectionStatus.NotConnected;
            }

            if (!State.Joined)
            {
                return ConnectionStatus.TryingToConnect;
            }

            return ConnectionStatus.Connected;
        }

        public enum ConnectionStatus
        {
            NotConnected,
            TryingToConnect,
            Connected
        }
    }
}
