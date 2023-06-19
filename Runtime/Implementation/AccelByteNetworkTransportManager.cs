// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Collections.Generic;
using Unity.Netcode;

using UnityEngine;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Models;
using AccelByte.Networking;
using Time = UnityEngine.Time;

public class AccelByteNetworkTransportManager : NetworkTransport
{
    AccelBytePeerIDAlias PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();

    IAccelByteSignalingBase signaling = null;
    ApiClient apiClient = null;

    private const int TurnServerAuthLifeTimeSeconds = 60 * 10;

    public bool IsCompleted(ulong clientId)
    {
        AccelByteJuice ice = (AccelByteJuice)PeerIdToICEConnectionMap[clientId];
        if (ice is null)
        {
            return false;
        }

        return ice.IsCompleted();
    }

    public bool IsConnected(ulong clientId)
    {
        IAccelByteICEBase ice = PeerIdToICEConnectionMap[clientId];
        if (ice is null)
        {
            return false;
        }

        return ice.IsConnected;
    }

    #region TARGET_HOST_USER_ID
    private string TargetedHostUserID = null;

    private bool isServer = false;
    public bool IsServer() {  return isServer; }

    public AccelByteAuthInterface AuthInterface = null;

    /// <summary>
    /// Set the user ID that host the session and intended to establish connection
    /// </summary>
    /// <param name="userId"></param>
    /// <returns>Success to set or not</returns>
    public bool SetTargetHostUserId(string userId)
    {
        if (TargetedHostUserID == null)
        {
            TargetedHostUserID = userId;
            return true;
        }
        else
        {
            //A connection has been establish because the targeted userId is already set
            return false;
        }
    }
    private void ResetTargetHostUserId() { TargetedHostUserID = null; }
    #endregion

    ulong ServerClientIdPrivate = 0;
    public override ulong ServerClientId => ServerClientIdPrivate;

    Dictionary<string, Queue<IncomingPacketFromDataChannel>> bufferedIncomingData = new Dictionary<string, Queue<IncomingPacketFromDataChannel>>();

    public override void DisconnectLocalClient()
    {
        ResetTargetHostUserId();
        var userIDs = PeerIdToICEConnectionMap?.GetAllUserID();
        if (userIDs != null)
        {
            foreach (var userId in userIDs)
            {
                PeerIdToICEConnectionMap[userId].ClosePeerConnection();
            }
        }
        PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (PeerIdToICEConnectionMap.GetAlias(clientId) == TargetedHostUserID)
        {
            ResetTargetHostUserId();
        }
        PeerIdToICEConnectionMap[clientId]?.ClosePeerConnection();
        PeerIdToICEConnectionMap?.Remove(clientId);

        apiClient.coroutineRunner.Run(() =>
        {
            InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
        });
    }

    public override ulong GetCurrentRtt(ulong clientId)
    {
        return 0;
    }

    public override void Initialize(NetworkManager networkManager = null)
    {
        if (apiClient == null)
        {
            Debug.LogException(new Exception("Please call Initialize(ApiClient inApiClient) to set the ApiClient first."));
        }

        if (signaling == null)
        {
            AssignSignaling(new AccelByteLobbySignaling());
        }
    }

    // This function can't be avoided since we cannot change the override Initialize() signature.
    public void Initialize(ApiClient inApiClient)
    {
        if (inApiClient == null)
        {
            Debug.LogException(new Exception("Please provide a valid ApiClient."));
        }
        apiClient = inApiClient;
        AssignSignaling(new AccelByteLobbySignaling(apiClient));
    }

    private void AssignSignaling(AccelByteLobbySignaling inSignaling)
    {
        signaling = inSignaling;
        signaling.OnWebRTCSignalingMessage += OnSignalingMessage;
    }

    private void OnSignalingMessage(WebRTCSignalingMessage signalingMessage)
    {
        Report.GetFunctionLog(GetType().Name);
        {
            AccelByteDebug.Log(signalingMessage);
        }

        string currentPeerID = signalingMessage.PeerID;
        IAccelByteICEBase connection;
        if (PeerIdToICEConnectionMap.Contain(currentPeerID))
        {
            connection = PeerIdToICEConnectionMap[currentPeerID];
        }
        else
        {
            connection = CreateNewConnection(currentPeerID, false);
        }

        connection?.OnSignalingMessage(signalingMessage.Message);
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        foreach (var key in bufferedIncomingData.Keys)
        {
            if (bufferedIncomingData[key].Count == 0)
            {
                continue;
            }
            var entry = bufferedIncomingData[key].Dequeue();
            clientId = entry.ClientID;
            payload = new ArraySegment<byte>(entry.Data);
            receiveTime = entry.GetRealTimeSinceStartUp();
            return NetworkEvent.Data;
        }

        clientId = default;
        payload = default;
        receiveTime = default;

        return NetworkEvent.Nothing;
    }

    private bool initSend = false;

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        IAccelByteICEBase ice = PeerIdToICEConnectionMap[clientId];
        if(ice is null)
        {
            return;
        }

        if (((AccelByteJuice)ice).IsCompleted() is false)
        {
            return;
        }

        if (initSend)
        {
            byte[] copy = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, copy, 0, payload.Count);
            ice.Send(copy);
        }
        else
        {
            ice.Send(payload.Array);
            initSend = true;
        }
    }

    public override void Shutdown()
    {
        DisconnectLocalClient();
        AuthInterface?.Clear();
    }

    public override bool StartClient()
    {
        Report.GetFunctionLog(GetType().Name);

        if (apiClient == null || apiClient?.GetApi<Lobby,LobbyApi>().IsConnected == false)
        {
            return false;
        }

        if (TargetedHostUserID == null)
        {
            //Please Call SetTargetHostUserId first before trying to establish connection
            return false;
        }

        if (AuthInterface is null)
        {
            AuthInterface = new AccelByteAuthInterface();
        }
        else
        {
            AuthInterface.Clear();
        }

        AuthInterface.Initialize(apiClient, false);

        var rtc = CreateNewConnection(TargetedHostUserID, true);

        if (AccelBytePlugin.Config.UseTurnManager)
        {
            apiClient.coroutineRunner.Run(() => StartClientUsingTurnManager(rtc));
            return true;
        }
        else
        {
            int port = 0;
            if (int.TryParse(AccelBytePlugin.Config.TurnServerPort, out port) ||
                AccelBytePlugin.Config.TurnServerHost == string.Empty ||
                AccelBytePlugin.Config.TurnServerUsername == string.Empty ||
                AccelBytePlugin.Config.TurnServerPassword == string.Empty)
            {
                AccelByteDebug.LogWarning("Can not join a session, missing configuration.");
                return false;
            }

            rtc.RequestConnect(AccelBytePlugin.Config.TurnServerHost, port, AccelBytePlugin.Config.TurnServerUsername, AccelBytePlugin.Config.TurnServerPassword);
            return true;
        }
    }

    private void StartClientUsingTurnManager(IAccelByteICEBase rtc)
    {
        apiClient.GetApi<TurnManager, TurnManagerApi>().GetClosestTurnServer(result =>
        {
            apiClient.coroutineRunner.Run(() => OnClientGetClosestTurnServer(result));
        });
    }

    private void OnClientGetClosestTurnServer(Result<TurnServer> result)
    {
        var rtc = this.PeerIdToICEConnectionMap[TargetedHostUserID];
        if (result.IsError || result.Value == null)
        {
            AccelByteDebug.LogWarning("AccelByteNetworkManager can't get closest turn server");

            InvokeOnTransportEvent(NetworkEvent.Disconnect, PeerIdToICEConnectionMap.GetAlias(TargetedHostUserID), default, default);
            rtc.ClosePeerConnection();
            return;
        }

        var closestTurnServer = result.Value;
        AccelByteDebug.Log($"Selected TURN server: {closestTurnServer.ip}:{closestTurnServer.port}");

        if (AccelBytePlugin.Config.TurnServerSecret == string.Empty)
        {
            AccelByteDebug.Log("TURN using dynamic auth secret");
            RequestCredentialAndConnect(rtc, closestTurnServer);
            return;
        }

        // Authentication life time to server
        int currentTime = closestTurnServer.current_time + TurnServerAuthLifeTimeSeconds;
        string username = currentTime + ":" + AccelBytePlugin.Config.TurnServerUsername;

        System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
        byte[] key = encoding.GetBytes(AccelBytePlugin.Config.TurnServerSecret);
        byte[] value = encoding.GetBytes(username);
        string password = "";
        using (var hmac = new System.Security.Cryptography.HMACSHA1(key, false))
        {
            byte[] passwordBytes = hmac.ComputeHash(value);
            //string computedHash = BitConverter.ToString(passwordBytes).Replace("-", "").ToLower();
            //password = Convert.ToBase64String(encoding.GetBytes(computedHash));
            password = Convert.ToBase64String(passwordBytes);
        }

        rtc.RequestConnect(closestTurnServer.ip, closestTurnServer.port, username, password);
    }

    public override bool StartServer()
    {
        ResetTargetHostUserId();

        if (apiClient == null || apiClient?.GetApi<Lobby, LobbyApi>().IsConnected == false)
        {
            return false;
        }

        isServer = true;

        if (AuthInterface is null)
        {
            AuthInterface = new AccelByteAuthInterface();
        }
        else
        {
            AuthInterface.Clear();
        }

        AuthInterface.Initialize(apiClient, isServer);

        return true;
    }

    private IAccelByteICEBase CreateNewConnection(string peerID, bool asClient)
    {
        Report.GetFunctionLog(GetType().Name);

        AccelByteJuice ice = new AccelByteJuice(signaling)
        {
            ForceRelay = false,
        };

        ulong clientID = PeerIdToICEConnectionMap.Add(peerID, ice);
        if (asClient)
        {
            ServerClientIdPrivate = clientID;
        }

        ice.SetPeerID(peerID);

        ice.OnICEDataChannelConnected = resultPeerID => {
            apiClient.coroutineRunner.Run(() => OnConnected(resultPeerID, clientID));
        };

        ice.OnICEDataChannelClosed = resultPeerID => {
            DisconnectRemoteClient(clientID);
        };

        ice.OnICEDataChannelConnectionError = resultPeerID => {
            DisconnectRemoteClient(clientID);
        };

        if (AccelBytePlugin.Config.EnableAuthHandshake)
        {
            ice.OnICEDataChannelConnected += resultPeerID =>
            {
                ice.OnICEDataIncoming = (resultPeerID, resultPacket) => OnIncomingAuth(resultPeerID, clientID, resultPacket);
                apiClient.coroutineRunner.Run(() =>
                    {
                        OnSetupAuth(resultPeerID, clientID, this, !asClient);
                    }
                );
            };

            ice.OnICEDataChannelCompleted = resultPeerID =>
            {
                apiClient.coroutineRunner.Run(() => OnNotifyHandshakeBegin(resultPeerID));
            };

            ice.OnICEDataChannelClosed += resultPeerID =>
            {
                apiClient.coroutineRunner.Run(() => OnCloseAuth(resultPeerID));
            };
        }
        else
        {
            ice.OnICEDataIncoming = (resultPeerID, resultPacket) => OnIncoming(resultPeerID, clientID, resultPacket);
        }

        return ice;
    }

    private void OnConnected(string resultPeerID, ulong clientID)
    {
        if (bufferedIncomingData.ContainsKey(resultPeerID))
        {
            bufferedIncomingData[resultPeerID] = new Queue<IncomingPacketFromDataChannel>();
        }
        else
        {
            bufferedIncomingData.Add(resultPeerID, new Queue<IncomingPacketFromDataChannel>());
        }
        InvokeOnTransportEvent(NetworkEvent.Connect, clientID, default, Time.realtimeSinceStartup);
    }

    private void OnIncoming(string resultPeerID, ulong clientID, byte[] resultPacket)
    {
        bufferedIncomingData[resultPeerID].Enqueue(new IncomingPacketFromDataChannel(resultPacket, clientID));
    }

    private void Start()
    {
    }

    private void Update()
    {
        var userIDs = PeerIdToICEConnectionMap?.GetAllUserID();
        if (userIDs != null)
        {
            foreach (var userId in userIDs)
            {
                ((AccelByteJuice)PeerIdToICEConnectionMap[userId]).Tick();
            }
        }

        // for secure handshaking.
        AuthInterface?.Tick();
    }

    private void RequestCredentialAndConnect(IAccelByteICEBase rtc, TurnServer selectedTurnServer)
    {
        apiClient.GetApi<TurnManager, TurnManagerApi>()
            .GetTurnServerCredential(selectedTurnServer.region, selectedTurnServer.ip, selectedTurnServer.port,
                result =>
            {
                if (result.IsError || result.Value == null)
                {
                    AccelByteDebug.LogError("AccelByteNetworkManager can't get credential for selected turn server");
                    AccelByteDebug.LogError(result.Error.Message);

                    InvokeOnTransportEvent(NetworkEvent.Disconnect, PeerIdToICEConnectionMap.GetAlias(TargetedHostUserID), default, default);
                    rtc.ClosePeerConnection();
                    return;
                }

                var credential = result.Value;

                rtc.RequestConnect(credential.ip, credential.port, credential.username, credential.password);
            });
    }

	#region AuthHandler
    private void OnSetupAuth(string resultPeerID, ulong clientID, AccelByteNetworkTransportManager networkTransportMgr, bool inServer)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            return;
        }

        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).SetupAuth(clientID, networkTransportMgr, inServer);
    }

    private void OnNotifyHandshakeBegin(string resultPeerID)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            return;
        }

        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).NotifyHandshakeBegin();
    }

    private void OnCloseAuth(string resultPeerID)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            return;
        }

        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).OnCloseAuth();
    }

    private void OnIncomingAuth(string resultPeerID, ulong clientID, byte[] resultPacket)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            return;
        }

        var packet = ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).OnIncomingAuth(resultPacket);
        if (packet != null)
        {
            OnIncoming(resultPeerID, clientID, resultPacket);
        }
    }

    public void OnIncomingBase(string resultPeerID, ulong clientID)
    {
        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).OnICEDataIncoming = (resultPeerID, resultPacket) => OnIncoming(resultPeerID, clientID, resultPacket);
    }
    #endregion AuthHandler
}
