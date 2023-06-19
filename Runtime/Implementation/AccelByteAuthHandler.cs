// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using AccelByte.Core;
using AccelByte.Models;
using System.Runtime.Serialization;

public class AccelByteAuthHandler
{
    private const int resendRequestInterval = 3;

    /** Settings */
    private EState state = EState.Uninitialized;
    private bool active { get; set; }

    public bool IsActive() { return active; }

    /** handler for encrypting with the RSA key */
    private RSACrypto rsaCrypto = null;

    /** handler for encrypting with the AES key */
    private AESCrypto aesCrypto = null;

    private AccelByteAuthInterface authInterface = null;

    public Action OnPeerClose { get; set; }
    public Action OnIncomingBase { get; set; }

    private IAccelByteICEBase ice = null;

    private string userId = string.Empty;

    private float lastTimestamp = resendRequestInterval;

    private bool isServer = false;

    private enum EState : uint
    {
        Uninitialized = 0,
        SentKey,
        RecvedKey,
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

            CheckStateForResend();
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

    public void CheckStateForResend()
    {
        lastTimestamp -= UnityEngine.Time.deltaTime;

        if (lastTimestamp <= 0.0f)
        {
            lastTimestamp = resendRequestInterval;
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
                SendAuthResult();
            }
            else
            {
                RequestResend();
            }
        }
    }

    public bool Setup(IAccelByteICEBase inIce, AccelByteAuthInterface inAuthInterface, bool inServer)
    {
        Report.GetFunctionLog(GetType().Name);

        ice = inIce;
        if (ice is null)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] Setup failed. authInterface is null.");
            goto fail;
        }

        authInterface = inAuthInterface;
        if (authInterface is null)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] Setup failed. authInterface is null.");
            goto fail;
        }

        if (authInterface?.IsActive() is false)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(inServer ? "DS" : "CL")}) Setup failed. authInterface is not activated.");
            goto fail;
        }

        if (SetComponentReady(inServer) is false)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(inServer ? "DS" : "CL")}) Setup failed.");
            goto fail;
        }

        active = inServer;
        isServer = inServer;

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
        string functionName = "NetCleanUp";
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
                AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] failed to generate AES Key.");
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
                AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] failed to generate RSA Key.");
                return false;
            }
        }
        return true;
    }

    public byte[] Incoming(byte[] packet)
    {
        if (IsActive() is false)
        {
            var header = ABNetUtilities.Deserialize<AuthHeader>(packet);
            AccelByteDebug.LogWarning($"AUTH HANDLER: [{(IsServer() ? "DS" : "CL")}] Incoming. ({header.Type}) authHandler is not active.");
            CompletedHandshaking(false);
            return packet;
        }

        if (state != EState.Initialized)
        {
            return IncomingHandshake(packet);
        }
        return packet;
    }

    public void NotifyHandshakeBegin()
    {
        active = true;
        SendKey();
    }

    private byte[] IncomingHandshake(byte[] packet)
    {
        if ((authInterface is null) || (authInterface.IsActive() is false))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: authInterface is not valid. maybe, the dedicated server is not started.");
            CompletedHandshaking(false);
            return null;
        }

        var header = ABNetUtilities.Deserialize<AuthHeader>(packet);

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
                        // request resending key to DS.
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
                    SendAuthResult();
                    return null;
                }
            default:
                {
                    return packet;
                }
        }
    }

    private void SendAuthResult()
    {
        if (IsServer())
        {
            if (authInterface != null)
            {
                switch (authInterface.GetAuthStatus(userId))
                {
                    case AccelByteAuthInterface.EAccelByteAuthStatus.AuthSuccess:
                        {
                            AuthenticateUserResult(true);
                            return;
                        }
                    case AccelByteAuthInterface.EAccelByteAuthStatus.AuthFail:
                    case AccelByteAuthInterface.EAccelByteAuthStatus.KickUser:
                    case AccelByteAuthInterface.EAccelByteAuthStatus.FailKick:
                        {
                            AuthenticateUserResult(false);
                            return;
                        }
                    default:
                        {
                            return;
                        }
                }
            }
            else
            {
                CompletedHandshaking(false);
            }
        }
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
                if ((state == EState.Uninitialized) || (state == EState.SentKey))
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
            OnIncomingBase?.Invoke();
            Completed();
        }
        else
        {
            state = EState.AuthFail;
        }
    }

    private void SendPublicKey()
    {
        RsaKeyData packetData = new RsaKeyData();

        packetData.Modulus = rsaCrypto.ExportPublicKeyModulus();
        packetData.Exponent = rsaCrypto.ExportPublicKeyExponent();

        if ((packetData.Modulus.Length == 0) || (packetData.Exponent.Length == 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RSA Key has something wrong. {packetData.Modulus.Length} {packetData.Exponent.Length}");
            CompletedHandshaking(false);
            return;
        }

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
        if ((data.Modulus.Length == 0) || (data.Exponent.Length == 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) RSA Key has something wrong. {data.Modulus.Length} {data.Exponent.Length}");
            CompletedHandshaking(false);
            return false;
        }

        if (rsaCrypto.ImportPublicKey(data.Modulus, data.Exponent, true))
        {
            state = EState.RecvedKey;
            return true;
        }
        return false;
    }

    private void SendKeyAES()
    {
        AesKeyData packetData = new AesKeyData();

        packetData.Key = aesCrypto.GetKeyBytes();
        packetData.IV = aesCrypto.GetIVBytes();

        if ((packetData.Key.Length == 0) || (packetData.IV.Length == 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) AES Key has something wrong. {packetData.Key.Length} {packetData.IV.Length}");
            CompletedHandshaking(false);
            return;
        }

        packetData.Key = EncryptRSA(aesCrypto.GetKeyBytes());
        if ((packetData.Key is null) || (packetData.Key.Length <= 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: RSA Encryption skipped as plain text size is too large. send smaller packets for secure data.");
            CompletedHandshaking(false);
            return;
        }

        packetData.IV = EncryptRSA(aesCrypto.GetIVBytes());
        if ((packetData.IV is null) || (packetData.IV.Length <= 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: RSA Encryption skipped as plain text size is too large. send smaller packets for secure data.");
            CompletedHandshaking(false);
            return;
        }

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
        if (data.Key.Length == 0 || data.IV.Length == 0)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) AES Key(en) has something wrong. {data.Key.Length} {data.IV.Length}");
            CompletedHandshaking(false);
            return false;
        }


        var Key = DecryptRSA(data.Key);
        var IV = DecryptRSA(data.IV);

        if ((Key.Length == 0) || (IV.Length == 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) AES Key has something wrong. {Key.Length} {IV.Length}");
            CompletedHandshaking(false);
            return false;
        }

        if (aesCrypto.ImportKey(Key, IV))
        {
            state = EState.RecvedKey;
            return true;
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

        userId = authInterface?.GetAuthData();

        if (string.IsNullOrEmpty(userId))
        {
            AccelByteDebug.LogWarning($"({(IsServer() ? ("DS") : ("CL"))}) AUTH HANDLER: user id is null or empty.");
            CompletedHandshaking(false);
            return;
        }

        AuthUserData packetData = new AuthUserData();

        var byUserId = ABCryptoUtilities.StringToBytes(userId);

        var cipherText = EncryptAES(byUserId, 256);
        packetData.UserIdSize = cipherText.Length;
        packetData.UserId = cipherText;

        if ((packetData.UserId is null) || (packetData.UserId.Length <= 0))
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: AES Encryption skipped as plain text size is too large. send smaller packets for secure data.");
            CompletedHandshaking(false);
            return;
        }

        byte[] outPacket = ABNetUtilities.Serialize<AuthUserData>(packetData);

        if (SendPacket(outPacket))
        {
            state = EState.SentAuth;
        }
    }

    private void RecvAuthData(byte[] packetData)
    {
        if ((authInterface is null) || (authInterface.IsActive() is false))
        {
            CompletedHandshaking(false);
            return;
        }

        var data = ABNetUtilities.Deserialize<AuthUserData>(packetData);

        byte[] originData = new byte[data.UserIdSize];

        Buffer.BlockCopy(data.UserId, 0, originData, 0, data.UserIdSize);

        var plainText = DecryptAES(originData, data.UserIdSize);
        userId = ABCryptoUtilities.BytesToString(plainText);

        if (authInterface.AuthenticateUser(userId))
        {
            state = EState.WaitForAuth;
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
            return true;
        }

        CompletedHandshaking(false);

        return false;
    }

    private void AuthenticateUserResult(bool Result)
    {
        if (OnSendAuthResult(Result))
        {
            CompletedHandshaking(Result);
        }

        if (!Result)
        {
            authInterface.MarkUserForKick(userId);
        }
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
            else if (EState.WaitForAuth != state)
            {
                return;
            }
            else
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendAuth;
            }
        }
        else
        {
            if (EState.SentKey == state)
            {
                packetData.Type = AccelByte.Models.EAccelByteAuthMsgType.ResendKey;
            }
            else
            {
                return;
            }
        }

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
        if (ice?.Send(outPacket) <= 0)
        {
            AccelByteDebug.LogWarning($"AUTH HANDLER: ({(IsServer() ? ("DS") : ("CL"))}) SendPacket: Failed. ({outPacket.Length})");
            return false;
        }
        lastTimestamp = resendRequestInterval;

        return true;
    }

    private byte[] EncryptRSA(byte[] inData)
    {
        return rsaCrypto?.Encrypt(inData);
    }

    private byte[] DecryptRSA(byte[] inData)
    {
        return rsaCrypto?.Decrypt(inData);
    }

    private byte[] EncryptAES(byte[] inData, int size)
    {
        return aesCrypto?.Encrypt(inData);
    }

    private byte[] DecryptAES(byte[] inData, int size)
    {
        return aesCrypto?.Decrypt(inData);
    }
}
