﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Avalonia.Media.Imaging;
using Nerdbank.QRCodes;
using QRCoder;

namespace Nerdbank.Zcash.App.ViewModels;

public class ReceivingAddress : IDisposable
{
	public ReceivingAddress(IViewModelServices viewModelServices, ZcashAddress address, PaymentRequestDetailsViewModel? paymentRequestDetails, string header)
	{
		this.Header = header;
		if (address.HasShieldedReceiver)
		{
			this.Header = $"{this.Header} 🛡️";
		}

		this.Address = address;

		if (paymentRequestDetails is not null)
		{
			Zip321PaymentRequestUris.PaymentRequest paymentRequest = new(paymentRequestDetails.ToDetails(address));
			this.FullText = paymentRequest.ToString();
			this.ShortText = $"{this.FullText[..30]}...";
		}
		else
		{
			this.FullText = address;
			this.ShortText = $"{address.Address[..15]}...{address.Address[^15..]}";
		}

		try
		{
			this.QRCode = this.CreateQRCode();
		}
		catch (InvalidOperationException)
		{
			// This fails in unit tests because Avalonia Bitmap can't find required UI services.
		}

#pragma warning disable VSTHRD110 // Observe result of async calls - VSTHRD110 bug that is fixed but not yet released.
		this.CopyCommand = ReactiveCommand.CreateFromTask(() => viewModelServices.TopLevel?.Clipboard?.SetTextAsync(this.FullText) ?? Task.CompletedTask);
#pragma warning restore VSTHRD110 // Observe result of async calls
	}

	public string Header { get; }

	public string? Subheading => !this.Address.HasShieldedReceiver ? Strings.TransparentAddressSubheading : null;

	public Bitmap? QRCode { get; }

	public ZcashAddress Address { get; }

	public string FullText { get; }

	public string ShortText { get; }

	public ReactiveCommand<Unit, Unit> CopyCommand { get; }

	public void Dispose()
	{
		this.QRCode?.Dispose();
	}

	private Bitmap CreateQRCode()
	{
		QRCodeGenerator generator = new();
		QREncoder encoder = new() { NoPadding = true };
		QRCodeData data = generator.CreateQrCode(this.FullText, encoder.ECCLevel);
		return new Bitmap(new MemoryStream(encoder.Encode(data, ".png", null)));
	}
}