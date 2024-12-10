// Copyright (c) 2024 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

var AccelByteRTCPeerConnection = {

    $AccelByteRTCPeerInstance: [],

    $CurrentLogHandler: {
        currentLogLevel: null,
        logHandler: null,
    },

    $LogLevel: {
        Error: 0,
        Assert: 1,
        Warning: 2,
        Log: 3,
        Verbose: 4,
        Exception: 5
    },

    //#region Active Interop Functions
    InitRTCPeerConnection : function (identifier, logLevel, turnIp, turnUsername, turnPassword, turnPort) 
    {
        let peerInstance = {
            rtcPeer: null,
            channelId: 0,
            //callback handler
            stateChangedHandler: null,
            candidateFoundHandler: null,
            gatheringDoneHandler: null,
            dataReceivedHandler: null,
            dataChannel: null,
            //utils
            remoteDescriptionSet: false,
            collectedRemoteCandidates: [],
            gatherCandidateDone: false,
            collectedGatheredCandidates: [],
            isCreatingAnswer: false,
            gatherCandidateComplete: false,
            isProcessingCandidates: false,
            isProcessingRemoteCandidates: false,
            isIceGatheringDone: false,
            iceGatheringTimeoutInMs: 15000
        };
        peerInstance.channelId = identifier;

        const turnIpStr = UTF8ToString(turnIp);
        const turnUsernameStr = UTF8ToString(turnUsername);
        const turnPasswordStr = UTF8ToString(turnPassword);

        const peer = new RTCPeerConnection({
            iceServers: [
                {
                    urls: 'turn:' + turnIpStr + ':' + turnPort,
                    username: turnUsernameStr,
                    credential: turnPasswordStr
                }
                ,{
                    urls: 'stun:' + turnIpStr + ':' + turnPort,
                    username: turnUsernameStr,
                    credential: turnPasswordStr
                }
            ]
        });

        peerInstance.rtcPeer = peer;
        AccelByteRTCPeerInstance[identifier] = peerInstance;

        if (CurrentLogHandler.currentLogLevel === null) 
        {
            let logLevelStr = UTF8ToString(logLevel).toLowerCase();
            let tempLogLevel = LogLevel.Error;
            switch (logLevelStr) {
                case "exception":
                    tempLogLevel = LogLevel.Exception;
                    break;
                case "verbose":
                    tempLogLevel = LogLevel.Verbose;
                    break;
                case "log":
                    tempLogLevel = LogLevel.Log;
                    break;
                case "warning":
                    tempLogLevel = LogLevel.Warning;
                    break;
                case "assert":
                    tempLogLevel = LogLevel.Assert;
                    break;
                case "error":
                default:
                    tempLogLevel = LogLevel.Error;
                    break;
            };
            CurrentLogHandler.currentLogLevel = tempLogLevel;
        }

        var iceGatherTimeout;
        var iceGatherTimeoutSet = false;

        peerInstance.rtcPeer.onicecandidate = (event) => 
        {
            if (iceGatherTimeoutSet === false) 
            {
                iceGatherTimeout = setTimeout(() => {peerInstance.isIceGatheringDone = true}, peerInstance.iceGatheringTimeoutInMs);
                iceGatherTimeoutSet = true;
            }

            if (event.candidate) 
            {
                const candidateStr = JSON.stringify(event.candidate);
                RTCPrintLogV2(identifier, LogLevel.Log, `candidate found`);
                RTCPrintLogV2(identifier, LogLevel.Verbose, `candidate: ${candidateStr}`);
                peerInstance.collectedGatheredCandidates.push(candidateStr);
            }
            else 
            {
                RTCPrintLogV2(identifier, LogLevel.Log, "Gathering is done from listener");
                peerInstance.isIceGatheringDone = true;
            }
        };

        peerInstance.rtcPeer.onconnectionstatechange = (ev) => {
            switch (peerInstance.rtcPeer.connectionState) 
            {
                case 'new':
                case 'connecting':
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State ${peerInstance.rtcPeer.connectionState}`);
                    break;
                case 'connected':
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State ${peerInstance.rtcPeer.connectionState}`);
                    RTCChangeState(identifier, "CONNECTED");

                    setTimeout(() => {
                        RTCPrintLogV2(identifier, LogLevel.Log, `wait 500ms before completed`);
                    }, 500);  // Adjust this timeout based on your processing needs
                    RTCChangeState(identifier, "COMPLETED");
                    break;
                case 'disconnected':
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State ${peerInstance.rtcPeer.connectionState}`);
                    RemovePeerInstance(identifier);
                    break;
                case 'failed':
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State ${peerInstance.rtcPeer.connectionState}`);
                    RTCChangeState(identifier, "FAILED");
                    RemovePeerInstance(identifier);
                    break;
                case 'closed':
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State ${peerInstance.rtcPeer.connectionState}`);
                    RemovePeerInstance(identifier);
                    break;
                default:
                    RTCPrintLogV2(identifier, LogLevel.Log, `Connection State Unknown ${peerInstance.rtcPeer.connectionState}`);
                    break;
            }
        };

        peerInstance.rtcPeer.ondatachannel = (event) => 
        {
            const receivedDataChannel = event.channel;
            receivedDataChannel.onopen = () => 
            { 
                RTCPrintLogV2(identifier, LogLevel.Log, "DataChannel is open"); 
            };
            receivedDataChannel.onmessage = (event) => 
            { 
                RTCPrintLogV2(identifier, LogLevel.Verbose, "Message from DataChannel");
                RTCTriggerReceivedMessage(identifier, event.data);
            };

            AccelByteRTCPeerInstance[identifier].dataChannel = receivedDataChannel;
        };

        return peerInstance;
    },

    RTCRemovePeer : function(id)
    {
        RemovePeerInstance(id);
    },

    RTCGetLocalDescriptionCallback : async function(id, callback)
    {
        RTCPrintLogV2(id, LogLevel.Log, `GET Local Description Callback id: ${id}`);

        if (AccelByteRTCPeerInstance[id].isCreatingAnswer)
        {
            RTCPrintLogV2(id, LogLevel.Log, `Get existing local description`);

            while (AccelByteRTCPeerInstance[id].rtcPeer.localDescription === null 
                || AccelByteRTCPeerInstance[id].rtcPeer.localDescription === undefined) 
            {
                RTCPrintLogV2(id, LogLevel.Verbose, `Wait until the local description is set`);
                await new Promise(resolve => setTimeout(resolve, 100));
            }

            const description = AccelByteRTCPeerInstance[id].rtcPeer.localDescription;
            RTCPrintLogV2(id, LogLevel.Verbose, `description='${description.sdp}' type=${description.type}`);

            const offerStr = JSON.stringify(description);
            const buffer = stringToNewUTF8(offerStr);
            {{{ makeDynCall('vi', 'callback') }}} (buffer);
            _free(buffer);
            _free(offerStr);
        }
        else 
        {
            const channel = AccelByteRTCPeerInstance[id].rtcPeer.createDataChannel("datachannel");
            channel.onopen = (event) => 
            {
                RTCPrintLogV2(id, LogLevel.Log, "Data channel is open");
            };
            channel.onmessage = (event) => 
            {
                RTCPrintLogV2(id, LogLevel.Verbose, "Message from DataChannel");
                RTCTriggerReceivedMessage(id, event.data);
            };
            AccelByteRTCPeerInstance[id].dataChannel = channel;

            AccelByteRTCPeerInstance[id].rtcPeer.createOffer()
            .then(async (offer) => 
            {        
                await AccelByteRTCPeerInstance[id].rtcPeer.setLocalDescription(offer);
                RTCPrintLogV2(id, LogLevel.Log, `Create a new OFFER`);
                RTCPrintLogV2(id, LogLevel.Verbose, `description=${offer.sdp} type=${offer.type}`);

                const offerStr = JSON.stringify(offer);
                const buffer = stringToNewUTF8(offerStr);
                {{{ makeDynCall('vi', 'callback') }}} (buffer);
                _free(buffer);
                _free(offerStr);
            });
        }
    },

    RTCSetRemoteDescriptionCallback : function(id, offer, callback)
    {
        const offerStr = UTF8ToString(offer);
        const offerParsed = JSON.parse(offerStr); 

        RTCPrintLogV2(id, LogLevel.Log, `set Remote description id}`);
        RTCPrintLogV2(id, LogLevel.Verbose, `desc='${offerParsed.sdp}' type='${offerParsed.type}'`);
        const desc = new RTCSessionDescription({
            type: offerParsed.type,
            sdp: offerParsed.sdp
        });

        if (offerParsed.type !== `answer`) 
        {
            AccelByteRTCPeerInstance[id].rtcPeer.setRemoteDescription(desc)
            .then(() => 
            {
                AccelByteRTCPeerInstance[id].isCreatingAnswer = true;
                AccelByteRTCPeerInstance[id].rtcPeer.createAnswer();
            })
            .then((answer) => 
            {
                RTCPrintLogV2(id, LogLevel.Log, `Local Description is set with an answer`);
                AccelByteRTCPeerInstance[id].rtcPeer.setLocalDescription(answer);
            })
            .then(() => 
            {
                AccelByteRTCPeerInstance[id].collectedRemoteCandidates = [];
                AccelByteRTCPeerInstance[id].remoteDescriptionSet = true;
                {{{ makeDynCall('vi', 'callback') }}} (0);
            });
        }
        else
        {
            AccelByteRTCPeerInstance[id].rtcPeer.setRemoteDescription(desc)
            .then(() => 
            {
                AccelByteRTCPeerInstance[id].collectedRemoteCandidates = [];
                RTCPrintLogV2(id, LogLevel.Log, `No need to CreateAnswer`);
            })
            .then(() => 
            {
                AccelByteRTCPeerInstance[id].remoteDescriptionSet = true;
                {{{ makeDynCall('vi', 'callback') }}} (0);
            });
        }
    },

    RTCAddRemoteCandidate: function (id, sdp) 
    {
        const sdpString = UTF8ToString(sdp);
        RTCPrintLogV2(id, LogLevel.Log, `Add Remote candidate id ${id}`);
        RTCPrintLogV2(id, LogLevel.Verbose, `candidate sdp: '${sdpString}'`);
        const candidate = JSON.parse(sdpString);

        AccelByteRTCPeerInstance[id].collectedRemoteCandidates.push(candidate);
        ProcessRemoteCandidates(id);

        return true;
    },

    $ProcessRemoteCandidates: function(id)
    {
        if (AccelByteRTCPeerInstance[id].isProcessingRemoteCandidates === true 
            || AccelByteRTCPeerInstance[id].collectedRemoteCandidates.length === 0
            || AccelByteRTCPeerInstance[id].remoteDescriptionSet !== true) {
            return;
        }
    
        AccelByteRTCPeerInstance[id].isProcessingRemoteCandidates = true;
        const candidateStr = AccelByteRTCPeerInstance[id].collectedRemoteCandidates.shift();  
    
        AccelByteRTCPeerInstance[id].rtcPeer.addIceCandidate(candidateStr);
    
        setTimeout(() => {
            AccelByteRTCPeerInstance[id].isProcessingRemoteCandidates = false;
            ProcessRemoteCandidates(id);
        }, 100);
    },

    RTCGatherCandidates: async function (id) 
    {
        RTCPrintLogV2(id, LogLevel.Log, `Gather Candidates`);
        RTCChangeState(id, "GATHERING");

        while (AccelByteRTCPeerInstance[id].isIceGatheringDone === false) 
        {
            RTCPrintLogV2(id, LogLevel.Verbose, `Wait until is IceGatheringDone`);
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        RTCProcessCandidateFound(id);

        while (AccelByteRTCPeerInstance[id].collectedGatheredCandidates.length > 0) 
        {
            RTCPrintLogV2(id, LogLevel.Verbose, `Wait until is all gathered candidate processed`);
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        RTCChangeState(id, "CONNECTING");
    },

    RTCSetRemoteGatheringDone : function(id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Set remote gathering done`);
        AccelByteRTCPeerInstance[id].gatherCandidateDone = true;
    },

    RTCSendData : function (id, arrayOfBytes, length)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Send data`);
        try
        {
            const channel = AccelByteRTCPeerInstance[id].dataChannel;
            let byteArray = new Uint8Array(HEAPU8.buffer, arrayOfBytes, length);
            channel.send(byteArray);
            return true;
        }
        catch (e)
        {
            RTCPrintLogV2(id, LogLevel.Warning, `failed to send data ${e}`);
            return false;
        }
    },
    
    RTCGetSelectedLocalCandidates : function (id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Get Selected Local candidates`);
    },

    RTCGetSelectedRemoteCandidates : function(id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Get Selected Remote candidates`);
    },

    RTCGetSelectedLocalAddresses : function(id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Selected Local address`);
    },

    RTCGetSelectedRemoteAddresses : function(id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Selected Remote address`);
    },

    RTCGetConnectionState : function (id)
    {
        RTCPrintLogV2(id, LogLevel.Log, `Get connection State`);
        return AccelByteRTCPeerInstance[id].rtcPeer.connectionState();
    },
    //#endregion

    //#region Listener Setter
    RTCSetLogHandler : function(callbackHandler) 
    {
        if(CurrentLogHandler.logHandler === null)
        {
            CurrentLogHandler.logHandler = callbackHandler;
        }
    },

    RTCSetStateChangedHandler : function(id, callbackHandler) 
    {
        AccelByteRTCPeerInstance[id].stateChangedHandler = callbackHandler;
    },

    RTCSetCandidateFoundHandler : function(id, handler) 
    {
        AccelByteRTCPeerInstance[id].candidateFoundHandler = handler;
    },

    RTCSetGatheringDoneHandler : function(id, handler) 
    {
        AccelByteRTCPeerInstance[id].gatheringDoneHandler = handler;
    },

    RTCSetDataReceivedHandler : function(id, handler) 
    {
        AccelByteRTCPeerInstance[id].dataReceivedHandler = handler;
    },
    //#endregion

    //#region Listener Trigger
    $RTCTriggerReceivedMessage: function (id, message)
    {
        try 
        {
            const byteArray = new Uint8Array(message);

            const byteArrayPtr = Module._malloc(byteArray.length);
            Module.HEAPU8.set(byteArray, byteArrayPtr);

            {{{ makeDynCall('viii', 'AccelByteRTCPeerInstance[id].dataReceivedHandler') }}} (id, byteArrayPtr, byteArray.length);

            _free(byteArray);
            _free(byteArrayPtr);
        } 
        catch (e) 
        {
            RTCPrintLogV2(id, LogLevel.Warning, `Error invoking dataReceivedHandler: ${e}`);
        }
    },

    $RTCProcessCandidateFound : function(id)
    {
        if (AccelByteRTCPeerInstance[id].isProcessingCandidates === true 
            || AccelByteRTCPeerInstance[id].collectedGatheredCandidates.length === 0) {
            return;  // Don't process if we're already processing, or no candidates left
        }
    
        AccelByteRTCPeerInstance[id].isProcessingCandidates = true; 
        
        const candidateStr = AccelByteRTCPeerInstance[id].collectedGatheredCandidates.shift();  
    
        RTCTriggerCandidateFound(id, candidateStr);
    
        setTimeout(() => {
            AccelByteRTCPeerInstance[id].isProcessingCandidates = false;
            RTCProcessCandidateFound(id);
        }, 100);
    },

    $RTCTriggerCandidateFound: function (id, description) 
    {
        try 
        {
            const buffer = stringToNewUTF8(description);
            {{{ makeDynCall('vii', 'AccelByteRTCPeerInstance[id].candidateFoundHandler') }}} (id, buffer);
            _free(buffer);
        } 
        catch (e) 
        {
            RTCPrintLogV2(id, LogLevel.Warning, `Error invoking candidateFoundHandler: ${e}`);
        }
    },

    $RTCTriggerGatheringDone: function (id) 
    {
        try 
        {
            const buffer = stringToNewUTF8(0);
            {{{ makeDynCall('vii', 'AccelByteRTCPeerInstance[id].gatheringDoneHandler') }}} (id, buffer);
            _free(buffer);
        } 
        catch (e) 
        {
            RTCPrintLogV2(id, LogLevel.Warning, `Error invoking gatheringDoneHandler: ${e}`);
        }
    },

    $RTCPrintLog: function (message) 
    {
        try 
        {
            const buffer = stringToNewUTF8(message);
            {{{ makeDynCall('vi', 'CurrentLogHandler.logHandler') }}} (buffer);
            _free(buffer);
        } 
        catch (e) 
        {
            RTCPrintLogV2(id, LogLevel.Warning, `Error invoking logHandler: ${e}`);
        }
    },

    $RTCPrintLogV2: function(id, level, message) 
    {
        if (level <= CurrentLogHandler.currentLogLevel) 
        {
            const finalMessage = `from PeerId: ${id} ` + message;
            RTCPrintLog(finalMessage);
        }
    },

    $RTCChangeState : function(id, newState)
    {
        try 
        {
            const buffer = stringToNewUTF8(newState);
            {{{ makeDynCall('vii', 'AccelByteRTCPeerInstance[id].stateChangedHandler') }}} (id, buffer);
            _free(buffer);
        } 
        catch (e) 
        {
            RTCPrintLogV2(id, LogLevel.Warning, `Error invoking stateChangedHandler: ${e}`);
        }
    },
    //#endregion

    //#region Utils
    $RemovePeerInstance : function(id)
    {
        try
        {
            RTCPrintLogV2(id, LogLevel.Log, `Removing Peer ${id}`);

            AccelByteRTCPeerInstance[id].rtcPeer.close();
            AccelByteRTCPeerInstance[id].forEach(attr => 
            {
                attr.forEach(element => 
                {
                    delete element;   
                });
                delete attr;
            });

            AccelByteRTCPeerInstance[id] = null;

            _free(AccelByteRTCPeerInstance[id]);
        }
        catch
        {
            console.log(`Couldn't delete peer ${id}`);
        }
    }
    //#endregion
};

autoAddDeps(AccelByteRTCPeerConnection, '$ProcessRemoteCandidates');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCProcessCandidateFound');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCTriggerReceivedMessage');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCTriggerCandidateFound');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCTriggerGatheringDone');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCPrintLogV2');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCPrintLog');
autoAddDeps(AccelByteRTCPeerConnection, '$RTCChangeState');
autoAddDeps(AccelByteRTCPeerConnection, '$LogLevel');
autoAddDeps(AccelByteRTCPeerConnection, '$CurrentLogHandler');
autoAddDeps(AccelByteRTCPeerConnection, '$RemovePeerInstance');
autoAddDeps(AccelByteRTCPeerConnection, '$AccelByteRTCPeerInstance');
mergeInto(LibraryManager.library, AccelByteRTCPeerConnection);