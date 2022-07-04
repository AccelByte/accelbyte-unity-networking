# AccelByte Networking Plugin #
## Overview
This plugin consist of NetworkTransport layer that allow the Unity game to enable  peer-to-peer communication using AccelByte service.
This plugin relies on WebRTC protocol.

## Getting Started
### Prerequisiste
Clone this repository and also copy ([AccelByte Unity SDK](https://github.com/AccelByte/accelbyte-unity-sdk)) repository.

### Installation
* Open your Unity project.
* Open the Package Manager.
* Add [Netcode for GameObjects] package from Unity resource, minimum 1.0.8.
* Add package from disk and direct it to both AccelByte packages from the prerequisite (this repository & [AccelByte Unity SDK](https://github.com/AccelByte/accelbyte-unity-sdk))
* Add these additional entries to your assembly definition:
    * com.AccelByte.Networking
    * com.unity.netcode.runtime
    * Unity.WebRTC.Runtime
