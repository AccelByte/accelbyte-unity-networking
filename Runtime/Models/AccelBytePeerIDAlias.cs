// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Collections.Generic;

namespace AccelByte.Networking
{
    internal class AccelBytePeerIDAlias
    {
        /// <summary>
        /// Collection of a connection to each peer based on UserID as the key.
        /// Messaging to specific player should use specific/assigned ICEBase too.
        /// </summary>
        private readonly Dictionary<string, IAccelByteICEBase> userIdToICEConnectionDictionary =
            new Dictionary<string, IAccelByteICEBase>();

        /// <summary>
        /// Facilitate the necessity of Unity transport.
        /// The usage of ulong ClientID is limited in the Unity Transport's scope.
        /// ulong ClientID identifier is not propagated to the remote peer, isolated in this player.
        /// Don't use it in the context of ICE connection because we rely completely to AccelByte UserID.
        /// </summary>
        private readonly Dictionary<string, ulong> userIdToClientIdDictionary = new Dictionary<string, ulong>();

        private const int bufferLength = 8;

        public IAccelByteICEBase this[string key] => userIdToICEConnectionDictionary[key];

        public IAccelByteICEBase this[ulong key]
        {
            get
            {
                var keyUserId = GetAlias(key);
                if (keyUserId.Length == 0)
                {
                    return null;
                }

                return userIdToICEConnectionDictionary[keyUserId];
            }
        }

        public bool Contain(string userID)
        {
            return userIdToICEConnectionDictionary.ContainsKey(userID);
        }

        public bool Contain(ulong clientID)
        {
            return userIdToClientIdDictionary.ContainsValue(clientID);
        }

        public string GetAlias(ulong clientId)
        {
            using var enumerator = userIdToClientIdDictionary.GetEnumerator();
            do
            {
                if (enumerator.Current.Value == clientId)
                {
                    return enumerator.Current.Key;
                }
            } while (enumerator.MoveNext());

            return "";
        }

        public ulong GetAlias(string userId)
        {
            userIdToClientIdDictionary.TryGetValue(userId, out ulong value);
            return value;
        }

        /// <summary>
        /// Add the ICE AccelByte implementation to this collection to the designated key
        /// The ulong ClientID will be generated too and returned.
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="ice"></param>
        /// <returns></returns>
        public ulong Add(string userID, IAccelByteICEBase ice)
        {
            if (userIdToICEConnectionDictionary.ContainsKey(userID))
            {
                userIdToICEConnectionDictionary[userID] = ice;
                return userIdToClientIdDictionary[userID];
            }

            var random = new Random();
            byte[] buffer = new byte[bufferLength];
            random.NextBytes(buffer);
            ulong clientID = BitConverter.ToUInt64(buffer, 0);
            userIdToClientIdDictionary.Add(userID, clientID);
            userIdToICEConnectionDictionary.Add(userID, ice);
            return clientID;
        }

        public void Remove(ulong clientId)
        {
            var userId = GetAlias(clientId);
            userIdToClientIdDictionary.Remove(userId);
            userIdToICEConnectionDictionary.Remove(userId);
        }

        public List<string> GetAllUserID()
        {
            return new List<string>(userIdToICEConnectionDictionary.Keys);
        }

        public List<ulong> GetAllClientID()
        {
            return new List<ulong>(userIdToClientIdDictionary.Values);
        }
    }
}