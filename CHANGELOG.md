# Changelog

All notable changes to this project will be documented in this file. See [standard-version](https://github.com/conventional-changelog/standard-version) for commit guidelines.

### [0.3.17](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.17%0D0.3.16) (2025-06-18)


### Features

* add log injection for test debugging ([447a73d](https://github.com/AccelByte/accelbyte-unity-networking/commits/447a73d00326cabd2df00af038f9ec81da493d11))

### [0.3.16](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.16%0D0.3.15) (2025-05-21)


### Features

* add log and organize assembly info ([7a64151](https://github.com/AccelByte/accelbyte-unity-networking/commits/7a641516be4a3d279204848ab2aae863b8ed0e40))
* improve juice state handling ([0e5b9a2](https://github.com/AccelByte/accelbyte-unity-networking/commits/0e5b9a24f7a7ed14dbefbd7b36710a68cb00a152))

### [0.3.15](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.15%0D0.3.14) (2025-03-17)


### Documentations

* update page link ([7572190](https://github.com/AccelByte/accelbyte-unity-networking/commits/7572190360ec16da8a378032b982fbef100bd3b2))

### [0.3.14](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.14%0D0.3.13) (2025-02-20)


### Bug Fixes

* fix disconnected host after kicking a client ([a0073e1](https://github.com/AccelByte/accelbyte-unity-networking/commits/a0073e1517bdbb74045da91f30e0398731fe09e5))
* fix key not found and invalid batch size from netcode ([42e94df](https://github.com/AccelByte/accelbyte-unity-networking/commits/42e94df610e348440a5ab34967a14b01453be640))

### [0.3.13](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.13%0D0.3.12) (2025-01-16)

### [0.3.12](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.12%0D0.3.11) (2024-12-10)


### Features

* added webgl p2p implementation ([5f2410a](https://github.com/AccelByte/accelbyte-unity-networking/commits/5f2410abaa3d21e07c6426206ab5ff5ff23edb2a))


### Documentations

* update reamde ([660e074](https://github.com/AccelByte/accelbyte-unity-networking/commits/660e074ca56ee9f0a4be9a87fe2a2c83c13607a9))

### [0.3.11](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.11%0D0.3.10) (2024-10-01)


### Documentations

* update package dependency ([825a827](https://github.com/AccelByte/accelbyte-unity-networking/commits/825a827819a4976b005ceabb798be5674f59d457))

### [0.3.10](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.10%0D0.3.9) (2024-09-24)


### Bug Fixes

* change username current time type from int to long ([1d8aa7d](https://github.com/AccelByte/accelbyte-unity-networking/commits/1d8aa7def84c2df1c04b08eec59e287434012f6c))

### [0.3.9](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.9%0D0.3.8) (2024-05-08)


### Bug Fixes

* update package version ([b554f6d](https://github.com/AccelByte/accelbyte-unity-networking/commits/b554f6dddff3f09c709c89c40c54e76e1e80de86))

### [0.3.8](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.8%0D0.3.7) (2024-04-08)

### [0.3.7](https://github.com/AccelByte/accelbyte-unity-networking/branches/compare/0.3.7%0D0.3.6) (2024-03-08)


### Features

* Added in data fragmentation for packets above 1kb ([4eaeb56](https://github.com/AccelByte/accelbyte-unity-networking/commits/4eaeb5689c0cd31a64bce4d7081d20ce259ff323))

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
