uniffi::include_scaffolding!("ffi");

#[macro_use]
extern crate lazy_static;

mod backend_client;
mod error;
mod grpc;
mod interop;
mod orchard;
mod sapling;

use interop::{
    lightwallet_disconnect_server, lightwallet_get_block_height, lightwallet_init,
    lightwallet_sync, BirthdayHeights, ChainType, LightWalletError, OrchardNote, SaplingNote,
    SyncResult, Transaction, TransactionSendDetail, UserBalances, WalletInfo,
};
