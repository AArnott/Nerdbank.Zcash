﻿// Copyright (c) Andrew Arnott. All rights reserved.
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

	/// <summary>
	/// Initializes a new instance of the <see cref="LightWalletClient"/> class.
	/// </summary>
	/// <param name="serverUrl">The URL of a lightwallet server to use.</param>
	/// <param name="network">The network the wallet operates on.</param>
	/// <param name="dataFile">The path to the sqlite database to load (or create) that stores the decrypted transactions.</param>
	public LightWalletClient(Uri serverUrl, ZcashNetwork network, string dataFile)
	{
		Requires.NotNull(serverUrl);

		this.dbinit = new(dataFile, ToChainType(network));
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
		try
		{
			return LightWalletMethods.GetBlockHeight(lightWalletServerUrl.AbsoluteUri);
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw new LightWalletException(ex);
		}
	}

	/// <summary>
	/// Adds an account to the wallet, if does not already exist.
	/// </summary>
	/// <param name="account">The account to add.</param>
	/// <exception cref="InvalidOperationException">Thrown if the account's network doesn't match the one this instance was created with.</exception>
	public void AddAccount(ZcashAccount account)
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

		if (this.accountIds.TryGetValue(account, out uint id))
		{
			// The database already has this account.
			// But we'll replace our copy with the given one which may contain a spending key.
			this.accountIds = this.accountIds.Remove(account).Add(account, id);
			return;
		}

		try
		{
			id = LightWalletMethods.AddAccount(this.dbinit, this.serverUrl.AbsoluteUri, account.HDDerivation.Value.Wallet.Seed.ToArray(), (uint?)account.BirthdayHeight);
			this.accountIds = this.accountIds.Add(account, id);
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw new LightWalletException(ex);
		}
	}

	/// <summary>
	/// Gets the accounts in the wallet.
	/// </summary>
	/// <returns>A snapshot of the accounts.</returns>
	/// <remarks>
	/// This will include view-only accounts that were added to the wallet in another session.
	/// They can be upgraded to spending accounts by passing the <see cref="ZcashAccount"/> instance
	/// with a spending key to <see cref="AddAccount(ZcashAccount)"/>.
	/// </remarks>
	public IEnumerable<ZcashAccount> GetAccounts()
	{
		return this.accountIds.Keys;
	}

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

	/// <inheritdoc cref="DownloadTransactionsAsync(IProgress{SyncProgress}?, IProgress{IReadOnlyCollection{Transaction}}, CancellationToken)"/>
	public Task<SyncResult> DownloadTransactionsAsync(CancellationToken cancellationToken) => this.DownloadTransactionsAsync(null, null, cancellationToken);

	/// <summary>
	/// Scans the blockchain for new transactions related to this account.
	/// </summary>
	/// <param name="statusUpdates">
	/// An optional receiver of updates that report on progress toward the tip of the blockchain.
	/// Because scanning the blockchain may take a <em>very</em> long time, reporting progress to the user is <em>highly</em> recommended.
	/// </param>
	/// <param name="discoveredTransactions">
	/// Receives the transactions that are discovered during the scan.
	/// </param>
	/// <param name="cancellationToken">
	/// A cancellation token.
	/// There may be a substantial delay between cancellation and when the blockchain scan is suspended in order to conclude work on the current 'batch'.
	/// Cancellation does <em>not</em> result in an <see cref="OperationCanceledException"/> being thrown,
	/// but rather concludes the operation early with an indication of the last block that was scanned.
	/// </param>
	/// <returns>A task with the result of the scan.</returns>
	public async Task<SyncResult> DownloadTransactionsAsync(
		IProgress<SyncProgress>? statusUpdates,
		IProgress<IReadOnlyCollection<Transaction>>? discoveredTransactions,
		CancellationToken cancellationToken)
	{
		await TaskScheduler.Default.SwitchTo(true);
		uniffi.LightWallet.SyncResult result = LightWalletMethods.Sync(
			this.dbinit,
			this.serverUrl.AbsoluteUri,
			new SyncUpdateSink(this.Network, statusUpdates, discoveredTransactions));

		return new SyncResult(result);
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
			.Select(t => CreateTransaction(t, this.Network))
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
	/// <returns>The transaction ID.</returns>
	public async Task<TxId> SendAsync(ZcashAccount account, IReadOnlyCollection<SendItem> payments, IProgress<SendProgress>? progress, CancellationToken cancellationToken)
	{
		Requires.NotNull(account);
		Requires.NotNullOrEmpty(payments);

		List<TransactionSendDetail> details = [.. payments
			.Select(p => new TransactionSendDetail((ulong)(p.Amount * ZatsPerZEC), p.Memo.RawBytes.ToArray(), p.ToAddress))];

		await TaskScheduler.Default.SwitchTo(alwaysYield: true);
		try
		{
			SendTransactionResult result = LightWalletMethods.Send(this.dbinit, this.serverUrl.AbsoluteUri, this.GetUnifiedSpendingKeyBytes(account), MinimumConfirmations, details);
			return new TxId(result.txid);
		}
		catch (uniffi.LightWallet.LightWalletException ex)
		{
			throw new LightWalletException(ex);
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

		return new(this.Network.AsSecurity(), LightWalletMethods.GetUserBalances(this.dbinit, accountId, MinimumConfirmations));
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
				return new TxId(LightWalletMethods.Shield(this.dbinit, this.serverUrl.AbsoluteUri, this.GetUnifiedSpendingKeyBytes(account), address).txid);
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
	/// Initializes a new instance of the <see cref="SendItem"/> class.
	/// </summary>
	/// <param name="d">The uniffi data to copy from.</param>
	private static SendItem CreateSendItem(TransactionSendDetail d)
		=> new(ZcashAddress.Decode(d.recipient), ZatsToZEC(d.value), d.memo is null ? Memo.NoMemo : new Memo(d.memo.ToArray()));

	/// <summary>
	/// Initializes a new instance of the <see cref="RecvItem"/> class.
	/// </summary>
	/// <param name="d">The uniffi shielded note.</param>
	private static RecvItem CreateRecvItem(ShieldedNote d)
		=> new(ZcashAddress.Decode(d.recipient), ZatsToZEC(d.value), new Memo(d.memo.ToArray()), d.isChange);

	/// <summary>
	/// Initializes a new instance of the <see cref="RecvItem"/> class.
	/// </summary>
	/// <param name="d">The uniffi transparent note.</param>
	private static RecvItem CreateRecvItem(TransparentNote d)
		=> new(ZcashAddress.Decode(d.recipient), ZatsToZEC(d.value), Memo.NoMemo, IsChange: false);

	/// <summary>
	/// Initializes a new instance of the <see cref="Transaction"/> class.
	/// </summary>
	/// <param name="t">The uniffi transaction to copy data from.</param>
	/// <param name="network">The network this transaction is on.</param>
	private static Transaction CreateTransaction(uniffi.LightWallet.Transaction t, ZcashNetwork network)
		=> new(new TxId(t.txid), t.minedHeight, t.expiredUnmined, t.blockTime, ZatsToZEC(t.accountBalanceDelta), ZatsToZEC(t.fee), [.. t.outgoing.Select(CreateSendItem)], [.. t.incomingShielded.Select(CreateRecvItem), .. t.incomingTransparent.Select(CreateRecvItem)]);

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

		this.accountIds = builder.ToImmutable();
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
	public record struct BirthdayHeights(ulong OriginalBirthdayHeight, ulong BirthdayHeight, ulong? RebirthHeight)
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
	/// Describes the final result of a blockchain scan.
	/// </summary>
	/// <param name="Success">Indicates overall success of the operation.</param>
	/// <param name="LatestBlock">The last blocked scanned.</param>
	public record SyncResult(
		bool Success,
		ulong LatestBlock)
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SyncResult"/> class
		/// based on data coming from the native code.
		/// </summary>
		/// <param name="copyFrom">The native data to copy from.</param>
		internal SyncResult(uniffi.LightWallet.SyncResult copyFrom)
			: this(true, copyFrom.latestBlock)
		{
		}
	}

	/// <summary>
	/// The data in a periodic status report during a <see cref="DownloadTransactionsAsync(IProgress{SyncProgress}?, IProgress{IReadOnlyCollection{Transaction}}?, CancellationToken)"/> operation.
	/// </summary>
	/// <param name="Current">The step that the operation has most recently completed.</param>
	/// <param name="Total">The number of steps currently believed to be in the operation.</param>
	/// <param name="LastError">The last error encountered during the scan.</param>
	public record SyncProgress(
		ulong Current,
		ulong Total,
		string? LastError)
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SyncProgress"/> class.
		/// </summary>
		/// <param name="data">The interop type to copy data from.</param>
		internal SyncProgress(SyncUpdateData data)
			: this(data.current, data.total, data.lastError)
		{
		}
	}

	private class SyncUpdateSink(
		ZcashNetwork network,
		IProgress<SyncProgress>? statusUpdates,
		IProgress<IReadOnlyCollection<Transaction>>? discoveredTransactions)
		: SyncUpdate
	{
		public void UpdateStatus(SyncUpdateData data) => statusUpdates?.Report(new(data));

		public void ReportTransactions(List<uniffi.LightWallet.Transaction> transactions)
			=> discoveredTransactions?.Report(transactions.Select(t => CreateTransaction(t, network)).ToList());
	}
}
