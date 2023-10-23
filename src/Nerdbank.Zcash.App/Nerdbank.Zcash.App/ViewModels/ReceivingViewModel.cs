﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Nerdbank.QRCodes;
using QRCoder;

namespace Nerdbank.Zcash.App.ViewModels;

public class ReceivingViewModel : ViewModelBase, IDisposable
{
	private readonly IViewModelServicesWithSelectedAccount viewModelServices;

	private readonly Contact? observingContact;

	private readonly Contact.AssignedSendingAddresses? assignedAddresses;

	private readonly uint transparentAddressIndex;

	private ReceivingAddress displayedAddress;

	public ReceivingViewModel()
		: this(new DesignTimeViewModelServices(), null, null)
	{
	}

	public ReceivingViewModel(IViewModelServicesWithSelectedAccount viewModelServices, Contact? observingContact, PaymentRequestDetailsViewModel? paymentRequestDetailsViewModel)
	{
		this.viewModelServices = viewModelServices;
		this.observingContact = observingContact;

		QRCodeGenerator generator = new();
		QREncoder encoder = new() { NoPadding = true };

		ZcashAccount account = viewModelServices.SelectedAccount;
		this.assignedAddresses = observingContact?.GetOrCreateSendingAddressAssignment(account);
		if (account.HasDiversifiableKeys)
		{
			DiversifierIndex diversifierIndex = this.assignedAddresses?.AssignedDiversifier ?? new(DateTime.UtcNow.Ticks);
			UnifiedAddress unifiedAddress = account.GetDiversifiedAddress(ref diversifierIndex);
			this.Addresses.Add(new(viewModelServices, unifiedAddress, paymentRequestDetailsViewModel, Strings.UnifiedReceivingAddressHeader));

			if (unifiedAddress.GetPoolReceiver<SaplingReceiver>() is { } saplingReceiver)
			{
				SaplingAddress saplingAddress = new(saplingReceiver, unifiedAddress.Network);
				this.Addresses.Add(new(viewModelServices, saplingAddress, paymentRequestDetailsViewModel, Strings.SaplingReceivingAddressHeader));
			}
		}

		if (viewModelServices.SelectedAccount.IncomingViewing.Transparent is { } transparent)
		{
			// Consume a fresh transparent address for this receiver.
			// We'll bump the max index up by one if the owner indicates the address was actually 'consumed' by the receiver.
			this.transparentAddressIndex = this.assignedAddresses?.AssignedTransparentAddressIndex ?? (viewModelServices.SelectedAccount.MaxTransparentAddressIndex is uint idx ? idx + 1 : 1);
			TransparentAddress transparentAddress = viewModelServices.SelectedAccount.GetTransparentAddress(this.transparentAddressIndex);
			this.Addresses.Add(new(viewModelServices, transparentAddress, paymentRequestDetailsViewModel, Strings.TransparentReceivingAddressHeader));
		}

		this.PaymentRequestDetails = new(this.viewModelServices.SelectedAccount.Network.AsSecurity());
		this.displayedAddress = this.Addresses[0];
		this.RecordTransparentAddressShownIfApplicable();
	}

	public string Title => "Receive Zcash";

	public ReceivingAddress DisplayedAddress
	{
		get => this.displayedAddress;
		set
		{
			this.RaiseAndSetIfChanged(ref this.displayedAddress, value);
			this.RecordTransparentAddressShownIfApplicable();
		}
	}

	public ObservableCollection<ReceivingAddress> Addresses { get; } = new();

	public PaymentRequestDetailsViewModel PaymentRequestDetails { get; }

	public string AddPaymentRequestCaption => "Payment details";

	/// <summary>
	/// Gets a message informing the user as to when the last payment was received at this address, if any.
	/// This is specifically to help the user confirm that the sender has actually sent them something, so
	/// as they're waiting, we should show them transactions even from the mempool.
	/// If it has few confirmations, we should show that too.
	/// </summary>
	public string PaymentReceivedText => $"You received {this.DisplayedAddress.Address.Network.AsSecurity().Amount(0.1m)} at this address 5 seconds ago";

	public void Dispose()
	{
		foreach (ReceivingAddress address in this.Addresses)
		{
			address.Dispose();
		}

		this.Addresses.Clear();
	}

	private void RecordTransparentAddressShownIfApplicable()
	{
		if (this.DisplayedAddress.Address is TransparentAddress && this.assignedAddresses is not null)
		{
			this.assignedAddresses.AssignedTransparentAddressIndex ??= this.transparentAddressIndex;
			if (this.transparentAddressIndex > this.viewModelServices.SelectedAccount.MaxTransparentAddressIndex || this.viewModelServices.SelectedAccount.MaxTransparentAddressIndex is null)
			{
				this.viewModelServices.SelectedAccount.MaxTransparentAddressIndex = this.transparentAddressIndex;
			}
		}
	}
}
