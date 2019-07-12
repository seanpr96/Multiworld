﻿using System;
using MultiWorldProtocol.Messaging;
using MultiWorldProtocol.Messaging.Definitions;

namespace MultiWorldProtocol.Messaging.Definitions.Messages
{
    [MWMessageType(MWMessageType.ItemReceiveMessage)]
    public class MWItemReceiveMessage : MWMessage
    {
        public string Item { get; set; }
        public string From { get; set; }

        public MWItemReceiveMessage()
        {
            MessageType = MWMessageType.ItemReceiveMessage;
        }
    }

    public class MWItemReceiveDefinition : MWMessageDefinition<MWItemReceiveMessage>
    {
        public MWItemReceiveDefinition() : base(MWMessageType.ItemReceiveMessage)
        {
            Properties.Add(new MWMessageProperty<string, MWItemReceiveMessage>(nameof(MWItemReceiveMessage.Item)));
            Properties.Add(new MWMessageProperty<string, MWItemReceiveMessage>(nameof(MWItemReceiveMessage.From)));
        }
    }
}