use sapling::{
    keys::{ExpandedSpendingKey, FullViewingKey},
    zip32::{
        sapling_derive_internal_fvk, DiversifiableFullViewingKey, DiversifierKey,
        ExtendedSpendingKey,
    },
    PaymentAddress, SaplingIvk,
};
use zcash_primitives::zip32::{ChildIndex, DiversifierIndex, Scope};

#[no_mangle]
pub extern "C" fn derive_sapling_ivk_from_fvk(fvk: *const [u8; 96], ivk: *mut [u8; 32]) -> i32 {
    let fvk = unsafe { &*fvk };
    let ivk = unsafe { &mut *ivk };

    let fvk = match FullViewingKey::read(&fvk[..]) {
        Ok(fvk) => fvk,
        Err(_) => return -1,
    };

    ivk.copy_from_slice(&fvk.vk.ivk().to_repr());

    0
}

#[no_mangle]
pub extern "C" fn decrypt_sapling_diversifier(
    fvk: *const [u8; 96],
    dk: *const [u8; 32],
    receiver: *const [u8; 43],
    diversifier_index: *mut [u8; 11],
    scope: *mut u8,
) -> i32 {
    let fvk = unsafe { &*fvk };
    let dk = unsafe { &*dk };
    let receiver = unsafe { &*receiver };
    let diversifier_index = unsafe { &mut *diversifier_index };
    let scope = unsafe { &mut *scope };

    let mut fvk_dk = [0u8; 128];
    fvk_dk[..96].copy_from_slice(fvk);
    fvk_dk[96..].copy_from_slice(dk);

    if let Some(dfvk) = DiversifiableFullViewingKey::from_bytes(&fvk_dk) {
        if let Some(receiver) = PaymentAddress::from_bytes(receiver) {
            if let Some((idx, s)) = dfvk.decrypt_diversifier(&receiver) {
                diversifier_index.copy_from_slice(idx.as_bytes());
                *scope = match s {
                    Scope::External => 0,
                    Scope::Internal => 1,
                };
                0
            } else {
                // Everything was valid. But the receiver did *not* come from this key.
                1
            }
        } else {
            -2
        }
    } else {
        -1
    }
}

#[no_mangle]
pub extern "C" fn decrypt_sapling_diversifier_with_ivk(
    ivk: *const [u8; 32],
    dk: *const [u8; 32],
    receiver: *const [u8; 43],
    diversifier_index: *mut [u8; 11],
) -> i32 {
    let ivk = unsafe { &*ivk };
    let dk = unsafe { &*dk };
    let receiver = unsafe { &*receiver };
    let diversifier_index = unsafe { &mut *diversifier_index };

    let fr = jubjub::Fr::from_bytes(ivk);
    if fr.is_none().into() {
        return -1;
    }
    let ivk = SaplingIvk(fr.unwrap());

    let dk = DiversifierKey::from_bytes(*dk);

    let address = PaymentAddress::from_bytes(receiver);
    if address.is_none() {
        return -2;
    }
    let address = address.unwrap();

    let j = dk.diversifier_index(address.diversifier());

    // Now use the ivk to generate a receiver with this diversifier index.
    // If the receiver matches the one that came in, then this ivk produced the given receiver.
    // If it doesn't match, then this ivk did not produce the given receiver so no index is correct.
    let regenerated_diversifier = dk.diversifier(j);
    if regenerated_diversifier.is_none() {
        return -3;
    }
    let regenerated_address = ivk.to_payment_address(regenerated_diversifier.unwrap());
    if regenerated_address == Some(address) {
        diversifier_index.copy_from_slice(j.as_bytes());
        0
    } else {
        // Everything was valid. But the receiver did *not* come from this key.
        1
    }
}

#[no_mangle]
pub extern "C" fn derive_sapling_child(
    ext_sk: *const [u8; 169],
    child_index: u32,
    child: *mut [u8; 169],
) -> i32 {
    let ext_sk = unsafe { &*ext_sk };
    let child_index = match ChildIndex::from_index(child_index) {
        Some(index) => index,
        None => return -2,
    };
    let child_bytes = unsafe { &mut *child };

    // Do the same thing as derive_child, but write the result to the child variable and return an error code when it fails.
    match ExtendedSpendingKey::from_bytes(ext_sk) {
        Ok(sk) => {
            let derived_child = sk.derive_child(child_index);
            child_bytes.copy_from_slice(&derived_child.to_bytes());
            0
        }
        Err(_) => -1,
    }
}

#[no_mangle]
pub extern "C" fn derive_internal_fvk_sapling(
    fvk: *const [u8; 96],
    dk: *const [u8; 32],
    internal_fvk: *mut [u8; 96],
    internal_dk: *mut [u8; 32],
) -> i32 {
    let fvk = unsafe { &*fvk };
    let dk = unsafe { &*dk };
    let internal_fvk = unsafe { &mut *internal_fvk };
    let internal_dk = unsafe { &mut *internal_dk };

    if let Ok(fvk) = FullViewingKey::read(&fvk[..]) {
        let dk = DiversifierKey::from_bytes(dk.to_owned());
        let internal_key = sapling_derive_internal_fvk(&fvk, &dk);
        internal_fvk.copy_from_slice(&internal_key.0.to_bytes()[..]);
        internal_dk.copy_from_slice(&internal_key.1.as_bytes()[..]);
        0
    } else {
        -1
    }
}

// Technically all that *should* be required is the expanded spending key + dk
// But the API offered by the crates we use only offer internal key derivation
// for an extended spending key. We could rewrite the derivation easily enough
// if needed.
#[no_mangle]
pub extern "C" fn derive_internal_sk_sapling(
    ext_sk: *const [u8; 169],
    internal_ext_sk: *mut [u8; 169],
) -> i32 {
    let ext_sk = unsafe { &*ext_sk };
    let internal_ext_sk = unsafe { &mut *internal_ext_sk };

    if let Ok(ext_sk) = ExtendedSpendingKey::from_bytes(ext_sk) {
        let internal = ext_sk.derive_internal();
        internal_ext_sk.copy_from_slice(&internal.to_bytes()[..]);
        0
    } else {
        -1
    }
}

#[no_mangle]
pub extern "C" fn get_sapling_expanded_sk(sk: *const [u8; 32], expsk: *mut [u8; 96]) {
    let sk = unsafe { &*sk };
    let expsk_bytes = unsafe { &mut *expsk };

    let expsk = ExpandedSpendingKey::from_spending_key(sk);
    expsk_bytes.copy_from_slice(&expsk.to_bytes());
}

#[no_mangle]
pub extern "C" fn get_sapling_fvk_from_expanded_sk(
    expsk: *const [u8; 96],
    fvk: *mut [u8; 96],
) -> i32 {
    let expsk = unsafe { &*expsk };
    let fvk_bytes = unsafe { &mut *fvk };

    match ExpandedSpendingKey::from_bytes(expsk) {
        Ok(expsk) => {
            let fvk = FullViewingKey::from_expanded_spending_key(&expsk);
            match fvk.write(&mut fvk_bytes[..]) {
                Ok(_) => 0,
                _ => -2,
            }
        }
        _ => -1,
    }
}

#[no_mangle]
pub extern "C" fn get_sapling_receiver(
    ivk: *const [u8; 32],
    dk: *const [u8; 32],
    diversifier_index: *mut [u8; 11],
    receiver: *mut [u8; 43],
) -> i32 {
    let ivk = unsafe { &*ivk };
    let dk = unsafe { &*dk };
    let diversifier_index = unsafe { &mut *diversifier_index };
    let receiver = unsafe { &mut *receiver };
    let dk = DiversifierKey::from_bytes(*dk);

    let j = DiversifierIndex::from(*diversifier_index);

    let fr = jubjub::Fr::from_bytes(ivk);
    if fr.is_some().into() {
        let ivk = SaplingIvk(fr.unwrap());
        if let Some((index, d)) = dk.find_diversifier(j) {
            diversifier_index.copy_from_slice(index.as_bytes());
            if let Some(addr) = ivk.to_payment_address(d) {
                receiver.copy_from_slice(&addr.to_bytes());
                0
            } else {
                -3
            }
        } else {
            -2
        }
    } else {
        -1
    }
}
