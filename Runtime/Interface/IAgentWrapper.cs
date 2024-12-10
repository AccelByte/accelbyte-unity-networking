// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;

namespace AccelByte.Networking.Interface
{
    public interface IAgentWrapper: IDisposable
    {
        /// <summary>
        /// Get local description of LibJuice agent, this description is used for peer
        /// </summary>
        /// <param name="excludeCandidates">Remove candidates entry from sdp, needed to force using relay</param>
        /// <returns></returns>
        public void GetLocalDescription(Action<string> callback, bool excludeCandidates = false);
        
        public void SetRemoteDescription(string sdp, Action<bool> callback);

        public bool AddRemoteCandidate(string sdp);

        public bool GatherCandidates();

        public bool SetRemoteGatheringDone();

        public bool SendData(byte[] data);

        public string GetSelectedLocalCandidates();

        public string GetSelectedRemoteCandidates();

        public string GetSelectedLocalAddresses();

        public string GetSelectedRemoteAddresses();

        public Models.Enum.AgentConnectionState GetAgentState();
    }
}
