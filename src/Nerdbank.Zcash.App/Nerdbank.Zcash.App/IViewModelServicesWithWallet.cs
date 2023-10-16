﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Zcash.App;

public interface IViewModelServicesWithWallet : IViewModelServices
{
	/// <summary>
	/// Gets the wallet data model.
	/// </summary>
	new ZcashWallet Wallet { get; }

	/// <summary>
	/// Gets or sets the active account.
	/// </summary>
	ZcashAccount SelectedAccount { get; set; }
}