using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AccelByteNullICE : MonoBehaviour, IAccelByteICEBase
{
	public IAccelByteSignalingBase Signaling { get; set; }
	public bool IsInitiator { get; set; }
	public bool IsConnected { get; set; }
	public string PeerID { get; set; }
	public Action<string> OnICEDataChannelConnected { get; set; }
	public Action<string> OnICEDataChannelConnectionError { get; set; }
	public Action<string> OnICEDataChannelClosed { get; set; }
	public Action<string /*RemotePeerID*/, byte[] /*Data*/> OnICEDataIncoming { get; set; }

	public void ClosePeerConnection()
	{
		throw new NotImplementedException();
	}

	public bool IsPeerReady()
	{
		throw new NotImplementedException();
	}

	public void JsonToString(ref string output, string jsonObject)
	{
		throw new NotImplementedException();
	}

	public void OnSignalingMessage(string message)
	{
		throw new NotImplementedException();
	}

	public bool RequestConnect(string serverURL, int serverPort, string username, string password)
	{
		throw new NotImplementedException();
	}

	public void Send(byte[] data)
	{
		throw new NotImplementedException();
	}

	public void SetPeerID(string peerID)
	{
		throw new NotImplementedException();
	}

	public void SetSignaling(IAccelByteSignalingBase signaling)
	{
		throw new NotImplementedException();
	}

	// Start is called before the first frame update
	void Start()
	{
		
	}

	// Update is called once per frame
	void Update()
	{
		
	}
}
