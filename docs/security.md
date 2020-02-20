---
id: security
title: Server Security
sidebar_label: Security
---

FMS Insight implements a JSON+REST-based HTTP server and runs on the cell
controller. By default, FMS Insight listens on port 5000 with no TLS and no
authentication; any computer which can connect to the port can view data and
download jobs.

To implement security, we recommend one of two possible variants.

- FMS Insight can be configured to require TLS and require OpenID connect authentication for
  single-sign-on. This requires the cell controller computer (running FMS Insight) to be directly connected
  to the existing factory network, but a firewall can be used to only allow access on the specific
  port running FMS Insight.
- A cell-local private network containing the machines, cell-controller, and FMS Insight is created.
  A reverse HTTP proxy connects the cell-local private network to the factory network.

## FMS Insight HTTPS, Authentication, and Firewall

In this design, FMS Insight is configured to require TLS/HTTPS and implements
authentication using OpenID Connect. We have tested with AzureAD, Okta, and
Keycloak, but any OpenID/OAuth2 compliant identity provider should work. An
external firewall is placed between the cell-controller and the factory
network and is configured to only allow traffic on the FMS Insight port.

To enable HTTPS, a server certificate must be created and stored in a pfx file. This
should be signed by the enterprise domain, but could also be a self-signed certificate.
Once created, within the FMS Insight [config.ini](server-config.md) file, enable the
`TLSCertFile` setting with the path to the certificate.

Within your identity provider, create an application for FMS Insight and create a client.
The client must require implicit grant flow with no client secret. The Redirect URI for the client
must be set to the common name from the TLS certificate with a root path.
Finally, most identity providers allow the administrator to restrict an
application to a set of users (e.g. Azure Active Directory allows assigning specific users
or groups to the application).

Once the client has been created, there are four settings in the [config.ini](server-config.md).

- `OpenIDConnectAuthority`: This setting is the path to the well-known configuration document for the identity provider.
  For example, for AzureAD, this is `https://login.microsoftonline.com/{tenant}/v2.0` where `{tenant}` is your tenant GUID.
- `OpenIDConnectClientID`: This should be the ClientID for the implicit grant flow client. This ClientID is passed
  to the FMS Insight webpage which then performs the implicit flow grant to obtain a token.
- `AuthAuthority`: This must be identical to the `OpenIDConnectAuthority`.
- `AuthTokenAudiences`: This is a list of audiences which the server uses to validate the identity tokens. In almost all
  cases, the audience matches the ClientID. Multiple audiences are separated by semi-colons.

## Cell-Local Private Network With Proxy

In this design, a cell-local private network is created with a proxy computer to the factory network.
The design is based on a networking DMZ-setup.

- The machines and cell controller are placed on a private network. This network has no DNS,
  no DHCP, and no route or access to any other network. IP Addresses are statically assigned.

- The cell controller computer runs FMS Insight with no TLS, listening on port 5000.

- The factory provides a computer with two network cards. One network card connects
  to the cell-local private network and is statically assigned an IP address.
  The second network card connects to the existing factory network. This computer can join the domain,
  enable Windows Updates, and use all the typical configuration of the Factory's IT.
  This computer should deny ALL network traffic to be routed between the cell-local network and the
  factory network. All communication is proxied (a networking DMZ setup). The factory provides this
  computer, allowing you to use your preferred supplier and configuration.

- The proxy computer runs a reverse-HTTP-proxy. This can be Nginx, IIS, or something else. We
  also provide the source code for a small C# reverse-proxy server built using ASP.NET Core and Kestrel
  (~250 lines of code) which you can use if you wish it. The reverse-proxy implements TLS and requires connections over HTTPS.

- Either the reverse-proxy or FMS Insight itself is configured to require OpenID Connect/OAuth2 Authentication.
  This allows single-sign-on and only authenticated users will then be able to view data and create jobs.
  FMS Insight has been tested with AzureAD, Okta, and Keycloak, but any OpenID Connect compliant
  identity provider should work.

- Software running on the factory network connects and authenticates with the reverse-proxy, which
  passes the requests along to FMS Insight running on the cell controller.

#### Reverse-Proxy Implemented Authentication

One option for authentication is for the reverse-proxy to require authentication. The reverse-proxy
should require OpenID Connect authentication for all routes starting with `/api` except for `/api/v1/fms/fms-information`.
Our small C# reverse-proxy implements OpenID Connect authentication and has been tested with AzureAD, Okta,
and Keycloak. Alternatively, Nginx+ or IIS can be configured to require authentication.

In this scenario, two settings are needed in FMS Insight's [config.ini](server-config.md):

- `OpenIDConnectAuthority`: This setting is the path to the well-known configuration document for the identity provider.
  For example, for AzureAD, this is `https://login.microsoftonline.com/{tenant}/v2.0` where `{tenant}` is your tenant GUID.
- `OpenIDConnectClientID`: This is the ClientID for an implicit grant flow client. This ClientID is passed
  to the FMS Insight webpage which then performs the implicit flow grant to authenticate the user and obtain a token.

These settings are just returned to clients in the `/api/v1/fms/fms-information` route, FMS Insight itself does not
check the request for a proper JWT token.

Next, the reverse-proxy is configured with TLS certificates and authentication. For our small C# reverse-proxy,
this is done in the appsettings.json file.

#### FMS Insight Implemented Authentication

If the reverse-proxy does not implement authentication, FMS Insight itself can verify the tokens. The main challenge
is that the FMS Insight server requires access to the OpenID Connect Authority well-known configuration document
to be able to obtain the signing keys. For example, in AzureAD, the well-known document
is `https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration` where tenant is your tenant GUID.
This document is required to be accessible by the FMS Insight server to obtain the rotating signing keys of the
identity provider. Since FMS Insight is running on the cell-controller on the cell-local private network and the proxy
computer denies all routed traffic, FMS Insight has no direct access to the well-known configuration document.

Thus, the reverse-proxy must also proxy the well-known configuration document. That is, the reverse-proxy is
configured to first proxy all connections from the factory network to FMS Insight. In addition, the reverse-proxy
is then also configured to proxy the request from FMS Insight to the OpenID Connect configuration document to
the real identity provider.

The configuration in FMS Insight's [config.ini](server-config.md) are as follows:

- `OpenIDConnectAuthority`: The real identity-provided authority URL.
  For example, for AzureAD, this is `https://login.microsoftonline.com/{tenant}/v2.0` where `{tenant}` is your tenant GUID.
- `OpenIDConnectClientID`: This is the ClientID for an implicit grant flow client. This ClientID is passed
  to the FMS Insight webpage which then performs the implicit flow grant to authenticate the user and obtain a token.
- `AuthAuthority`: The path to the reverse-proxy to obtain the OpenID configuration. For example, it could be
  `http://{reverse-proxy-ip-address}/openid-authority`. The reverse-proxy should then be configured to proxy all
  paths starting with `/openid-authority/*` to the real identity provider.
- `AuthTokenAudiences`: This is a list of audiences which the server uses to validate the identity tokens. In almost all
  cases, the audience matches the ClientID. Multiple audiences are separated by semi-colons. Thus this setting should
  be all the ClientIDs created separated by semi-colons.
