Unreal BuildGraph Steam Deploy

This set of tasks deals with login, manifest generation, and deployment to a steam app.
Currently only supports 1 depot per build.

The ConfigVdfEnvVar is expected to be a base64 encoded zip file containing your config.vdf authentication file for steamcmd.

Example node graph:
```
	<Node Name="Deploy to Steam" Requires="#CookedFiles">
		<Property Name="ConfigEnvVarName" Value="STEAM_CONFIG_VDF" />
		<Property Name="AppManifestFile" Value="$(SteamOutputDir)/app_$(AppId).vdf" />
		<Delete Files="$(AppManifestFile)" />
		<Horde-SetSecretEnvVar Name="$(ConfigEnvVarName)" Secret="SteamConfigVdf.value" If="$(IsBuildMachine)" />
		<Steam-Auth ConfigVdfEnvVar="$(ConfigEnvVarName)" />
		<Steam-CreateAppManifest
			AppId="1234"
			BuildDescription="Build for CL 123"
			ReleaseBranch="steambranch"
			ContentRootDir="$(PackageBuildOutputDir)"
			Depot1LocalDir="OptionalGameContentSubdirectoryInContentRootDir"
			Depot1DepotPath="InstallLocationOnSteamClients"
			ManifestOutputFile="$(AppManifestFile)"
			Tag="#SteamAppManifest" />
		<Steam-DeployBuild Username="yoursteambuilduseraccount" AppManifestFile="$(AppManifestFile)" />
	</Node>
```

### Creating the ConfigVdf file
1. `$ steamcmd +login <username> <password> +quit`
2. Finish login process (may need steam guard or webpage)
3. Zip config.vdf and get the base64 of the zip file. Powershell example:
```
PS > $zipPath = (Get-Item .).FullName + "\config.zip"
PS > Compress-Archive -Path .\config\config.vdf -DestinationPath $zipPath
PS > [Convert]::ToBase64String([IO.File]::ReadAllBytes($zipPath))
```
4. Put this string in a secure location available to your graph. The example node graph above uses a horde secret, but ultimately you just need it in an environment variable that the `Steam-Auth` task can read.
