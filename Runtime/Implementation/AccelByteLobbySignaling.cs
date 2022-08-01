using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Models;

public class AccelByteLobbySignaling : IAccelByteSignalingBase
{
    private AccelByte.Core.ApiClient apiClient = null;
    private Lobby CurrentLobby = null;

    public AccelByteLobbySignaling(AccelByte.Core.ApiClient inApiClient = null)
    {
        if (inApiClient == null)
        {
            CurrentLobby = AccelBytePlugin.GetLobby();
        }
        else
        {
            apiClient = inApiClient;
            CurrentLobby = apiClient.GetApi<Lobby, LobbyApi>();
        }
        CurrentLobby.SignalingP2PNotification += OnSignalingP2PNotification;
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
