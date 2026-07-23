using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillEntity : MAEntity, ICastRangePresenter
{
    private Transform _rangeTrans;
    private LineRenderer _rangeLineRenderer;
    private const int CastRangeSegments = 96;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _rangeTrans = transform.Find("CastRange");
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        HideCastRange();
        base.OnHide(isShutdown, userData);
    }

    public virtual void ShowCastRange(float radius)
    {
        if (radius <= 0.01f)
        {
            if (_rangeTrans != null)
                _rangeTrans.gameObject.SetActive(false);
            return;
        }

        EnsureCastRange();
        _rangeTrans.localScale = Vector3.one;
        _rangeTrans.gameObject.SetActive(true);
        UpdateCastRangeCircle(radius);
    }

    public virtual void HideCastRange()
    {
        ShowCastRange(0f);
    }

    private void EnsureCastRange()
    {
        if (_rangeTrans != null)
            return;

        GameObject rangeObject = new GameObject("CastRange");
        _rangeTrans = rangeObject.transform;
        _rangeTrans.SetParent(transform, false);
        _rangeTrans.localPosition = Vector3.zero;
        _rangeTrans.localRotation = Quaternion.identity;

        _rangeLineRenderer = rangeObject.AddComponent<LineRenderer>();
        _rangeLineRenderer.useWorldSpace = false;
        _rangeLineRenderer.loop = true;
        _rangeLineRenderer.positionCount = CastRangeSegments;
        _rangeLineRenderer.widthMultiplier = 0.08f;
        _rangeLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _rangeLineRenderer.startColor = new Color(0.37f, 0.5f, 1f, 0.8f);
        _rangeLineRenderer.endColor = _rangeLineRenderer.startColor;
    }

    private void UpdateCastRangeCircle(float radius)
    {
        if (_rangeLineRenderer == null)
        {
            float diameter = radius * 2f;
            _rangeTrans.localScale = new Vector3(diameter, diameter, 1f);
            return;
        }

        for (int i = 0; i < CastRangeSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / CastRangeSegments;
            _rangeLineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.03f, Mathf.Sin(angle) * radius));
        }
    }

}
