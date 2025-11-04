// Copyright (c) 2025 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using AccelByte.Core;
using AccelByte.Models;
using System;
using System.Collections.Generic;

namespace AccelByte.Networking.Utils
{
    internal class AccelByteJuicePayloadUtils
    {
        private readonly uint chunkMarker = 0xCAFEBABE;
        private int headerSize;

        private readonly Dictionary<uint, List<byte[]>> chunkBuffer = new();
        private readonly Dictionary<uint, int> chunkCounts = new();

        private int maxPacketChunkSize = 1024;

        private int offsetMarker = 0;
        private int offsetPacketId;
        private int offsetChunkIndex;
        private int offsetTotalChunks;
        private int offsetPayloadLength;

        private uint nextPacketId = 1;

        public AccelByteJuicePayloadUtils(int inMaxPacketChunkSize = 0)
        {
            if (inMaxPacketChunkSize > 0)
            {
                maxPacketChunkSize = inMaxPacketChunkSize;
            }

            headerSize = CalculateHeaderSize();
        }

        public int GetMaxPacketChunkSize()
        {
            return maxPacketChunkSize;
        }

        private int CalculateHeaderSize()
        {
            var sampleHeader = new JuicePayloadHeaderModel();

            offsetPacketId = offsetMarker + sampleHeader.MarkerSize;
            offsetChunkIndex = offsetPacketId + sampleHeader.PacketIdSize;
            offsetTotalChunks = offsetChunkIndex + sampleHeader.ChunkIndexSize;
            offsetPayloadLength = offsetTotalChunks + sampleHeader.TotalChunksSize;

            return offsetPayloadLength + sampleHeader.PayloadLengthSize;        
        }

        public List<byte[]> SerializeData(byte[] data, int dataSize)
        {
            if (dataSize + headerSize <= maxPacketChunkSize)
            {
                return new List<byte[]> { data };
            }

            int payloadSize = maxPacketChunkSize - headerSize;
            ushort totalChunks = (ushort)Math.Ceiling((double)dataSize / payloadSize);
            List<byte[]> output = new();

            uint packetId = GetNextPacketId();

            for (ushort i = 0; i < totalChunks; i++)
            {
                int offset = i * payloadSize;
                int chunkLength = Math.Min(payloadSize, dataSize - offset);

                JuicePayloadHeaderModel header = new JuicePayloadHeaderModel
                {
                    PacketId = packetId,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    PayloadLength = (ushort)chunkLength
                };

                byte[] headerBytes = SerializeHeader(header);
                byte[] chunkPayload = new byte[chunkLength];
                Array.Copy(data, offset, chunkPayload, 0, chunkLength);

                // Combine header and payload
                byte[] fullPacket = new byte[headerBytes.Length + chunkPayload.Length];
                Buffer.BlockCopy(headerBytes, 0, fullPacket, 0, headerBytes.Length);
                Buffer.BlockCopy(chunkPayload, 0, fullPacket, headerBytes.Length, chunkPayload.Length);

                output.Add(fullPacket);
            }

            return output;
        }

        public byte[] DeserializeData(byte[] data, int dataSize)
        {
            if (dataSize < this.headerSize)
            {
                return data;
            }

            uint marker = BitConverter.ToUInt32(data, offsetMarker);
            if (marker != chunkMarker)
            {
                return data;
            }

            var header = DeserializeHeader(data);
            int headerSize = this.headerSize;

            byte[] payload = new byte[header.PayloadLength];
            Array.Copy(data, headerSize, payload, 0, header.PayloadLength);

            if (!chunkBuffer.ContainsKey(header.PacketId))
            {
                chunkBuffer[header.PacketId] = new List<byte[]>(new byte[header.TotalChunks][]);
                chunkCounts[header.PacketId] = 0;
            }

            chunkBuffer[header.PacketId][header.ChunkIndex] = payload;
            chunkCounts[header.PacketId]++;

            if (chunkCounts[header.PacketId] >= header.TotalChunks)
            {
                List<byte> result = new();
                foreach (var chunk in chunkBuffer[header.PacketId])
                {
                    result.AddRange(chunk);
                }

                // Clean up
                chunkBuffer.Remove(header.PacketId);
                chunkCounts.Remove(header.PacketId);

                return result.ToArray();
            }

            // Waiting for more chunks
            return null;
        }

        private byte[] SerializeHeader(JuicePayloadHeaderModel headerData)
        {
            byte[] buffer = new byte[headerSize];

            Array.Copy(BitConverter.GetBytes(chunkMarker), 0, buffer, offsetMarker, headerData.MarkerSize);
            Array.Copy(BitConverter.GetBytes(headerData.PacketId), 0, buffer, offsetPacketId, headerData.PacketIdSize);
            Array.Copy(BitConverter.GetBytes(headerData.ChunkIndex), 0, buffer, offsetChunkIndex, headerData.ChunkIndexSize);
            Array.Copy(BitConverter.GetBytes(headerData.TotalChunks), 0, buffer, offsetTotalChunks, headerData.TotalChunksSize);
            Array.Copy(BitConverter.GetBytes(headerData.PayloadLength), 0, buffer, offsetPayloadLength, headerData.PayloadLengthSize);

            return buffer;
        }

        private JuicePayloadHeaderModel DeserializeHeader(byte[] data)
        {
            return new JuicePayloadHeaderModel
            {
                ChunkIndex = BitConverter.ToUInt16(data, offsetChunkIndex),
                TotalChunks = BitConverter.ToUInt16(data, offsetTotalChunks),
                PayloadLength = BitConverter.ToUInt16(data, offsetPayloadLength)
            };
        }

        private uint GetNextPacketId()
        {
            nextPacketId++;

            // If uint is overflow, it will go back to 0
            // dont use packet id 1, just continue to 1
            if (nextPacketId == 0)
            {
                AccelByteDebug.LogVerbose($"PacketId is overflown, restarting to 1");
                nextPacketId = 1;
            }
            
            return nextPacketId;
        }

    }
}