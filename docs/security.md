---
id: security
title: Server Security
sidebar_label: Security
---

## Secure HTTPS

By default, FMS Insight does not use security and all communication is over HTTP.
To enable HTTPS, a server certificate must be created and stored in a file, and the
path to this file must be added to the [config.ini](server-config.md) file in the `TLSCertFile` setting.

If you have an enterprise root certificate authority pre-installed on the
devices (usually as part of them joining the local AD domain), a private
certificate signed by the certificate authority should be created and then
used by specifying the path to the certificate in the `config.ini` file.
This will prevent browser warnings and is the most secure.

If you do not have an enterprise root certificate authority or cannot obtain
signed certificates, you can create a self-signed certificate. This will present
a warning on each device by the browser on first load and you will be required to
trust the self-signed certificate.

To create a self-signed certificate, the following PowerShell commands can be used.
You must replace the `$fmsInsightServer` variable with the IP address or DNS name that
will be used in the browser to connect to the FMS Insight server.

```
$fmsInsightServer = "172.16.17.18"
$certificate = New-SelfSignedCertificate -CertStoreLocation cert:\localmachine\my -dnsname $fmsInsightServer -NotAfter (GetDate).AddYears(10)
Export-PfxCertificate -cert ("cert:\localMachine\my\" + $certificate.Thumbprint) -FilePath "c:\ProgramData\SeedTactics\FMSInsight\cert.pfx"
```

The above lines can be pasted one-by-one into an administrator PowerShell. Once completed,
the certificate will be stored in `c:\ProgramData\SeedTactics\FMSInsight\cert.pfx`. This path
should be then set as the `TLSCertFile` property in `config.ini` (should just require un-commenting
the `TLSCertFile` line).

## Authentication

Authentication is via OpenID Connect and OAuth2. Any OpenID Connect identity provider such as
Azure Active Directory, Office 365, Okta, Keycloak, Google, or many others will work. If you don't have an OpenID
Connect identity provider (e.g. older versions of Active Directory or other SAML providers), you
could use something like [Okta](https://www.okta.com/).

Within your identity provider, create an application for FMS Insight and create a client.
The client must require implict grant flow with no client secret. The Redirect URI for the client
must be set to the common name from the TLS certificate with a root path. For example, say that you
created a TLS certificate with common name `cell-10-fms-insight.mycompany.com` and a DNS entry
mapping `cell-10-fms-insight.mycompany.com` to the FMS Insight cell controller. Then when configuring
the client, you would set the Redirect URL to `https://cell-10-fms-insight.mycompany.com/`. Note down
the ClientID and find the authority URL for the identity provider. Finally, most identity providers allow
the administrator to restrict an application to a set of users (e.g. Azure Active Directory allows assigning
specific users or groups to the application). In this way, only certian users will be allowed to login and use
FMS Insight.

Optionally, if you are going to be accessing FMS Insight for [automatic job download](creating-jobs.md)
without a user present, create a second client with a client secret and using the client-credentials OAuth2
flow. The automatic download will use this client for the machine-to-machine authentication.

Once the client has been created, there are three settings in the
[config.ini](server-config.ini).

- `OpenIDConnectAuthority`: This setting is the path to the well-known configuration document for the identity provider.
  From this document, FMS Insight obtains the signing keys and login URLs.
- `OpenIDConnectClientID`: This should be the ClientID for the implicit grant flow client. This ClientID is passed
  to the FMS Insight webpage which then performs the implicit flow grant to obtain a token.
- `AuthTokenAudiences`: This is a list of audiences which the server uses to validate the identity tokens. In almost all
  cases, the audience matches the ClientID. Multiple audiences are separated by semi-colons. Thus this setting should
  be all the ClientIDs created separated by semi-colons.
