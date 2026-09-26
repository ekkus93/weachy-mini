# RMA-191 network-security reconciliation — 2026-09-25

## Scope

This note records the RMA-191 network-security configuration decision for the initial Android release. It is deliberately narrow: it does not close the full RMA-191 checklist by itself and does not claim provider endpoint trust.

## Policy decision

The release network-security policy is a split platform/application policy:

1. no checked-in Android manifest grants blanket `android:usesCleartextTraffic="true"`;
2. public remote model URLs require public HTTPS through `ReachyNetworkEndpointSecurity.RequirePublicHttpsUri`;
3. OpenAI-compatible provider profiles default to `ReachyProviderTlsMode.RequireHttps`;
4. HTTP provider profiles are accepted only in `TrustedLocalNetworkCleartext` mode and only when `ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost` accepts the host;
5. the shared HTTP transport disables automatic redirects and rechecks final scheme, host, and port before accepting provider responses;
6. cloud/local-network provider use still requires explicit privacy-boundary confirmation before request creation.

## Why there is no static allow-list XML

Android Network Security Config is a static manifest resource. The product intentionally supports user-entered local inference endpoints on loopback, RFC1918, link-local, `.localhost`, and `.local` hosts. Static XML cannot safely express that exact dynamic trusted-LAN set without either blocking supported local deployments or allowing broader cleartext than the application policy allows.

A blanket manifest `usesCleartextTraffic=true` would weaken defense-in-depth and is therefore not introduced. A blanket platform prohibition would contradict the explicitly supported trusted-LAN provider mode and the existing physical local-provider experiments. The reconciled release posture is therefore: no blanket manifest cleartext allowance, target-SDK platform defaults, and centralized application-layer validation that rejects public cleartext and redirect weakening before traffic is sent.

## Closure boundary

This document and its companion regression test are sufficient design evidence for the RMA-191 network-security review item once merged and exact-head qualified. They do not prove that any arbitrary third-party endpoint is safe, and they do not authorize provider traffic without user configuration and privacy-boundary confirmation.
