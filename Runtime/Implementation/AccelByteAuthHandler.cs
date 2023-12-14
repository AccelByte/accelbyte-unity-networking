// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using AccelByte.Core;
using AccelByte.Models;
using System.Runtime.Serialization;
using System.Text;

public class AccelByteAuthHandler
{
    private const float resendRequestInterval = 3.0f;

    /** Settings */
    private EState state = EState.Uninitialized;
    private bool active { get; set; }

    public bool IsActive() { return active; }

    /** handler for encrypting with the RSA key */
    private RSACrypto rsaCrypto = null;

    /** handler for encrypting with the AES key */
    private AESCrypto aesCrypto = null;

    private AccelByteAuthInterface authInterface = null;

    public Action OnIncomingBase { get; set; }
    public Action OnPeerClose { get; set; }

    private IAccelByteICEBase ice = null;

    private string userId = string.Empty;
    private string authToken = string.Empty;
    private int recvSegCount = 0;

    private float lastTimestamp = resendRequestInterval;

    private bool isServer = false;
    /// <summary>
    /// avoid to match to Unity Netcode for GameObject NetworkBatchHeader.MagicValue
    /// </summary>
    internal static readonly byte[] MagicValue = new byte[2]{211,111};

    private enum EState : uint
    {
        Uninitialized = 0,
        SentKey,
        RecvedKey,
        WaitForJwks,
        ReadyJwks,
        WaitForAuth,
        SentAuth,
        AuthFail,
        Initialized
    }

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

    public void Tick()
    {
        if (IsActive())
        {
            if (state is EState.AuthFail)
            {
                AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) handshaking Failed.(disconnected)");
                NetCleanUp();
                return;
            }

            ProcessState();
        }
    }

    public void Clear()
    {
        state = EState.Uninitialized;
        Completed();
    }

    private void Completed()
    {
        rsaCrypto?.Clear();
        aesCrypto?.Clear();

        rsaCrypto = null;
        aesCrypto = null;

        active = false;

        authInterface = null;

        ice = null;

        userId = string.Empty;
        authToken = string.Empty;

        recvSegCount = 0;

        lastTimestamp = resendRequestInterval;
    }

    public bool IsConnected()
    {
        return (state is EState.Initialized);
    }

    public bool IsCompleted()
    {
        return ((state is EState.AuthFail) || (state is EState.Initialized));
    }

    public void ProcessState()
    {
        lastTimestamp -= UnityEngine.Time.deltaTime;

        if (lastTimestamp <= 0.0f)
        {
            if (EState.RecvedKey == state)
            {
                if (IsServer())
                {
                    SendKeyAES();
                }
                else
                {
                    SendAuthData();
                }
            }
            else if (EState.WaitForAuth == state)
            {
                if (SendAuthResult(false) == false)
                {
                    lastTimestamp = 0.1f;
                    return;
                }
            }
            else if (EState.WaitForJwks == state)
            {
                VerifyAuthToken();
            }
            else
            {
                RequestResend();
            }
            lastTimestamp = resendRequestInterval;
        }
    }

    public bool Setup(IAccelByteICEBase inIce, AccelByteAuthInterface inAuthInterface, bool inServer)
    {
        string functionName = $"[{(inServer ? "DS" : "CL")}] Setup";
        Report.GetFunctionLog(GetType().Name, functionName);

        active = inServer;
        isServer = inServer;

        ice = inIce;
        if (ice is null)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(inServer ? "DS" : "CL")}] Setup failed. ice is null.");
            goto fail;
        }

        authInterface = inAuthInterface;
        if (authInterface is null)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(inServer ? "DS" : "CL")}] Setup failed. authInterface is null.");
            goto fail;
        }

        if (SetComponentReady(inServer) is false)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(inServer ? "DS" : "CL")}) Setup failed.");
            goto fail;
        }
        NotifyHandshakeBegin();
        return true;

    fail:
        NetCleanUp();
        return false;
    }

    public bool IsServer()
    {
        return isServer;
    }

    private void NetCleanUp()
    {
        string functionName = $"[{(IsServer() ? "DS" : "CL")}] NetCleanUp";
        Report.GetFunctionLog(GetType().Name, functionName);

        OnPeerClose?.Invoke();
        Completed();
    }

    private bool SetComponentReady(bool inServer)
    {
        if (inServer)
        {
            if (aesCrypto is null)
            {
                aesCrypto = new AESCrypto();
            }
            else
            {
                aesCrypto.Clear();
            }

            if (aesCrypto.GenerateKey() is false)
            {
                AccelByteDebug.LogWarning($"AUTH HANDLER: [{(inServer ? "DS" : "CL")}] failed to generate AES Key.");
                return false;
            }
        }
        else
        {
            if (rsaCrypto is null)
            {
                rsaCrypto = new RSACrypto();
            }
            else
            {
                rsaCrypto.Clear();
            }

            if (rsaCrypto.GenerateKey(true) is false)
            {
                AccelByteDebug.LogWarning($"AUTH HANDLER: [{(inServer ? "DS" : "CL")}] failed to generate RSA Key.");
                return false;
            }
        }
        return true;
    }

    public byte[] Incoming(byte[] packet)
    {
        if (IsActive() is false)
        {
            return packet;
        }

        if (state != EState.Initialized)
        {
            return IncomingHandshake(packet);
        }
        return packet;
    }

    private void NotifyHandshakeBegin()
    {
        if (IsServer())
        {
            authInterface?.UpdateJwks();
        }
        else
        {
            active = true;
            SendKey();
        }
    }

    private byte[] IncomingHandshake(byte[] rPacket)
    {
        if ((authInterface is null) || (authInterface.IsActive() is false))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] authInterface is not valid.");
            CompletedHandshaking(false);
            return null;
        }
        var packet = new byte[rPacket.Length-MagicValue.Length];
        Buffer.BlockCopy(rPacket, MagicValue.Length, packet, 0, packet.Length);
        var header = ABNetUtilities.Deserialize<AuthHeader>(packet);
        AccelByteDebug.LogVerbose($"+{DateTime.UtcNow.Millisecond}ms AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) IncomingHandshake type:{header.Type} state:{state}");
        switch (header.Type)
        {
            case AccelByte.Models.EAccelByteAuthMsgType.RSAKey:
                {
                    if (RecvPublicKey(packet))
                    {
                        SendKeyAES();
                    }
                    else
                    {
                        // request resending key to Client.
                        RequestResend();
                    }

                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.AESKey:
                {
                    if (RecvKeyAES(packet))
                    {
                        SendAuthData();
                    }
                    else
                    {
                        // request resending key to DS.
                        RequestResend();
                    }
                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.Auth:
                {
                    RecvAuthData(packet);
                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.Result:
                {
                    var data = ABNetUtilities.Deserialize<AuthResultData>(packet);
                    CompletedHandshaking(data.Result);
                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.ResendKey:
                {
                    SendKey();
                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.ResendAuth:
                {
                    SendAuthData();
                    return null;
                }
            case AccelByte.Models.EAccelByteAuthMsgType.ResendResult:
                {
                    SendAuthResult(true);
                    return null;
                }
            default:
                {
                    return packet;
                }
        }
    }

    private bool SendAuthResult(bool resend)
    {
        if (!resend && (state == EState.Initialized))
        {
            return false;
        }

        if (IsServer())
        {
            if (authInterface != null)
            {
                switch (authInterface.GetAuthStatus(userId))
                {
                    case AccelByteAuthInterface.EAccelByteAuthStatus.AuthSuccess:
                        {
                            return AuthenticateUserResult(true);
                        }
                    case AccelByteAuthInterface.EAccelByteAuthStatus.AuthFail:
                        {
                            return AuthenticateUserResult(false);
                        }
                    default:
                        {
                            return false;
                        }
                }
            }
            else
            {
                CompletedHandshaking(false);
                return true;
            }
        }
        return false;
    }

    private void SendKey()
    {
        // Exchange the RSA Public key
        if (IsServer())
        {
            if (IsActive())
            {
                if (state == EState.RecvedKey)
                {
                    SendKeyAES();
                }
            }
        }
        else
        {
            if (IsActive())
            {
                if (state == EState.Uninitialized)
                {
                    SendPublicKey();
                }
            }
        }
    }

    private void CompletedHandshaking(bool bResult)
    {
        string functionName = $"[{(IsServer() ? ("DS") : ("CL"))}] CompletedHandshaking({bResult})";
        Report.GetFunctionLog(GetType().Name, functionName);

        if (bResult)
        {
            state = EState.Initialized;
            Completed();
            OnIncomingBase?.Invoke();
        }
        else
        {
            state = EState.AuthFail;
        }
    }

    private void SendPublicKey()
    {
        if(state==EState.SentKey)
        {
            return;
        }
        RsaKeyData packetData = new RsaKeyData();
        
        packetData.Modulus = rsaCrypto.ExportPublicKeyModulus();
        packetData.Exponent = rsaCrypto.ExportPublicKeyExponent();

        if ((packetData.Modulus.Length < 256) || (packetData.Modulus.Length > 512) || (packetData.Exponent.Length < 2) || (packetData.Exponent.Length > 4))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RSA Key has something wrong. {packetData.Modulus.Length} {packetData.Exponent.Length}");
            CompletedHandshaking(false);
            return;
        }

        packetData.ModulusSize = packetData.Modulus.Length;
        packetData.ExponentSize = packetData.Exponent.Length;

        var outPacket = ABNetUtilities.Serialize<RsaKeyData>(packetData);
        if (SendPacket(outPacket))
        {
            state = EState.SentKey;
        }
    }

    private bool RecvPublicKey(byte[] packetData)
    {
        if (rsaCrypto is null)
        {
            rsaCrypto = new RSACrypto();
        }

        var data = ABNetUtilities.Deserialize<RsaKeyData>(packetData);
        if ((data.ModulusSize < 256) || (data.ModulusSize > 512) || (data.ExponentSize < 2) || (data.ExponentSize > 4))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RSA Key's length has something wrong. {data.Modulus.Length} {data.Exponent.Length}");
            CompletedHandshaking(false);
            return false;
        }

        byte[] modulus = new byte[data.ModulusSize];
        Buffer.BlockCopy(data.Modulus, 0, modulus, 0, data.ModulusSize);

        byte[] exponent = new byte[data.ExponentSize];
        Buffer.BlockCopy(data.Exponent, 0, exponent, 0, data.ExponentSize);

        if ((modulus.Length < 256) || (modulus.Length > 512) || (exponent.Length < 2) || (exponent.Length > 4))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RSA Key has something wrong. {modulus.Length} {exponent.Length}");
            CompletedHandshaking(false);
            return false;
        }

        if (rsaCrypto.ImportPublicKey(modulus, exponent, true))
        {
            state = EState.RecvedKey;
            return true;
        }
        return false;
    }

    private void SendKeyAES()
    {
        AesKeyData packetData = new AesKeyData();

        if ((aesCrypto.GetKeyBytes().Length < 16) || (aesCrypto.GetKeyBytes().Length > 32) || (aesCrypto.GetIVBytes().Length != 16))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) AES Key has something wrong. {packetData.Key.Length} {packetData.IV.Length}");
            CompletedHandshaking(false);
            return;
        }

        packetData.Key = EncryptRSA(aesCrypto.GetKeyBytes());
        packetData.IV = EncryptRSA(aesCrypto.GetIVBytes());

        packetData.KeySize = packetData.Key.Length;
        packetData.IVSize = packetData.IV.Length;

        var outPacket = ABNetUtilities.Serialize<AesKeyData>(packetData);
        if (SendPacket(outPacket))
        {
            state = EState.SentKey;
        }
    }

    private bool RecvKeyAES(byte[] packetData)
    {
        if (aesCrypto is null)
        {
            aesCrypto = new AESCrypto();
        }

        var data = ABNetUtilities.Deserialize<AesKeyData>(packetData);

        byte[] Key = new byte[data.KeySize];
        Buffer.BlockCopy(data.Key, 0, Key, 0, data.KeySize);

        byte[] IV = new byte[data.IVSize];
        Buffer.BlockCopy(data.IV, 0, IV, 0, data.IVSize);

        var originKey = DecryptRSA(Key);
        var originIV = DecryptRSA(IV);

        if ((originKey.Length > 32) || (originKey.Length < 16) || (originIV.Length != 16))
        {
            CompletedHandshaking(false);
            return false;
        }

        if (aesCrypto.ImportKey(originKey, originIV))
        {
            state = EState.RecvedKey;
            return true;
        }
        else
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) AES Key import failed.");
            CompletedHandshaking(false);
        }
        return false;
    }

    /**
     * set up the client's secure data from Credentials information.
     */
    private void SendAuthData()
    {
        /** the client handler */
        if (authInterface is null)
        {
            return;
        }

        authToken = authInterface?.GetAuthData();
        if (string.IsNullOrEmpty(authToken))
        {
            AccelByteDebug.LogWarning($"({(IsServer() ? ("DS") : ("CL"))}) AUTH HANDLER: authToken is null or empty.");
            CompletedHandshaking(false);
            return;
        }

        // Convert the authToken string to bytes
        byte[] byAuthToken = ABCryptoUtilities.StringToBytes(authToken);

        int packetLength = GetMaxTokenSizeInBytes();
        int maxSegments = (int)Math.Ceiling((double)byAuthToken.Length / packetLength);
        // Create segments and send them
        for (int i = 0; i < maxSegments; i++)
        {
            // Determine the size of data for the current segment
            int dataSize = Math.Min(packetLength, byAuthToken.Length - (i * packetLength));

            // Create a byte array for the segment data
            byte[] segmentData = new byte[dataSize];
            Buffer.BlockCopy(byAuthToken, i * packetLength, segmentData, 0, dataSize);

            // Call the SendAuthData function with the segment data
            if (!SendAuthData(segmentData, maxSegments))
            {
                CompletedHandshaking(false);
                return;
            }
        }

        state = EState.SentAuth;
    }

    private bool SendAuthData(byte[] segmentData, int maxSegments)
    {
        var cipherText = EncryptAES(segmentData);

        AuthUserData packetData = new AuthUserData();
        packetData.MaxSegments = maxSegments;
        packetData.AuthToken = cipherText;
        packetData.PacketSize = cipherText.Length;

        byte[] outPacket = ABNetUtilities.Serialize<AuthUserData>(packetData);
        return SendPacket(outPacket);
    }

    private void RecvAuthData(byte[] packetData)
    {
        if(state==EState.ReadyJwks)
        {
            return;
        }
        if ((authInterface is null) || (authInterface.IsActive() is false))
        {
            CompletedHandshaking(false);
            return;
        }

        var data = ABNetUtilities.Deserialize<AuthUserData>(packetData);
        byte[] segmentBytes = new byte[data.PacketSize];
        Buffer.BlockCopy(data.AuthToken, 0, segmentBytes, 0, data.PacketSize);
        
        var plainText = DecryptAES(segmentBytes);
        if (plainText != null)
        {
            var plainStr = ABCryptoUtilities.BytesToString(plainText);
            if(authToken.Contains(plainStr))
            {
                return;
            }
            authToken += plainStr;

            if (++recvSegCount == data.MaxSegments)
            {
                recvSegCount = 0;
                VerifyAuthToken();
            }
        }
    }

    private void VerifyAuthToken()
    {
        if (authInterface.UpdateJwks())
        {
            state = EState.ReadyJwks;
            OnVerifyAuthToken();
        }
        else
        {
            state = EState.WaitForJwks;
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] VerifyAuthToken(): {state}");
        }
    }
    private void OnVerifyAuthToken()
    {
        if (authInterface.VerifyAuthToken(authToken, out userId))
        {
            OnAuthenticateUser();
        }
        else
        {
            CompletedHandshaking(false);
        }
    }

    private void OnAuthenticateUser()
    {
        if (authInterface.AuthenticateUser(userId))
        {
            state = EState.WaitForAuth;
            lastTimestamp = 0.1f;
        }
        else
        {
            CompletedHandshaking(false);
        }
    }

    private bool OnSendAuthResult(bool InAuthResult)
    {
        AuthResultData packetData = new AuthResultData();
        packetData.Result = InAuthResult;

        byte[] outPacket = ABNetUtilities.Serialize<AuthResultData>(packetData);
        if (SendPacket(outPacket))
        {
            state = EState.SentAuth;
            return true;
        }

        CompletedHandshaking(false);

        return false;
    }

    private bool AuthenticateUserResult(bool Result)
    {
        if (OnSendAuthResult(Result))
        {
            authInterface?.RemoveUser(userId);
            CompletedHandshaking(Result);
            return true;
        }
        return false;
    }

    private void RequestResend()
    {
        AuthHeader packetData = new AuthHeader();

        if (IsServer())
        {
            if (EState.Uninitialized == state)
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendKey;
            }
            else if (EState.SentKey == state)
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendAuth;
            }
            else
            {
                return;
            }
        }
        else
        {
            if (EState.SentKey == state)
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendKey;
            }
            else if (EState.SentAuth == state)
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendResult;
            }
            else
            {
                return;
            }
        }

        AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RequestResend. {state} {lastTimestamp}");

        byte[] outPacket = ABNetUtilities.Serialize(packetData);
        if (SendPacket(outPacket))
        {
            return;
        }
    }

    private bool SendPacket(byte[] outPacket)
    {
        if (IsActive() is false)
        {
            return false;
        }

        if ((outPacket is null) || (outPacket.Length <= 0))
        {
            return false;
        }
        // SendPacket is a low level send
        byte[] sendPacket = new byte[MagicValue.Length+outPacket.Length];
        Buffer.BlockCopy(MagicValue, 0, sendPacket, 0, MagicValue.Length);
        Buffer.BlockCopy(outPacket, 0, sendPacket, MagicValue.Length, outPacket.Length);
        AccelByteDebug.LogVerbose($"+{DateTime.UtcNow.Millisecond}ms AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) SendPacket state:{state}");
        if (ice?.Send(sendPacket) <= 0)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) SendPacket: Failed. ({outPacket.Length})");
            return false;
        }
        lastTimestamp = resendRequestInterval;
        return true;
    }

    private int GetMaxTokenSizeInBytes()
    {
        return (aesCrypto.GetBlockSize() / 8) * 40;
    }

    private byte[] EncryptRSA(byte[] inData)
    {
        return rsaCrypto?.Encrypt(inData);
    }

    private byte[] DecryptRSA(byte[] inData)
    {
        return rsaCrypto?.Decrypt(inData);
    }

    private byte[] EncryptAES(byte[] inData)
    {
        return aesCrypto?.Encrypt(inData);
    }

    private byte[] DecryptAES(byte[] inData)
    {
        return aesCrypto?.Decrypt(inData);
    }
}
