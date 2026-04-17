using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public class PopTextManager : GameFrameworkComponent
{
    private readonly Vector3 heroPopOffset = new Vector3(0f, 2.0f, 0f);
    private readonly Vector3 popRiseOffset = new Vector3(0f, 1.5f, 0f);
    private bool eventSubscribed;
    private bool waitingEventReadyLogged;

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        TrySubscribeEvents();
    }

    private void Update()
    {
        if (!eventSubscribed)
            TrySubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
    }

    private void TrySubscribeEvents()
    {
        if (eventSubscribed)
            return;

        if (GF.Event == null)
        {
            if (!waitingEventReadyLogged)
            {
                waitingEventReadyLogged = true;
                Debug.Log("[PopTextManager] Waiting for GF.Event to become ready...");
            }

            return;
        }

        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        eventSubscribed = true;
        waitingEventReadyLogged = false;
        Debug.Log("[PopTextManager] Subscribed IngameValueChangedEventArgs.");
    }

    private void UnsubscribeEvents()
    {
        if (!eventSubscribed)
            return;

        if (GF.Event != null)
        {
            try
            {
                GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
            }
            catch (GameFrameworkException)
            {
                // PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
            }
        }

        eventSubscribed = false;
    }

    private void OnIngameValueChanged(object sender, GameEventArgs e)
    {
        if (e is not IngameValueChangedEventArgs args)
            return;

        if (args.DataType != IngameValueType.Coin)
            return;

        int delta = args.Value - args.OldValue;
        if (delta <= 0)
            return;

        ShowCoinGainPopText(delta);
    }

    private void ShowCoinGainPopText(int deltaCoin)
    {
        if (deltaCoin <= 0)
            return;

        if (GF.Entity == null)
            return;

        if (!TryGetHeroHeadPosition(out Vector3 startPos))
        {
            Log.Warning("[PopTextManager] Coin gain pop text skipped: player hero not ready. deltaCoin={0}", deltaCoin);
            return;
        }

        Vector3 endPos = startPos + popRiseOffset;
        string content = $"+{deltaCoin}";
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), content, endPos, DamageTextType.Coin);
    }

    private bool TryGetHeroHeadPosition(out Vector3 position)
    {
        position = Vector3.zero;

        if (EntityRegistry.Player is MAEntity playerEntity)
        {
            position = playerEntity.transform.position + heroPopOffset;
            return true;
        }

        if (EntityRegistry.Player != null)
        {
            position = EntityRegistry.Player.Position + heroPopOffset;
            return true;
        }

        return false;
    }
}