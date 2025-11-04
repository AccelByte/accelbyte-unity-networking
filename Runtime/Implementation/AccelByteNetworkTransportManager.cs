// Copyright (c) 2025 AccelByte Inc. All Rights Reserved.
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
using System.Collections;

public class AccelByteNetworkTransportManager : NetworkTransport
{
    private AccelBytePeerIDAlias PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();

    private IAccelByteSignalingBase signaling = null;
    private ApiClient apiClient = null;

    private const int TurnServerAuthLifeTimeSeconds = 60 * 10;

    private AccelByte.Models.Config clientConfig;

    #region Test Utils
    private P2POptionalParameters additionalLogger = null;
    private AccelByteJuice overriddedAccelByteJuice = null;
    #endregion

    public bool IsCompleted(ulong clientId)
    {
        IAccelByteICEBase ice = PeerIdToICEConnectionMap[clientId];
        if (ice is null)
        {
            return false;
        }

        if (ice is AccelByteJuice)
        {
            var abJuice = (AccelByteJuice)ice;
            return abJuice.IsCompleted();
        }
        else
        {
            return true;
        }
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
        //A connection has been establish because the targeted userId is already set
        if (TargetedHostUserID != null) return false;

        TargetedHostUserID = userId;
        return true;
    }

    private void ResetTargetHostUserId() { TargetedHostUserID = null; }
    #endregion

    ulong ServerClientIdPrivate = 0;

    private readonly Dictionary<string, Queue<IncomingPacketFromDataChannel>> bufferedIncomingData = new Dictionary<string, Queue<IncomingPacketFromDataChannel>>();
    private readonly WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

    #region OVERRIDE_VARIABLES    
    
    public override ulong ServerClientId => ServerClientIdPrivate;

    #endregion

    /// <summary>
    /// Override every abstract functions from NetworkTransport
    /// If possible, AVOID call this function directly (either from AccelByte Unity SDK or AccelByte Networking SDK)
    /// Simply because there is a possibility that these functions will be executed when a Netcode function is called
    /// </summary>
    #region OVERRIDE_FUNCTIONS
    
    public override void Initialize(NetworkManager networkManager = null)
    {
        if (apiClient == null)
        {
            Debug.LogException(new Exception("Please call Initialize(ApiClient inApiClient) to set the ApiClient first."));
        }
    }

    public override void DisconnectLocalClient()
    {
        ResetTargetHostUserId();
        var clientIDs = PeerIdToICEConnectionMap?.GetAllClientID();
        if (clientIDs != null)
        {
            foreach (var clientId in clientIDs)
            {
                apiClient.coroutineRunner.Run(() =>
                {
                    InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId,
                        default, Time.realtimeSinceStartup);
                });
                
                PeerIdToICEConnectionMap[clientId].ClosePeerConnection();
                PeerIdToICEConnectionMap?.Remove(clientId);
            }
        }
        PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (!isServer)
        {
            apiClient.coroutineRunner.Run(() =>
            {
                InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
            });
        }

        CleanupRemoteClientConnection(clientId);
    }

    public override ulong GetCurrentRtt(ulong clientId)
    {
        return 0;
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

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        DoSend(clientId, payload, networkDelivery);
    }

    public override void Shutdown()
    {
        AuthInterface?.Clear();
        TriggerCleanupExistingClientConnection();
    }

    public override bool StartServer()
    {
        ResetTargetHostUserId();

        if (apiClient == null || apiClient?.GetLobby().IsConnected == false)
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

        var rtc = CreateNewConnection(TargetedHostUserID, true, additionalLogger?.Logger);

        if (clientConfig.UseTurnManager)
        {
            apiClient.coroutineRunner.Run(() => StartClientUsingTurnManager(rtc));
            return true;
        }
        else
        {
            int port = 0;
            if (int.TryParse(clientConfig.TurnServerPort, out port) ||
                clientConfig.TurnServerHost == string.Empty ||
                clientConfig.TurnServerUsername == string.Empty ||
                clientConfig.TurnServerPassword == string.Empty)
            {
                AccelByteDebug.LogWarning("Can not join a session, missing configuration.");
                return false;
            }

            rtc.RequestConnect(clientConfig.TurnServerHost, port, clientConfig.TurnServerUsername, clientConfig.TurnServerPassword);
            return true;
        }
    }
    #endregion

    // This function can't be avoided since we cannot change the override Initialize() signature.
    /// <summary>
    /// Initialize AccelByteNetworkTransportManager to support P2P multiplayer with Unity NetCode
    /// </summary>
    /// <param name="inApiClient">ApiClient from existing AccelByteSDK</param>
    public void Initialize(ApiClient inApiClient)
    {
        Initialize(inApiClient, null);
    }

    internal void Initialize(ApiClient inApiClient, P2POptionalParameters optionalParameter)
    {
        additionalLogger = optionalParameter;
        if (inApiClient == null)
        {
            Debug.LogException(new Exception("Please provide a valid ApiClient."));
        }
        apiClient = inApiClient;
        AssignSignaling(new AccelByteLobbySignaling(apiClient));
        clientConfig = inApiClient.Config;
        
        if (clientConfig == null)
        {
            Debug.LogException(new Exception("UnitySDK doesn't have a valid Config."));
        }

        AccelByteDebug.Log($"AccelByteNetworkTransportManager Initialized (AuthHandler Enabled: {clientConfig.EnableAuthHandshake}, " +
                           $"PeerMonitorInterval: {clientConfig.PeerMonitorIntervalMs} ms, " +
                           $"PeerMonitorTimeout: {clientConfig.PeerMonitorTimeoutMs} ms, " +
                           $"HostCheckTimeout: {clientConfig.HostCheckTimeoutInSeconds} s)");
    }

    private void AssignSignaling(AccelByteLobbySignaling inSignaling)
    {
        signaling = inSignaling;
        signaling.OnWebRTCSignalingMessage += OnSignalingMessage;
    }

    private void OnSignalingMessage(WebRTCSignalingMessage signalingMessage)
    {
        var message = signalingMessage.ToString();
        AccelByteDebug.Log(message);
        additionalLogger?.Logger?.Log(message);

        string currentPeerID = signalingMessage.PeerID;
        IAccelByteICEBase connection;
        if (PeerIdToICEConnectionMap.Contain(currentPeerID))
        {
            connection = PeerIdToICEConnectionMap[currentPeerID];
        }
        else
        {
            connection = CreateNewConnection(currentPeerID, false, additionalLogger?.Logger);
        }

        connection?.OnSignalingMessage(signalingMessage.Message);
    }

    private void StartClientUsingTurnManager(IAccelByteICEBase rtc)
    {
        apiClient.GetTurnManager().GetTurnServers(
        optionalParam: new GetTurnServerOptionalParameters
        {
            Logger = additionalLogger?.Logger,
            ApiTracker = additionalLogger?.ApiTracker
        }
        , result =>
        {
            if (result.IsError)
            {
                AccelByteDebug.LogWarning($"Failed to get Turn servers [{result.Error.Code}] : {result.Error.Message}");
                return;
            }

            TurnServerList turnServerList = result.Value;
            AccelByteResult<TurnServer, Error> closestTurnManagerTask = turnServerList.GetClosestTurnServer();
            closestTurnManagerTask.OnSuccess(closestTurnServer =>
            {
                apiClient.coroutineRunner.Run(() => OnClientGetClosestTurnServer(closestTurnServer));
            });
            closestTurnManagerTask.OnFailed(error =>
            {
                AccelByteDebug.LogWarning($"AccelByteNetworkManager can't get closest turn server [{error.Code}]:{error.Message}");

                InvokeOnTransportEvent(NetworkEvent.Disconnect, PeerIdToICEConnectionMap.GetAlias(TargetedHostUserID), default, default);
                rtc.ClosePeerConnection();
            });
        });
    }
    
    private void OnClientGetClosestTurnServer(TurnServer closestTurnServer)
    {
        var rtc = this.PeerIdToICEConnectionMap[TargetedHostUserID];
        AccelByteDebug.Log($"Selected TURN server: {closestTurnServer.ip}:{closestTurnServer.port}");

        if (clientConfig.TurnServerSecret == string.Empty)
        {
            AccelByteDebug.Log("TURN using dynamic auth secret");
            RequestCredentialAndConnect(rtc, closestTurnServer);
            return;
        }

        // Authentication life time to server
        long currentTime = closestTurnServer.current_time + TurnServerAuthLifeTimeSeconds;
        string username = currentTime + ":" + clientConfig.TurnServerUsername;

        System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
        byte[] key = encoding.GetBytes(clientConfig.TurnServerSecret);
        byte[] value = encoding.GetBytes(username);
        string password = "";
        using (var hmac = new System.Security.Cryptography.HMACSHA1(key, false))
        {
            byte[] passwordBytes = hmac.ComputeHash(value);
            password = Convert.ToBase64String(passwordBytes);
        }

        rtc.RequestConnect(closestTurnServer.ip, closestTurnServer.port, username, password);
    }

    private IAccelByteICEBase CreateNewConnection(string peerID, bool asClient, IDebugger newLogger = null)
    {
        var ice = CreateAccelByteJuice(signaling, clientConfig, newLogger);
        ice.ForceRelay = false;

        ulong clientID = PeerIdToICEConnectionMap.Add(peerID, ice);
        if (asClient)
        {
            ServerClientIdPrivate = clientID;
        }

        ice.SetPeerID(peerID);

        ice.OnICEDataChannelCompleted = resultPeerID => {
            apiClient.coroutineRunner.Run(() => OnConnected(resultPeerID, clientID));
        };

        ice.OnICEDataChannelClosed = resultPeerID => {
            TriggerCleanupRemoteClientConnection(clientID);
        };

        ice.OnICEDataChannelConnectionError = resultPeerID => {
            TriggerCleanupRemoteClientConnection(clientID);
        };

        if (clientConfig.EnableAuthHandshake)
        {
            ice.OnICEDataChannelCompleted += resultPeerID =>
            {
                ice.OnICEDataIncoming = (resultPeerID, resultPacket) => OnIncomingAuth(resultPeerID, clientID, resultPacket);
            };

            ice.OnICEDataChannelClosed += resultPeerID =>
            {
                apiClient.coroutineRunner.Run(() => OnCloseAuth(resultPeerID));
            };
            ice.OnGatheringDone += resultPeerID =>
            {
                apiClient.coroutineRunner.Run(StartAuthSetup(resultPeerID, clientID, NetworkManager.Singleton.IsServer));
            };
        }
        else
        {
            ice.OnICEDataIncoming = (resultPeerID, resultPacket) => OnIncoming(resultPeerID, clientID, resultPacket);
        }

        return ice;
    }

    private void TriggerCleanupExistingClientConnection()
    {
        foreach (var clientId in PeerIdToICEConnectionMap.GetAllClientID())
        {
            TriggerCleanupRemoteClientConnection(clientId);
        }
    }
    
    private void TriggerCleanupRemoteClientConnection(ulong clientId)
    {
        apiClient.coroutineRunner.Run(() =>
        {
            InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
        });

        CleanupRemoteClientConnection(clientId);
    }

    private void CleanupRemoteClientConnection(ulong clientId)
    {
        if (PeerIdToICEConnectionMap.GetAlias(clientId) == TargetedHostUserID)
        {
            ResetTargetHostUserId();
        }
        PeerIdToICEConnectionMap[clientId]?.ClosePeerConnection();
        PeerIdToICEConnectionMap?.Remove(clientId);
        additionalLogger?.Logger?.Log($"Cleaning up connection from {clientId}");
    }

    private IEnumerator StartAuthSetup(string resultPeerID, ulong clientID, bool inServer)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            yield break;
        }
        yield return waitForEndOfFrame;
        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).SetupAuth(clientID, this, inServer);

        AuthInterface.OnContainSession = (peerID) =>
        {
            return this.Contain(peerID);
        };
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
        AccelByteDebug.Log($"{nameof(AccelByteNetworkTransportManager)} connected to (clientID: {clientID}, peerID: {resultPeerID}, realTimeSinceStartup: {Time.realtimeSinceStartup})");
    }

    private void OnIncoming(string resultPeerID, ulong clientID, byte[] resultPacket)
    {
        bufferedIncomingData[resultPeerID].Enqueue(new IncomingPacketFromDataChannel(resultPacket, clientID));
    }

    private void Update()
    {
        var userIDs = PeerIdToICEConnectionMap?.GetAllUserID();
        if (userIDs != null)
        {
            foreach (var userId in userIDs)
            {
                PeerIdToICEConnectionMap[userId]?.Tick();
            }
        }

        // for secure handshaking.
        AuthInterface?.Tick();
    }

    private void RequestCredentialAndConnect(IAccelByteICEBase rtc, TurnServer selectedTurnServer)
    {
        apiClient.GetTurnManager()
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
    private void OnSetupAuth(string resultPeerID, ulong clientID, bool inServer)
    {
        if ( PeerIdToICEConnectionMap.Contain(resultPeerID) is false )
        {
            return;
        }

        ((AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID]).SetupAuth(clientID, this, inServer);

        AuthInterface.OnContainSession = (peerID) =>
        {
            return this.Contain(peerID);
        };
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
        var ice = (AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID];
        if(IsAuthPacket(resultPacket))
        {
            ice.OnIncomingAuth(resultPacket);
        }
        else
        {
            if(!ice.IsPeerMonitorPacket(resultPacket))
            {
                OnIncoming(resultPeerID, clientID, resultPacket);
            }
        }
    }

    public void OnIncomingBase(string resultPeerID, ulong clientID)
    {
        apiClient.coroutineRunner.Run(StartClientListeningIncomingBase(resultPeerID, clientID));
    }
    public bool Contain(string resultPeerID)
    {
        try
        {
            return PeerIdToICEConnectionMap.Contain(resultPeerID);
        }
        catch (Exception)
        {
            return false;
        }
    }
    private bool IsAuthPacket(byte[] packet)
    {
        var magics = AccelByteAuthHandler.MagicValue;
        if(packet.Length<=magics.Length)
        {
            return false;
        }
        for (int i = 0; i < magics.Length; i++)
        {
            if(packet[i]!=magics[i])
            {
                return false;
            }
        }
        return true;
        
    }
    private IEnumerator StartClientListeningIncomingBase(string resultPeerID, ulong clientID)
    {
        yield return waitForEndOfFrame;
        var ice = (AccelByteJuice)PeerIdToICEConnectionMap[resultPeerID];
        ice.OnICEDataIncoming = (rPeerID, resultPacket) => OnIncoming(rPeerID, clientID, resultPacket);
        ice.StartPeerMonitor();
        AccelByteDebug.LogVerbose("Peer monitor started");
    }
    #endregion AuthHandler

    #region Test Utils
    internal AccelByteJuice CreateAccelByteJuice(IAccelByteSignalingBase inSignaling, Config inConfig, IDebugger logger = null)
    {
        if (overriddedAccelByteJuice != null)
        {
            return overriddedAccelByteJuice;
        }
        else
        {
            var ice = new AccelByteJuice(inSignaling, inConfig, logger);
            return ice;
        }
    }

    internal void SetMockAccelByteJuice(AccelByteJuice abJuice)
    {
        overriddedAccelByteJuice = abJuice;
    }

    internal void SetOptionalParam(P2POptionalParameters optionalParameters)
    {
        additionalLogger = optionalParameters;

        foreach (var clientId in PeerIdToICEConnectionMap.GetAllClientID())
        {
            var ice = PeerIdToICEConnectionMap[clientId];
            if (ice is AccelByteJuice)
            {
                (ice as AccelByteJuice).SetActiveDebugger(additionalLogger?.Logger);
            }
        }
    }

    internal bool DoSend(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        IAccelByteICEBase ice = PeerIdToICEConnectionMap[clientId];
        if (ice is null)
        {
            return false;
        }

        if (ice is AccelByteJuice)
        {
            var abJuice = (AccelByteJuice)ice;
            if (abJuice.IsCompleted() is false)
            {
                return false;
            }
        }

        if (payload.Array == null || payload.Count == 0)
        {
            return false;
        }

        byte[] copy = new byte[payload.Count];
        Array.Copy(payload.Array, payload.Offset, copy, 0, payload.Count);
        var sentPacketLen = ice.Send(copy);

        return sentPacketLen > 0;
    }
    #endregion
}
