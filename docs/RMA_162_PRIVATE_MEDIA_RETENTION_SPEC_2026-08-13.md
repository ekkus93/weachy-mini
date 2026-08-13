# RMA-162 Private-Media Retention Policy

## Status

Implementation specification for RMA-162. This task defines the product boundary for conversation media and history persistence. It does not introduce a recording feature or a user media archive.

## Production media-retention contract

The production application defaults to no persistent retention for three private-media classes:

- raw camera frames;
- microphone audio;
- media attached to or derived for cloud requests.

`ReachyPrivateMediaRetentionPolicy` is the authoritative policy boundary. `RecordingEnabled` and `MediaExportEnabled` are false, and persistent retention requests for every `ReachyPrivateMediaKind` fail closed until a future explicit consent flow changes the contract deliberately.

A future recording or media-export implementation must not bypass this boundary. Enabling it requires an explicit user opt-in, visible recording/export status, bounded retention, and corresponding tests before the policy can permit persistence.

## Temporary-media contract

Some providers may require a file-oriented API even though the product does not retain the media. Those cases use `ReachyPrivateMediaTemporaryFileStore` rather than durable settings or general persistent storage.

The Unity adapter roots that store in `Application.temporaryCachePath`, under the dedicated `reachy-private-media` directory. A temporary media file is represented by an `IDisposable` lease. Disposing the lease deletes the file immediately. Failed creation removes any partial file, and store initialization purges abandoned files left by a prior interrupted process.

The temporary store is not an export mechanism and exposes no path promotion into durable application storage.

## Conversation-history contract

Conversation-history persistence is independent from raw media retention. It is opt-in only: persistence is enabled only when the durable `HistoryEnabled` setting is true and the selected retention period is greater than zero.

Supported retention values remain bounded to `0`, `7`, `30`, and `90` days. Zero means session-only history. When history is disabled, no persistent history is authorized regardless of the dormant retention preference.

This task establishes the policy and settings contract; it does not fabricate a conversation-history database where none currently exists.

## User-visible consent gate

The Privacy settings panel explicitly states that raw camera, microphone, and cloud-request media are not retained by default. It also exposes visible controls stating:

- `MEDIA RECORDING  OFF — OPT-IN REQUIRED`;
- `MEDIA EXPORT  UNAVAILABLE — CONSENT REQUIRED`.

These controls report an explicit unavailable reason. They do not silently enable media retention and are the required UI placeholder before any future recording/export implementation may be introduced.

## Acceptance-evidence carve-out

`ReachyCameraTextureEvidence` intentionally writes RMA-092 camera PNGs for an explicitly launched physical acceptance test. That path is test evidence, is gated by the RMA-092 acceptance launch extra, and is not a production media-retention feature.

RMA-162 regressions lock that distinction by requiring the production temporary-media adapter to use `Application.temporaryCachePath` while recognizing the existing acceptance-only PNG writer. A future production camera-media writer must go through a separately reviewed retention/consent design rather than copying the acceptance harness.

## Diagnostics and exports

RMA-162 does not authorize private media in diagnostics bundles. The project-level Phase 17 rule remains that diagnostics must exclude secrets and private media by default. Any future diagnostics export that includes private media would require a separate explicit user authorization contract and tests.
