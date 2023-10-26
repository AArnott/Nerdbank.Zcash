﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Nerdbank.Cryptocurrencies;

namespace Nerdbank.Zcash.App.ViewModels;

public class FirstLaunchViewModel : ViewModelBase, IHasTitle
{
	private readonly IViewModelServices viewModelServices;

	[Obsolete("For design-time use only.", error: true)]
	public FirstLaunchViewModel()
		: this(new DesignTimeViewModelServices(empty: true))
	{
	}

	public FirstLaunchViewModel(IViewModelServices viewModelServices)
	{
		this.StartNewWalletCommand = ReactiveCommand.Create(this.CreateNewAccount);
		this.StartNewWalletAdvancedCommand = ReactiveCommand.Create(this.CreateNewAccountAdvanced);
		this.ImportWalletCommand = ReactiveCommand.Create(this.ImportWallet);
		this.viewModelServices = viewModelServices;
	}

	public string Title => "Welcome";

	public string Greeting => Strings.AppGreeting;

	public Bitmap Logo => Resources.ZcashLogo;

	public string StartNewWalletButtonText => Strings.StartNewWalletButtonText;

	public string StartNewWalletExplanation => Strings.StartNewWalletExplanation;

	public string ImportWalletButtonText => Strings.ImportWalletButtonText;

	public string ImportWalletExplanation => Strings.ImportWalletExplanation;

	public ReactiveCommand<Unit, Unit> StartNewWalletCommand { get; }

	public string StartNewWalletAdvancedCommandCaption => Strings.StartNewWalletAdvancedCommandCaption;

	public ReactiveCommand<Unit, Unit> StartNewWalletAdvancedCommand { get; }

	public ReactiveCommand<Unit, Unit> ImportWalletCommand { get; }

	private void CreateNewAccount()
	{
		Bip39Mnemonic mnemonic = Bip39Mnemonic.Create(Zip32HDWallet.MinimumEntropyLengthInBits);
		Zip32HDWallet zip32 = new(mnemonic, ZcashNetwork.MainNet);
		Account accountModel = this.viewModelServices.Wallet.Add(new ZcashAccount(zip32));
		accountModel.Name = "Main";

		this.viewModelServices.SelectedAccount = this.viewModelServices.Wallet.First();
		this.viewModelServices.ReplaceViewStack(new HomeScreenViewModel(this.viewModelServices));
	}

	private void CreateNewAccountAdvanced()
	{
		this.viewModelServices.NavigateTo(new CreateNewWalletViewModel(this.viewModelServices));
	}

	private void ImportWallet()
	{
		ImportAccountViewModel importAccountViewModel = new(this.viewModelServices);
		importAccountViewModel.ImportCommand.Subscribe(account =>
		{
			if (account is not null)
			{
				Account accountModel = this.viewModelServices.Wallet.Add(account);

				// The user imported the wallet to begin with, so they evidently have a copy somewhere else.
				if (accountModel.MemberOf is not null)
				{
					accountModel.MemberOf.IsSeedPhraseBackedUp = true;
				}

				this.viewModelServices.ReplaceViewStack(new HomeScreenViewModel(this.viewModelServices));
			}
		});
		this.viewModelServices.NavigateTo(importAccountViewModel);
	}
}
