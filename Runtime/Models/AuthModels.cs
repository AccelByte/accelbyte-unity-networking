// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System.Runtime.Serialization;
using System.Runtime.InteropServices;

namespace AccelByte.Models
{
    public enum EAccelByteAuthMsgType : byte
    {
        [EnumMember] RSAKey = 0,
        [EnumMember] AESKey,
        [EnumMember] Auth,
        [EnumMember] Result,
        [EnumMember] ResendKey,
        [EnumMember] ResendAuth,
        [EnumMember] ResendResult,
        [EnumMember] Max
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class AuthHeader
    {
        public AuthHeader(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.Max)
        {
            Type = inType;
        }

        [MarshalAs(UnmanagedType.U1)]
        public EAccelByteAuthMsgType Type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class AesKeyData : AuthHeader
    {
        public AesKeyData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.AESKey) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.U2)]
        public int KeySize;
        public int IVSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] Key;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] IV;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class RsaKeyData : AuthHeader
    {
        public RsaKeyData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.RSAKey) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.U2)]
        public int ModulusSize;

        [MarshalAs(UnmanagedType.U2)]
        public int ExponentSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] Modulus;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Exponent;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class AuthUserData : AuthHeader
    {
        public AuthUserData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.Auth) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.U1)]
        public int MaxSegments;

        [MarshalAs(UnmanagedType.U2)]
        public int PacketSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (16 * 48))]
        public byte[] AuthToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class AuthResultData : AuthHeader
    {
        public AuthResultData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.Result) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.I1)]
        public bool Result;
    }
}
