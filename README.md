# Aiursoft MarkToHtml

<a name="marktohtml"></a>

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/aiursoftweb/markToHtml/blob/master/LICENSE)
[![Pipeline stat](https://badge-proxy.aiursoft.com/aiursoft/markToHtml/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/markToHtml/-/pipelines)
[![Test Coverage](https://badge-proxy.aiursoft.com/aiursoft/markToHtml/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/markToHtml/-/pipelines)
[![S360 compliance](https://s360.aiursoft.com/badge.svg?project=aiursoft%2Fmarktohtml)](https://s360.aiursoft.com/SupplyChain?tab=projects&project=aiursoft%2Fmarktohtml&scope=baseline&ecosystem=all)
[![Man hours](https://manhours.aiursoft.com/r/github.com/aiursoftweb/markToHtml.svg)](https://manhours.aiursoft.com/r/github.com/aiursoftweb/markToHtml.html)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Fmarktohtml.aiursoft.com)](https://marktohtml.aiursoft.com)
[![Docker](https://img.shields.io/docker/pulls/aiursoft/marktohtml.svg)](https://hub.docker.com/r/aiursoft/marktohtml)

A simple tool to convert your Markdown into clean, sanitized HTML.

![screenshot](./screenshot.png)

Default user name is `admin@default.com` and default password is `Admin@123456!`.

## Overview

### What is MarkToHtml?

<a name="why-marktohtml-for-your-organization"></a>

MarkToHtml is a production-ready, enterprise-grade knowledge base platform designed for teams that value simplicity without sacrificing essential features. While commercial solutions like Notion charge $10+ per user monthly and lack self-hosting options, MarkToHtml delivers a comprehensive document collaboration system that deploys in minutes and costs nothing to run on your own infrastructure.

Key enterprise features include:

**Flexible Data Infrastructure.** Seamlessly switch between SQLite for quick deployment, MySQL for scalability, SQL Server for enterprise environments, or in-memory databases for testing. No complex configuration required.

**Enterprise Authentication.** Native OpenID Connect integration allows seamless connection to your existing identity provider with automatic role synchronization. Built-in user management provides a complete authentication solution out of the box.

**Fine-Grained Access Control.** Role-Based Access Control system with comprehensive permissions for users, roles, and documents. Dynamic navigation automatically adapts to user permissions, ensuring users only see what they should.

**Global-Ready Platform.** AI-powered translation supporting 27 languages enables worldwide team collaboration without language barriers. Comprehensive localization coverage ensures consistent user experience across cultures.

## Try

Try a running MarkToHtml [here](https://marktohtml.aiursoft.com).

## Install with Docker

Run the installer with the desired port (omit it to choose an available port):

```bash
curl -fsSL https://raw.githubusercontent.com/aiursoftweb/marktohtml/master/install.sh | sudo bash -s -- 8080
```

Installation starts after three seconds; press Ctrl+C to cancel. Then open `http://localhost:8080`.

## Run manually

Requirements about how to run

1. Install [.NET 10 SDK](http://dot.net/) and [Node.js](https://nodejs.org/).
2. The .NET build automatically runs `npm ci` and `npm run build` to prepare the frontend assets. Node.js and npm must be available when building from source.
3. Execute `dotnet run --project src/Aiursoft.MarkToHtml/Aiursoft.MarkToHtml.csproj -- --urls http://localhost:5000` to run the app.
4. Use your browser to view [http://localhost:5000](http://localhost:5000).

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5` to run the app.

## Run in Docker

First, install Docker [here](https://docs.docker.com/get-docker/).

Then run the following commands in a Linux shell:

```bash
image=aiursoft/marktohtml
appName=marktohtml
sudo docker pull $image
sudo docker run -d --name $appName --restart unless-stopped -p 5000:5000 -v /var/www/$appName:/data $image
```

That will start a web server at `http://localhost:5000` and you can test the app.

The docker image has the following context:

| Properties  | Value                           |
|-------------|---------------------------------|
| Image       | aiursoft/marktohtml         |
| Ports       | 5000                            |
| Binary path | /app                            |
| Data path   | /data                           |
| Config path | /data/appsettings.json          |

## Usage

Open the application at the URL selected in the installation or run steps above.

## Configuration

The application defaults are defined in [`src/Aiursoft.MarkToHtml/appsettings.json`](src/Aiursoft.MarkToHtml/appsettings.json). The Docker image initializes `/data/appsettings.json` on its first start and keeps it for later starts; persist `/data`. Override nested values with double-underscore environment variables, such as `Logging__LogLevel__Default`.

```jsonc
{
  "ConnectionStrings": {
    "AllowCache": "True",

    "DbType": "Sqlite",
    "DefaultConnection": "DataSource=app.db;Cache=Shared"

    // sudo docker run -d --name db -e MYSQL_RANDOM_ROOT_PASSWORD=true -e MYSQL_DATABASE=template -e MYSQL_USER=template -e MYSQL_PASSWORD=template_password -p 3306:3306 hub.aiursoft.com/mysql
    //"DbType": "MySql",
    //"DefaultConnection": "Server=localhost;Database=template;Uid=template;Pwd=template_password;"
  },
  "Storage": {
    "Path": "/tmp/data",
    "PublicOrigin": "",
    "RequireDedicatedInlineOrigin": false,
    "AllowArbitraryInlineOnDedicatedOrigin": false,
    "SafeInlineMediaExtensions": [ "svg" ],
    "SafeInlineSvgSubfolders": [ "markdown-images" ],
    "UploadPolicy": {
      "RequireAuthenticatedUser": true,
      "ReplaceSpacesWithHyphens": true
    }
  },
  "AppSettings": {
    "AuthProvider": "Local", // OIDC or Local
    "DefaultRole": "",
    "PersistsSignIn": false,
    "OIDC": {
      // https://auth.aiursoft.com/application/o/template
      "Authority": "https://auth.aiursoft.com/application/o/template",
      "ClientId": "",
      "ClientSecret": "",
      "RolePropertyName": "groups",
      "UsernamePropertyName": "preferred_username",
      "UserDisplayNamePropertyName": "name",
      "EmailPropertyName": "email",
      "UserIdentityPropertyName": "sub"
    },
    "Local": {
      "AllowRegister": true,
      "AllowWeakPassword": true
    }
  },
  "TtsSettings": {
    "BaseUrl": "https://tts.aiursoft.com/",
    "ApiToken": "",
    "DefaultVoice": "reporter-zh",
    "DefaultFormat": "mp3",
    "DefaultSpeed": 1.0
  },
  "GlobalSettings": {
    // You can initialize or override global settings here.
    // "Allow_User_Adjust_Nickname": "True"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "Clickhouse": {
      "Enabled": false,
      "ConnectionString": "Host=localhost;Protocol=http;Port=8123;User=default;Password=;Database=AspNetCoreLogs",
      "TableName": "MarkToHtmlLogs"
    }
  },
  "AllowedHosts": "*"
}
```

### Shared settings

- `ConnectionStrings` selects the database provider and connection. Prepare the external database before choosing MySQL; keep the SQLite file persistent when using SQLite.
- `Storage` configures local application files. Keep the storage directory persistent and use the documented hostname and upload policies for shared downloads.
- `AppSettings` controls authentication and account synchronization. Choose local accounts or configure the OIDC authority, client and `/signin-oidc` callback; use environment variables for credentials.
- Uncommented `GlobalSettings` entries override the database-backed administration settings. Leave entries commented to use the stored values or application defaults.
- `Logging:LogLevel` controls application and framework verbosity. Configure the optional ClickHouse exporter only when its server and credentials are ready.
- `AllowedHosts` is the ASP.NET Core Host-header allowlist, not a CORS setting.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.

## License

Licensed under the [MIT License](LICENSE).
