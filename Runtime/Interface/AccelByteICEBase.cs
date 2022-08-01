using System;
using Unity.WebRTC;
using System.Runtime.Serialization;
using Newtonsoft.Json;

public interface IAccelByteICEBase
{
	public void SetPeerID(string peerID);

	/// <summary>
	/// Process signaling message
	/// </summary>
	/// <param name="message">Message from signaling service</param>
	public void OnSignalingMessage(string message);

	/// <summary>
	/// Request connect to <PeerId>
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
	public void Send(byte[] data);

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

[DataContract]
public class AccelByteSignalingRequest
{
	[DataMember] public EAccelByteSignalingMessageType Type { get; set; }
	[DataMember] public string Host { get; set; }
	[DataMember] public string Username { get; set; }
	[DataMember] public string Password { get; set; }
	[DataMember] public int Port { get; set; }
	[DataMember] public EAccelByteSignalingServerType Server_Type { get; set; }//underscore required to follow the 
	[DataMember] public string Description { get; set; }
}

[DataContract]
public enum EAccelByteSignalingMessageType
{
	[EnumMember] ERROR = 0,
	[EnumMember] ICE = 1,
	[EnumMember] SDP = 2,
	[EnumMember] CANDIDATE = 3
}

[DataContract]
public enum EAccelByteSignalingServerType
{
	[EnumMember] ERROR = 0,
	[EnumMember] OFFER = 1,
	[EnumMember] ANSWER = 2,
	[EnumMember] ON_ICE_CANDIDATE = 3
}

public static class AccelByteICEUtility
{
	public static EAccelByteSignalingMessageType GetSignalingMessageTypeFromMessage(string message)
	{
		byte[] decodedMessage = Convert.FromBase64String(message);
		string decodedMessageString = System.Text.Encoding.UTF8.GetString(decodedMessage);
		var result = JsonConvert.DeserializeObject<AccelByteSignalingRequest>(decodedMessageString);

		return result.Type;
	}

	public static EAccelByteSignalingServerType GetSignalingServerTypeFromMessage(string message)
	{
		byte[] decodedMessage = Convert.FromBase64String(message);
		string decodedMessageString = System.Text.Encoding.UTF8.GetString(decodedMessage);
		var result = JsonConvert.DeserializeObject<AccelByteSignalingRequest>(decodedMessageString);

		return result.Server_Type;
	}

	/// <summary>
	/// Called when there is an incoming Offer.
	/// Parse the content to get the server info (host, username, password, port).
	/// Then the info will be used to create peer connection
	/// </summary>
	/// <param name="message"></param>
	/// <returns></returns>
	public static AccelByteSignalingRequest SignalingRequestFromString(string message)
	{
		byte[] decodedMessage = Convert.FromBase64String(message);
		string decodedMessageString = System.Text.Encoding.UTF8.GetString(decodedMessage);

		var result = JsonConvert.DeserializeObject<AccelByteSignalingRequest>(decodedMessageString);
		return result;
	}

	public static string SignalingRequestToString(AccelByteSignalingRequest request)
	{
		var serialized = JsonConvert.SerializeObject(request);
		var encodedJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serialized));

		return encodedJson;
	}
}