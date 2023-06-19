// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;

public interface IAccelByteICEBase
{
	public void SetPeerID(string peerID);

	/// <summary>
	/// Process signaling message
	/// </summary>
	/// <param name="message">Message from signaling service</param>
	public void OnSignalingMessage(string message);

	/// <summary>
	/// Request connect to peer
	/// </summary>
	/// <param name="serverURL"></param>
	/// <param name="serverPort"></param>
	/// <param name="username"></param>
	/// <param name="password"></param>
	/// <returns>Is success</returns>
	public bool RequestConnect(string serverURL, int serverPort, string username, string password);

	/// <summary>
	/// Send data to connected peer data channel
	/// </summary>
	/// <param name="data">Data to sent</param>
	/// <returns>Is success</returns>
	public int Send(byte[] data);

	/// <summary>
	/// Disconnect peer connection
	/// </summary>
	public void ClosePeerConnection();

	/// <summary>
	/// Check if peer instance requirement met
	/// </summary>
	/// <returns>Is requirement met</returns>
	public bool IsPeerReady();


	IAccelByteSignalingBase Signaling { get; set; }
	bool IsInitiator { get; set; }
	bool IsConnected { get; set; }
	string PeerID { get; set; }

	public Action<string /*Remote peer ID*/> OnICEDataChannelConnected { get; set; }
	public Action<string /*Error message*/> OnICEDataChannelConnectionError { get; set; }
	public Action<string /*Remote peer ID*/> OnICEDataChannelClosed { get; set; }
	public Action<string /*RemotePeerID*/, byte[] /*Data*/> OnICEDataIncoming { get; set; }
};
