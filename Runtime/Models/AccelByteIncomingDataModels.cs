// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;

namespace AccelByte.Networking
{
    /// <summary>
    /// Incoming data model for incoming data, Received time is using UTC time of machine
    /// </summary>
    public class IncomingPacketFromDataChannel
    {
        /// <summary>
        /// Array of byte containing data received from peer
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        /// Time of received data in UTC time of machine
        /// </summary>
        public readonly DateTime ReceivedTime;

        /// <summary>
        /// ClientID of peer
        /// </summary>
        public readonly ulong ClientID;

        public IncomingPacketFromDataChannel(byte[] data, ulong clientID, DateTime receivedTime = default)
        {
            Data = data;
            ReceivedTime = receivedTime == default ?  DateTime.UtcNow : receivedTime;
            ClientID = clientID;
        }

        public float GetRealTimeSinceStartUp()
        {
            var UnityStartUpTimeUtc = DateTime.UtcNow.Subtract(
                TimeSpan.FromSeconds(UnityEngine.Time.realtimeSinceStartup));

            return (float)(ReceivedTime - UnityStartUpTimeUtc).TotalSeconds;
        }
    }
}