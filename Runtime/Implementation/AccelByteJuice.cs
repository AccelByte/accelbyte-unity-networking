﻿// Copyright (c) 2023 - 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Text;
using System.Collections.Generic;
using AccelByte.Api;
using AccelByte.Core;
using Newtonsoft.Json;

namespace AccelByte.Networking
{
    public class AccelByteJuice : IAccelByteICEBase
    {
        #region PublicQueue

        // Store all juice tasks to execute synchronously in main thread
        public static readonly Queue<JuiceTask> JuiceTasks = new Queue<JuiceTask>();

        #endregion

        #region Public Members and Properties

        public IAccelByteSignalingBase Signaling { get; set; }
        public bool IsInitiator { get; set; }
        public bool IsConnected { get; set; }
        public string PeerID { get; set; }
        public Action<string /*RemotePeerID*/> OnICEDataChannelConnected { get; set; }
        public Action<string> OnICEDataChannelCompleted { get; set; }
        public Action<string /*RemotePeerID*/> OnICEDataChannelConnectionError { get; set; }
        public Action<string /*RemotePeerID*/> OnICEDataChannelClosed { get; set; }
        public Action<string /*RemotePeerID*/, byte[] /*Data*/> OnICEDataIncoming { get; set; }
        public Action<string /*RemotePeerID*/> OnGatheringDone { get; set; }

        public bool ForceRelay = false;

        private static readonly int MaxDataSizeInBytes = 65000;

        #endregion

        #region Private Members

        private readonly int hostCheckTimeoutS = 30;
        private readonly int peerMonitorIntervalMs = 1000;
        private readonly int peerMonitorTimeoutMs = 10000;

        private AccelByteLibJuiceAgent juiceAgent;
        private readonly JsonSerializerSettings iceJsonSerializerSettings = new JsonSerializerSettings();

        private PeerStatus peerStatus = PeerStatus.NotHosting;

        private static readonly byte[] peerMonitorData = { 32 };
        private DateTime lastTimePeerMonitored = DateTime.UtcNow;
        private bool isMonitoringPeer;
        private int peerConsecutiveErrorCount;
        private bool isPeerAlive;

        private bool isCheckingHost;
        private DateTime initialConnectionTime;

        private string turnServerURL;
        private ushort turnServerPort;
        private string turnServerUsername;
        private string turnServerPassword;

        private const int maxPacketChunkSize = 1024;
        private const string chunkDelimiter = "|Chunk|";
        private List<byte[]> receivedChunkData = new List<byte[]>();
        private bool isReceivingChunkData = false;

        private Models.Config GetClientConfig()
        {
            Models.Config retval = AccelByteSDK.GetClientConfig();
            return retval;
        }
        #endregion

        #region IAccelByteICEBase Implementation

        public AccelByteJuice(IAccelByteSignalingBase inSignaling)
        {
            Signaling = inSignaling;
            iceJsonSerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            Models.Config clientConfig = GetClientConfig(); 

            if (clientConfig.HostCheckTimeoutInSeconds > 0)
            {
                hostCheckTimeoutS = clientConfig.HostCheckTimeoutInSeconds;
            }
            if (clientConfig.PeerMonitorIntervalMs > 0)
            {
                peerMonitorIntervalMs = clientConfig.PeerMonitorIntervalMs;
            }
            if (clientConfig.PeerMonitorTimeoutMs > 0)
            {
                peerMonitorTimeoutMs = clientConfig.PeerMonitorTimeoutMs;
            }
        }

        public void SetPeerID(string peerID)
        {
            PeerID = peerID;
        }

        public void OnSignalingMessage(string message)
        {
            var request = DeserializeSignalingMessage(message);

            switch (request.Type)
            {
                case SignalingMessageType.CheckRequest:
                    SendSignaling(new SignalingMessage
                    {
                        Type = SignalingMessageType.CheckResponse,
                        IsHosting = true
                    });
                    break;

                case SignalingMessageType.CheckResponse:
                    peerStatus = request.IsHosting ? PeerStatus.Hosting : PeerStatus.NotHosting;
                    break;

                case SignalingMessageType.Offer:
                    turnServerURL = request.Host;
                    turnServerPort = (ushort)request.Port;
                    turnServerUsername = request.Username;
                    turnServerPassword = request.Password;

                    IsInitiator = false;

                    if (!CreateLibJuiceAgent())
                    {
                        AccelByteDebug.LogError($"{GetAgentRoleStr()}: Failed to create LibJuice agent");
                        OnICEDataChannelConnectionError(PeerID);
                        break;
                    }

                    juiceAgent.SetRemoteDescription(request.Description);
                    juiceAgent.GatherCandidates();

                    SendSignaling(new SignalingMessage
                    {
                        Type = SignalingMessageType.Answer,
                        Description = juiceAgent.GetLocalDescription(true)
                    });
                    break;

                case SignalingMessageType.Answer:
                    juiceAgent?.SetRemoteDescription(request.Description);
                    juiceAgent?.GatherCandidates();
                    break;

                case SignalingMessageType.Candidate:
                    if (ForceRelay && !request.Description.Contains("typ relay"))
                    {
                        break;
                    }
                    juiceAgent?.AddRemoteCandidate(request.Description);
                    break;

                case SignalingMessageType.GatheringDone:
                    juiceAgent?.SetRemoteGatheringDone();
                    AccelByteDebug.Log($"{GetAgentRoleStr()}: remote gathering done");
                    OnGatheringDone?.Invoke(PeerID);
                    break;

                case SignalingMessageType.Error:
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool RequestConnect(string serverURL, int serverPort, string username, string password)
        {
            IsInitiator = true;

            if (Signaling == null)
            {
                AccelByteDebug.LogError($"{GetAgentRoleStr()}: Can't initiating connection signaling is null");
                return false;
            }

            if (!Signaling.IsConnected())
            {
                AccelByteDebug.LogError($"{GetAgentRoleStr()}: Can't initiating connection signaling is not connected");
                return false;
            }

            turnServerURL = serverURL;
            turnServerPort = (ushort)serverPort;
            turnServerUsername = username;
            turnServerPassword = password;

            var msg = new SignalingMessage
            {
                Type = SignalingMessageType.CheckRequest
            };

            peerStatus = PeerStatus.WaitingReply;
            SendSignaling(msg);

            StartHostCheckWatcher();

            return true;
        }

        public int Send(byte[] data)
        {
            if (juiceAgent == null)
            {
                AccelByteDebug.LogError($"{GetAgentRoleStr()}: juice agent is null, can't send message");
                return -1;
            }

            if (data.Length > MaxDataSizeInBytes)
            {
                AccelByteDebug.LogWarning($"{GetAgentRoleStr()}: can't send data larger than {MaxDataSizeInBytes} bytes");
                return -1;
            }

            byte[] delimiter = Encoding.ASCII.GetBytes(chunkDelimiter);
            int actualMaxPacketChunkSize = maxPacketChunkSize + delimiter.Length;
            if (data.Length > actualMaxPacketChunkSize)
            {
                double packetAmount = Math.Ceiling((double)data.Length / (double)maxPacketChunkSize);
                int sentPackets = 0;
                for (int i = 0; i < packetAmount; i++)
                {
                    byte[] chunk;
                    var sourceIndex = maxPacketChunkSize * i;
                    const int destinationIndex = 0;
                    bool isLastPacket = sourceIndex + maxPacketChunkSize > data.Length;

                    if (!isLastPacket)
                    {
                        chunk = new byte[actualMaxPacketChunkSize];
                        Array.Copy(data, sourceIndex, chunk, destinationIndex, actualMaxPacketChunkSize);
                        for (int x = maxPacketChunkSize; x < actualMaxPacketChunkSize; x++)
                        {
                            chunk[x] = delimiter[x - maxPacketChunkSize];
                        }
                    }
                    else
                    {
                        int remainingSize = data.Length - sourceIndex;
                        chunk = new byte[remainingSize];
                        Array.Copy(data, sourceIndex, chunk, destinationIndex, remainingSize);
                    }

                    if (!juiceAgent.SendData(chunk))
                    {
                        OnICEDataChannelConnectionError(PeerID);
                        return -1;
                    }
                    sentPackets++;
                }

                AccelByteDebug.LogVerbose($"Sent {sentPackets} fragmented packets from {data.Length} sized data");
                return data.Length;
            }

            if (!juiceAgent.SendData(data))
            {
                OnICEDataChannelConnectionError(PeerID);
                return -1;
            }

            // AccelByteDebug.LogVerbose($"{GetAgentRoleStr()} juice agent sent data ({data.Length} bytes): {string.Join(" ", data)}");
            return data.Length;
        }

        public void ClosePeerConnection()
        {
            IsConnected = false;
            StopPeerMonitor();

            if (juiceAgent == null)
            {
                AccelByteDebug.Log($"{GetAgentRoleStr()}: juice agent is null, skip disposing");
                return;
            }

            juiceAgent.Dispose();
            juiceAgent = null;
            AccelByteDebug.Log($"{GetAgentRoleStr()}: connection closed and juice agent disposed");
        }

        public bool IsPeerReady()
        {
            return peerStatus == PeerStatus.Hosting &&
                   juiceAgent.GetState() == JuiceState.Completed;
        }

        #endregion

        #region Private Methods

        private void SendSignaling(SignalingMessage message)
        {
            Signaling.SendMessage(PeerID, SerializeSignalingMessage(message));
        }

        private bool CreateLibJuiceAgent()
        {
            if (juiceAgent != null)
            {
                AccelByteDebug.Log($"{GetAgentRoleStr()}: juice agent is not null, not creating");
                return false;
            }

            juiceAgent =
                new AccelByteLibJuiceAgent(this, turnServerURL, turnServerUsername, turnServerPassword, turnServerPort);

            AccelByteDebug.Log($"{GetAgentRoleStr()}: juice agent created and initialized");
            return true;
        }

        private void StartHostCheckWatcher()
        {
            initialConnectionTime = DateTime.UtcNow;
            isCheckingHost = true;
        }

        private void StopHostCheckWatcher()
        {
            isCheckingHost = false;
        }

        private void HostCheckWatcher()
        {
            if ((DateTime.UtcNow - initialConnectionTime).TotalSeconds > hostCheckTimeoutS)
            {
                StopHostCheckWatcher();
                AccelByteDebug.LogError($"{GetAgentRoleStr()}: timeout while waiting for host check reply, no reply after {hostCheckTimeoutS} seconds");
                OnICEDataChannelConnectionError(PeerID);
                return;
            }

            switch (peerStatus)
            {
                case PeerStatus.NotHosting:
                    StopHostCheckWatcher();
                    AccelByteDebug.LogError($"{GetAgentRoleStr()}: peer is not hosting");
                    OnICEDataChannelConnectionError(PeerID);
                    break;
                case PeerStatus.WaitingReply:
                    break;
                case PeerStatus.Hosting:
                    StopHostCheckWatcher();
                    if (!CreateLibJuiceAgent())
                    {
                        AccelByteDebug.LogError($"{GetAgentRoleStr()}: failed to create juice agent");
                        OnICEDataChannelConnectionError(PeerID);
                        break;
                    }

                    var msg = new SignalingMessage
                    {
                        Type = SignalingMessageType.Offer,
                        Host = turnServerURL,
                        Username = turnServerUsername,
                        Password = turnServerPassword,
                        Port = turnServerPort,
                        Description = juiceAgent.GetLocalDescription(true)
                    };
                    SendSignaling(msg);

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal void StartPeerMonitor()
        {
            isMonitoringPeer = true;
        }

        private void StopPeerMonitor()
        {
            isMonitoringPeer = false;
        }

        private void MonitorPeer()
        {
            if (!isMonitoringPeer)
            {
                return;
            }

            var elapsedTime = (DateTime.UtcNow - lastTimePeerMonitored).TotalMilliseconds;
            if (elapsedTime < peerMonitorIntervalMs) return;

            lastTimePeerMonitored = DateTime.UtcNow;

            juiceAgent.SendData(peerMonitorData);

            if (isPeerAlive)
            {
                peerConsecutiveErrorCount = 0;
            }
            else
            {
                peerConsecutiveErrorCount++;
            }

            int maxPeerConsecutiveError = peerMonitorTimeoutMs / peerMonitorIntervalMs;
            if (peerConsecutiveErrorCount > maxPeerConsecutiveError)
            {
                AccelByteDebug.LogWarning($"{GetAgentRoleStr()}: peer is not responding after {peerConsecutiveErrorCount * peerMonitorIntervalMs} ms");
                OnICEDataChannelClosed(PeerID);
            }

            isPeerAlive = false;
        }

        private string GetAgentRoleStr()
        {
            return IsInitiator ? "client" : "host";
        }

        private static SignalingMessage DeserializeSignalingMessage(string message)
        {
            byte[] decodedMessage = Convert.FromBase64String(message);
            string decodedMessageString = Encoding.UTF8.GetString(decodedMessage);

            var result = JsonConvert.DeserializeObject<SignalingMessage>(decodedMessageString);
            return result;
        }

        private static string SerializeSignalingMessage(SignalingMessage message)
        {
            var serialized = JsonConvert.SerializeObject(message);
            var encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(serialized));

            return encodedJson;
        }

        #endregion

        #region Callback Handler

        public void OnJuiceStateChanged(JuiceState state)
        {
            switch (state)
            {
                case JuiceState.Connected:
                    IsConnected = true;
                    OnICEDataChannelConnected?.Invoke(PeerID);
                    var info =
                        $"{GetAgentRoleStr()}: selected remote candidates {juiceAgent.GetSelectedRemoteCandidates()}";
                    AccelByteDebug.Log(info);
                    break;
                case JuiceState.Completed:
                    if (GetClientConfig().EnableAuthHandshake is false)
                    {
                        StartPeerMonitor();
                    }
                    OnICEDataChannelCompleted?.Invoke(PeerID);
                    break;
                case JuiceState.Disconnected:
                case JuiceState.Failed:
                    OnICEDataChannelConnectionError(PeerID);
                    break;
                case JuiceState.Gathering:
                    break;
                case JuiceState.Connecting:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }

            AccelByteDebug.Log($"{GetAgentRoleStr()}: juice agent state changed to {state}");
        }

        public void OnJuiceCandidateFound(string sdp, bool isSuccess)
        {
            if (IsConnected)
            {
                return;
            }

            var msg = new SignalingMessage
            {
                Type = SignalingMessageType.Candidate,
                Description = sdp
            };
            SendSignaling(msg);
            AccelByteDebug.LogVerbose($"{GetAgentRoleStr()}: juice agent found candidate {isSuccess} {sdp}");
        }

        public void OnJuiceGatheringDone()
        {
            var msg = new SignalingMessage()
            {
                Type = SignalingMessageType.GatheringDone
            };
            SendSignaling(msg);

            AccelByteDebug.LogVerbose($"{GetAgentRoleStr()}: juice agent gathering done, sent signaling to peer");
        }

        public void OnJuiceDataReceived(byte[] data, int size)
        {
            isPeerAlive = true;

            if (data == null || size <= 0)
            {
                return;
            }

            if (isMonitoringPeer)
            {
                if (size == 1 && data[0] == peerMonitorData[0])
                {
                    return;
                }
            }

            var dataString = Encoding.ASCII.GetString(data);
            if (dataString.EndsWith(chunkDelimiter))
            {
                isReceivingChunkData = true;
                byte[] processedChunk = new byte[maxPacketChunkSize];
                const int arrayStartIndex = 0;

                Array.Copy(data, arrayStartIndex, processedChunk, arrayStartIndex, maxPacketChunkSize);
                receivedChunkData.Add(processedChunk);

                return;
            }

            if (isReceivingChunkData)
            {
                isReceivingChunkData = false;
                receivedChunkData.Add(data);
                List<byte> mergedData = new List<byte>();
                foreach (var chunk in receivedChunkData)
                {
                    mergedData.AddRange(chunk);
                }
                receivedChunkData.Clear();

                OnICEDataIncoming?.Invoke(PeerID, mergedData.ToArray());
                return;
            }

            OnICEDataIncoming?.Invoke(PeerID, data);
        }

        #endregion

        #region authHandler

        // for secure handshaking.
        private AccelByteAuthHandler authHandler;

        public void SetupAuth(ulong clientId, AccelByteNetworkTransportManager networkTransportMgr, bool inServer)
        {
            if (GetClientConfig().EnableAuthHandshake)
            {
                if (authHandler is null)
                {
                    authHandler = new AccelByteAuthHandler();
                }
                else
                {
                    authHandler.Clear();
                }

                authHandler.OnPeerClose = () =>
                {
                    networkTransportMgr.DisconnectRemoteClient(clientId);
                };

                authHandler.OnIncomingBase = () =>
                {
                    networkTransportMgr.OnIncomingBase(PeerID, clientId);
                };

                if (authHandler.Setup(this, networkTransportMgr.AuthInterface, inServer) is false)
                {
                    authHandler.Clear();
                    authHandler = null;
                }
            }
        }

        public void OnCloseAuth()
        {
            authHandler?.Clear();
        }

        public byte[] OnIncomingAuth(byte[] packet)
        {
            if (authHandler != null)
            {
                return authHandler.Incoming(packet);
            }
            return packet;
        }

        public bool IsCompleted()
        {
            if (authHandler != null)
            {
                return authHandler.IsCompleted();
            }
            return IsConnected;
        }

        public void Tick()
        {
            if (authHandler != null)
            {
                authHandler.Tick();
            }

            while (JuiceTasks.Count != 0)
            {
                var task = JuiceTasks.Dequeue();
                task?.Execute();
            }

            if (isCheckingHost)
            {
                HostCheckWatcher();
            }

            MonitorPeer();
        }

        #endregion authHandler

        internal bool IsPeerMonitorPacket(byte[] packet)
        {
            return packet.Length == peerMonitorData.Length &&
                packet[0] == peerMonitorData[0];
        }
    }
}