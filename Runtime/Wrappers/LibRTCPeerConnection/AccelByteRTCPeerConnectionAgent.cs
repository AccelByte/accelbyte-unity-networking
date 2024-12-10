// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AccelByte.Networking.Interface;
using AccelByte.Networking.Models.Enum;

namespace AccelByte.Networking
{
    public enum RTCPeerLogLevel
    {
        Error = 0,
        Assert = 1,
        Warning = 2,
        Log = 3,
        Verbose = 4,
        Exception = 5
    }
    
    public class AccelByteRTCPeerConnectionAgent : IAgentWrapper
    {
        private static RTCPeerLogLevel currentLogLevel = RTCPeerLogLevel.Error;
        
        public AccelByteRTCPeerConnectionAgent(AccelByteJuice juice, AccelByte.Models.Config clientConfig, string ipAddress, string username, string password, ushort port)
        {
            identifier = NextKey;
            NextKey = identifier + 1;

            currentLogLevel = ConvertToPeerLogLevel(clientConfig.DebugLogFilter);
            InitRTCPeerConnection(identifier, currentLogLevel.ToString(), ipAddress, username, password, port);
            Initialize();
            
            rtcPeerDictionary.Add(identifier, juice);
        }

#region Disposal

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        private void Dispose(bool isDisposing)
        {
            if (disposed)
            {
                return;
            }

            if (isDisposing)
            {
                // clean up managed resource if there is any in the future
            }

            lock (rtcPeerAgentLock)
            {
                // clean up un-managed resource from jslib
                rtcPeerDictionary.Remove(identifier);
                RTCRemovePeer(identifier);
            }

            disposed = true;
        }

        ~AccelByteRTCPeerConnectionAgent()
        {
            Dispose(false);
        }
        
#endregion

#region Public and Interface Methods
        
        public void GetLocalDescription(Action<string> callback, bool excludeCandidates = false)
        {
            lock (rtcPeerAgentLock)
            {
                getLocalDescriptionTask = new RTCPeerGetLocalDescriptionTask(callback);
                RTCGetLocalDescriptionCallback(identifier, RTCPeerGetLocalDescriptionHandler);
            }
        }
        
        public void SetRemoteDescription(string sdp, Action<bool> callback)
        {
            lock (rtcPeerAgentLock)
            {
                setRemoteDescriptionTask = new RTCSetRemoteDescriptionTask(callback);
                RTCSetRemoteDescriptionCallback(identifier, sdp, RTCSetRemoteDescriptionHandler);
            }
        }
        
        public bool AddRemoteCandidate(string sdp)
        {
            lock (rtcPeerAgentLock)
            {
                return RTCAddRemoteCandidate(identifier, sdp);
            }
        }
        
        public bool GatherCandidates()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCGatherCandidates(identifier);
            }
        }
        
        public bool SetRemoteGatheringDone()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCSetRemoteGatheringDone(identifier);
            }
        }
        
        public bool SendData(byte[] data)
        {
            lock (rtcPeerAgentLock)
            {
                bool isSuccess = RTCSendData(identifier, data, data.Length);
                return Convert.ToBoolean(isSuccess);
            }
        }
        
        public string GetSelectedLocalCandidates()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCGetSelectedLocalCandidates(identifier);
            }
        }
        
        public string GetSelectedRemoteCandidates()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCGetSelectedRemoteCandidates(identifier);
            }
        }
        
        public string GetSelectedLocalAddresses()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCGetSelectedLocalAddresses(identifier);
            }
        }
        
        public string GetSelectedRemoteAddresses()
        {
            lock (rtcPeerAgentLock)
            {
                return RTCGetSelectedRemoteAddresses(identifier);
            }
        }
        
        public AgentConnectionState GetAgentState()
        {
            lock (rtcPeerAgentLock)
            {
                var result = AgentConnectionState.Failed;
                var state = RTCGetConnectionState(identifier);
                if (state.ToLower() == "new")
                {
                    result = AgentConnectionState.Gathering;
                }
                else if (state.ToLower() == "connecting")
                {
                    result = AgentConnectionState.Connecting;
                }
                else if (state.ToLower() == "connected")
                {
                    result = AgentConnectionState.Connected;
                }
                else if (state.ToLower() == "disconnected")
                {
                    result = AgentConnectionState.Disconnected;
                }
                else if (state.ToLower() == "failed")
                {
                    result = AgentConnectionState.Failed;
                }
                else if (state.ToLower() == "closed")
                {
                    result = AgentConnectionState.Disconnected;
                }

                return result;
            }
        }
        
#endregion

#region Interop Methods
        
        [DllImport("__Internal")]
        private static extern void InitRTCPeerConnection (int identifier, string logLevel, string turnIp, string turnUsername, string turnPassword, int turnPort);
        
        [DllImport("__Internal")]
        private static extern bool RTCAddRemoteCandidate (int id, string sdp);
        
        [DllImport("__Internal")]
        private static extern bool RTCGatherCandidates(int id);
        
        [DllImport("__Internal")]
        private static extern bool RTCSetRemoteGatheringDone (int id);
        
        [DllImport("__Internal")]
        private static extern bool RTCSendData (int id, byte[] data, int dataLen);
        
        [DllImport("__Internal")]
        private static extern string RTCGetSelectedLocalCandidates (int id);
        
        [DllImport("__Internal")]
        private static extern string RTCGetSelectedRemoteCandidates(int id);
        
        [DllImport("__Internal")]
        private static extern string RTCGetSelectedLocalAddresses(int id);
        
        [DllImport("__Internal")]
        private static extern string RTCGetSelectedRemoteAddresses(int id);
        
        [DllImport("__Internal")]
        private static extern string RTCGetConnectionState (int id);
        
        [DllImport("__Internal")]
        private static extern void RTCRemovePeer (int id);
        
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RTCSetStateChangedHandler (int id, IntPtr handler);
        
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RTCSetCandidateFoundHandler (int id, IntPtr handler);
        
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RTCSetGatheringDoneHandler (int id, IntPtr handler);
        
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RTCSetDataReceivedHandler (int id, IntPtr handler);
        
        [DllImport("__Internal")]
        private static extern void RTCGetLocalDescriptionCallback (int id, Action<string> callback);
        
        [DllImport("__Internal")]
        private static extern void RTCSetRemoteDescriptionCallback (int id, string sdp, Action<int> callback);
        
        [DllImport("__Internal")]
        private static extern void RTCSetLogHandler (Action<string> callback);
        
#endregion
        
#region Static Methods
        
        private static int NextKey = 0;
        private static readonly IDictionary<int, AccelByteJuice> rtcPeerDictionary = new Dictionary<int, AccelByteJuice>();
        
        private static RTCPeerGetLocalDescriptionTask getLocalDescriptionTask;
        private static RTCSetRemoteDescriptionTask setRemoteDescriptionTask;
        
        [AOT.MonoPInvokeCallback(typeof(RTCStateChangedDelegate))]
        private static void RTCStateChangedHandler(int id, string state)
        {
            var transformResult = JuiceState.Failed;
            if (state.ToLower() == JuiceState.Gathering.ToString().ToLower())
            {
                transformResult = JuiceState.Gathering;
            }
            else if (state.ToLower() == JuiceState.Connecting.ToString().ToLower())
            {
                transformResult = JuiceState.Connecting;
            }
            else if (state.ToLower() == JuiceState.Connected.ToString().ToLower())
            {
                transformResult = JuiceState.Connected;
            }
            else if (state.ToLower() == JuiceState.Completed.ToString().ToLower())
            {
                transformResult = JuiceState.Completed;
            }
            else if (state.ToLower() == JuiceState.Failed.ToString().ToLower())
            {
                transformResult = JuiceState.Failed;
            }

            if (rtcPeerDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new RTCPeerStateChangedTask(juiceIce, transformResult));
            }
        }
        
        [AOT.MonoPInvokeCallback(typeof(Action<string>))]
        private static void RTCPeerLogHandler(string message)
        {
            AccelByteJuice.JuiceTasks.Enqueue(new RTCPeerLogTask(currentLogLevel, message));
        }
        
        [AOT.MonoPInvokeCallback(typeof(Action<string>))]
        private static void RTCPeerGetLocalDescriptionHandler(string message)
        {
            getLocalDescriptionTask.SetDescription(message);
            AccelByteJuice.JuiceTasks.Enqueue(getLocalDescriptionTask);
            getLocalDescriptionTask = null;
        }
        
        [AOT.MonoPInvokeCallback(typeof(Action<int>))]
        private static void RTCSetRemoteDescriptionHandler(int setUpResult)
        {
            setRemoteDescriptionTask.SetResult(setUpResult == 0);
            AccelByteJuice.JuiceTasks.Enqueue(setRemoteDescriptionTask);
            setRemoteDescriptionTask = null;
        }

        [AOT.MonoPInvokeCallback(typeof(RTCCandidateFoundDelegate))]
        private static void RTCPeerCandidateFoundHandler(int id, string description)
        {
            if (rtcPeerDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new RTCPeerCandidateFoundTask(juiceIce, description, !string.IsNullOrEmpty(description)));
            }
        }
        
        [AOT.MonoPInvokeCallback(typeof(RTCGatheringDoneDelegate))]
        private static void RTCPeerGatheringDoneHandler(int id, string result)
        {
            if (rtcPeerDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new RTCPeerGatheringDoneTask(juiceIce));
            }
        }
        
        [AOT.MonoPInvokeCallback(typeof(RTCDataReceivedDelegate))]
        private static void RTCDataReceivedHandler(int id, IntPtr dataPtr, int size)
        {
            if (rtcPeerDictionary.TryGetValue(id, out var juiceIce))
            {
                byte[] data = new byte[size];
                Marshal.Copy(dataPtr, data, 0, size);
                AccelByteJuice.JuiceTasks.Enqueue(new RTCPeerDataReceivedTask(juiceIce, data, size));
            }
        }
#endregion
        
#region Unmanaged pointer for listener delegate
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RTCDataReceivedDelegate(int id, IntPtr dataPtr, int size);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RTCGatheringDoneDelegate(int id, string result);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RTCCandidateFoundDelegate(int id, string sdp);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RTCStateChangedDelegate(int id, string sdp);
#endregion

#region Private Properties
        private readonly object rtcPeerAgentLock = new object();
        private readonly int identifier;
        private bool disposed;
#endregion

#region Private Methods
        private void Initialize()
        {
            lock (rtcPeerAgentLock)
            {
                RTCSetLogHandler(RTCPeerLogHandler);

                RTCStateChangedDelegate stateChangedDelegate = RTCStateChangedHandler;
                RTCSetStateChangedHandler(identifier, Marshal.GetFunctionPointerForDelegate(stateChangedDelegate));

                RTCCandidateFoundDelegate candidateFoundDelegate = RTCPeerCandidateFoundHandler;
                RTCSetCandidateFoundHandler(identifier, Marshal.GetFunctionPointerForDelegate(candidateFoundDelegate));

                RTCGatheringDoneDelegate gatheringDoneDelegate = RTCPeerGatheringDoneHandler;
                RTCSetGatheringDoneHandler(identifier, Marshal.GetFunctionPointerForDelegate(gatheringDoneDelegate));

                RTCDataReceivedDelegate dataReceivedDelegate = RTCDataReceivedHandler;
                RTCSetDataReceivedHandler(identifier, Marshal.GetFunctionPointerForDelegate(dataReceivedDelegate));
            }
        }

        private RTCPeerLogLevel ConvertToPeerLogLevel(string logLevelInput)
        {
            var result = RTCPeerLogLevel.Error; 
            var isSuccess = Enum.TryParse<RTCPeerLogLevel>(logLevelInput, out RTCPeerLogLevel convertedResult);
            
            if (isSuccess)
            {
                result = convertedResult;
            }
            
            return result;
        }
        
#endregion
    }
}
