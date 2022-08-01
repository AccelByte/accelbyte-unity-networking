using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

using UnityEngine;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Models;
using Time = UnityEngine.Time;

public class AccelByteICEData
{
    public string PeerID;
    public byte[] Data;

    public AccelByteICEData(string peerID, byte[] data)
    {
        PeerID = peerID;
        Data = data;
    }
}

public class AccelBytePeerIDAlias
{
    /// <summary>
    /// Collection of a connection to each peer based on UserID as the key.
    /// Messaging to specific player should use specific/assigned ICEBase too.
    /// </summary>
    private readonly Dictionary<string, IAccelByteICEBase> userIdToICEConnectionDictionary = new Dictionary<string, IAccelByteICEBase>();

    /// <summary>
    /// Facilitate the necessity of Unity transport.
    /// The usage of ulong ClientID is limited in the Unity Transport's scope.
    /// ulong ClientID identifier is not propagated to the remote peer, isolated in this player.
    /// Don't use it in the context of ICE connection because we rely completely to AccelByte UserID.
    /// </summary>
    private readonly Dictionary<string, ulong> userIdToClientIdDictionary = new Dictionary<string, ulong>();

    public IAccelByteICEBase this[string key] => userIdToICEConnectionDictionary[key];

    public IAccelByteICEBase this[ulong key]
    {
        get
        {
            var keyUserId = GetAlias(key);
            if (keyUserId.Length == 0)
            {
                return null;
            }
            else
            {
                return userIdToICEConnectionDictionary[keyUserId];
            }
        }
    }

    public bool Contain(string userID)
    {
        return userIdToICEConnectionDictionary.ContainsKey(userID);
    }

    public bool Contain(ulong clientID)
    {
        return userIdToClientIdDictionary.ContainsValue(clientID);
    }

    public string GetAlias(ulong clientId)
    {
        var enumerator = userIdToClientIdDictionary.GetEnumerator();
        do
        {
            if (enumerator.Current.Value == clientId)
            {
                return enumerator.Current.Key;
            }
        }
        while (enumerator.MoveNext());
        return "";
    }

    public ulong GetAlias(string userId)
    {
        ulong value = 0;
        userIdToClientIdDictionary.TryGetValue(userId, out value);
        return value;
    }

    /// <summary>
    /// Add the ICE AccelByte implementation to this collection to the designated key
    /// The ulong ClientID will be generated too and returned.
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="ice"></param>
    /// <returns></returns>
    public ulong Add(string userID, IAccelByteICEBase ice)
    {
        if (userIdToICEConnectionDictionary.ContainsKey(userID))
        {
            userIdToICEConnectionDictionary[userID] = ice;
            return userIdToClientIdDictionary[userID];
        }
        else
        {
            var random = new System.Random();
            byte[] buffer = new byte[8];
            random.NextBytes(buffer);
            ulong clientID = BitConverter.ToUInt64(buffer, 0);
            userIdToClientIdDictionary.Add(userID, clientID);
            userIdToICEConnectionDictionary.Add(userID, ice);
            return clientID;
        }
    }

    public void Remove(ulong clientId)
    {
        var userId = GetAlias(clientId);
        userIdToClientIdDictionary.Remove(userId);
        userIdToICEConnectionDictionary.Remove(userId);
    }

    public List<string> GetAllUserID()
    {
        return new List<string>(userIdToICEConnectionDictionary.Keys);
    }

    public List<ulong> GetAllClientID()
    {
        return new List<ulong>(userIdToClientIdDictionary.Values);
    }
}

class IncomingPacketFromDataChannel
{
    public byte[] data;
    public float receivedTime = 0.0f;
    public ulong clientID;
    public IncomingPacketFromDataChannel(byte[] data_, ulong clientID_)
    {
        data = data_;
        receivedTime = Time.realtimeSinceStartup;
        clientID = clientID_;
    }
};

public class AccelByteNetworkTransportManager : NetworkTransport
{
    AccelBytePeerIDAlias PeerIdToICEConnectionMap = new AccelBytePeerIDAlias();

    IAccelByteSignalingBase Signaling = null;
    AccelByte.Core.ApiClient apiClient = null;

    const int TurnServerAuthLifeTimeSeconds = 60 * 10;

    #region TARGET_HOST_USER_ID
    private string TargetedHostUserID = null;
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

    #region SESSION_BROWSER
    
    /// <summary>
    /// Host should have this information
    /// </summary>
    private SessionBrowserData CreatedSessionBrowserData = null;
    private void ResetCreatedSessionBrowserData() { CreatedSessionBrowserData = null; }

    public string GetHostedSessionID() { return CreatedSessionBrowserData == null ? CreatedSessionBrowserData.session_id : ""; }

    [SerializeField] public SessionBrowserCreateRequest SessionBrowserCreationRequest = null;

    public void SetSessionBrowserCreationRequest(SessionBrowserGameSetting gameSessionSetting, string username, string game_version)
    {
        SessionBrowserCreationRequest = new SessionBrowserCreateRequest();
        SessionBrowserCreationRequest.game_session_setting = gameSessionSetting;
        SessionBrowserCreationRequest.game_version = game_version;
        SessionBrowserCreationRequest.username = username;
        SessionBrowserCreationRequest.session_type = SessionType.p2p;
        SessionBrowserCreationRequest.Namespace = AccelBytePlugin.Config.Namespace;
    }

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

        if (CreatedSessionBrowserData != null)
        {
            string sessionID = CreatedSessionBrowserData.session_id;
            apiClient.GetApi<SessionBrowser,SessionBrowserApi>().RemoveGameSession(sessionID, callback =>
            {
                AccelByteDebug.Log("Host " + (callback.IsError ? "failed to" : "succesfully") + "remove the registered session. SessionID : " + sessionID);
            });
            CreatedSessionBrowserData = null;
        }
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (PeerIdToICEConnectionMap.GetAlias(clientId) == TargetedHostUserID)
        {
            ResetTargetHostUserId();
        }
        PeerIdToICEConnectionMap[clientId]?.ClosePeerConnection();
        PeerIdToICEConnectionMap?.Remove(clientId);
        InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
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

        if (Signaling == null)
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

    private void AssignSignaling(AccelByteLobbySignaling signaling)
    {
        Signaling = signaling;
        Signaling.OnWebRTCSignalingMessage += OnSignalingMessage;
    }

    private void OnSignalingMessage(WebRTCSignalingMessage signalingMessage)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);
        {
            var content = AccelByteICEUtility.SignalingRequestFromString(signalingMessage.Message);
            string printedLog = "Message from: " + signalingMessage.PeerID + "\n" + content.Type + ":" + content.Server_Type + "\nDescription:\n" + content.Description + "\n=============================\n" + Time.timeAsDouble;
            AccelByteDebug.Log(printedLog);
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
            clientId = entry.clientID;
            payload = new ArraySegment<byte>(entry.data);
            receiveTime = entry.receivedTime;
            return NetworkEvent.Data;
        }

        clientId = default;
        payload = default;
        receiveTime = default;
        
        return NetworkEvent.Nothing;
    }

    bool initSend = false;
    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        IAccelByteICEBase ice = PeerIdToICEConnectionMap[clientId];
        if (initSend)
        {
            byte[] copy = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, copy, 0, payload.Count);
            ice.Send(copy);
        }
        else
        {
            ice?.Send(payload.Array);
            initSend = true;
        }
    }

    public override void Shutdown()
    {
        DisconnectLocalClient();
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
        return;
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

        if (SessionBrowserCreationRequest == null)
        {
            //Please call SetSessionBrowserCreationRequest() because the request should not be null
            return false;
        }

        apiClient.GetApi<SessionBrowser, SessionBrowserApi>().CreateGameSession(SessionBrowserCreationRequest, OnCreateGameSession);
        return true;
    }

    private void OnCreateGameSession(Result<SessionBrowserData> result)
    {
        if (result.IsError)
        {
            //Failed to create a game session
            return;
        }

        CreatedSessionBrowserData = result.Value;
    }


    private IAccelByteICEBase CreateNewConnection(string peerID, bool asClient)
    {
        Report.GetFunctionLog(GetType().Name);

        AccelByteUnityICE ice = new AccelByteUnityICE(apiClient, (IAccelByteSignalingBase)this.Signaling);

        ulong clientID = PeerIdToICEConnectionMap.Add(peerID, ice);
        if (asClient)
        {
            ServerClientIdPrivate = clientID;
        }

        ice.SetPeerID(peerID);

        ice.OnICEDataChannelConnected += resultPeerID => {
            AccelByteDebug.Log("Connected to: " + peerID);
            apiClient.coroutineRunner.Run(()=>OnConnected(resultPeerID, clientID));
        };

        ice.OnICEDataChannelClosed += resultPeerID => {
            apiClient.coroutineRunner.Run(() =>
                InvokeOnTransportEvent(NetworkEvent.Disconnect, clientID, default, Time.realtimeSinceStartup)
        );
        };

        ice.OnICEDataChannelConnectionError += resultPeerID => {
            apiClient.coroutineRunner.Run(() =>
                InvokeOnTransportEvent(NetworkEvent.Disconnect, clientID, default, Time.realtimeSinceStartup)
        );
        };

        ice.OnICEDataIncoming += (resultPeerID, resultPacket) =>OnIncoming(resultPeerID, clientID, resultPacket);;

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
                ((AccelByteUnityICE)PeerIdToICEConnectionMap[userId]).Tick();
            }
        }

    }

}
