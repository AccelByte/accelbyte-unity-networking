// Copyright (c) 2023 - 2025 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using AccelByte.Core;

namespace AccelByte.Networking
{
    public class JuiceStateChangedTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly JuiceState state;

        public JuiceStateChangedTask(AccelByteJuice inJuiceInstance, JuiceState inState)
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

    public class JuiceCandidateFoundTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly string sdp;
        private readonly bool isSuccess;

        public JuiceCandidateFoundTask(AccelByteJuice inJuiceInstance, string inSdp, bool inIsSuccess)
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

    public class JuiceGatheringDoneTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;

        public JuiceGatheringDoneTask(AccelByteJuice inJuiceInstance)
        {
            juiceInstance = inJuiceInstance;
        }

        public void Execute(IDebugger logger)
        {
            juiceInstance?.OnJuiceGatheringDone();
            logger?.Log($"{GetType().Name}");
        }
    }

    public class JuiceDataReceivedTask : JuiceTask
    {
        private readonly AccelByteJuice juiceInstance;
        private readonly byte[] data;
        private readonly int size;

        public JuiceDataReceivedTask(AccelByteJuice inJuiceInstance, byte[] inData, int inSize)
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

    public class JuiceLogTask : JuiceTask
    {
        private readonly JuiceLogLevel logLevel;
        private readonly string message;

        public JuiceLogTask(JuiceLogLevel inLogLevel, string inMessage)
        {
            logLevel = inLogLevel;
            message = inMessage;
        }

        public void Execute(IDebugger logger)
        {
            var logMessage = $"JUICE_NATIVE_LOG {logLevel}: {message}";
            AccelByteDebug.LogVerbose(logMessage);
            logger?.Log(logMessage);
        }
    }
}