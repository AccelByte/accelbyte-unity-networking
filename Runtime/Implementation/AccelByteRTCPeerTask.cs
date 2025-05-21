// Copyright (c) 2025 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Drawing;
using AccelByte.Core;
namespace AccelByte.Networking
{
    public class RTCPeerStateChangedTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly JuiceState state;

        public RTCPeerStateChangedTask(AccelByteJuice inJuiceInstance, JuiceState inState)
        {
            juiceInstance = inJuiceInstance;
            state = inState;
        }

        public void Execute(IDebugger logger)
        {
            juiceInstance?.OnJuiceStateChanged(state);
            logger?.Log($"{GetType().Name} state: {state}");
        }
    }

    public class RTCPeerCandidateFoundTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly string sdp;
        private readonly bool isSuccess;

        public RTCPeerCandidateFoundTask(AccelByteJuice inJuiceInstance, string inSdp, bool inIsSuccess)
        {
            juiceInstance = inJuiceInstance;
            sdp = inSdp;
            isSuccess = inIsSuccess;
        }

        public void Execute(IDebugger logger)
        {
            juiceInstance?.OnJuiceCandidateFound(sdp, isSuccess);
            logger?.Log($"{GetType().Name} sdp: {sdp} - {isSuccess}");
        }
    }

    public class RTCPeerGatheringDoneTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;

        public RTCPeerGatheringDoneTask(AccelByteJuice inJuiceInstance)
        {
            juiceInstance = inJuiceInstance;
        }

        public void Execute(IDebugger logger)
        {
            juiceInstance?.OnJuiceGatheringDone();
            logger?.Log($"{GetType().Name}");
        }
    }

    public class RTCPeerDataReceivedTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly byte[] data;
        private readonly int size;
    
        public RTCPeerDataReceivedTask(AccelByteJuice inJuiceInstance, byte[] inData, int inSize)
        {
            juiceInstance = inJuiceInstance;
            data = inData;
            size = inSize;
        }

        public void Execute(IDebugger logger)
        {
            juiceInstance?.OnJuiceDataReceived(data, size);
            logger?.Log($"{GetType().Name} size {size}");
        }
    }

    public class RTCPeerLogTask : JuiceTask
    {
        private readonly RTCPeerLogLevel logLevel;
        private readonly string message;
    
        public RTCPeerLogTask(RTCPeerLogLevel inLogLevel, string inMessage)
        {
            logLevel = inLogLevel;
            message = inMessage;
        }
    
        public void Execute(IDebugger logger)
        {
            var logMessage = $"RTC_PEER_LOG {logLevel}: {message}";
            AccelByteDebug.LogVerbose(logMessage);
            logger?.Log(logMessage);
        }
    }

    public class RTCPeerGetLocalDescriptionTask : JuiceTask
    {
        private string localDescription;
        private Action<string> callback;

        public RTCPeerGetLocalDescriptionTask(Action<string> inCallback)
        {
            callback = inCallback;
        }
        
        public void SetDescription(string inDescription)
        {
            localDescription = inDescription;
        }
        
        public void Execute(IDebugger logger)
        {
            callback?.Invoke(localDescription);
            logger?.Log($"{GetType().Name} sdp {localDescription}");
        }
    }
    
    public class RTCSetRemoteDescriptionTask : JuiceTask
    {
        private bool setupResult;
        private Action<bool> callback;

        public RTCSetRemoteDescriptionTask(Action<bool> inCallback)
        {
            callback = inCallback;
        }
        
        public void SetResult(bool inResult)
        {
            setupResult = inResult;
        }
        
        public void Execute(IDebugger logger)
        {
            callback?.Invoke(setupResult);
            logger?.Log($"{GetType().Name} result {setupResult}");
        }
    }
}
