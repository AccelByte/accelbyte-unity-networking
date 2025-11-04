// Copyright (c) 2025 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

namespace AccelByte.Models
{
    public class JuicePayloadHeaderModel
    {
        public uint Marker;
        public uint PacketId;
        public ushort ChunkIndex;        
        public ushort TotalChunks;
        public ushort PayloadLength;

        //Make sure that the data types are perfectly match
        public readonly int MarkerSize = sizeof(uint);
        public readonly int PacketIdSize = sizeof(uint);
        public readonly int ChunkIndexSize = sizeof(ushort);
        public readonly int TotalChunksSize = sizeof(ushort);
        public readonly int PayloadLengthSize = sizeof(ushort);
    }
}