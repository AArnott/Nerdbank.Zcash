﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Avalonia.Controls;

namespace Nerdbank.Zcash.App;

public interface IViewModelServices
{
	/// <summary>
	/// Gets the wallet data model.
	/// </summary>
	ZcashWallet Wallet { get; }

	/// <summary>
	/// Gets or sets the active account.
	/// </summary>
	ZcashAccount? SelectedAccount { get; set; }

	/// <summary>
	/// Gets the HD wallet that contains the <see cref="SelectedAccount"/>, if any.
	/// </summary>
	HDWallet? SelectedHDWallet => this.SelectedAccount is not null ? this.Wallet.GetHDWalletFor(this.SelectedAccount) : null;

	/// <summary>
	/// Gets the persisted collection of contacts.
	/// </summary>
	IContactManager ContactManager { get; }

	TopLevel? TopLevel { get; }

	/// <summary>
	/// Pushes a view model onto the view stack.
	/// </summary>
	/// <param name="viewModel">The new view model.</param>
	/// <remarks>
	/// This will no-op if the given view model is already the current view model.
	/// </remarks>
	void NavigateTo(ViewModelBase viewModel);

	/// <summary>
	/// Pops the current view model off the view stack, effectively moving the view "back" one step.
	/// </summary>
	/// <param name="ifCurrentViewModel">The view model that is expected to be on top at the time of the call. If specified, the stack will only be popped if this is the top view model.</param>
	void NavigateBack(ViewModelBase? ifCurrentViewModel = null);

	/// <summary>
	/// Replaces the entire view stack with a new view model.
	/// </summary>
	/// <param name="viewModel">The new view model to select.</param>
	/// <remarks>
	/// This is useful primarily at the start of the app, when the user may not see the main home screen right away due to a first launch experience.
	/// </remarks>
	void ReplaceViewStack(ViewModelBase viewModel);
}
