// Copyright (c) 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using AccelByte.Core;

namespace AccelByte.Networking
{
    public enum JuiceLogLevel
    {
        Verbose = 0,
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    public enum JuiceState
    {
        Disconnected = 0,
        Gathering,
        Connecting,
        Connected,
        Completed,
        Failed
    }

    #region Unmanaged Function Pointer

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JuiceLogCallBackDelegate(JuiceLogLevel logLevel, string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JuiceStateChangedDelegate(int id, JuiceState state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JuiceCandidateFoundDelegate(int id, string sdp, bool isSuccess);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JuiceGatheringDoneDelegate(int id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JuiceDataReceivedDelegate(int id, IntPtr dataPtr, int size);

    #endregion

    public sealed class AccelByteLibJuiceAgent : IDisposable
    {
        #region Public Methods and Members

        public static readonly JuiceLogLevel LogLevel = JuiceLogLevel.Fatal;

        public AccelByteLibJuiceAgent(AccelByteJuice juice, string url, string username, string password, ushort port)
        {
            identifier = NextKey;
            NextKey = identifier + 1;

            juiceWrapper = CreateJuiceWrapper(identifier, url, username, password, port);
            Initialize();

            LibJuiceDictionary.Add(identifier, juice);
        }

        /// <summary>
        /// Get local description of LibJuice agent, this description is used for peer
        /// </summary>
        /// <param name="excludeCandidates">Remove candidates entry from sdp, needed to force using relay</param>
        /// <returns></returns>
        public string GetLocalDescription(bool excludeCandidates = false)
        {
            lock (juiceAgentLock)
            {
                var strPtr = GetJuiceLocalDescription(juiceWrapper);

                string localDescription = Marshal.PtrToStringAnsi(strPtr);
                JuiceFreeAllocatedString(strPtr);

                if (localDescription != null && excludeCandidates)
                {
                    var rgx = new Regex("a=candidate.*$\n", RegexOptions.Multiline);
                    localDescription = rgx.Replace(localDescription, "");
                }

                return localDescription;
            }
        }

        public bool SetRemoteDescription(string sdp)
        {
            lock (juiceAgentLock)
            {
                return SetJuiceRemoteDescription(juiceWrapper, sdp);
            }
        }

        public bool AddRemoteCandidate(string sdp)
        {
            lock (juiceAgentLock)
            {
                return JuiceAddRemoteCandidate(juiceWrapper, sdp);
            }
        }

        public bool GatherCandidates()
        {
            lock (juiceAgentLock)
            {
                return JuiceGatherCandidates(juiceWrapper);
            }
        }

        public bool SetRemoteGatheringDone()
        {
            lock (juiceAgentLock)
            {
                return JuiceSetRemoteGatheringDone(juiceWrapper);
            }
        }

        public bool SendData(byte[] data)
        {
            lock (juiceAgentLock)
            {
                return JuiceSendData(juiceWrapper, data, data.Length);
            }
        }

        public string GetSelectedLocalCandidates()
        {
            lock (juiceAgentLock)
            {
                var strPtr = GetJuiceSelectedLocalCandidates(juiceWrapper);
                string localCandidates = Marshal.PtrToStringAnsi(strPtr);
                JuiceFreeAllocatedString(strPtr);
                return localCandidates;
            }
        }

        public string GetSelectedRemoteCandidates()
        {
            lock (juiceAgentLock)
            {
                var strPtr = GetJuiceSelectedRemoteCandidates(juiceWrapper);
                string localCandidates = Marshal.PtrToStringAnsi(strPtr);
                JuiceFreeAllocatedString(strPtr);
                return localCandidates;
            }
        }

        public string GetSelectedLocalAddresses()
        {
            lock (juiceAgentLock)
            {
                var strPtr = GetJuiceSelectedLocalAddresses(juiceWrapper);
                string localCandidates = Marshal.PtrToStringAnsi(strPtr);
                JuiceFreeAllocatedString(strPtr);
                return localCandidates;
            }
        }

        public string GetSelectedRemoteAddresses()
        {
            lock (juiceAgentLock)
            {
                var strPtr = GetJuiceSelectedRemoteAddresses(juiceWrapper);
                string localCandidates = Marshal.PtrToStringAnsi(strPtr);
                JuiceFreeAllocatedString(strPtr);
                return localCandidates;
            }
        }

        public JuiceState GetState()
        {
            lock (juiceAgentLock)
            {
                return GetJuiceState(juiceWrapper);
            }
        }

        #endregion

        #region Private Methods

        private void Initialize()
        {
            lock (juiceAgentLock)
            {
                SetJuiceLogLevel(LogLevel);
                JuiceLogCallBackDelegate logCallBackDelegate = JuiceLogHandler;
                SetJuiceLogHandler(Marshal.GetFunctionPointerForDelegate(logCallBackDelegate));

                JuiceStateChangedDelegate stateChangedDelegate = JuiceStateChangedHandler;
                SetJuiceStateChangedHandler(juiceWrapper, Marshal.GetFunctionPointerForDelegate(stateChangedDelegate));

                JuiceCandidateFoundDelegate candidateFoundDelegate = JuiceCandidateFoundHandler;
                SetJuiceCandidateFoundHandler(juiceWrapper, Marshal.GetFunctionPointerForDelegate(candidateFoundDelegate));

                JuiceGatheringDoneDelegate gatheringDoneDelegate = JuiceGatheringDoneHandler;
                SetJuiceGatheringDoneHandler(juiceWrapper, Marshal.GetFunctionPointerForDelegate(gatheringDoneDelegate));

                JuiceDataReceivedDelegate dataReceivedDelegate = JuiceDataReceivedHandler;
                SetJuiceDataReceivedHandler(juiceWrapper, Marshal.GetFunctionPointerForDelegate(dataReceivedDelegate));

                InitializeJuiceWrapper(juiceWrapper);
            }
        }

        #endregion

        #region Private Members

        private const string AccelByteJuiceDllName = "AccelByteLibjuiceWrapper.dll";

        private readonly IntPtr juiceWrapper;

        private readonly object juiceAgentLock = new object();

        private readonly int identifier;

        #endregion

        // static handler can't be avoided since only static method that can be called from unmanaged code
        #region StaticHandler

        private static int NextKey = 0;

        private static readonly IDictionary<int, AccelByteJuice> LibJuiceDictionary = new Dictionary<int, AccelByteJuice>();

        [AOT.MonoPInvokeCallback(typeof(JuiceStateChangedDelegate))]
        private static void JuiceStateChangedHandler(int id, JuiceState state)
        {
            if (LibJuiceDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new JuiceStateChangedTask(juiceIce, state));
            }
        }

        [AOT.MonoPInvokeCallback(typeof(JuiceCandidateFoundDelegate))]
        private static void JuiceCandidateFoundHandler(int id, string sdp, bool isSuccess)
        {
            if (LibJuiceDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new JuiceCandidateFoundTask(juiceIce, sdp, isSuccess));
            }
        }

        [AOT.MonoPInvokeCallback(typeof(JuiceGatheringDoneDelegate))]
        private static void JuiceGatheringDoneHandler(int id)
        {
            if (LibJuiceDictionary.TryGetValue(id, out var juiceIce))
            {
                AccelByteJuice.JuiceTasks.Enqueue(new JuiceGatheringDoneTask(juiceIce));
            }
        }

        [AOT.MonoPInvokeCallback(typeof(JuiceDataReceivedDelegate))]
        private static void JuiceDataReceivedHandler(int id, IntPtr dataPtr, int size)
        {
            if (LibJuiceDictionary.TryGetValue(id, out var juiceIce))
            {
                byte[] data = new byte[size];
                Marshal.Copy(dataPtr, data, 0, size);

                AccelByteJuice.JuiceTasks.Enqueue(new JuiceDataReceivedTask(juiceIce, data, size));
            }
        }

        [AOT.MonoPInvokeCallback(typeof(JuiceLogCallBackDelegate))]
        private static void JuiceLogHandler(JuiceLogLevel logLevel, string message)
        {
            AccelByteJuice.JuiceTasks.Enqueue(new JuiceLogTask(logLevel, message));
        }

        #endregion

        #region Function from DLL

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateJuiceWrapper(int id, string host, string username, string password, ushort port);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeleteJuiceWrapper(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetJuiceLogLevel(JuiceLogLevel logLevel);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetJuiceLogHandler(IntPtr handler);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetJuiceStateChangedHandler(IntPtr juiceWrapper, IntPtr handler);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetJuiceCandidateFoundHandler(IntPtr juiceWrapper, IntPtr handler);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetJuiceGatheringDoneHandler(IntPtr juiceWrapper, IntPtr handler);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetJuiceDataReceivedHandler(IntPtr juiceWrapper, IntPtr handler);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void InitializeJuiceWrapper(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetJuiceLocalDescription(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SetJuiceRemoteDescription(IntPtr juiceWrapper, string sdp);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool JuiceAddRemoteCandidate(IntPtr juiceWrapper, string sdp);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool JuiceGatherCandidates(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool JuiceSetRemoteGatheringDone(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool JuiceSendData(IntPtr juiceWrapper, byte[] data, long size);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetJuiceSelectedLocalCandidates(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetJuiceSelectedRemoteCandidates(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetJuiceSelectedLocalAddresses(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetJuiceSelectedRemoteAddresses(IntPtr juiceWrapper);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void JuiceFreeAllocatedString(IntPtr pointer);

        [DllImport(AccelByteJuiceDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern JuiceState GetJuiceState(IntPtr juicewrapper);

        #endregion

        #region IDisposable Implementation

        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool isDisposing)
        {
            if (disposed)
            {
                return;
            }

            if (isDisposing)
            {
                // clean up managed resource if there is any in the future
            }

            lock (juiceAgentLock)
            {
                // clean up un-managed resource from C++ Dll
                LibJuiceDictionary.Remove(identifier);
                DeleteJuiceWrapper(juiceWrapper);
            }

            disposed = true;
        }

        ~AccelByteLibJuiceAgent()
        {
            Dispose(false);
        }

        #endregion
    }
}
