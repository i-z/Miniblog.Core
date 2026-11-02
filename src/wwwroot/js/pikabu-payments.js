/* global window, fetch */
// grantOnce must persist the reward and purchaseId atomically before resolving.
window.createPikabuPayments = function (sdk, endpoint, grantOnce) {
    "use strict";
    if (typeof grantOnce !== "function") {
        throw new Error("A durable, idempotent grantOnce handler is required");
    }
    const pending = new Map();

    async function process(purchase) {
        if (pending.has(purchase.purchaseId)) return pending.get(purchase.purchaseId);
        const task = (async function () {
            const signedData = await purchase.getSignedData();
            const signedPlayerData = await sdk.player.getSignedData();
            const response = await fetch(endpoint, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ signedData, signedPlayerData })
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.error || "Purchase verification failed");
            }
            if (result.purchaseId !== purchase.purchaseId || result.productId !== purchase.productId) {
                throw new Error("Purchase mismatch");
            }
            // Repeated calls (including after restarting the game) must not grant twice.
            // Use the verified playerId and isTestPurchase to select the correct save.
            await grantOnce(result);
            if (result.isConsumable) {
                const consumed = await sdk.store.consumePurchase(result.purchaseId);
                if (!consumed.success) throw new Error("Purchase consumption failed; retry on next launch");
            }
            return result;
        }());
        pending.set(purchase.purchaseId, task);
        try { return await task; }
        finally { pending.delete(purchase.purchaseId); }
    }

    return {
        getProducts: function () { return sdk.store.getProducts(); },
        purchase: async function (productId, developerPayload) {
            const result = await sdk.store.purchase({ productId, developerPayload });
            if (!result.success) return result;
            return process(result.purchase);
        },
        restore: async function () {
            const purchases = await sdk.store.getPurchases();
            // One failure must not prevent recovery of the other purchases.
            return Promise.allSettled(purchases.filter(function (p) { return p.isConsumable; }).map(process));
        }
    };
};
