using System;
using System.Collections.Generic;
using AppHub.Helpers;
using AppHub.Models;
using AppHub.Services;

namespace AppHub.Infrastructure;

public static class AppServices
{
	private static bool _initialized;

	public static StorageService Storage { get; private set; }

	public static AppConfig Config { get; private set; }

	public static AppLogger Logger { get; private set; }

	public static AppCatalogService Catalog { get; private set; }

	public static IconService IconService { get; private set; }

	public static LaunchService LaunchService { get; private set; }

	public static ProcessControlService ProcessService { get; private set; }

	public static StatusRefreshScheduler StatusScheduler { get; private set; }

	public static AutoStartService AutoStartService { get; private set; }

	public static void Initialize()
	{
		if (!_initialized)
		{
			Storage = new StorageService();
			Config = Storage.Load<AppConfig>() ?? new AppConfig();
			MigrateConfig();
			if (string.IsNullOrWhiteSpace(Config.Settings.LogDirectory))
			{
				Config.Settings.LogDirectory = PathHelper.GetDefaultLogDirectory();
				Storage.Save(Config);
			}
			Logger = new AppLogger(Config.Settings.LogDirectory);
			Logger.SetLogMaxSizeBytes((long)Config.Settings.LogMaxSizeMb * 1024L * 1024);
			Catalog = new AppCatalogService(Storage, Config, Logger);
			IconService = new IconService(Logger);
			ProcessService = new ProcessControlService(Catalog, Config.Settings, Logger);
			LaunchService = new LaunchService(Catalog, ProcessService, Logger);
			StatusScheduler = new StatusRefreshScheduler(ProcessService, Config.Settings, Logger);
			AutoStartService = new AutoStartService(Logger);
			if (Config.Settings.AutoStart)
			{
				AutoStartService.Enable();
			}
			_initialized = true;
		}
	}

	private static void MigrateConfig()
	{
		bool changed = false;
		if (Config.SchemaVersion < 1)
		{
			Config.SchemaVersion = 1;
			changed = true;
		}
		if (Config.SchemaVersion < 2)
		{
			foreach (ApplicationItem app in Config.Apps)
			{
				app.GroupStates ??= new Dictionary<string, ApplicationGroupState>();
				Dictionary<string, ApplicationGroupState> normalized = new Dictionary<string, ApplicationGroupState>();
				foreach (KeyValuePair<string, ApplicationGroupState> pair in app.GroupStates)
				{
					string scope = AppGroupScope.Normalize(pair.Key);
					normalized[scope] = new ApplicationGroupState
					{
						IsPinned = pair.Value?.IsPinned ?? false
					};
				}
				app.GroupStates = normalized;
				if (app.IsPinned && !app.GetPinned(AppGroupScope.AllGroups))
				{
					app.SetPinned(AppGroupScope.AllGroups, isPinned: true);
				}
			}
			Config.SchemaVersion = 2;
			changed = true;
		}
		if (Config.SchemaVersion < 3)
		{
			if (Config.Settings.BackgroundEffectStrength < 0 || Config.Settings.BackgroundEffectStrength > 100)
			{
				Config.Settings.BackgroundEffectStrength = 40;
			}
			if (!Enum.IsDefined(typeof(BackgroundStyle), Config.Settings.BackgroundStyle))
			{
				Config.Settings.BackgroundStyle = BackgroundStyle.None;
			}
			Config.SchemaVersion = 3;
			changed = true;
		}
		if (Config.SchemaVersion < 4)
		{
			if (!Enum.IsDefined(typeof(BackgroundStyle), Config.Settings.LightBackgroundStyle))
			{
				Config.Settings.LightBackgroundStyle = BackgroundStyle.None;
			}
			if (!Enum.IsDefined(typeof(BackgroundStyle), Config.Settings.DarkBackgroundStyle))
			{
				Config.Settings.DarkBackgroundStyle = BackgroundStyle.None;
			}
			if (Config.Settings.LightBackgroundEffectStrength < 0 || Config.Settings.LightBackgroundEffectStrength > 100)
			{
				Config.Settings.LightBackgroundEffectStrength = 40;
			}
			if (Config.Settings.DarkBackgroundEffectStrength < 0 || Config.Settings.DarkBackgroundEffectStrength > 100)
			{
				Config.Settings.DarkBackgroundEffectStrength = 40;
			}
			if (!string.IsNullOrWhiteSpace(Config.Settings.BackgroundImagePath) && string.IsNullOrWhiteSpace(Config.Settings.LightBackgroundImagePath))
			{
				Config.Settings.LightBackgroundImagePath = Config.Settings.BackgroundImagePath;
			}
			if (Config.Settings.LightBackgroundStyle == BackgroundStyle.None && Config.Settings.BackgroundStyle != BackgroundStyle.None)
			{
				Config.Settings.LightBackgroundStyle = Config.Settings.BackgroundStyle;
			}
			if (Config.Settings.LightBackgroundEffectStrength == 40 && Config.Settings.BackgroundEffectStrength != 40)
			{
				Config.Settings.LightBackgroundEffectStrength = Config.Settings.BackgroundEffectStrength;
			}
			Config.SchemaVersion = 4;
			changed = true;
		}
		if (Config.SchemaVersion < 5)
		{
			Config.Settings.LightSidebarOpacity = 92;
			Config.Settings.LightProgramOpacity = 92;
			Config.Settings.DarkSidebarOpacity = 88;
			Config.Settings.DarkProgramOpacity = 88;
			Config.SchemaVersion = 5;
			changed = true;
		}
		if (Config.SchemaVersion < AppConfig.CurrentSchemaVersion)
		{
			Config.SchemaVersion = AppConfig.CurrentSchemaVersion;
			changed = true;
		}
		if (changed)
		{
			Storage.Save(Config);
		}
	}

	public static void ApplyCommandLine(CommandLineOptions options)
	{
		bool changed = false;
		if (options.LogDirectory != null && options.LogDirectory.Length > 0)
		{
			Config.Settings.LogDirectory = options.LogDirectory;
			Logger.SetLogDirectory(options.LogDirectory);
			changed = true;
		}
		if (options.AutoStart.HasValue)
		{
			Config.Settings.AutoStart = options.AutoStart.Value;
			if (options.AutoStart.Value)
			{
				AutoStartService.Enable();
			}
			else
			{
				AutoStartService.Disable();
			}
			changed = true;
		}
		if (changed)
		{
			Storage.Save(Config);
		}
	}
}
