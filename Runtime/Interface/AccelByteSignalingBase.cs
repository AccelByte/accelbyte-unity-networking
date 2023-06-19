// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;

public struct WebRTCSignalingMessage
{
	public string PeerID;
	public string Message;

	public override string ToString()
	{
		byte[] decodedMessage = Convert.FromBase64String(Message);
		string decodedMessageString = System.Text.Encoding.UTF8.GetString(decodedMessage);

		return $"Signaling from: {PeerID}, Message: {decodedMessageString}";
	}
}

public interface IAccelByteSignalingBase
{
	public Action<WebRTCSignalingMessage> OnWebRTCSignalingMessage { get; set; }
	public void Init();
	public bool IsConnected();
	public void Connect();
	public void SendMessage(string PeerID, string Message);
}
