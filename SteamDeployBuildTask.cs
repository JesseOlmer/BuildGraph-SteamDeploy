using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using EpicGames.Core;
using Microsoft.Extensions.Logging;
using UnrealBuildBase;
using IdentityModel.Client;

namespace AutomationTool.Tasks
{
	/// <summary>
	/// Parameters for a <see cref="SteamAuthTask"/>.
	/// </summary>
	public class SteamAuthTaskParameters
	{
		/// <summary>
		/// Environment Variable name which contains the Base64 encoded config.vdf file authenticated for the build account
		/// </summary>
		[TaskParameter(Optional = true)] public string ConfigVdfEnvVar = "SteamConfigVdf";
	}

	/// <summary>
	/// Load the steamcmd config.vdf from an base64 encoded zip file in an environment variable
	/// </summary>
	[TaskElement("Steam-Auth", typeof(SteamAuthTaskParameters))]
	public class SteamAuthTask : SteamTaskBase
	{
		private SteamAuthTaskParameters Parameters;

		/// <summary>
		/// Constructor
		/// </summary>
		public SteamAuthTask(SteamAuthTaskParameters InParameters)
		{
			Parameters = InParameters;
		}

		/// <inheritdoc />
		public override Task ExecuteAsync(JobContext Job, HashSet<FileReference> BuildProducts, Dictionary<string, HashSet<FileReference>> TagNameToFileSet)
		{
			Logger.LogInformation("Creating SteamCmd config.vdf from environment");
			List<FileReference> OutputFiles = new List<FileReference>();

			string base64 = System.Environment.GetEnvironmentVariable(Parameters.ConfigVdfEnvVar);
			if (string.IsNullOrEmpty(base64))
			{
				throw new AutomationException("Environment variable {0} not set", Parameters.ConfigVdfEnvVar);
			}

			DirectoryReference ConfigDir = DirectoryReference.Combine(SteamContentBuilderDir, "config");
			byte[] bytes = Convert.FromBase64String(base64);
			try
			{
				using (MemoryStream ms = new MemoryStream(bytes))
				{
					using (ZipArchive ZipArchive = new ZipArchive(ms, ZipArchiveMode.Read))
					{
						foreach (ZipArchiveEntry Entry in ZipArchive.Entries)
						{
							if(Entry.FullName.EndsWith("/"))
							{
								// ignore directories
								continue;
							}

							FileReference OutputFile = FileReference.Combine(ConfigDir, Entry.FullName);
							Logger.LogInformation($"Extracting {Entry.FullName} to {OutputFile.FullName}");

							DirectoryReference.CreateDirectory(OutputFile.Directory);
							Entry.ExtractToFile_CrossPlatform(OutputFile.FullName, true);
							OutputFiles.Add(OutputFile);
						}
					}
				}
			}
			catch (Exception e)
			{
				throw new AutomationException(ExitCode.Error_UnknownDeployFailure, e, "Failed extracting steam config.vdf");
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public override void Write(XmlWriter Writer)
		{
			Write(Writer, Parameters);
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindConsumedTagNames()
		{
			yield break;
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindProducedTagNames()
		{
			yield break;
		}
	}

	/// <summary>
	/// Parameters for a <see cref="SteamCreateAppManifestTask"/>.
	/// </summary>
	public class SteamCreateAppManifestTaskParameters
	{
		/// <summary>
		/// Steam AppId
		/// </summary>
		[TaskParameter] public int AppId;

		/// <summary>
		/// Description of the build in the steamworks build database
		/// </summary>
		[TaskParameter(Optional = true)] public string BuildDescription = string.Empty;

		/// <summary>
		/// Root directory for content. Depot path(s) will be relative to this path.
		/// </summary>
		[TaskParameter] public DirectoryReference ContentRootDir = null!;

		/// <summary>
		/// Directory containing the files to be uploaded to the first depot. Path is relative to <see cref="ContentRootDir"/>.
		/// </summary>
		[TaskParameter] public string Depot1LocalDir = string.Empty;

		/// <summary>
		/// Relative path in the installed Steam depot. Corresponds to Depot1.FileMapping.DepotPath in the app manifest.
		/// </summary>
		[TaskParameter] public string Depot1DepotPath = string.Empty;

		/// <summary>
		/// Set this build live on the specified branch
		/// </summary>
		[TaskParameter] public string ReleaseBranch = string.Empty;

		/// <summary>
		/// Path to write the manifest file to
		/// </summary>
		[TaskParameter] public string ManifestOutputFile = null!;

		/// <summary>
		/// Build cache and log file output directory
		/// </summary>
		[TaskParameter(Optional = true)] public string BuildOutputDir = "BuildOutput";

		/// <summary>
		/// Tag to be applied to build products of this task.
		/// </summary>
		[TaskParameter(Optional = true, ValidationType = TaskParameterValidationType.TagList)]
		public string Tag = string.Empty;
	}

	/// <summary>
	/// Write deployment and depot manifests for a Steam build upload.
	/// </summary>
	[TaskElement("Steam-CreateAppManifest", typeof(SteamCreateAppManifestTaskParameters))]
	public class SteamCreateAppManifestTask : SteamTaskBase
	{
		private SteamCreateAppManifestTaskParameters Parameters;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="InParameters"></param>
		public SteamCreateAppManifestTask(SteamCreateAppManifestTaskParameters InParameters)
		{
			Parameters = InParameters;
		}

		/// <inheritdoc />
		public override async Task ExecuteAsync(JobContext Job, HashSet<FileReference> BuildProducts, Dictionary<string, HashSet<FileReference>> TagNameToFileSet)
		{
			if (Parameters.Depot1DepotPath == "")
			{
				Logger.Log(LogLevel.Information, "Depot1DepotPath parameter not set. Defaulting to root directory ('.')");
				Parameters.Depot1DepotPath = ".";
			}

			int depot1Id = Parameters.AppId + 1;
			string AppManifestText = $@"
""AppBuild""
{{
	""AppID"" ""{Parameters.AppId}""
	""Desc"" ""{Parameters.BuildDescription}""
	""SetLive"" ""{Parameters.ReleaseBranch}""
	""ContentRoot"" ""{Parameters.ContentRootDir}""
	""BuildOutput"" ""{Parameters.BuildOutputDir}""
	""Depots""
	{{
		""{depot1Id}""
		{{
			""FileMapping""
			{{
				""LocalPath"" ""{Parameters.Depot1LocalDir}/*""
				""DepotPath"" ""{Parameters.Depot1DepotPath}""
				""Recursive"" ""1""
			}}
		}}
	}}
}}";

			if (!Directory.Exists(Parameters.ContentRootDir.FullName))
			{
				throw new AutomationException($"{nameof(Parameters.ContentRootDir)} must exist.");
			}

			if (!Directory.Exists(Path.Combine(Parameters.ContentRootDir.FullName, Parameters.Depot1LocalDir)))
			{
				throw new AutomationException(
					$"{nameof(Parameters.Depot1LocalDir)} must be relative to {nameof(Parameters.ContentRootDir)}, and must exist.");
			}
			
			FileReference AppManifestFile = ResolveFile(Parameters.ManifestOutputFile);
			if (!DirectoryReference.Exists(AppManifestFile.Directory))
			{
				DirectoryReference.CreateDirectory(AppManifestFile.Directory);
			}

			await FileReference.WriteAllTextAsync(AppManifestFile, AppManifestText);

			foreach (string TagName in FindTagNamesFromList(Parameters.Tag))
			{
				FindOrAddTagSet(TagNameToFileSet, TagName).Add(AppManifestFile);
			}

			BuildProducts.Add(AppManifestFile);
		}

		/// <inheritdoc />
		public override void Write(XmlWriter Writer)
		{
			Write(Writer, Parameters);
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindConsumedTagNames()
		{
			yield break;
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindProducedTagNames()
		{
			return FindTagNamesFromList(Parameters.Tag);
		}
	}

	/// <summary>
	/// Parameters for a <see cref="SteamDeployBuildTask"/>.
	/// </summary>
	public class SteamDeployBuildTaskParameters
	{
		/// <summary>
		/// Steam username for build account
		/// </summary>
		[TaskParameter] public string Username = string.Empty;

		/// <summary>
		/// Path to the application manifest which should be uploaded
		/// </summary>
		[TaskParameter] public string AppManifestFile;
	}

	/// <summary>
	/// Write deployment and depot manifests for a Steam build upload.
	/// </summary>
	[TaskElement("Steam-DeployBuild", typeof(SteamDeployBuildTaskParameters))]
	public class SteamDeployBuildTask : SteamTaskBase
	{
		private SteamDeployBuildTaskParameters Parameters;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="InParameters"></param>
		public SteamDeployBuildTask(SteamDeployBuildTaskParameters InParameters)
		{
			Parameters = InParameters;

			if (string.IsNullOrEmpty(Parameters.Username))
			{
				throw new AutomationException("Steam build username not set");
			}
		}

		/// <inheritdoc />
		public override Task ExecuteAsync(JobContext Job, HashSet<FileReference> BuildProducts, Dictionary<string, HashSet<FileReference>> TagNameToFileSet)
		{
			FileReference AppManifest = ResolveFile(Parameters.AppManifestFile);
			if (!File.Exists(AppManifest.FullName))
			{
				throw new AutomationException($"AppManifest file not found: {AppManifest.FullName}");
			}

			DirectoryReference SteamCmdLogs = DirectoryReference.Combine(SteamContentBuilderDir, "logs");
			try
			{
				FileUtils.ForceDeleteDirectoryContents(SteamCmdLogs);
				
				// TODO(jesse): Change paths based on platform
				FileReference SteamCmdExe = FileReference.Combine(SteamContentBuilderDir, "steamcmd.exe");
				if (!FileReference.Exists(SteamCmdExe))
				{
					throw new AutomationException($"SteamCmd is missing from deployment. Check AutoSDK. Searched {SteamCmdExe.FullName}");
				}

				return ExecuteAsync(SteamCmdExe.FullName, $"+login \"{Parameters.Username}\" +run_app_build \"{AppManifest.FullName}\" +quit", SteamCmdExe.Directory.FullName);
			}
			catch
			{
				// TODO: Tag steamcmd output as build artifacts?
				throw;
			}
		}

		/// <inheritdoc />
		public override void Write(XmlWriter Writer)
		{
			Write(Writer, Parameters);
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindConsumedTagNames()
		{
			foreach(string TagName in FindTagNamesFromFilespec(Parameters.AppManifestFile))
			{
				yield return TagName;
			}
		}

		/// <inheritdoc />
		public override IEnumerable<string> FindProducedTagNames()
		{
			yield break;
		}
	}

	/// <summary>
	/// Base class for Steam tasks
	/// </summary>
	public abstract class SteamTaskBase : SpawnTaskBase
	{
		/// <summary>
		/// Directory containing the Steam Content Builder part of the sdk
		/// </summary>
		protected readonly DirectoryReference SteamContentBuilderDir;

		/// <summary>
		/// Constructor
		/// </summary>
		protected SteamTaskBase()
		{
			string AutoSdkRoot = Environment.GetEnvironmentVariable("UE_SDKS_ROOT");
			if (string.IsNullOrEmpty(AutoSdkRoot))
			{
				throw new AutomationException("Environment variable UE_SDKS_ROOT not set");
			}
			SteamContentBuilderDir = new DirectoryReference(Path.Combine(AutoSdkRoot, "HostWin64/Win64/steam/tools/ContentBuilder/builder"));
		}
	}
}
