# Pachka

Minimalistic open source cross-platform package registry server by DeepDreamGames, intended primarily to be used with the Unity Package Manager to share packages between projects. 
Reads metadata (`package.json`) of GZipped package Tarballs from a folder and serves them to clients via [npm](https://github.com/npm/registry/blob/master/docs/REGISTRY-API.md) protocol. 


# Why

Packages is a great way to share code and assets between Unity projects, but with all due respect - installing, setting up, running and maintaining `node.js`, `npm` and `Verdaccio` may sometimes be an overkill for such purpose. 


# Features

* Cross-platform (tested on Windows, Linux and Mac)
* Low memory consumption (consumes less than 15 MB of RAM with 60 active packages). 
* References only `mscorlib.dll`, `System.dll` and `System.Core.dll` (contains custom implementations for Tar, Json Reader, Json Writer and (non-allocating!) [Semantic Version](https://semver.org/) value parsing and comparison)
* Basic command line interface for interactivity and logging which can be extended as necessary (see `RegisterCommands()` in `Application.cs`). Available commands: 
  - `help` - list available commmands
  - `clear` - clear console
  - `start` - start server (in case it was stopped)
  - `stop` - stop server
  - `restart` - restart server
  - `list` - list registered packages and their versions
  - `scan` - rescan packages in folder (server will be stopped and restarted if it was running)
  - `shutdown` `quit` `exit` - shutdown server
  - `verbosity` - change verbosity level of a running application, to override value defined in config


# Limitations

* Probably insecure, since it was intended for use only in controlled networks and security is of no concern (but let me know if there's anything I can do to make it more secure). 
* Has no graceful exeptions handling. If something will fail - you'll see it in console and the server will not start. However, you have all the source code so you can see what went wrong and fix it. 
* Have not been tested with multiple clients simultaneously making requests to the server. 


# Prerequisites

* [.NET Framework 4.7.2](http://go.microsoft.com/fwlink/?linkid=863265)


# Basic Usage

1. Download the [latest](https://github.com/deepdreamgames/pachka/releases) release and extract it. 

2. Allow executable through Firewall and open port `80` for inbound connections. 

3. Put your packages into the `Packages` folder. 

4. Launch server executable. 


# Advanced Usage

1. Create or edit configuration file (`config.json`) in the same folder as server executable (or specify relative path to config file as first command line argument). You can assign multiple endpoints there and specify port (e.g. `http://other.url:4873`):

```
{
	"endpoints": [ "http://packages.local" ], // Comma-separated list of endpoints which server will be listening to
	"path": "Packages", // Relative path to the folder with packages
	"extensions": [ ".tgz", ".tar.gz", ".taz" ], // Comma-separated list of package file extensions (not formats! Since only GZipped Tarballs are supported). Leading '.' is optional
	"verbosity": "Log" // Logging verbosity level: "None", "Exception", "Error", "Warning", "Log", "Info", "Debug" (or 0 to 6 correspondingly). Default - "Log"
}
```

2.1 If you plan to access registry from the same device where the server is running - add the following line to the end of your `C:\Windows\System32\drivers\etc\hosts` file (you'll need to run your text editor as an administrator in order to be able to change this file in Windows):

```
127.0.0.1	packages.local
```

2.2 If you want to access the server from another device in your network - you can setup DNS forwarding in your router. 
E.g. for Zyxel routers - access [http://192.168.0.1/a](http://192.168.0.1/a) and type the following commands:
 - `ip host packages.local 192.168.0.105` where `packages.local` is the endpoint (without port number) you've specified in `config.json` and `192.168.0.105` is an IP address of your server (make sure to assign static IP to your server first to make sure that it won't change). 
 - `system configuration save` - to save this configuration (so you won't have to type the command above each time after router restart). 

3. Allow an app through Firewall and open port you've specified in `config.json` (`80` by default for `http` url endpoints). 

4. Put your packages into the folder you've specified in `config.json` (e.g. `Packages`). 

5. Launch server executable. 


# Client setup (Unity)

In your project, add the following lines to your `Packages/manifest.json` file just before "dependencies" section and change url and [scopes](https://docs.unity3d.com/Manual/upm-scoped.html) as necessary:

```
  "scopedRegistries": [
    {
      "name": "My Local Registry",
      "url": "http://localhost",
      "scopes": [
        "com.deepdreamgames"
      ]
    }
  ],
```


# Changelog

* 1.0.0 - initial release


# License

[MIT License](https://raw.githubusercontent.com/deepdreamgames/pachka/main/LICENSE)


# Support

Buy me a beer [<img src="https://img.shields.io/badge/%24-donate-yellow">](https://paypal.me/slice3d)