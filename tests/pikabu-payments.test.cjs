const { test } = require('node:test');
const assert = require('node:assert/strict');
global.window = {};
require('../src/wwwroot/js/pikabu-payments.js');

test('verify, persist once, consume; recover after failed consume', async () => {
    const order = { purchaseId: 'p1', productId: 'coins', isConsumable: true, getSignedData: async () => 'purchase-jwt' };
    let consumed = 0;
    let granted = 0;
    let saved = false;
    global.fetch = async (_url, options) => {
        assert.deepEqual(JSON.parse(options.body), { signedData: 'purchase-jwt', signedPlayerData: 'player-jwt' });
        return { ok: true, json: async () => ({ success: true, ...order }) };
    };
    const sdk = {
        player: { getSignedData: async () => 'player-jwt' },
        store: {
            purchase: async () => ({ success: true, purchase: order }),
            getPurchases: async () => [order],
            consumePurchase: async () => {
                assert.equal(saved, true);
                return { success: ++consumed > 1 };
            }
        }
    };
    const payments = window.createPikabuPayments(sdk, '/pikabu/payments', async () => {
        if (!saved) { granted++; saved = true; }
    });
    await assert.rejects(payments.purchase('coins'), /consumption failed/);
    assert.equal((await payments.restore())[0].status, 'fulfilled');
    assert.equal(granted, 1);
    assert.equal(consumed, 2);

    global.fetch = async () => ({ ok: false, json: async () => ({ success: false, error: 'invalid_signature' }) });
    await assert.rejects(payments.purchase('coins'), /invalid_signature/);
    assert.equal(consumed, 2);

    global.fetch = async () => ({ ok: true, json: async () => ({ success: true, ...order }) });
    const failedSave = window.createPikabuPayments(sdk, '/pikabu/payments', async () => { throw new Error('save failed'); });
    await assert.rejects(failedSave.purchase('coins'), /save failed/);
    assert.equal(consumed, 2);

    sdk.store.purchase = async () => ({ success: false, reason: 'USER_CANCELLED' });
    assert.equal((await payments.purchase('coins')).reason, 'USER_CANCELLED');
    assert.equal(consumed, 2);
});
