# Changelog

All notable changes to this project will be documented in this file. See [standard-version](https://github.com/conventional-changelog/standard-version) for commit guidelines.

### [0.3.6](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.6%0D0.3.5) (2023-12-14)


### Bug Fixes

* client cannot connect to host with secure handshaking enabled ([6c05d11](https://github.com/AccelByte/accelbyte-unity-networking/commits/6c05d115d381f9f340949d15589730eda5c413c7))

### [0.3.5](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.5%0D0.3.4) (2023-09-25)


### Refactors

* The host cannot access the GameServer API for P2P. Instead, refactor to authenticate using bans information in the access token. [JSC-1608] ([5d7cf04](https://github.com/AccelByte/accelbyte-unity-networking/commits/5d7cf040b670bdab851b42841bf64ed67b63ae5d))

### [0.3.4](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.4%0D0.3.3) (2023-08-28)


### Bug Fixes

* update x64 dll to fix insufficient buffer on receive ([a92f74f](https://github.com/AccelByte/accelbyte-unity-networking/commits/a92f74f027447108a26906d2f51c9f32c8e9ae34))
* when peer is not responding only call connection closed event to avoid network object destroyed ([bb2454e](https://github.com/AccelByte/accelbyte-unity-networking/commits/bb2454e30056ce290357ae9ba6d651b63c0a18e3))

### [0.3.3](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.3%0D0.3.2) (2023-08-14)


### Bug Fixes

* remove data length checking and always send copy of byte[] from ArraySegment to avoid sending improperly initialized ArraySegment.Array ([37b9417](https://github.com/AccelByte/accelbyte-unity-networking/commits/37b94173b8d18ba815336e3f20c68b93c1e2e440))

### [0.3.2](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.2%0D0.3.1) (2023-07-31)


### Bug Fixes

* refactor libjuice interaction with unity resource to avoid game thread stuck ([f0b0c1b](https://github.com/AccelByte/accelbyte-unity-networking/commits/f0b0c1bd8fac50450cd335ca0ad2f8deecb19720))

### [0.3.1](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.1%0D0.3.0) (2023-07-03)


### Features

* add dynamic library for PS4 and PS5 ([a4bc08c](https://github.com/AccelByte/accelbyte-unity-networking/commits/a4bc08ce6a674898342cee57bbcbda96e09d4dcb))

## [0.3.0](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.0%0D0.2.9) (2023-06-19)


### ⚠ BREAKING CHANGES

* migration from unity webrtc to libjuice and network transport manager no longer handle session browser

### Features

* change unity webrtc to libjuice and remove session browser ([acdefde](https://github.com/AccelByte/accelbyte-unity-networking/commits/acdefde94a89140d9a5acc78b344c2389f5791f7))


### Documentations

* redirect readme to doc portal ([5d40c32](https://github.com/AccelByte/accelbyte-unity-networking/commits/5d40c32e626f6f445b4321cba3d637b30dca2f56))

### [0.2.9](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.9%0D0.2.8) (2023-05-08)

### [0.2.8](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.8%0D0.2.7) (2023-04-26)


### Bug Fixes

* add missing meta ([cab1d04](https://github.com/AccelByte/accelbyte-unity-networking/commits/cab1d04a7aa369db8da06f9ad86319ecda4cd305))

### [0.2.7](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.7%0D0.2.6) (2023-04-19)


### Bug Fixes

* add missing meta ([2d1fd50](https://github.com/AccelByte/accelbyte-unity-networking/commits/2d1fd504f861789d45f34352a79b619f3e6c285f))

### [0.2.6](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.6%0D0.2.5) (2023-04-10)

### [0.2.5](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.5%0D0.2.4) (2023-03-27)

### [0.2.4](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.4%0D0.2.3) (2023-02-13)


### Features

* dynamic TURN server authentication ([deb6fab](https://github.com/AccelByte/accelbyte-unity-networking/commits/deb6fabf81653e7f40cb6ae1b62657bf760e02cf))

### [0.2.3](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.3%0D0.2.2) (2023-01-30)


### Features

* dynamic TURN server authentication ([c673e1b](https://github.com/AccelByte/accelbyte-unity-networking/commits/c673e1b996713eb0240a4d499eeed8933fc10d01))

### [0.2.2](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.2%0D0.2.1) (2022-08-29)


### Bug Fixes

* **p2p:** modify TransportManager and ICE due to rejoin disconnection issue ([02e5f47](https://github.com/AccelByte/accelbyte-unity-networking/commits/02e5f4721b94b5517c33893cb556089a10cf6b6e))

### [0.2.1](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.1%0D0.2.0) (2022-08-15)


### Bug Fixes

* wrong ternary operator GetHostedSessionID() function ([afbe411](https://github.com/AccelByte/accelbyte-unity-networking/commits/afbe4116c117c71ee2742cab11333a8acd71bc77))

## [0.2.0](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.2.0%0D0.1.0) (2022-08-01)


### ⚠ BREAKING CHANGES

* **apiclient:** change the Transport initialization & usage

### Features

* **apiclient:** enforce apiclient usage across the networking plugin ([92ec357](https://github.com/AccelByte/accelbyte-unity-networking/commits/92ec357e7a815fd05dd8a894b88d1733eaa3d107))
