using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Models;

public class AccelByteLobbySignaling : IAccelByteSignalingBase
{
    private AccelByte.Api.Lobby CurrentLobby = null;

    public AccelByteLobbySignaling(AccelByte.Api.Lobby lobby = null)
    {
        if (lobby == null)
        {
            CurrentLobby = AccelBytePlugin.GetLobby();
        }
        else
        {
            CurrentLobby = lobby;
        }
    }

    public Action<WebRTCSignalingMessage> OnWebRTCSignalingMessage { get; set; }

    public void Connect()
    {
        if (!IsConnected())
        {
            CurrentLobby.Connect();
        }
    }

    public void Init()
    {
        CurrentLobby.SignalingP2PNotification += OnSignalingP2PNotification;
    }

    private void OnSignalingP2PNotification(Result<SignalingP2P> result)
    {
        var output = new WebRTCSignalingMessage();
        output.PeerID = result.Value.destinationId;
        output.Message = result.Value.message;
        OnWebRTCSignalingMessage.Invoke(output);
    }

    public bool IsConnected()
    {
        return CurrentLobby.IsConnected;
    }

    public void SendMessage(string PeerID, string Message)
    {
        CurrentLobby.SendSignalingMessage(PeerID, Message);
    }
}
