using System;
using System.IO;
using System.Windows.Forms;
using Dinah.Core.Logging;
using FileManager;
using LibationWinForms;
using LibationWinForms.Dialogs;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LibationLauncher
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			Application.SetHighDpiMode(HighDpiMode.SystemAware);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			createSettings();
			initLogging();

			Application.Run(new Form1());
		}

		private static void createSettings()
		{
			var config = Configuration.Instance;
			if (configSetupIsComplete(config))
				return;

			var isAdvanced = false;

			var setupDialog = new SetupDialog();
			setupDialog.NoQuestionsBtn_Click += (_, __) =>
			{
				config.DecryptKey ??= "";
				config.LocaleCountryCode ??= "us";
				config.DownloadsInProgressEnum ??= "WinTemp";
				config.DecryptInProgressEnum ??= "WinTemp";
				config.Books ??= Configuration.AppDir;
			};
			// setupDialog.BasicBtn_Click += (_, __) => // no action needed
			setupDialog.AdvancedBtn_Click += (_, __) => isAdvanced = true;
			setupDialog.ShowDialog();

			if (isAdvanced)
			{
				var dialog = new LibationFilesDialog();
				if (dialog.ShowDialog() != DialogResult.OK)
					MessageBox.Show("Libation Files location not changed");
			}

			if (configSetupIsComplete(config))
				return;

			if (new SettingsDialog().ShowDialog() == DialogResult.OK)
				return;

			MessageBox.Show("Initial set up cancelled.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			Application.Exit();
			Environment.Exit(0);
		}

		private static bool configSetupIsComplete(Configuration config)
			=> config.FilesExist
			&& !string.IsNullOrWhiteSpace(config.LocaleCountryCode)
			&& !string.IsNullOrWhiteSpace(config.DownloadsInProgressEnum)
			&& !string.IsNullOrWhiteSpace(config.DecryptInProgressEnum);

		private static void initLogging()
		{
			var config = Configuration.Instance;

			ensureLoggingConfig(config);
			ensureSerilogConfig(config);

			// override path. always use current libation files
			var logPath = Path.Combine(Configuration.Instance.LibationFiles, "Log.log");
			config.SetWithJsonPath("Serilog.WriteTo[1].Args", "path", logPath);

			//// hack which achieves the same
			//configuration["Serilog:WriteTo:1:Args:path"] = logPath;

			// CONFIGURATION-DRIVEN (json)
			var configuration = new ConfigurationBuilder()
				.AddJsonFile(config.SettingsJsonPath)
				.Build();
			Log.Logger = new LoggerConfiguration()
			  .ReadFrom.Configuration(configuration)
			  .CreateLogger();

			//// MANUAL HARD CODED
			//Log.Logger = new LoggerConfiguration()
			//	.Enrich.WithCaller()
			//	.MinimumLevel.Information()
			//	.WriteTo.File(logPath,
			//		rollingInterval: RollingInterval.Month,
			//		outputTemplate: code_outputTemplate)
			//	.CreateLogger();

			Log.Logger.Information("Begin Libation");

			// .Here() captures debug info via System.Runtime.CompilerServices attributes. Warning: expensive
			//var withLineNumbers_outputTemplate = "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message}{NewLine}in method {MemberName} at {FilePath}:{LineNumber}{NewLine}{Exception}{NewLine}";
			//Log.Logger.Here().Debug("Begin Libation. Debug with line numbers");
		}

		private static string defaultLoggingLevel = "Information";
		private static void ensureLoggingConfig(Configuration config)
		{
			if (config.GetObject("Logging") != null)
				return;

			// "Logging": {
			//   "LogLevel": {
			//     "Default": "Debug"
			//   }
			// }
			var loggingObj = new JObject
			{
				{
					"LogLevel", new JObject { { "Default", defaultLoggingLevel } }
				}
			};
			config.SetObject("Logging", loggingObj);
		}

		private static void ensureSerilogConfig(Configuration config)
		{
			if (config.GetObject("Serilog") != null)
				return;

			// default. for reference. output example:
			// 2019-11-26 08:48:40.224 -05:00 [DBG] Begin Libation
			var default_outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
			// with class and method info. output example:
			// 2019-11-26 08:48:40.224 -05:00 [DBG] (at LibationWinForms.Program.init()) Begin Libation
			var code_outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (at {Caller}) {Message:lj}{NewLine}{Exception}";

			// "Serilog": {
			//   "MinimumLevel": "Information"
			//   "WriteTo": [
			//     {
			//       "Name": "Console"
			//     },
			//     {
			//       "Name": "File",
			//       "Args": {
			//         "rollingInterval": "Day",
			//         "outputTemplate": ...
			//       }
			//     }
			//   ],
			//   "Using": [ "Dinah.Core" ],
			//   "Enrich": [ "WithCaller" ]
			// }
			var serilogObj = new JObject
			{
				{ "MinimumLevel", defaultLoggingLevel },
				{ "WriteTo", new JArray
					{
						new JObject { {"Name", "Console" } },
						new JObject
						{
							{ "Name", "File" },
							{ "Args",
								new JObject
								{
									// for this sink to work, a path must be provided. we override this below
									{ "path", Path.Combine(Configuration.Instance.LibationFiles, "_Log.log") },
									{ "rollingInterval", "Month" },
									{ "outputTemplate", code_outputTemplate }
								}
							}
						}
					}
				},
				{ "Using", new JArray{ "Dinah.Core" } }, // dll's name, NOT namespace
				{ "Enrich", new JArray{ "WithCaller" } },
			};
			config.SetObject("Serilog", serilogObj);
		}
	}
}