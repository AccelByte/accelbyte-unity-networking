// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Models;

namespace AccelByte.Networking
{
    public class AccelByteLobbySignaling : IAccelByteSignalingBase
    {
        private AccelByte.Core.ApiClient apiClient = null;
        private Lobby currentLobby = null;

        public AccelByteLobbySignaling(AccelByte.Core.ApiClient inApiClient = null)
        {
            if (inApiClient == null)
            {
                currentLobby = AccelBytePlugin.GetLobby();
            }
            else
            {
                apiClient = inApiClient;
                currentLobby = apiClient.GetApi<Lobby, LobbyApi>();
            }
            currentLobby.SignalingP2PNotification += OnSignalingP2PNotification;
        }

        public Action<WebRTCSignalingMessage> OnWebRTCSignalingMessage { get; set; }

        public void Connect()
        {
            if (!IsConnected())
            {
                currentLobby.Connect();
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
            return currentLobby.IsConnected;
        }

        public void SendMessage(string PeerID, string Message)
        {
            currentLobby.SendSignalingMessage(PeerID, Message);
        }
    }
}