// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using AccelByte.Networking.Interface;

namespace AccelByte.Networking
{
    internal static class AgentWrapperFactory
    {
        private static IAgentWrapper agentWrapper;
        private static bool needOverride = false;
        
        public static IAgentWrapper CreateDefaultAgentWrapper(AccelByteJuice juice, AccelByte.Models.Config clientConfig, string turnServerURL, string turnServerUsername, string turnServerPassword, ushort turnServerPort)
        {
            if (needOverride)
            {
                needOverride = false;
                return agentWrapper;
            }
            
#if UNITY_WEBGL && !UNITY_EDITOR
            return new AccelByteRTCPeerConnectionAgent(juice, clientConfig, turnServerURL, turnServerUsername, turnServerPassword, turnServerPort);
#else
            return new AccelByteLibJuiceAgent(juice, turnServerURL, turnServerUsername, turnServerPassword, turnServerPort);
#endif
        }

        internal static void OverrideAgentWrapper(IAgentWrapper newAgentWrapper)
        {
            agentWrapper = newAgentWrapper;
            needOverride = true;
        }
    }
}
