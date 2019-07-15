﻿namespace MultiWorldProtocol.Messaging
{
    public enum MWMessageType
    {
        InvalidMessage=0,
        SharedCore=1,
        ConnectMessage,
        ReconnectMessage,
        DisconnectMessage,
        JoinMessage,
        JoinConfirmMessage,
        LeaveMessage,
        ItemConfigurationRequestMessage,
        ItemConfigurationMessage,
        ItemConfigurationConfirmMessage,
        ItemReceiveMessage,
        ItemReceiveConfirmMessage,
        ItemSendMessage,
        ItemSendConfirmMessage,
        NotifyMessage,
        PingMessage
    }
}
