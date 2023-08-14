// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Collections.Generic;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Server;
using AccelByte.Models;
using Newtonsoft.Json.Linq;

public class AccelByteAuthInterface
{
    private const float updateInterval = 1.0f;
    private const float pendingKickTimeout = 15.0f;

    private Dictionary<string, AuthUser> authUsers = new Dictionary<string, AuthUser>();

    private float currentUpdateTime = updateInterval;

    private bool active { get; set; }

    private User user = null;
    private ServerUserAccount userAdmin = null;
    private DedicatedServer ds = null;

    private bool isServer = false;

    public Func<string, bool> OnContainSession { get; set; }

    // for Jwk Set JSON.
    private JwkSet jwkSet;
    private bool isRequestJwks = false;

    /**
     *  AccelByte AuthInterface.
     *
     *  For the most part, this is fully automated. You simply just need to add the auth handler and your server will now
     *  require AccelByte Authentication for any incoming users. If a player fails to respond correctly, they will be kicked.
     */
    public enum EAccelByteAuthStatus : uint {
        None = 0,
        AuthSuccess = (1 << 0),
        AuthFail = (1 << 1),
        ValidationStarted = (1 << 2),
        Timeout = (1 << 3),
        HasOrIsPendingAuth = AuthSuccess | ValidationStarted
    }

    public class AuthUser {
        public AuthUser(EAccelByteAuthStatus inStatus=EAccelByteAuthStatus.None, int inCode=0, string inMesg="")
        {
            Status = inStatus;
            ErrorCode = inCode;
            ErrorMessage = inMesg;
            PendingTimestamp = 0.0f;
        }

        public void SetStatus(EAccelByteAuthStatus inStatus)
        {
            Status = inStatus;
        }

        public void SetStatusAnd(EAccelByteAuthStatus inStatus)
        {
            Status &= inStatus;
        }

        public void SetStatusOr(EAccelByteAuthStatus inStatus)
        {
            Status |= inStatus;
        }

        public void SetFail(int inErrorCode, string inErrorMessage)
        {
            ErrorCode = inErrorCode;
            ErrorMessage = inErrorMessage;
        }

        public EAccelByteAuthStatus Status { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public float PendingTimestamp { get; set; }
    }

    public delegate void JwksEventDelegate(bool wasSuccessful, JwkSet jwkSet);
    public delegate void AuthEventDelegate(bool wasSuccessful, string userId);

    public event JwksEventDelegate OnJwksEvent;
    public event AuthEventDelegate OnAuthEvent;

    public void InvokeOnJwksEvent(bool wasSuccessful, JwkSet inJwkSet)
    {
        OnJwksEvent?.Invoke(wasSuccessful, inJwkSet);
    }

    protected void InvokeOnAuthEvent(bool wasSuccessful, string userId)
    {
        OnAuthEvent?.Invoke(wasSuccessful, userId);
    }

    public void Clear()
    {
        active = false;
        authUsers?.Clear();
    }

    public void Tick()
    {
        if (IsActive() is false)
        {
            return;
        }

        UpdateKickUser();
        UpdateJwks();
    }

    public void Initialize(ApiClient inApiClient, bool inServer)
    {
        if (AccelBytePlugin.Config.EnableAuthHandshake is false)
        {
            active = false;
            return;
        }

        if (IsActive())
        {
            AccelByteDebug.LogWarning($"AUTH: [{(inServer ? "DS" : "CL")}] AuthInterface was activated.");
            return;
        }

        isServer = inServer;

        if (IsServer())
        {
            OnJwksEvent = OnJwksCompleted();
            OnAuthEvent = OnAuthUserCompleted();

            userAdmin = AccelByteServerPlugin.GetUserAccount();
            ds = AccelByteServerPlugin.GetDedicatedServer();
        }
        else
        {
            user = inApiClient?.GetUser();
        }

        active = true;
    }

    public bool IsActive()
    {
        return active;
    }

    public string GetAuthData()
    {
        return user?.Session?.AuthorizationToken;
    }

    public bool AuthenticateUser(string inUserId)
    {
        if (IsServer() is false)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] this is not the dedicated server.");
            return false;
        }

        AuthUser targetUser = GetOrCreateUser(inUserId);
        if (targetUser is null)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] AuthenticateUser failed. user was not created.");
            return false;
        }

        targetUser.PendingTimestamp = UnityEngine.Time.realtimeSinceStartup;
        targetUser.SetStatusOr(EAccelByteAuthStatus.Timeout);

        if (IsInSessionUser(inUserId))
        {
            return CheckUserState(inUserId);
        }
        else
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] This user ({inUserId}) didn't join a session.");
            InvokeOnAuthEvent(false, inUserId);
        }
        return false;
    }

    private bool CheckUserState(string inUserId)
    {
        AuthUser targetUser = GetUser(inUserId);
        if (targetUser is null)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] AuthenticateUser failed. user was not created.");
            return false;
        }

        if (EnumHasAnyFlags(targetUser.Status, EAccelByteAuthStatus.HasOrIsPendingAuth))
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] The user ({inUserId}) has authenticated or is currently authenticating. Skipping reauth");
            return true;
        }

        // Create the user in the list if we don't already have them.
        if (EnumHasAnyFlags(targetUser.Status, EAccelByteAuthStatus.ValidationStarted) is false)
        {
            GetBanUser(inUserId);
            targetUser.SetStatusOr(EAccelByteAuthStatus.ValidationStarted);
        }

        return true;
    }

    public EAccelByteAuthStatus GetAuthStatus(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return EAccelByteAuthStatus.None;
        }

        AuthUser targetUser = GetUser(inUserId);
        if (targetUser != null)
        {
            return targetUser.Status;
        }

        return EAccelByteAuthStatus.AuthFail;
    }

    private void OnAuthSuccess(string inUserId)
    {
        if (IsServer() is false)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] this is not the dedicated server.");
            return;
        }

        AuthUser targetUser = GetUser(inUserId);
        if (targetUser is null)
        {
            // If we are missing an user here, this means that they were recently deleted or we never knew about them.
            return;
        }

        targetUser?.SetStatus(EAccelByteAuthStatus.AuthSuccess);
    }

    private void OnAuthFail(string inUserId, int inErrorCode, string inErrorMessage)
    {
        if (IsServer() is false)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] this is not the dedicated server.");
            return;
        }

        AuthUser targetUser = GetUser(inUserId);
        if (targetUser is null)
        {
            // If we are missing an user here, this means that they were recently deleted or we never knew about them.
            return;
        }

        // Remove the validation start flag
        targetUser.SetStatus(EAccelByteAuthStatus.AuthFail);
        targetUser.SetFail(inErrorCode, inErrorMessage);
    }

    AuthEventDelegate OnAuthUserCompleted()
    {
        return (bool wasSuccessful, string userId) =>
        {
            if (wasSuccessful)
            {
                OnAuthSuccess(userId);
            }
            else
            {
                OnAuthFail(userId, 0, "Baned User.");
            }
        };
    }

    private void GetBanUser(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] GetBanUser: user id is null or empty.");
            InvokeOnAuthEvent(false, inUserId);
            return;
        }

        if (userAdmin is null)
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] GetBanUser: userAdmin is null. ({inUserId})");
            InvokeOnAuthEvent(false, inUserId);
            return;
        }

        GetBanUserInfo(inUserId);
    }

    public void RemoveUser(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return;
        }

        authUsers.Remove(inUserId);
    }

    private AuthUser GetUser(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return null;
        }

        if (authUsers.ContainsKey(inUserId))
        {
            return authUsers[inUserId];
        }

        return null;
    }

    private AuthUser GetOrCreateUser(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return null;
        }

        AuthUser targetUser = GetUser(inUserId);

        if (targetUser is null)
        {
            if (IsServer() is false)
            {
                return null;
            }

            AuthUser newUser = new AuthUser();
            authUsers.Add(inUserId, newUser);
            return newUser;
        }
        return targetUser;
    }

    private bool IsInSessionUser(string inUserId)
    {
        if(OnContainSession != null)
        {
            return OnContainSession.Invoke(inUserId);
        }
        return false;
    }

    public bool UpdateJwks()
    {
        if ((jwkSet != null) && (jwkSet.keys.Count > 0))
        {
            return true;
        }

        if (isRequestJwks is false)
        {
            if (ds is null)
            {
                return true;
            }

            isRequestJwks = true;
            GetJwks();
        }

        return false;
    }

    public bool VerifyAuthToken(string inAuthToken, out string inUserId)
    {
        Jwt jwt = new Jwt(inAuthToken);

        // Verify the JWT with the RSA public key
        foreach (var key in jwkSet.keys)
        {
            RsaPublicKey publicKey = new RsaPublicKey(key.GetValue("n").ToString(), key.GetValue("e").ToString());
            EJwtResult verifyResult = jwt.VerifyWith(publicKey);
            if (verifyResult == EJwtResult.Ok)
            {
                // Extract the userId from the payload
                JObject payload = jwt.Payload();
                if (payload != null)
                {
                    if (payload.TryGetValue("sub", out JToken userIdToken))
                    {
                        // userId successfully extracted from the payload
                        inUserId = userIdToken.Value<string>();
                        return true;
                    }
                    else
                    {
                        // Handle invalid userId
                        AccelByteDebug.LogWarning($"AUTH: The field name is not 'sub' for userId.");
                    }
                }
                else
                {
                    // Handle invalid Payload
                    AccelByteDebug.LogWarning($"AUTH: Payload is invalid.");
                }
            }
            else
            {
                // Handle invalid VerifyResult
                AccelByteDebug.LogWarning($"AUTH: Fail to verify result({verifyResult}).");
            }
        }

        inUserId = string.Empty;
        return false;
    }

    private void UpdateKickUser()
    {
        if (authUsers?.Count == 0)
        {
            return;
        }

        if (IsServer() is false)
        {
            return;
        }

        currentUpdateTime -= UnityEngine.Time.deltaTime;

        if (currentUpdateTime <= 0.0f)
        {
            currentUpdateTime = updateInterval;
            foreach (var user in authUsers)
            {
                if (EnumHasAnyFlags(user.Value.Status, EAccelByteAuthStatus.Timeout))
                {
                    if (IsInSessionUser(user.Key))
                    {
                        float currentTime = UnityEngine.Time.realtimeSinceStartup;
                        if ((currentTime - user.Value.PendingTimestamp) >= pendingKickTimeout)
                        {
                            user.Value.PendingTimestamp = 0.0f;
                            OnAuthFail(user.Key, 0, "timeout");
                        }
                        else if (!CheckUserState(user.Key))
                        {
                            OnAuthFail(user.Key, 0, "failed");
                        }
                    }
                    else
                    {
                        OnAuthFail(user.Key, 0, "not in session");
                    }
                }
            }
        }
    }

    JwksEventDelegate OnJwksCompleted()
    {
        return (bool wasSuccessful, JwkSet inJwkSet) =>
        {

            if (wasSuccessful)
            {
                jwkSet = inJwkSet;
            }
            else
            {
                jwkSet = null;
            }
        };
    }

    private void GetJwks()
    {
        // Create the JWKS in the list if we don't already have them.
        ds.GetJwks(result => {
            JwkSet inJwkSet = null;
            bool wasSuccessful = !result.IsError;
            if (wasSuccessful)
            {
                inJwkSet = result.Value;
            }

            isRequestJwks = false;
            InvokeOnJwksEvent(wasSuccessful, inJwkSet);
        });
    }

    private void GetBanUserInfo(string inUserId)
    {
        userAdmin.GetUserBanInfo(inUserId, result =>
        {
            bool wasSuccessful = false;
            if (!result.IsError && (0 == result.Value.data.Length))
            {
                wasSuccessful = true;
            }
            InvokeOnAuthEvent(wasSuccessful, inUserId);
        });
    }

    private static bool EnumHasAnyFlags<T>(T value, T flags) where T : Enum
    {
        uint valueUInt = Convert.ToUInt32(value);
        uint flagsUInt = Convert.ToUInt32(flags);

        return (valueUInt & flagsUInt) != 0;
    }

    public bool IsServer()
	{
        return isServer;
    }
}
