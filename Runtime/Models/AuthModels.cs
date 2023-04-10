// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;

namespace AccelByte.Models
{
    [DataContract]
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

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Key;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] IV;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class RsaKeyData : AuthHeader
    {
        public RsaKeyData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.RSAKey) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Modulus;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Exponent;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class AuthUserData : AuthHeader
    {
        public AuthUserData(EAccelByteAuthMsgType inType = EAccelByteAuthMsgType.Auth) : base(inType)
        {
        }

        [MarshalAs(UnmanagedType.U2)]
        public int UserIdSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] UserId;
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
