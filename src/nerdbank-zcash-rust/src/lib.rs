uniffi::include_scaffolding!("ffi");

#[macro_use]
extern crate lazy_static;

mod analysis;
mod backing_store;
mod block_source;
mod blockrange;
mod error;
mod grpc;
mod interop;
mod lightclient;
mod orchard;
mod prover;
mod resilience;
mod sapling;
mod send;
mod shield;
mod sql_statements;
mod sync;

#[cfg(test)]
mod test_constants;

use analysis::{BirthdayHeights, UserBalances};
use interop::{
    add_account, add_diversifier, cancel, disconnect_server, get_accounts, get_birthday_height,
    get_birthday_heights, get_block_height, get_min_unspent_height, get_sync_height,
    get_transactions, get_unshielded_utxos, get_user_balances, import_account_ufvk, init, rescan,
    send, shield, simulate_send, sync, AccountInfo, CancellationSource, ChainType, DbInit,
    LightWalletError, Pool, SendDetails, SendTransactionResult, SyncUpdate, SyncUpdateData,
    Transaction, TransactionNote, TransactionSendDetail, TransparentNote,
};
