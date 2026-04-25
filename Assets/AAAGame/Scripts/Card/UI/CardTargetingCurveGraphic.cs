using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.Card.UI
{
    /// <summary>
    /// uGUI 曲线绘制组件，用于卡牌和准星之间的连线效果。
    /// </summary>
    public sealed class CardTargetingCurveGraphic : MaskableGraphic
    {
        [SerializeField]
        [Min(1f)]
        private float thickness = 12f;

        [SerializeField]
        [Range(4, 64)]
        private int segmentCount = 24;

        private bool m_HasCurve;
        private Vector2 m_StartPoint;
        private Vector2 m_StartControlPoint;
        private Vector2 m_EndControlPoint;
        private Vector2 m_EndPoint;

        public float Thickness
        {
            get => thickness;
            set
            {
                float clamped = Mathf.Max(1f, value);
                if (Mathf.Approximately(thickness, clamped))
                    return;

                thickness = clamped;
                SetVerticesDirty();
            }
        }

        public int SegmentCount
        {
            get => segmentCount;
            set
            {
                int clamped = Mathf.Clamp(value, 4, 64);
                if (segmentCount == clamped)
                    return;

                segmentCount = clamped;
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void SetCurve(Vector2 startPoint, Vector2 endPoint, float curveHeight)
        {
            m_HasCurve = true;
            m_StartPoint = startPoint;
            m_EndPoint = endPoint;

            Vector2 direction = endPoint - startPoint;
            float distance = direction.magnitude;
            Vector2 normalizedDirection = distance > 0.0001f
                ? direction / distance
                : Vector2.up;

            float horizontalAbs = Mathf.Abs(direction.x);
            float verticalAbs = Mathf.Abs(direction.y);
            float horizontalRatio = distance > 0.0001f ? horizontalAbs / distance : 0f;
            float verticalRatio = distance > 0.0001f ? verticalAbs / distance : 0f;
            float horizontalSign = Mathf.Abs(direction.x) > 0.0001f ? Mathf.Sign(direction.x) : 0f;
            Vector2 midpoint = (startPoint + endPoint) * 0.5f;

            float startLift = Mathf.Max(
                curveHeight * Mathf.Lerp(0.6f, 0.95f, horizontalRatio),
                distance * Mathf.Lerp(0.12f, 0.24f, horizontalRatio));

            float startSideOffset = horizontalSign * Mathf.Max(
                horizontalAbs * 0.18f,
                curveHeight * 0.28f * horizontalRatio);

            float endSideOffset = horizontalSign * Mathf.Max(
                horizontalAbs * 0.26f,
                curveHeight * 0.42f * horizontalRatio);

            float endLift = Mathf.Max(
                curveHeight * Mathf.Lerp(0.12f, 0.3f, 1f - verticalRatio),
                distance * 0.08f);

            float endPullBack = Mathf.Max(
                curveHeight * 0.12f,
                distance * Mathf.Lerp(0.05f, 0.11f, horizontalRatio));

            float middleLift = Mathf.Max(
                curveHeight * Mathf.Lerp(1.05f, 1.35f, horizontalRatio),
                distance * Mathf.Lerp(0.18f, 0.32f, horizontalRatio));

            float middleSideOffset = horizontalSign * Mathf.Max(
                horizontalAbs * 0.12f,
                curveHeight * 0.18f * horizontalRatio);

            Vector2 apexPoint = midpoint
                + Vector2.up * middleLift
                + Vector2.right * middleSideOffset;

            Vector2 startLeadPoint = startPoint
                + Vector2.up * startLift
                + Vector2.right * startSideOffset;

            Vector2 endLeadPoint = endPoint
                + Vector2.up * endLift
                + Vector2.right * endSideOffset
                - normalizedDirection * endPullBack;

            m_StartControlPoint = Vector2.Lerp(startLeadPoint, apexPoint, 0.48f);
            m_EndControlPoint = Vector2.Lerp(endLeadPoint, apexPoint, 0.56f);

            SetVerticesDirty();
        }

        public void ClearCurve()
        {
            if (!m_HasCurve)
                return;

            m_HasCurve = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!m_HasCurve)
                return;

            Vector2 previousPoint = Evaluate(0f);
            for (int i = 1; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector2 currentPoint = Evaluate(t);
                AddSegment(vh, previousPoint, currentPoint);
                previousPoint = currentPoint;
            }
        }

        private Vector2 Evaluate(float t)
        {
            float oneMinusT = 1f - t;
            float oneMinusTSqr = oneMinusT * oneMinusT;
            float tSqr = t * t;

            return oneMinusTSqr * oneMinusT * m_StartPoint
                   + 3f * oneMinusTSqr * t * m_StartControlPoint
                   + 3f * oneMinusT * tSqr * m_EndControlPoint
                   + tSqr * t * m_EndPoint;
        }

        private void AddSegment(VertexHelper vh, Vector2 startPoint, Vector2 endPoint)
        {
            Vector2 direction = endPoint - startPoint;
            if (direction.sqrMagnitude <= 0.000001f)
                return;

            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);

            int startIndex = vh.currentVertCount;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = startPoint - normal;
            vh.AddVert(vertex);

            vertex.position = startPoint + normal;
            vh.AddVert(vertex);

            vertex.position = endPoint + normal;
            vh.AddVert(vertex);

            vertex.position = endPoint - normal;
            vh.AddVert(vertex);

            vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vh.AddTriangle(startIndex, startIndex + 2, startIndex + 3);
        }
    }
}
