using System;
using UnityEngine;

// События интерфейса — для звука (щелчок кнопки, покупка) и музыки магазина. UI только сообщает, подписчик — AudioCues.
public static class UiEvents
{
    public static event Action ButtonClicked;
    public static event Action<bool> ShopToggled;   // true — магазин открыли, false — закрыли
    public static event Action<bool> PurchaseDone;  // true — куплено, false — отказ (денег мало, рано...)

    public static void RaiseButtonClicked() => ButtonClicked?.Invoke();
    public static void RaiseShopToggled(bool open) => ShopToggled?.Invoke(open);
    public static void RaisePurchaseDone(bool success) => PurchaseDone?.Invoke(success);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners()
    {
        ButtonClicked = null;
        ShopToggled = null;
        PurchaseDone = null;
    }
}
