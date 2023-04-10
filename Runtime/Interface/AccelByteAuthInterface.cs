// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System.Collections.Generic;
using AccelByte.Api;
using AccelByte.Core;
using AccelByte.Server;

public class AccelByteAuthInterface
{
    private const float kickInterval = 1.0f;

    private Dictionary<string, AuthUser> authUsers = new Dictionary<string, AuthUser>();

    private float currentKickUserUpdateTime = kickInterval;

    private bool active { get; set; }

    /** API client that should be used for this task, use GetApiClient to get a valid instance */
    private ApiClient apiClient = null;
    private User user = null;
    private ServerUserAccount userAdmin = null;

    private bool isServer = false;

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
        KickUser = (1 << 3),
        FailKick = AuthFail | KickUser,
        HasOrIsPendingAuth = AuthSuccess | ValidationStarted
    }

    public class AuthUser {
        public AuthUser(EAccelByteAuthStatus inStatus=EAccelByteAuthStatus.None, int inCode=0, string inMesg="")
        {
            Status = inStatus;
            ErrorCode = inCode;
            ErrorMessage = inMesg;
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
    }

    public delegate void AuthEventDelegate(bool wasSuccessful, string userId);

    public event AuthEventDelegate OnAuthEvent;

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
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] AuthInterface was activated.");
            return;
        }

        if (inApiClient is null)
        {
            return;
        }

        apiClient = inApiClient;
        user = apiClient?.GetUser();

        isServer = inServer;

        if (IsServer())
        {
            OnAuthEvent = OnAuthUserCompleted();
            userAdmin = AccelByteServerPlugin.GetUserAccount();
        }

        active = true;
    }

    public bool IsActive()
    {
        return active;
    }

    public string GetAuthData()
    {
        return user?.Session?.UserId;
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

        if (targetUser.Status.HasFlag(EAccelByteAuthStatus.HasOrIsPendingAuth))
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] The user ({inUserId}) has authenticated or is currently authenticating. Skipping reauth");
            return true;
        }

        if (targetUser.Status.HasFlag(EAccelByteAuthStatus.FailKick))
        {
            AccelByteDebug.LogWarning($"AUTH: [{(IsServer() ? "DS" : "CL")}] If the user ({inUserId}) has already failed auth, do not attempt to re-auth them.");
            return false;
        }

        // Create the user in the list if we don't already have them.
        BanUser(inUserId);
        targetUser.SetStatusOr(EAccelByteAuthStatus.ValidationStarted);

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

    public void MarkUserForKick(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return;
        }

        AuthUser targetUser = GetUser(inUserId);

        targetUser?.SetStatusOr(EAccelByteAuthStatus.AuthFail);
    }

    public bool KickUser(string inUserId, bool suppressFailure)
    {
        if (IsServer() is false)
        {
            return false;
        }

        bool kickSuccess = false;
        AuthUser targetUser = GetUser(inUserId);
        if (targetUser is null)
        {
            // If we are missing an user here, this means that they were recently deleted or we never knew about them.
            return false;
        }

        // If we were able to kick them properly, call to remove their data.
        // Otherwise, they'll be attempted to be kicked again later.
        if (kickSuccess)
        {
            RemoveUser(inUserId);
        }

        return kickSuccess;
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

        targetUser?.SetStatusAnd(~EAccelByteAuthStatus.ValidationStarted);
        targetUser?.SetStatusOr(EAccelByteAuthStatus.AuthSuccess);
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
        targetUser.SetStatusAnd(~EAccelByteAuthStatus.ValidationStarted);
        targetUser.SetStatusOr(EAccelByteAuthStatus.AuthFail);

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

    public void BanUser(string inUserId)
    {
        if (string.IsNullOrEmpty(inUserId))
        {
            return;
        }

        if (userAdmin is null)
        {
            return;
        }

        userAdmin.GetUserBanInfo(inUserId, result => {
            bool wasSuccessful = false;
            if (result.IsError is false)
            {
                if (0 == result.Value.data.Length)
                {
                    wasSuccessful = true;
                }
            }
            InvokeOnAuthEvent(wasSuccessful, inUserId);
        });
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

        currentKickUserUpdateTime -= UnityEngine.Time.deltaTime;

        if (currentKickUserUpdateTime <= 0.0f)
        {
            currentKickUserUpdateTime = kickInterval;
            foreach (var user in authUsers)
            {
                if (user.Value.Status.HasFlag(EAccelByteAuthStatus.FailKick))
                {
                    if (KickUser(user.Key, user.Value.Status.HasFlag(EAccelByteAuthStatus.KickUser)))
                    {
                        // If we've modified the list, we can just end this frame.
                        return;
                    }
                    user.Value.SetStatusOr(EAccelByteAuthStatus.KickUser);
                }
            }
        }
    }

    public bool IsServer()
	{
        return isServer;
    }
}
