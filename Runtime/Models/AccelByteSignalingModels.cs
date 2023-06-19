// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System.Runtime.Serialization;

namespace AccelByte.Networking
{
    public enum SignalingMessageType
    {
        [EnumMember] CheckRequest = 0,
        [EnumMember] CheckResponse,
        [EnumMember] Offer,
        [EnumMember] Answer,
        [EnumMember] Candidate,
        [EnumMember] GatheringDone,
        [EnumMember] Error,
    }

    public enum PeerStatus
    {
        [EnumMember] NotHosting = 0,
        [EnumMember] WaitingReply,
        [EnumMember] Hosting
    }

    [DataContract, UnityEngine.Scripting.Preserve]
    public class SignalingMessage
    {
        [DataMember] public SignalingMessageType Type;
        [DataMember(EmitDefaultValue = false)] public string Host;
        [DataMember(EmitDefaultValue = false)] public string Username;
        [DataMember(EmitDefaultValue = false)] public string Password;
        [DataMember(EmitDefaultValue = false)] public int Port;
        [DataMember(EmitDefaultValue = false)] public string Description;
        [DataMember(EmitDefaultValue = false)] public bool IsHosting;
    }
}