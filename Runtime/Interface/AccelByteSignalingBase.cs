using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public struct WebRTCSignalingMessage
{
	public string PeerID;
	public string Message;
}

public interface IAccelByteSignalingBase
{
	public Action<WebRTCSignalingMessage> OnWebRTCSignalingMessage { get; set; }
	public void Init();
	public bool IsConnected();
	public void Connect();
	public void SendMessage(string PeerID, string Message);
}
