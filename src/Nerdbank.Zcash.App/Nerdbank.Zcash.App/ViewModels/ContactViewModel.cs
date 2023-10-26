﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Zcash.App.ViewModels;

public class ContactViewModel : ViewModelBase
{
	private readonly IViewModelServices viewModelServices;
	private readonly ObservableAsPropertyHelper<bool> isEmpty;
	private readonly ObservableAsPropertyHelper<bool> hasShieldedReceivingAddress;
	private readonly ObservableAsPropertyHelper<bool> hasAddress;
	private readonly ObservableAsPropertyHelper<string> sendCommandCaption;
	private readonly ObservableAsPropertyHelper<bool> isShowDiversifiedAddressButtonVisible;
	private string name = string.Empty;
	private string address = string.Empty;
	private string? myAddressShownToContact;

	public ContactViewModel(IViewModelServices viewModelServices)
	{
		this.viewModelServices = viewModelServices;

		this.WhenAnyValue(
			vm => vm.Name,
			vm => vm.Address,
			(n, a) => n is not null || a is not null)
			.ToProperty(this, vm => vm.IsEmpty, out this.isEmpty);
		this.WhenAnyValue(
			vm => vm.Address,
			addr => addr is not null && ZcashAddress.TryDecode(addr, out _, out _, out ZcashAddress? address) && address.HasShieldedReceiver)
			.ToProperty(this, vm => vm.HasShieldedReceivingAddress, out this.hasShieldedReceivingAddress);
		this.WhenAnyValue(
			vm => vm.Address,
			addr => addr is not null && ZcashAddress.TryDecode(addr, out _, out _, out _))
			.ToProperty(this, vm => vm.HasAddress, out this.hasAddress);
		this.WhenAnyValue(
			vm => vm.HasShieldedReceivingAddress,
			has => has ? "Send 🛡️" : "Send")
			.ToProperty(this, vm => vm.SendCommandCaption, out this.sendCommandCaption);
		this.WhenAnyValue<ContactViewModel, bool, string?>(
			vm => vm.MyAddressShownToContact,
			shown => shown is null)
			.ToProperty(this, vm => vm.IsShowDiversifiedAddressButtonVisible, out this.isShowDiversifiedAddressButtonVisible);

		this.ShowDiversifiedAddressCommand = ReactiveCommand.Create(() => { });

		IObservable<bool> canSend = this.WhenAnyValue(vm => vm.HasAddress);
		this.SendCommand = ReactiveCommand.Create(this.Send, canSend);

		this.LinkProperty(nameof(this.MyAddressShownToContact), nameof(this.HasContactSeenMyDiversifiedAddressCaption));
		this.LinkProperty(nameof(this.MyAddressShownToContact), nameof(this.IsShowDiversifiedAddressButtonVisible));
		this.LinkProperty(nameof(this.HasShieldedReceivingAddress), nameof(this.SendCommandCaption));
	}

	public bool IsEmpty => this.isEmpty.Value;

	/// <summary>
	/// Gets or sets the name of the contact.
	/// </summary>
	public string Name
	{
		get => this.name;
		set => this.RaiseAndSetIfChanged(ref this.name, value);
	}

	/// <summary>
	/// Gets or sets the Zcash address that may be used to send ZEC to the contact.
	/// </summary>
	public string Address
	{
		get => this.address;
		set => this.RaiseAndSetIfChanged(ref this.address, value);
	}

	public string AddressCaption => "Address";

	public bool HasAddress => this.hasAddress.Value;

	/// <summary>
	/// Gets or sets an address from the user's wallet that was shared with the contact.
	/// </summary>
	/// <remarks>
	/// This is useful because at a low level, we can detect which diversified address was used to send money to the wallet,
	/// so by giving each contact a unique diversified address from this user's wallet, we can detect which contact sent money.
	/// </remarks>
	public string? MyAddressShownToContact
	{
		get => this.myAddressShownToContact;
		set => this.RaiseAndSetIfChanged(ref this.myAddressShownToContact, value);
	}

	public string HasContactSeenMyDiversifiedAddressCaption => this.MyAddressShownToContact is null ? "❌ This contact has not seen your diversified address." : "✅ This contact has seen your diversified address.";

	public bool IsShowDiversifiedAddressButtonVisible => this.isShowDiversifiedAddressButtonVisible.Value;

	public string ShowDiversifiedAddressCommandCaption => "Share my diversified address with this contact";

	public ReactiveCommand<Unit, Unit> ShowDiversifiedAddressCommand { get; }

	public string SendCommandCaption => this.sendCommandCaption.Value;

	public ReactiveCommand<Unit, Unit> SendCommand { get; }

	/// <summary>
	/// Gets a value indicating whether the contact has a shielded receiving address.
	/// </summary>
	public bool HasShieldedReceivingAddress => this.hasShieldedReceivingAddress.Value;

	public void Send()
	{
		SendingViewModel sendingViewModel = new(this.viewModelServices)
		{
			RecipientAddress = this.Address,
		};
		this.viewModelServices.NavigateTo(sendingViewModel);
	}
}