// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using AccelByte.Core;

namespace AccelByte.Networking
{
    public interface JuiceTask
    {
        public void Execute(IDebugger logger);
    }
}
