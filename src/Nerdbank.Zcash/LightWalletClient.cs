// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.VisualStudio.Threading;
using uniffi.LightWallet;
using static Nerdbank.Zcash.Transaction;
using static Nerdbank.Zcash.ZcashUtilities;

namespace Nerdbank.Zcash;

/// <summary>
/// Exposes functionality of a lightwallet client.
/// </summary>
public partial class LightWalletClient : IDisposable
{
	/// <summary>
	/// The number of confirmations required to spend a note.
	/// </summary>
	/// <remarks>
	/// While only one confirmation is technically required by the protocol,
	/// more confirmations make reorgs less likely to invalidate a transaction that spends a note.
	/// Since spending a note requires revealing a nullifier, more than one attempt to spend a note
	/// allows folks who are watching the mempool to detect that the same spender is behind each attempt.
	/// </remarks>
	public const int MinimumConfirmations = 3;

	private readonly Uri serverUrl;
	private readonly DbInit dbinit;
	private ImmutableDictionary<ZcashAccount, uint> accountIds = ImmutableDictionary.Create<ZcashAccount, uint>(ZcashAccount.Equality.ByIncomingViewingKey);
	private ImmutableDictionary<uint, ZcashAccount> accountsById = ImmutableDictionary.Create<uint, ZcashAccount>();

	/// <summary>
	/// Initializes a new instance of the <see cref="LightWalletClient"/> class.
	/// </summary>
	/// <param name="serverUrl">
	/// <para>The URL of a lightwallet server to use.</para>
	/// <para>You may refer to <see cref="LightWalletServers"/> for well-known servers run by the Zcash Foundation,
	/// Electric Coin Company (ECC), and others.</para>
	/// </param>
	/// <param name="network">
	/// The network the wallet operates on.
	/// This must match the network that the server at <paramref name="serverUrl"/> operates on.
	/// </param>
	/// <param name="dataFile">The path to the sqlite database to load (or create) that stores the decrypted transactions.</param>
	public LightWalletClient(Uri serverUrl, ZcashNetwork network, string dataFile)
	{
		Requires.NotNull(serverUrl);

		this.dbinit = new(dataFile, ToChainType(network), MinimumConfirmations);
		this.serverUrl = serverUrl;
		this.Network = network;

		LightWalletMethods.Init(this.dbinit);
		this.ReadAccountsFromDatabase();
	}

	/// <summary>
	/// Gets the Zcash network that this client operates on.
	/// </summary>
	public ZcashNetwork Network { get; }

	/// <summary>
	/// Gets the birthday height of the account.
	/// </summary>
	/// <remarks>
	/// The birthday height is the length of the blockchain when the account was created.
	/// It serves to reduce initial sync times when this account is imported into another wallet.
	/// </remarks>
	/// <seealso cref="GetBirthdayHeights"/>
	public uint? BirthdayHeight => LightWalletMethods.GetBirthdayHeight(this.dbinit);

	/// <summary>
	/// Gets the block last downloaded from the blockchain.
	/// </summary>
	public uint? LastDownloadHeight => LightWalletMethods.GetSyncHeight(this.dbinit);

	/// <summary>
	/// Gets the length of the blockchain (independent of what may have been sync'd thus far.)
	/// </summary>
	/// <param name="lightWalletServerUrl">The URL of the lightwallet server to query.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The length of the blockchain.</returns>
	/// <exception cref="LightWalletException">Thrown if any error occurs.</exception>
	public static async ValueTask<uint> GetLatestBlockHeightAsync(Uri lightWalletServerUrl, CancellationToken cancellationToken)
	{
		Requires.NotNull(lightWalletServerUrl);

		await TaskScheduler.Default.SwitchTo(alwaysYield: true);
		return InvokeInterop(cancellation => LightWalletMethods.GetBlockHeight(lightWalletServerUrl.AbsoluteUri, cancellation), cancellationToken);
	}

	/// <summary>
	/// Adds an account to the wallet, if does not already exist.
	/// </summary>
	/// <param name="account">The account to add (or upgrade).</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the account has been added.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the account's network doesn't match the one this instance was created with.</exception>
	/// <remarks>
	/// An account may already exist in the database, in which case this method will upgrade the account
	/// as <see cref="UpgradeAccount(ZcashAccount)"/> would, adding a spending key if one is available.
	/// </remarks>
	public async Task AddAccountAsync(ZcashAccount account, CancellationToken cancellationToken)
	{
		Requires.NotNull(account);

		if (account.Network != this.Network)
		{
			throw new InvalidOperationException(Strings.FormatNetworkMismatch(this.Network, account.Network));
		}

		if (this.accountIds.TryGetValue(account, out uint id))
		{
			// The database already has this account.
			// But we'll replace our copy with the given one which may contain a spending key.
			this.accountIds = this.accountIds.Remove(account).Add(account, id);
			this.accountsById = this.accountsById.SetItem(id, account);
			return;
		}

		await TaskScheduler.Default.SwitchTo(alwaysYield: true);
		id = InvokeInterop(
			cancellation => account switch
			{
				{ HDDerivation: { } hd } => LightWalletMethods.AddAccount(
					this.dbinit,
					this.serverUrl.AbsoluteUri,
					hd.Wallet.Seed.ToArray(),
					hd.AccountIndex,
					(uint?)account.BirthdayHeight,
					cancellation),
				{ FullViewing.UnifiedKey: { } ufvk } => LightWalletMethods.ImportAccountUfvk(
					this.dbinit,
					this.serverUrl.AbsoluteUri,
					ufvk.ToString(),
					spendingKeyAvailable: true,
					(uint?)account.BirthdayHeight,
					cancellation),
				_ => throw new NotSupportedException("This account doesn't contain any of the supported key types."),
			},
			cancellationToken);
		this.accountIds = this.accountIds.Add(account, id);
		this.accountsById = this.accountsById.SetItem(id, account);
	}

	/// <summary>
	/// Adds an account to the wallet, if does not already exist.
	/// </summary>
	/// <param name="account">The account to add.</param>
	/// <exception cref="InvalidOperationException">Thrown if the account's network doesn't match the one this instance was created with.</exception>
	public void UpgradeAccount(ZcashAccount account)
	{
		Requires.NotNull(account);
		if (account.HDDerivation is null)
		{
			throw new NotSupportedException("Only HD derived accounts are supported at present.");
		}

		if (account.Network != this.Network)
		{
			throw new InvalidOperationException(Strings.FormatNetworkMismatch(this.Network, account.Network));
		}

		Verify.Operation(this.accountIds.TryGetValue(account, out uint id), Strings.UnrecognizedAccount);

		// The database already has this account.
		// But we'll replace our copy with the given one which may contain a spending key.
		this.accountIds = this.accountIds.Remove(account).Add(account, id);
		this.accountsById = this.accountsById.SetItem(id, account);
	}

	/// <summary>
	/// Gets the accounts in the wallet.
	/// </summary>
	/// <returns>A snapshot of the accounts.</returns>
	/// <remarks>
	/// This will include view-only accounts that were added to the wallet in another session.
	/// They can be upgraded to spending accounts by passing the <see cref="ZcashAccount"/> instance
	/// with a spending key to <see cref="UpgradeAccount(ZcashAccount)"/>.
	/// </remarks>
	public IEnumerable<ZcashAccount> GetAccounts()
	{
		return this.accountIds.Keys;
	}

	/// <summary>
	/// Gets the minimum block height corresponding to an unspent note in the wallet.
	/// </summary>
	/// <returns>The minimum block height, or <see langword="null" /> if there are no unspent notes.</returns>
	public uint? GetMinUnspentHeight() => LightWalletMethods.GetMinUnspentHeight(this.dbinit);

	/// <summary>
	/// Invalidates any scanned blocks beyond a given block.
	/// </summary>
	/// <param name="blockHeight">
	/// The height of the last block to consider valid.
	/// This must either be within 100 blocks of the current tip (<see cref="GetLatestBlockHeightAsync(CancellationToken)"/>,
	/// or less than the height of the oldest unspent note in the wallet (<see cref="GetMinUnspentHeight"/>).
	/// </param>
	/// <remarks>
	/// This method can be very slow, and should not be run concurrently with <see cref="DownloadTransactionsAsync"/>.
	/// </remarks>
	public void Rescan(uint blockHeight) => LightWalletMethods.Rescan(this.dbinit, blockHeight);

	/// <summary>
	/// Records a diversifier index as having been used by the given account.
	/// </summary>
	/// <param name="account">The account to associate with the diversifier index.</param>
	/// <param name="diversifierIndex">The diversifier index.</param>
	/// <returns>A unified address based on this diversifier index, containing as many receivers as are valid at this index.</returns>
	/// <remarks>
	/// <para>
	/// This is crucial when transparent addresses are involved, since only transparent addresses
	/// added to the wallet via this method will be monitored for incoming transactions.
	/// </para>
	/// <para>A conflict with an existing diversifier index already in the table is not considered an error.</para>
	/// </remarks>
	public UnifiedAddress AddDiversifier(ZcashAccount account, DiversifierIndex diversifierIndex)
	{
		Requires.NotNull(account);

		if (!this.accountIds.TryGetValue(account, out uint accountId))
		{
			throw new InvalidOperationException(Strings.UnrecognizedAccount);
		}

		return (UnifiedAddress)ZcashAddress.Decode(LightWalletMethods.AddDiversifier(this.dbinit, accountId, diversifierIndex[..].ToArray()));
	}

	/// <summary>
	/// Gets the various forms of birthday heights relevant to this account.
	/// </summary>
	/// <param name="account">The account to query.</param>
	/// <returns>The birthday heights.</returns>
	/// <remarks>
	/// The resulting struct contains fields which may be influenced by the completeness of the sync to the blockchain.
	/// </remarks>
	public BirthdayHeights GetBirthdayHeights(ZcashAccount account)
	{
		Requires.NotNull(account);
		if (!this.accountIds.TryGetValue(account, out uint accountId))
		{
			throw new InvalidOperationException(Strings.UnrecognizedAccount);
		}

		return new(LightWalletMethods.GetBirthdayHeights(this.dbinit, accountId));
	}

	/// <inheritdoc cref="GetLatestBlockHeightAsync(Uri, CancellationToken)"/>
	public ValueTask<uint> GetLatestBlockHeightAsync(CancellationToken cancellationToken) => GetLatestBlockHeightAsync(this.serverUrl, cancellationToken);

	/// <summary>
	/// Scans the blockchain for new transactions related to the accounts known to this wallet.
	/// </summary>
	/// <param name="statusUpdates">
	/// An optional receiver of updates that report on progress toward the tip of the blockchain.
	/// Because scanning the blockchain may take a <em>very</em> long time, reporting progress to the user is <em>highly</em> recommended.
	/// </param>
	/// <param name="discoveredTransactions">
	/// Receives the transactions that are discovered during the scan.
	/// </param>
	/// <param name="continually">
	/// A value indicating whether to keep scanning the blockchain,
	/// watching for mempool transactions and new blocks as they come.
	/// </param>
	/// <param name="cancellationToken">
	/// A cancellation token.
	/// There may be a substantial delay between cancellation and when the blockchain scan is suspended in order to conclude work on the current 'batch'.
	/// Cancellation does <em>not</em> result in an <see cref="OperationCanceledException"/> being thrown,
	/// but rather concludes the operation early with an indication of the last block that was scanned.
	/// </param>
	/// <returns>
	/// A task that never completes successfully when <paramref name="continually"/> is <see langword="true" />,
	/// or completes with the last <see cref="SyncProgress"/> after the blockchain has been fully downloaded as of when the call was made.
	/// </returns>
	public async Task<SyncProgress> DownloadTransactionsAsync(
		IProgress<SyncProgress>? statusUpdates,
		IProgress<IReadOnlyDictionary<ZcashAccount, IReadOnlyCollection<Transaction>>>? discoveredTransactions,
		bool continually,
		CancellationToken cancellationToken)
	{
		await TaskScheduler.Default.SwitchTo(true);
		SyncUpdateData result = InvokeInterop(
			cancellation => LightWalletMethods.Sync(
				this.dbinit,
				this.serverUrl.AbsoluteUri,
				new SyncUpdateSink(this, statusUpdates, discoveredTransactions),
				continually,
				cancellation),
			cancellationToken);

		return new SyncProgress(result);
	}

	/// <summary>
	/// Gets all the downloaded transactions for this account included in a given block or later.
	/// </summary>
	/// <param name="account">The account to download transactions for.</param>
	/// <param name="startingBlock">The minimum block number to return transactions for.</param>
	/// <returns>A list of transactions.</returns>
	public List<Transaction> GetDownloadedTransactions(ZcashAccount account, uint startingBlock = 0)
	{
		Requires.NotNull(account);
		if (!this.accountIds.TryGetValue(account, out uint accountId))
		{
			throw new InvalidOperationException(Strings.UnrecognizedAccount);
		}

		return LightWalletMethods.GetTransactions(this.dbinit, accountId, startingBlock)
			.Select(CreateTransaction)
			.OrderBy(t => t.When)
			.ToList();
	}

	/// <summary>
	/// Creates and broadcasts a transaction that sends Zcash to one or more recipients.
	/// </summary>
	/// <param name="account">The account whose funds are to be spent.</param>
	/// <param name="payments">The payments to be made.</param>
	/// <param name="progress">An optional receiver for progress updates.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The IDs of the transaction(s) that were broadcast to complete the transfer.</returns>
	/// <remarks>
	/// <para>
	/// A single logical transfer may be divided into multiple transactions, or steps, when the receiver's address
	/// does not allow shielded funds to be sent directly to it (e.g. <see cref="TexAddress"/>).
	/// In such cases, the shielded funds are first sent to a transparent address controlled by <em>this</em> account,
	/// and then from there to the receiver.
	/// </para>
	/// </remarks>
	/// <exception cref="LightWalletException">Thrown when the spend is invalid (e.g. insufficient funds).</exception>
	public async Task<ReadOnlyMemory<TxId>> SendAsync(ZcashAccount account, IReadOnlyCollection<LineItem> payments, IProgress<SendProgress>? progress, CancellationToken cancellationToken)
	{
		Requires.NotNull(account);
		Requires.NotNullOrEmpty(payments);

		List<TransactionSendDetail> details = LineItemsToSendDetails(payments);

		await TaskScheduler.Default.SwitchTo(alwaysYield: true);
		try
		{
			List<SendTransactionResult> result = LightWalletMethods.Send(
				this.dbinit,
				this.serverUrl.AbsoluteUri,
				this.GetUnifiedSpendingKeyBytes(account),
				details);
			return result.Select(r => new TxId(r.txid)).ToArray();
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw LightWalletException.Wrap(ex, cancellationToken);
		}
	}

	/// <summary>
	/// Creates the transfer proposal for a set of payments and returns data about it.
	/// </summary>
	/// <param name="account">The account that will be spending the funds.</param>
	/// <param name="payments">The line items to appear in this transaction.</param>
	/// <returns>The details of the proposal.</returns>
	/// <exception cref="LightWalletException">Thrown when the spend is invalid (e.g. insufficient funds).</exception>
	public SpendDetails SimulateSend(ZcashAccount account, IReadOnlyCollection<LineItem> payments)
	{
		Requires.NotNull(account);
		Requires.Argument(account.FullViewing is not null, nameof(account), Strings.FullViewingKeyRequired);

		try
		{
			SendDetails sendDetails = LightWalletMethods.SimulateSend(
				this.dbinit,
				account.FullViewing.UnifiedKey.TextEncoding,
				LineItemsToSendDetails(payments));

			return new SpendDetails(sendDetails);
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw LightWalletException.Wrap(ex);
		}
	}

	/// <summary>
	/// Gets user balances.
	/// </summary>
	/// <param name="account">The account to get balances for.</param>
	/// <returns>Pool balances.</returns>
	public AccountBalances GetBalances(ZcashAccount account)
	{
		Requires.NotNull(account);
		if (!this.accountIds.TryGetValue(account, out uint accountId))
		{
			throw new InvalidOperationException(Strings.UnrecognizedAccount);
		}

		return new(this.Network.AsSecurity(), LightWalletMethods.GetUserBalances(this.dbinit, accountId));
	}

	/// <summary>
	/// Discovers all non-zero balances for transparent addresses in this account.
	/// </summary>
	/// <param name="account">The account to get unshielded balances for.</param>
	/// <returns>A collection of transparent addresses with positive balances, sorted by the age of the oldest UTXO for each address.</returns>
	/// <remarks>
	/// This can be useful as an input into an algorithm that shields transparent funds.
	/// </remarks>
	public IReadOnlyList<(TransparentAddress Address, decimal Balance)> GetUnshieldedBalances(ZcashAccount account)
	{
		Requires.NotNull(account);
		if (!this.accountIds.TryGetValue(account, out uint accountId))
		{
			throw new InvalidOperationException(Strings.UnrecognizedAccount);
		}

		List<TransparentNote> utxos = LightWalletMethods.GetUnshieldedUtxos(this.dbinit, accountId);
		if (utxos.Count == 0)
		{
			return Array.Empty<(TransparentAddress, decimal)>();
		}

		Dictionary<string, (decimal Balance, int Age)> index = new();
		for (int i = 0; i < utxos.Count; i++)
		{
			TransparentNote utxo = utxos[i];
			if (index.TryGetValue(utxo.recipient, out (decimal Balance, int Age) existing))
			{
				index[utxo.recipient] = (existing.Balance + ZatsToZEC(utxo.value), existing.Age);
			}
			else
			{
				index[utxo.recipient] = (ZatsToZEC(utxo.value), i);
			}
		}

		return [..
			from tAddrString in index
			let addr = (TransparentAddress)ZcashAddress.Decode(tAddrString.Key)
			orderby tAddrString.Value.Age
			select (addr, tAddrString.Value.Balance)];
	}

	/// <summary>
	/// Shields all funds in a given transparent address.
	/// </summary>
	/// <param name="account">The account whose transparent funds should be shielded.</param>
	/// <param name="address">The address with UTXOs to be shielded. This value is recommended to have come from a random element in the list returned from <see cref="GetUnshieldedBalances"/>.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The transaction ID of the shielding transaction that has been broadcast.</returns>
	public Task<TxId> ShieldAsync(ZcashAccount account, TransparentAddress address, CancellationToken cancellationToken)
	{
		return Task.Run(
			delegate
			{
				return new TxId(LightWalletMethods.Shield(this.dbinit, this.serverUrl.AbsoluteUri, this.GetUnifiedSpendingKeyBytes(account), address).Single().txid);
			},
			cancellationToken);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
	}

	/// <summary>
	/// Converts our own <see cref="ZcashNetwork"/> enum to the uniffi's <see cref="ChainType"/> enum.
	/// </summary>
	/// <param name="network">The network to convert.</param>
	/// <returns>The uniffi equivalent value.</returns>
	/// <exception cref="ArgumentException">Thrown if the network value isn't recognized.</exception>
	private static ChainType ToChainType(ZcashNetwork network)
	{
		return network switch
		{
			ZcashNetwork.MainNet => ChainType.Mainnet,
			ZcashNetwork.TestNet => ChainType.Testnet,
			_ => throw new ArgumentException(),
		};
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="LineItem"/> class.
	/// </summary>
	/// <param name="d">The uniffi data to copy from.</param>
	private static LineItem CreateLineItem(TransactionNote d)
	{
		var recipient = ZcashAddress.Decode(d.recipient);

		// Rip out excess receivers that librustzcash manufactures and injects, and use standard encodings for each pool.
		if (recipient is CompoundUnifiedAddress ua && ua.Receivers.Count > 1)
		{
			recipient = d.pool switch
			{
				uniffi.LightWallet.Pool.Transparent when ua.GetPoolReceiver<TransparentP2PKHReceiver>() is { } p2pkh => new TransparentP2PKHAddress(p2pkh, recipient.Network),
				uniffi.LightWallet.Pool.Transparent when ua.GetPoolReceiver<TransparentP2SHReceiver>() is { } p2sh => new TransparentP2SHAddress(p2sh, recipient.Network),
				uniffi.LightWallet.Pool.Sapling => new SaplingAddress(recipient.GetPoolReceiver<SaplingReceiver>() ?? throw new ArgumentException(), recipient.Network),
				uniffi.LightWallet.Pool.Orchard => new OrchardAddress(recipient.GetPoolReceiver<OrchardReceiver>() ?? throw new ArgumentException(), recipient.Network),
				_ => throw new NotSupportedException(),
			};
		}

		return new(recipient, ZatsToZEC(d.value), d.memo is null ? Memo.NoMemo : new Memo(d.memo.ToArray()));
	}

	private static List<TransactionSendDetail> LineItemsToSendDetails(IReadOnlyCollection<LineItem> payments)
	{
		byte[]? GetMemoBytes(Transaction.LineItem line) =>
			line.ToAddress.HasShieldedReceiver ? line.Memo.RawBytes.ToArray() :
			line.Memo.MemoFormat == Zip302MemoFormat.MemoFormat.NoMemo ? null :
			throw new InvalidOperationException(Strings.MemoForTransparentReceiverNotSupported);
		List<TransactionSendDetail> details = [.. payments
			.Select(p => new TransactionSendDetail((ulong)(p.Amount * ZatsPerZEC), GetMemoBytes(p), p.ToAddress))];
		return details;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Transaction"/> class.
	/// </summary>
	/// <param name="t">The uniffi transaction to copy data from.</param>
	private static Transaction CreateTransaction(uniffi.LightWallet.Transaction t)
		=> new(new TxId(t.txid), t.minedHeight, t.expiredUnmined, t.blockTime, ZatsToZEC(t.accountBalanceDelta), t.fee.HasValue ? ZatsToZEC(t.fee.Value) : null, [.. t.outgoing.Select(CreateLineItem)], [.. t.incoming.Select(CreateLineItem)], [.. t.change.Select(CreateLineItem)]);

	/// <summary>
	/// Wraps an interop invocation in a <see langword="try" /> block and wraps
	/// any interop exceptions in the appropriate .NET exceptions.
	/// </summary>
	/// <typeparam name="T">The type of returned value.</typeparam>
	/// <param name="action">The interop action to invoke. A <see cref="Cancellation"/> object is provided if <paramref name="cancellationToken"/> is cancelable.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The result returned from <paramref name="action"/>.</returns>
	/// <exception cref="OperationCanceledException">Thrown if the operation was canceled via the <paramref name="cancellationToken"/>.</exception>
	/// <exception cref="LightWalletException">Thrown for any other error.</exception>
	private static T InvokeInterop<T>(Func<Cancellation?, T> action, CancellationToken cancellationToken)
	{
		using Cancellation? cancellation = cancellationToken.CanBeCanceled ? new(cancellationToken) : null;
		try
		{
			return action(cancellation);
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw LightWalletException.Wrap(ex, cancellationToken);
		}
	}

	/// <summary>
	/// Gets the raw byte encoding of the unified spending key for this account.
	/// </summary>
	private byte[] GetUnifiedSpendingKeyBytes(ZcashAccount account)
	{
		UnifiedSpendingKey usk = account.Spending?.UnifiedKey ?? throw new InvalidOperationException("No spending key.");
		byte[] uskBytes = new byte[500];
		int len = usk.ToBytes(uskBytes);
		return uskBytes[..len];
	}

	private void ReadAccountsFromDatabase()
	{
		var builder = this.accountIds.ToBuilder();
		try
		{
			foreach (AccountInfo accountInfo in LightWalletMethods.GetAccounts(this.dbinit))
			{
				if (accountInfo.uvk is not null)
				{
					ZcashAccount account = new(UnifiedViewingKey.Decode(accountInfo.uvk))
					{
						BirthdayHeight = accountInfo.birthdayHeights.originalBirthdayHeight,
					};
					builder.Add(account, accountInfo.id);
				}
			}
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw LightWalletException.Wrap(ex);
		}

		this.accountIds = builder.ToImmutable();
		this.accountsById = this.accountIds.ToImmutableDictionary(x => x.Value, x => x.Key);
	}

	/// <summary>
	/// The balances that applies to the transparent pool for a particular account.
	/// </summary>
	/// <param name="Balance">The pool balance.</param>
	public record struct TransparentPoolBalance(decimal Balance);

	/// <summary>
	/// The balances that apply to a single shielded pool for a particular account.
	/// </summary>
	/// <param name="Balance">The pool balance.</param>
	/// <param name="VerifiedBalance">The verified balance.</param>
	/// <param name="UnverifiedBalance">The unverified balance.</param>
	/// <param name="SpendableBalance">The spendable balance.</param>
	public record struct ShieldedPoolBalance(decimal Balance, decimal VerifiedBalance, decimal UnverifiedBalance, decimal SpendableBalance)
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ShieldedPoolBalance"/> struct
		/// with balances given in ZATs.
		/// </summary>
		/// <param name="balance"><inheritdoc cref="ShieldedPoolBalance(decimal, decimal, decimal, decimal)" path="/param[@name='Balance']"/></param>
		/// <param name="verified"><inheritdoc cref="ShieldedPoolBalance(decimal, decimal, decimal, decimal)" path="/param[@name='VerifiedBalance']"/></param>
		/// <param name="unverified"><inheritdoc cref="ShieldedPoolBalance(decimal, decimal, decimal, decimal)" path="/param[@name='UnverifiedBalance']"/></param>
		/// <param name="spendable"><inheritdoc cref="ShieldedPoolBalance(decimal, decimal, decimal, decimal)" path="/param[@name='SpendableBalance']"/></param>
		public ShieldedPoolBalance(ulong balance, ulong verified, ulong unverified, ulong spendable)
			: this(ZatsToZEC(balance), ZatsToZEC(verified), ZatsToZEC(unverified), ZatsToZEC(spendable))
		{
		}
	}

	/// <summary>
	/// Describes the various birthday heights that apply to a Zcash account.
	/// </summary>
	/// <param name="OriginalBirthdayHeight">The birthday height the account was created with.</param>
	/// <param name="BirthdayHeight">The height of the first block that contains a transaction.</param>
	/// <param name="RebirthHeight">The height of the block containing the oldest unspent note.</param>
	public record struct BirthdayHeights(uint OriginalBirthdayHeight, uint? BirthdayHeight, uint? RebirthHeight)
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="BirthdayHeights"/> struct.
		/// </summary>
		/// <param name="heights">The interop heights record to copy from.</param>
		internal BirthdayHeights(uniffi.LightWallet.BirthdayHeights heights)
			: this(heights.originalBirthdayHeight, heights.birthdayHeight, heights.rebirthHeight)
		{
		}
	}

	/// <summary>
	/// Describes a proposed spend.
	/// </summary>
	public record SpendDetails
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SpendDetails"/> class.
		/// </summary>
		/// <param name="fee">The total fee required for the transfer. This should be a positive value.</param>
		public SpendDetails(decimal fee)
		{
			Requires.Range(fee >= 0, nameof(fee));
			this.Fee = fee;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SpendDetails"/> class.
		/// </summary>
		/// <param name="details">The interop type to copy values from.</param>
		internal SpendDetails(SendDetails details)
			: this(ZatsToZEC(details.fee))
		{
		}

		/// <summary>
		/// Gets the total fee required for the transfer. This should be a positive value.
		/// </summary>
		public decimal Fee { get; private set; }
	}

	/// <summary>
	/// Carries details of a progress update on a send operation.
	/// </summary>
	/// <param name="Id">An id for the operation.</param>
	/// <param name="IsSendInProgress">A value indicating whether the send is in progress.</param>
	/// <param name="Progress">A value that is some proportion of the <paramref name="Total"/> value.</param>
	/// <param name="Total">Some value that <paramref name="Progress"/> is reaching for.</param>
	/// <param name="LastError">The last error to have occurred.</param>
	/// <param name="LastTransactionId">The ID of the last transaction to be created.</param>
	public record SendProgress(
		uint Id,
		bool IsSendInProgress,
		uint Progress,
		uint Total,
		string? LastError,
		string? LastTransactionId)
	{
	}

	/// <summary>
	/// The data in a periodic status report during a <see cref="DownloadTransactionsAsync"/> operation.
	/// </summary>
	/// <param name="LastFullyScannedBlock">The last block that has been fully scanned.</param>
	/// <param name="TipHeight">The length of the blockchain.</param>
	/// <param name="CurrentStep">The step that the operation has most recently completed.</param>
	/// <param name="TotalSteps">The number of steps currently believed to be in the operation.</param>
	/// <param name="LastError">The last error encountered during the scan.</param>
	public record SyncProgress(
		uint? LastFullyScannedBlock,
		uint TipHeight,
		ulong CurrentStep,
		ulong TotalSteps,
		string? LastError)
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SyncProgress"/> class.
		/// </summary>
		/// <param name="data">The interop type to copy data from.</param>
		internal SyncProgress(SyncUpdateData data)
			: this(data.lastFullyScannedBlock, data.tipHeight, data.currentStep, data.totalSteps, data.lastError)
		{
		}

		/// <summary>
		/// Gets the % completion in reaching the tip of the blockchain.
		/// </summary>
		public double PercentComplete => this.TotalSteps == 0 ? 0 : (double)this.CurrentStep * 100 / this.TotalSteps;
	}

	private class SyncUpdateSink(
		LightWalletClient client,
		IProgress<SyncProgress>? statusUpdates,
		IProgress<IReadOnlyDictionary<ZcashAccount, IReadOnlyCollection<Transaction>>>? discoveredTransactions)
		: SyncUpdate
	{
		public void UpdateStatus(SyncUpdateData data) => statusUpdates?.Report(new(data));

		public void ReportTransactions(List<uniffi.LightWallet.Transaction> transactions)
		{
			if (discoveredTransactions is null)
			{
				return;
			}

			ImmutableDictionary<ZcashAccount, IReadOnlyCollection<Transaction>> dictionary = ImmutableDictionary.CreateRange(
				from tx in transactions
				group tx by tx.accountId into g
				let account = client.accountsById[g.Key]
				select new KeyValuePair<ZcashAccount, IReadOnlyCollection<Transaction>>(
					account,
					g.Select(CreateTransaction).ToArray()));
			discoveredTransactions.Report(dictionary);
		}
	}

	private class Cancellation : CancellationSource, IDisposable
	{
		private readonly CancellationToken token;
		private CancellationTokenRegistration registration;

		public Cancellation(CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			this.token = token;
		}

		public void SetCancellationId(uint id)
		{
			this.registration = this.token.Register(() => LightWalletMethods.Cancel(id), useSynchronizationContext: false);
		}

		public void Dispose()
		{
			this.registration.Dispose();
		}
	}
}
