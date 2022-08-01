using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using Unity.WebRTC;
using Newtonsoft.Json;
using AccelByte.Core;

public class AccelByteUnityICE : IAccelByteICEBase
{
    public IAccelByteSignalingBase Signaling { get; set; }
    public bool IsInitiator { get; set; } = false;
    public bool IsConnected { get; set; }
    public string PeerID { get; set; } //As known as the remote peer ID though
    public Action<string> OnICEDataChannelConnected { get; set; }
    public Action<string> OnICEDataChannelConnectionError { get; set; }
    public Action<string> OnICEDataChannelClosed { get; set; }
    public Action<string /*RemotePeerID*/, byte[] /*Data*/> OnICEDataIncoming { get; set; }

    private ApiClient apiClient;
    private AccelByte.Core.CoroutineRunner coroutineRunner;
    private RTCPeerConnection PeerConnection;
    private RTCDataChannel DataChannel;
    private JsonSerializerSettings IceJsonSerializerSettings = new Newtonsoft.Json.JsonSerializerSettings();

    //state to control the sequence
    bool isRemoteDescriptionSet = false;
    private ConcurrentQueue<RTCIceCandidate> CandidateQueue = new ConcurrentQueue<RTCIceCandidate>();

    public void ClosePeerConnection()
    {
        IsConnected = false;
        DataChannel?.Close();
        DataChannel?.Dispose();
        DataChannel = null;
        PeerConnection?.Close();
        PeerConnection?.Dispose();
        PeerConnection = null;
    }

    public void Tick()
    {
        if (PeerConnection == null || !isRemoteDescriptionSet)
        {
            return;
        }

        if (CandidateQueue.TryDequeue(out RTCIceCandidate iterateCandidate) == false)
        {
            return;
        }
        while (iterateCandidate != null)
        {
            bool addingICECandidate = PeerConnection.AddIceCandidate(iterateCandidate);
            AccelByteDebug.Log("PeerConnection.AddIceCandidate " + (addingICECandidate ? "SUCCESS!!!" : "FAILED!!!") + "\nCandidate:" + iterateCandidate.Address + "\nPeer UserID:" + PeerID);
            iterateCandidate = null;
            CandidateQueue.TryDequeue(out iterateCandidate);
        };
    }

    public bool IsPeerReady()
    {
        return PeerConnection != null;
    }

    public void OnSignalingMessage(string signalingMessage)
    {
        switch (AccelByteICEUtility.GetSignalingMessageTypeFromMessage(signalingMessage))
        {
            case EAccelByteSignalingMessageType.ICE:
                switch (AccelByteICEUtility.GetSignalingServerTypeFromMessage(signalingMessage))
                {
                    case EAccelByteSignalingServerType.OFFER:
                        coroutineRunner.Run(OnSignalingOffer(signalingMessage));
                        return;
                    case EAccelByteSignalingServerType.ANSWER:
                        coroutineRunner.Run(OnSignalingAnswer(signalingMessage));
                        return;
                    default:
                        return;
                }
            case EAccelByteSignalingMessageType.CANDIDATE:
                var signalingRequest = AccelByteICEUtility.SignalingRequestFromString(signalingMessage);
                var iceCandidate = RTCIceCandidateFromString(signalingRequest.Description);
                CandidateQueue.Enqueue(iceCandidate);
                return;
            default:
                return;
        }

    }

    public void SetPeerID(string peerID)
    {
        PeerID = peerID;
    }

    public AccelByteUnityICE(ApiClient inApiClient, IAccelByteSignalingBase inSignaling)
    {
        apiClient = inApiClient;
        coroutineRunner = apiClient.coroutineRunner;
        Signaling = inSignaling;
        IceJsonSerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
    }

    private void CreatePeerConnection(AccelByteSignalingRequest info)
    {
        CreatePeerConnection(info.Host, info.Port, info.Username, info.Password);
    }

    private void CreatePeerConnection(string serverURL, int serverPort, string username, string password)
    {
        Report.GetFunctionLog(GetType().Name);
        AccelByteDebug.Log("serverURL: " + serverURL + "\nserverPort: " + serverPort + "\nusername: " + username + "\npassword: " + password);

        RTCConfiguration config = default;

        var iceServer = new RTCIceServer();
        iceServer.urls = new[] { "turn:" + serverURL + ":" + serverPort };
        iceServer.username = username;
        iceServer.credentialType = RTCIceCredentialType.Password;
        iceServer.credential = password;

        config.iceServers = new[] {
            iceServer,
            new RTCIceServer
            {
                urls = new[] { "stun:" + serverURL + ":" + serverPort},
                username = "",
                credential = "",
                credentialType = RTCIceCredentialType.Password
            }
        };
        PeerConnection = new RTCPeerConnection(ref config);
        ListenerSetup();
    }

    public bool RequestConnect(string serverURL, int serverPort, string username, string password)
    {
        Report.GetFunctionLog(GetType().Name);

        IsInitiator = true;
        if (Signaling == null || !Signaling.IsConnected())
        {
            //error here, check websocket connection to lobby service
            return false;
        }

        CreatePeerConnection(serverURL, serverPort, username, password);

        RTCDataChannelInit conf = new RTCDataChannelInit();
        var dataChannel = PeerConnection.CreateDataChannel("data", conf);
        SetDataChannel(dataChannel);
        dataChannel.OnMessage = OnDataChannelMessage;

        coroutineRunner.Run(CreateOffer());

        return true;
    }

    private IEnumerator CreateOffer()
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);
        var createOfferOperation = PeerConnection.CreateOffer();
        yield return createOfferOperation;

        if (createOfferOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("UnityICE CreateOffer() failure");
            yield return null;
        }

        if (PeerConnection.SignalingState != RTCSignalingState.Stable)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("UnityICE CreateOffer() wrong signaling state");
            yield return null;
        }

        coroutineRunner.Run(OnCreateOfferSuccess(PeerConnection, createOfferOperation.Desc));
    }

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection peerConnection, RTCSessionDescription description)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);

        var localDescriptionOperation = peerConnection.SetLocalDescription(ref description);
        yield return localDescriptionOperation;

        if (localDescriptionOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Unity ICE OnCreateOfferSuccess fail to SetLocalDescription");

            yield return null;
        }

        //send description through signaling OFFER
        var descriptionAsString = JsonConvert.SerializeObject(description, IceJsonSerializerSettings);

        var payload = new AccelByteSignalingRequest();
        payload.Type = EAccelByteSignalingMessageType.ICE;
        payload.Host = PeerConnection.GetConfiguration().iceServers[0].urls[0].Split(':')[1];
        payload.Port = int.Parse(PeerConnection.GetConfiguration().iceServers[0].urls[0].Split(':')[2]);
        payload.Username = PeerConnection.GetConfiguration().iceServers[0].username;
        payload.Password = PeerConnection.GetConfiguration().iceServers[0].credential;
        payload.Server_Type = EAccelByteSignalingServerType.OFFER;
        payload.Description = descriptionAsString;

        var message = AccelByteICEUtility.SignalingRequestToString(payload);
        Signaling.SendMessage(PeerID, message);
    }

    public void Send(byte[] data)
    {
        DataChannel?.Send(data);
    }

    private void ListenerSetup()
    {
        //Done from the inside after PeerConnectionCreated

        if (PeerConnection == null)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Failed to setup listener");
            return;
        }

        PeerConnection.OnIceConnectionChange = OnIceConnectionChange;
        PeerConnection.OnIceCandidate = OnIceCandidate;
        PeerConnection.OnDataChannel = OnDataChannel;
    }

    /// <summary>
    /// OnDataChannel is triggered if the connection has been established.
    /// Remote peer that waiting for an incoming offer, should not create <DataChannel>.
    /// So, it can rely on this and obtain the <DataChnnel> and the hold the value.
    /// </summary>
    /// <param name="channel">Contain the DataChannel & message</param>
    private void OnDataChannel(RTCDataChannel channel)
    {
        SetDataChannel(channel);
        DataChannel.OnMessage = OnDataChannelMessage;
    }

    private void SetDataChannel(RTCDataChannel channel)
    {
        if (DataChannel == null)
        {
            DataChannel = channel;
        }
    }

    private void OnDataChannelMessage(byte[] bytes)
    {
        OnICEDataIncoming(PeerID, bytes);
    }

    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);
        AccelByte.Core.AccelByteDebug.Log(state);

        switch (state)
        {
            case RTCIceConnectionState.Completed:
                break;
            case RTCIceConnectionState.Failed:
                ClosePeerConnection();
                OnICEDataChannelConnectionError?.Invoke("ICE Connection failed");
                break;
            case RTCIceConnectionState.Disconnected:
                ClosePeerConnection();
                OnICEDataChannelClosed?.Invoke(PeerID);
                break;
            case RTCIceConnectionState.New:
                break;
            case RTCIceConnectionState.Checking:
                break;
            case RTCIceConnectionState.Connected:
                if (IsConnected) { return; }
                IsConnected = true;
                OnICEDataChannelConnected?.Invoke(PeerID);
                break;
            case RTCIceConnectionState.Closed:
                break;
            case RTCIceConnectionState.Max:
                break;
        }
    }

    /// <summary>
    /// Expected to be called if the local description has been set
    /// Need to be forwarded to the remote peer using signaling message
    /// The remote peer need to listen the signaling for CANDIDATE message
    /// </summary>
    /// <param name="candidate"></param>
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);

        if (Signaling == null) return;

        var payload = new AccelByteSignalingRequest();
        payload.Type = EAccelByteSignalingMessageType.CANDIDATE;
        payload.Description = RTCIceCandidateToString(candidate);
        payload.Server_Type = EAccelByteSignalingServerType.ON_ICE_CANDIDATE;

        var message = AccelByteICEUtility.SignalingRequestToString(payload);
        Signaling.SendMessage(PeerID, message);
    }

    // On incoming offer: parse offer to create peer connection, set remote description, create answer, and set local description
    private IEnumerator OnSignalingOffer(string signalingMessage)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);

        var incomingSignalingRequest = AccelByteICEUtility.SignalingRequestFromString(signalingMessage);
        CreatePeerConnection(incomingSignalingRequest);

        var remoteDescription = RTCSessionDescriptionFromString(incomingSignalingRequest.Description);
        var setRemoteDescriptionOperation = PeerConnection.SetRemoteDescription(ref remoteDescription);
        yield return setRemoteDescriptionOperation;

        if (setRemoteDescriptionOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Unity ICE OnSignalingOffer fail to SetRemoteDescription");
            yield break;
        }
        isRemoteDescriptionSet = true;

        var createAnswerOperation = PeerConnection.CreateAnswer();
        yield return createAnswerOperation;
        if (createAnswerOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Unity ICE OnSignalingOffer fail to CreateAnswer");
            yield break;
        }

        yield return OnCreateAnswerSuccess(createAnswerOperation.Desc);
    }

    IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        var createAnswerOperationDescription = desc;
        var setLocalDescriptionOperation = PeerConnection.SetLocalDescription(ref createAnswerOperationDescription);
        yield return setLocalDescriptionOperation;
        if (setLocalDescriptionOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Unity ICE OnSignalingOffer fail to set setLocalDescriptionOperation");
            yield break;
        }

        var descriptionAsString = JsonConvert.SerializeObject(createAnswerOperationDescription, IceJsonSerializerSettings);
        var payload = new AccelByteSignalingRequest();
        payload.Type = EAccelByteSignalingMessageType.ICE;
        payload.Server_Type = EAccelByteSignalingServerType.ANSWER;
        payload.Description = descriptionAsString;

        var message = AccelByteICEUtility.SignalingRequestToString(payload);
        Signaling.SendMessage(PeerID, message);
    }

    // Set RemoteDescription after the offer is answered
    private IEnumerator OnSignalingAnswer(string signalingMessage)
    {
        AccelByte.Core.Report.GetFunctionLog(GetType().Name);

        var incomingSignalingRequest = AccelByteICEUtility.SignalingRequestFromString(signalingMessage);

        var remoteDescription = RTCSessionDescriptionFromString(incomingSignalingRequest.Description);
        var setRemoteDescriptionOperation = PeerConnection.SetRemoteDescription(ref remoteDescription);
        yield return setRemoteDescriptionOperation;

        if (setRemoteDescriptionOperation.IsError)
        {
            AccelByte.Core.AccelByteDebug.LogWarning("Unity ICE OnSignalingAnswer fail to SetRemoteDescription");
            yield return null;
        }

        isRemoteDescriptionSet = true;
    }

    #region Utility_Unity_WebRTC

    public string RTCIceCandidateToString(RTCIceCandidate iceCandidate)
    {
        var serializedIceCandidate = JsonConvert.SerializeObject(iceCandidate, IceJsonSerializerSettings);

        return serializedIceCandidate;
    }

    public RTCIceCandidate RTCIceCandidateFromString(string input)
    {
        var Object = JsonConvert.DeserializeObject<RTCIceCandidateInit>(input, IceJsonSerializerSettings);
        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = Object.candidate,
            sdpMid = Object.sdpMid,
            sdpMLineIndex = Object.sdpMLineIndex
        };

        AccelByte.Core.AccelByteDebug.Log(
            "Receiving RTCIceCandidateToString\nCandidate:" + init.candidate
            + "\nSdpMid:" + init.sdpMid +
            "\nSdpMLineIndex:" + init.sdpMLineIndex +
            "\nCOMPLETE\n" + input);
        RTCIceCandidate output = new RTCIceCandidate(init);

        return output;
    }

    public RTCSessionDescription RTCSessionDescriptionFromString(string message)
    {
        RTCSessionDescription output = JsonConvert.DeserializeObject<RTCSessionDescription>(message, IceJsonSerializerSettings);
        return output;
    }

    #endregion Utility_Unity_WebRTC
}
