---
title: FMS Insight Security
nav: FMS Insight Server > Security
description: >-
  Configure HTTPS, authentication, and network access for the FMS Insight server and browser client.
---

FMS Insight serves its browser client and HTTP API from the cell's server. The default configuration
uses HTTP on port 5000 without authentication. Anyone who can reach that port can access data and
available operations, including changes to jobs and material. Configure access as part of the
installation's network design.

## Network Layout

In a typical Mazak installation, FMS Insight runs directly on the cell-controller computer. Host or
network firewall rules restrict access to the Insight service to approved factory devices. Browsers
connect directly to Insight; this arrangement does not require a reverse proxy.

An alternative is to run FMS Insight on a separate, IT-managed computer with two network cards:

- One interface connects to a private cell-local network containing the controller, machines, and
  any robot controllers used by the integration.
- The other connects to the factory network, where operator tablets, engineering workstations, and
  scheduling or reporting services reach the Insight web application and API.

In this arrangement, Insight communicates with equipment on the cell-local network and serves
clients through its factory-facing address. Factory clients do not need direct access to the
equipment. Keep the cell-local network isolated from factory and internet routing, and disable IP
forwarding, NAT, and network bridging on the Insight computer. The two network cards provide the
connections; firewall and routing policy maintain the separation.

Agree on the controller connections and network addresses during commissioning. Keep equipment
addresses stable; a private cell network can use static addresses without depending on factory DNS
or DHCP. The connections required by each controller integration still need to be configured when
Insight runs on a separate computer. See the [Mazak integration notes](/fms-insight/mazak).

For either layout, allow approved clients to reach only the intended Insight service endpoint.
Provide both HTTP API and live WebSocket access through that endpoint. If sign-in is enabled,
browsers also need access to the identity provider, and the service validating tokens needs access
to its metadata and signing keys. Arrange server and device updates through the site's IT management
policy. HTTPS and authentication can be provided by Insight itself or by an optional reverse proxy
as described below.

## HTTPS and Browser Sign-In

For HTTPS directly on Insight, set `TLSCertFile` in the `SERVER` section of
[config.ini](/fms-insight/server-config) to a PFX certificate file. Use a certificate trusted by the
client devices and matching the hostname they use to reach Insight. A reverse proxy can instead
provide the HTTPS endpoint.

The browser client uses OpenID Connect authorization code flow with PKCE. Register it as a public
browser client without a client secret. Configure the redirect and post-logout URLs to the root of
the externally visible Insight address, including its scheme, hostname, port if used, and trailing
slash. The client requests the `openid profile` scopes. Configure the identity provider's browser
access and token settings to match the installation.

The client sends the resulting access token to the API. The service validating that token must use
its issuer and audience; an access token's audience is not necessarily the browser client's ID.
Restrict who can sign in through the identity provider's application assignments or access policy.

## Authentication in FMS Insight

The following settings in the `SERVER` section work together:

| Setting                  | Purpose                                                                                                                                          |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `OpenIDConnectAuthority` | Identity-provider authority URL used by browsers for discovery and sign-in. This is the authority base URL, not the full discovery-document URL. |
| `OpenIDConnectClientId`  | Client ID registered for the browser application.                                                                                                |
| `AuthAuthority`          | Authority used by the server to discover the issuer and signing keys for token validation. Normally the same authority as the browser uses.      |
| `AuthTokenAudiences`     | Accepted access-token audiences, separated by semicolons.                                                                                        |

All four settings must be present for Insight's built-in API authentication to be enabled. Setting
only the browser sign-in options does not protect the API. The server validates tokens for API
requests and the live event connection. The FMS information endpoint remains available before
sign-in so clients can discover the login configuration.

The validating server needs access to the identity provider's discovery document and signing keys,
including when keys rotate. For an isolated cell network, arrange this access explicitly with the
installation's IT team. A reachable discovery document alone is insufficient if its advertised key
endpoint is unreachable.

## Optional Reverse Proxy

A reverse proxy can provide the factory-facing HTTPS endpoint for Insight in either layout. It can
also be the connection between the factory network and an Insight server kept on the cell-local
network. Clients connect to the proxy's HTTPS address; the proxy forwards HTTP requests and the live
WebSocket event connection to Insight. Network rules should prevent clients from bypassing the proxy
to reach an unauthenticated backend.

Authentication can be enforced at the proxy or by Insight. If the proxy validates tokens, configure
Insight's `OpenIDConnectAuthority` and `OpenIDConnectClientId` for browser sign-in and configure the
proxy's issuer and access-token audiences separately. Protect API routes, including
`/api/v1/events`, while allowing `/api/v1/fms/fms-information` for login discovery. The event
connection uses a token query parameter, so the proxy must support that authentication path as well
as bearer tokens on ordinary API requests.

If Insight validates tokens instead, configure all four settings above and provide its required
access to discovery and signing keys. The browser still uses the externally reachable authority and
Insight address. Source for an example proxy is included in the FMS Insight repository; adapt and
validate the deployment for the site's network and identity provider.

After configuration changes, restart the relevant services and verify sign-in, ordinary API
requests, and live updates from a client device. Verify that unauthenticated access is rejected at
the intended boundary as well.
