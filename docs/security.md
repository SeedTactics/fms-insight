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

Authentication is via OpenID Connect. Any OpenID Connect identity provider such as
Azure Active Directory, Office 365, Google, or many others will work. If you don't have an OpenID
Connect identity provider (e.g. older versions of Active Directory or other SAML providers), you
could use something like [Okta](https://www.okta.com/).

To use, first configure a client with your OpenID Connect identity provider.
The client must support the implicit grant flow and provide access to the
`openid` and `profile` scopes. Also, the Redirect URI for the client should
match the IP address or DNS name that users use to access the FMS Insight
client (and is used in the SSL certificate).

Once the client has been created, set three settings in the
[config.ini](server-config.ini): `OpenIDConnectAuthority`,
`OpenIDConnectAudience`, and `OpenIDConnectClientId`. The
`OpenIDConnectAuthority` should be a URL route to the well-known
configuration document for the identity provider, the
`OpenIDConnectClientId` should be the client_id of the created client, and
the `OpenIDConnectAudience` should be the expected `aud` value in the
resulting tokens (typically the audience and the client id are the same).
Once configured, all routes in the fms-insight will require a bearer
authentication token and the client will redirect to the identity provider
to support log in.
