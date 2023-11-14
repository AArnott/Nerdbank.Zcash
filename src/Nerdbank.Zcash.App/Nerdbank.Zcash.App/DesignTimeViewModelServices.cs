﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Nerdbank.Cryptocurrencies.Exchanges;

namespace Nerdbank.Zcash.App;

internal class DesignTimeViewModelServices : IViewModelServices
{
	internal DesignTimeViewModelServices(bool empty = false)
	{
		if (!empty)
		{
			// Populate accounts.
			Bip39Mnemonic mnemonic = Bip39Mnemonic.Create(Zip32HDWallet.MinimumEntropyLengthInBits);
			HDWallet zec = new(new(mnemonic, ZcashNetwork.MainNet)) { Name = "Real money" };
			HDWallet taz = new(new(mnemonic, ZcashNetwork.TestNet)) { Name = "Play money" };

			this.Wallet.Add(zec);
			this.Wallet.Add(taz);

			Account playMoneyAccount = new(new ZcashAccount(taz.Zip32, 0)) { Name = Strings.FormatDefaultNameForFirstAccountWithTicker(taz.Zip32.Network), Balance = 1.23m };
			Account realAccount = new(new ZcashAccount(zec.Zip32, 0)) { Name = "Real ZEC", Balance = 0.023m };
			Account savingsAccount = new(new ZcashAccount(zec.Zip32, 1)) { Name = "Savings", Balance = 3.45m };

			this.Wallet.Add(playMoneyAccount);
			this.Wallet.Add(savingsAccount);
			this.Wallet.Add(realAccount);

			// Populate address book.
			this.ContactManager.Add(new Contact { Name = "Andrew Arnott", ReceivingAddress = ZcashAddress.Decode("t1a7w3qM23i4ajQcbX5wd6oH4zTY8Bry5vF") });
			this.ContactManager.Add(new Contact { Name = "Jason Arnott", ReceivingAddress = ZcashAddress.Decode("u17kydrnuh9k8dqtud9qugel5ym835xqg8jk5czy2qcxea0zucru7d9w0c9hcq43898l2d993taaqh6vr0u6yskjnn582vyvu8qqk6qyme0z2vfgcclxatca7cx2f45v2n9zfd7hmkwlrw0wt38z9ua2yvgdnvppucyf2cfsxwlyfy339k") });
			this.ContactManager.Add(new Contact { Name = "David Arnott" });
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	[Obsolete("Design-time only.")] // necessary to avoid the compile error about App() being obsolete
	public App App { get; } = new();

	public AppPlatformSettings AppPlatformSettings { get; } = App.CreateDesignTimeAppPlatformSettings();

	public ZcashWallet Wallet { get; } = new();

	public Account? MostRecentlyUsedAccount { get; set; }

	public IContactManager ContactManager { get; } = new DesignTimeContactManager();

	public TopLevel? TopLevel => null;

	public AppSettings Settings { get; } = new();

	public IExchangeRateProvider ExchangeRateProvider { get; } = new MockExchange();

	public IHistoricalExchangeRateProvider HistoricalExchangeRateProvider { get; } = new MockExchange();

	public void NavigateBack(ViewModelBase? ifCurrentViewModel)
	{
	}

	public T NavigateTo<T>(T viewModel)
		where T : ViewModelBase
	{
		return viewModel;
	}

	public T ReplaceViewStack<T>(T viewModel)
		where T : ViewModelBase
	{
		return viewModel;
	}

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	private class DesignTimeContactManager : IContactManager
	{
		private ObservableCollection<Contact> contacts = new();

		public DesignTimeContactManager()
		{
			this.Contacts = new(this.contacts);
		}

		public ReadOnlyObservableCollection<Contact> Contacts { get; }

		public void Add(Contact contact) => this.contacts.Add(contact);

		public bool Remove(Contact contact) => this.Remove(contact);
	}

	private class MockExchange : IExchangeRateProvider, IHistoricalExchangeRateProvider
	{
		public ValueTask<IReadOnlySet<TradingPair>> GetAvailableTradingPairsAsync(CancellationToken cancellationToken)
		{
			return new ValueTask<IReadOnlySet<TradingPair>>(ImmutableHashSet.Create(new TradingPair(Security.USD, Security.ZEC)));
		}

		public ValueTask<ExchangeRate> GetExchangeRateAsync(TradingPair tradingPair, DateTimeOffset when, CancellationToken cancellationToken)
		{
			return new(new ExchangeRate(tradingPair.Basis.Amount(10 + (when.Day * 2)), tradingPair.TradeInterest.Amount(1)));
		}

		public ValueTask<ExchangeRate> GetExchangeRateAsync(TradingPair tradingPair, CancellationToken cancellationToken)
		{
			return new(new ExchangeRate(tradingPair.Basis.Amount(30), tradingPair.TradeInterest.Amount(1)));
		}
	}
}
