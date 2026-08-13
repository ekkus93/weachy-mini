package com.ekkus93.weachy.providers;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/**
 * Inert placeholder retained only because repository automation could not delete the abandoned
 * headless-acceptance source in the same change set. The manifest does not expose this receiver,
 * and it performs no credential or application operation.
 */
public final class ReachyProviderCredentialAcceptanceReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        // Intentionally inert.
    }
}
