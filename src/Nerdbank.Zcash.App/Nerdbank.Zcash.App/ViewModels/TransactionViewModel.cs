﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Zcash.App.ViewModels;

public class TransactionViewModel : ViewModelBase
{
	private uint? blockNumber;
	private DateTimeOffset? when = DateTimeOffset.Now;
	private string otherPartyName = string.Empty;

	public TransactionViewModel()
	{
		this.LinkProperty(nameof(this.When), nameof(this.WhenFormatted));
	}

	public uint? BlockNumber
	{
		get => this.blockNumber;
		set => this.RaiseAndSetIfChanged(ref this.blockNumber, value);
	}

	public string BlockNumberFormatted => $"{this.BlockNumber:N0}";

	public string BlockNumberCaption => "Block #";

	public required string TransactionId { get; init; }

	public string TransactionIdCaption => "Transaction ID";

	public DateTimeOffset? When
	{
		get => this.when;
		set => this.RaiseAndSetIfChanged(ref this.when, value);
	}

	public string WhenFormatted => this.When?.ToString("g") ?? string.Empty;

	public string WhenCaption => "When";

	public required decimal Amount { get; init; }

	public ZcashAmountFormatted AmountFormatted => new(this.Amount, this.Network);

	public string AmountCaption => "Amount";

	public ZcashAddress? OtherPartyAddress { get; init; } = ZcashAddress.Parse("u1wwsl42efxdj727vfcgmcf7wgxdqjjen4wqu79666ujf4qj4sqgezjemnaf23dlsgct3etneqrf2py2qws0lt2jfxv0n9cx5yr7l9vwa4hqvcznu0kxz90vpz4tgrd327wl4s875883w0rq6zjkp67c6qthdwwa6kcw8pv3699sfy27qa");

	public string OtherPartyName
	{
		get => this.otherPartyName;
		set => this.RaiseAndSetIfChanged(ref this.otherPartyName, value);
	}

	public string OtherPartyNameCaption => "Name";

	public string? Memo { get; init; }

	public string MemoCaption => "Memo";

	public required bool IsIncoming { get; init; }
}
