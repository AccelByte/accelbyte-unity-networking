// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

namespace AccelByte.Networking.Models.Enum
{
    public enum AgentConnectionState
    {
        Disconnected = 0,
        Gathering,
        Connecting,
        Connected,
        Completed,
        Failed
    }
}
