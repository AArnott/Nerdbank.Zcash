// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Zcash.App.Views;
using IAsyncDisposable = System.IAsyncDisposable;

namespace Nerdbank.Zcash.App;

public partial class App : Application, IAsyncDisposable
{
	private const string DataFileName = "wallet.dat";
	private const string SettingsJsonFileName = "settings.json";

	private readonly List<Task> sendTransactionTasks = new();
	private readonly CancellationTokenSource shutdownTokenSource = new();

	private JoinableTaskContext? joinableTaskContext;
	private JoinableTaskFactory? shutdownBlockingTaskFactory;
	private JoinableTaskCollection? shutdownBlockingTasks;

	private AutoSaveManager<AppSettings>? appSettingsManager;
	private AppSettings? settings;
	private DataRoot? data;
	private AutoSaveManager<DataRoot>? dataRootManager;
	private WalletSyncManager? walletSyncManager;

	[Obsolete("Design-time only", error: true)]
	public App()
		: this(CreateDesignTimeAppPlatformSettings(), new DesignTimePlatformServices(), null)
	{
		Assumes.NotNull(SynchronizationContext.Current);
		this.InitializeFields();
	}

	public App(AppPlatformSettings platformSettings, IPlatformServices platformServices, string? velopackUpdateUrl)
	{
		this.AppPlatformSettings = platformSettings;
		this.PlatformServices = platformServices;
		this.SelfUpdating = new(this, velopackUpdateUrl);
	}

	public static new App? Current => (App?)Application.Current;

	public bool IsDesignTime => this.PlatformServices is DesignTimePlatformServices;

	public AppSettings Settings => this.settings ?? throw new InvalidOperationException();

	public UpdatingViewModel SelfUpdating { get; }

	public DataRoot Data => this.data ?? throw new InvalidOperationException();

	public AppPlatformSettings AppPlatformSettings { get; }

	public IPlatformServices PlatformServices { get; }

	/// <inheritdoc/>
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	/// <inheritdoc/>
	public override void OnFrameworkInitializationCompleted()
	{
		this.InitializeFields();

		MainViewModel mainViewModel;
		if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow
			{
				DataContext = mainViewModel = new MainWindowViewModel(this),
			};

			// Give ourselves a chance to clean up nicely.
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			desktop.MainWindow.Closed += (_, _) => this.DisposeAsync().Forget();

			if (this.SelfUpdating is not null)
			{
				_ = this.shutdownBlockingTaskFactory!.RunAsync(() => this.SelfUpdating.UpdateMyAppAsync(this.shutdownTokenSource.Token));
			}

			mainViewModel.TopLevel = TopLevel.GetTopLevel(desktop.MainWindow);
		}
		else if (this.ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
		{
			singleViewPlatform.MainView = new MainView
			{
				DataContext = mainViewModel = new MainViewModel(this),
			};
			mainViewModel.TopLevel = TopLevel.GetTopLevel(singleViewPlatform.MainView);
		}

		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>
	/// Initializes an application. Useful for unit tests to directly call.
	/// </summary>
	public void InitializeFields()
	{
		if (this.appSettingsManager is not null)
		{
			return;
		}

		Assumes.NotNull(SynchronizationContext.Current);
		this.joinableTaskContext = new JoinableTaskContext();
		this.shutdownBlockingTasks = this.joinableTaskContext.CreateCollection();
		this.shutdownBlockingTaskFactory = this.joinableTaskContext.CreateFactory(this.shutdownBlockingTasks);

		this.appSettingsManager = this.AppPlatformSettings.NonConfidentialDataPath is not null ? AutoSaveManager<AppSettings>.LoadOrCreate(Path.Combine(this.AppPlatformSettings.NonConfidentialDataPath, SettingsJsonFileName), enableAutoSave: true) : null;
		this.dataRootManager = this.AppPlatformSettings.ConfidentialDataPath is not null ? AutoSaveManager<DataRoot>.LoadOrCreate(Path.Combine(this.AppPlatformSettings.ConfidentialDataPath, DataFileName), enableAutoSave: true) : null;
		this.settings = this.appSettingsManager?.Data ?? new AppSettings();
		this.data = this.dataRootManager?.Data ?? new DataRoot();

		if (this.AppPlatformSettings.ConfidentialDataPath is not null)
		{
			this.walletSyncManager = new WalletSyncManager(this.joinableTaskContext, this.AppPlatformSettings.ConfidentialDataPath, this.Data.Wallet, this.Settings, this.Data.ContactManager, this.Data.ExchangeRates, this.PlatformServices);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await this.shutdownTokenSource.CancelAsync();

		await this.WaitForSendsAsync();

		if (this.walletSyncManager is not null)
		{
			await this.walletSyncManager.DisposeAsync();
		}

		if (this.appSettingsManager is not null)
		{
			await this.appSettingsManager.DisposeAsync();
		}

		if (this.dataRootManager is not null)
		{
			await this.dataRootManager.DisposeAsync();
		}

		if (this.shutdownBlockingTasks is not null)
		{
			await this.shutdownBlockingTasks.JoinTillEmptyAsync();
		}

		if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.Shutdown();
		}
	}

	public void StartSync()
	{
		this.walletSyncManager?.StartSyncing(this.Data.Wallet);
	}

	public void RegisterSendTransactionTask(Task sendTask)
	{
		this.sendTransactionTasks.Add(sendTask);
		_ = sendTask.ContinueWith(this.sendTransactionTasks.Remove, TaskScheduler.FromCurrentSynchronizationContext());
	}

	internal static AppPlatformSettings CreateDesignTimeAppPlatformSettings()
	{
		return new()
		{
			ConfidentialDataPath = null,
			NonConfidentialDataPath = null,
		};
	}

	private async Task WaitForSendsAsync()
	{
		while (this.sendTransactionTasks.Count > 0)
		{
			await Task.WhenAll(this.sendTransactionTasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	private class DesignTimePlatformServices : IPlatformServices
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsOnACPower => false;

		public bool IsNetworkMetered => false;

		public IDisposable? RequestSleepDeferral() => null;

		protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => this.PropertyChanged?.Invoke(this, e);
	}
}
