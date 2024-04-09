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
using System.Collections;

public class AccelByteNetworkTransportManager : NetworkTransport
{
    private AccelBytePeerIDAlias PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();

    private IAccelByteSignalingBase signaling = null;
    private ApiClient apiClient = null;

    private const int TurnServerAuthLifeTimeSeconds = 60 * 10;

	private AccelByte.Models.Config GetClientConfig()
	{
        var retval = AccelByteSDK.GetClientConfig();
        return retval;
	}

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
        //A connection has been establish because the targeted userId is already set
        if (TargetedHostUserID != null) return false;

        TargetedHostUserID = userId;
        return true;
    }

    private void ResetTargetHostUserId() { TargetedHostUserID = null; }
    #endregion

    ulong ServerClientIdPrivate = 0;

    public override ulong ServerClientId => ServerClientIdPrivate;

    private readonly Dictionary<string, Queue<IncomingPacketFromDataChannel>> bufferedIncomingData = new Dictionary<string, Queue<IncomingPacketFromDataChannel>>();
    private readonly WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

    public override void DisconnectLocalClient()
    {
        ResetTargetHostUserId();
        var userIDs = PeerIdToICEConnectionMap?.GetAllUserID();
        if (userIDs != null)
        {
            foreach (var userId in userIDs)
            {
                PeerIdToICEConnectionMap[userId].ClosePeerConnection();
                apiClient.coroutineRunner.Run(() =>
                {
                    InvokeOnTransportEvent(NetworkEvent.Disconnect, PeerIdToICEConnectionMap.GetAlias(userId),
                        default, Time.realtimeSinceStartup);
                });
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

        var config = GetClientConfig();

        AccelByteDebug.Log($"AccelByteNetworkTransportManager Initialized (AuthHandler Enabled: {config.EnableAuthHandshake}, " +
                           $"PeerMonitorInterval: {config.PeerMonitorIntervalMs} ms, " +
                           $"PeerMonitorTimeout: {config.PeerMonitorTimeoutMs} ms, " +
                           $"HostCheckTimeout: {config.HostCheckTimeoutInSeconds} s)");
    }

    private void AssignSignaling(AccelByteLobbySignaling inSignaling)
    {
        signaling = inSignaling;
        signaling.OnWebRTCSignalingMessage += OnSignalingMessage;
    }

    private void OnSignalingMessage(WebRTCSignalingMessage signalingMessage)
    {
        AccelByteDebug.Log(signalingMessage);

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

        if (payload.Array == null || payload.Count == 0)
        {
            return;
        }

        byte[] copy = new byte[payload.Count];
        Array.Copy(payload.Array, payload.Offset, copy, 0, payload.Count);
        ice.Send(copy);
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

        AccelByte.Models.Config config = GetClientConfig();

        if (config.UseTurnManager)
        {
            apiClient.coroutineRunner.Run(() => StartClientUsingTurnManager(rtc));
            return true;
        }
        else
        {
            int port = 0;
            if (int.TryParse(config.TurnServerPort, out port) ||
                config.TurnServerHost == string.Empty ||
                config.TurnServerUsername == string.Empty ||
                config.TurnServerPassword == string.Empty)
            {
                AccelByteDebug.LogWarning("Can not join a session, missing configuration.");
                return false;
            }

            rtc.RequestConnect(config.TurnServerHost, port, config.TurnServerUsername, config.TurnServerPassword);
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

        AccelByte.Models.Config config = GetClientConfig();

        if (config.TurnServerSecret == string.Empty)
        {
            AccelByteDebug.Log("TURN using dynamic auth secret");
            RequestCredentialAndConnect(rtc, closestTurnServer);
            return;
        }

        // Authentication life time to server
        int currentTime = closestTurnServer.current_time + TurnServerAuthLifeTimeSeconds;
        string username = currentTime + ":" + config.TurnServerUsername;

        System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
        byte[] key = encoding.GetBytes(config.TurnServerSecret);
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

        AccelByte.Models.Config config = GetClientConfig();

        if (config.EnableAuthHandshake)
        {
            ice.OnICEDataChannelConnected += resultPeerID =>
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
}
