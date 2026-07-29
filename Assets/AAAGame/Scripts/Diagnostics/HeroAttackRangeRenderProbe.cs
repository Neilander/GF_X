using System;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(32000)]
public sealed class HeroAttackRangeRenderProbe : MonoBehaviour
{
    private const string DisplayNodeName = "Display";
    private const string PreviewNodeName = "AttackRangePreview";
    private const float PreviewHeight = 0.04f;

    private struct Sample
    {
        public int RenderFrame;
        public ulong LogicFrame;
        public double Interpolation;
        public Vector3 LogicPosition;
        public Vector3 RootPosition;
        public Vector3 DisplayPosition;
        public Vector3 CircleCenter;
        public Vector3 RendererBoundsCenter;
        public Vector3 HealthBarPosition;
    }

    private HeroEntity m_Hero;
    private Transform m_Display;
    private Transform m_Preview;
    private LineRenderer m_Renderer;
    private Transform m_HealthBar;
    private Sample[] m_Samples;
    private int m_SampleCount;

    public void BeginCapture(HeroEntity hero, int renderFrameCount)
    {
        if (hero == null)
            throw new ArgumentNullException(nameof(hero));
        if (renderFrameCount < 2)
            throw new ArgumentOutOfRangeException(nameof(renderFrameCount), renderFrameCount, "Render probe requires at least two frames.");

        Transform display = hero.transform.Find(DisplayNodeName);
        if (display == null)
            throw new InvalidOperationException($"Hero range render probe requires '{DisplayNodeName}'. entity={hero.Id}.");
        Transform preview = display.Find(PreviewNodeName);
        if (preview == null)
            throw new InvalidOperationException($"Hero range render probe requires '{DisplayNodeName}/{PreviewNodeName}'. entity={hero.Id}.");
        LineRenderer lineRenderer = preview.GetComponent<LineRenderer>();
        if (lineRenderer == null)
            throw new InvalidOperationException($"Hero range render probe requires a LineRenderer. entity={hero.Id}.");
        GameObject healthBar = GameObject.Find($"HealthBar_{hero.Id}");
        if (healthBar == null)
            throw new InvalidOperationException($"Hero range render probe requires a health bar. entity={hero.Id}.");

        m_Hero = hero;
        m_Display = display;
        m_Preview = preview;
        m_Renderer = lineRenderer;
        m_HealthBar = healthBar.transform;
        m_Samples = new Sample[renderFrameCount];
        m_SampleCount = 0;
    }

    private void LateUpdate()
    {
        if (m_Samples == null)
            return;
        if (!LogicFrameRuntime.IsTimelineRunning)
            throw new InvalidOperationException("Hero range render probe lost the running logic timeline.");

        FixVector2 logicPosition = m_Hero.PositionFixed;
        m_Samples[m_SampleCount++] = new Sample
        {
            RenderFrame = Time.frameCount,
            LogicFrame = LogicFrameRuntime.CurrentFrame,
            Interpolation = LogicFrameRuntime.Interpolation,
            LogicPosition = new Vector3((float)logicPosition.x, m_Hero.transform.position.y, (float)logicPosition.y),
            RootPosition = m_Hero.transform.position,
            DisplayPosition = m_Display.position,
            CircleCenter = m_Preview.TransformPoint(new Vector3(0f, PreviewHeight, 0f)),
            RendererBoundsCenter = m_Renderer.bounds.center,
            HealthBarPosition = m_HealthBar.position,
        };

        if (m_SampleCount < m_Samples.Length)
            return;

        Debug.Log(BuildReport());
        m_Samples = null;
        Destroy(this);
    }

    private string BuildReport()
    {
        int missingRenderFrames = 0;
        float maxCircleDisplayOffset = 0f;
        float maxBoundsCircleOffset = 0f;
        float maxRelativeStepError = 0f;
        float maxLogicDisplayOffset = 0f;
        float maxCircleStep = 0f;
        float maxDisplayStep = 0f;
        float maxRootStep = 0f;
        float maxHealthBarRootStepError = 0f;
        float maxHealthBarDisplayStepError = 0f;
        int worstRelativeIndex = 1;
        int worstCircleStepIndex = 1;
        int worstHealthBarDisplayIndex = 1;

        for (int i = 0; i < m_SampleCount; i++)
        {
            Sample current = m_Samples[i];
            maxCircleDisplayOffset = Mathf.Max(maxCircleDisplayOffset, HorizontalDistance(current.CircleCenter, current.DisplayPosition));
            maxBoundsCircleOffset = Mathf.Max(maxBoundsCircleOffset, HorizontalDistance(current.RendererBoundsCenter, current.CircleCenter));
            maxLogicDisplayOffset = Mathf.Max(maxLogicDisplayOffset, HorizontalDistance(current.DisplayPosition, current.LogicPosition));
            if (i == 0)
                continue;

            Sample previous = m_Samples[i - 1];
            missingRenderFrames += Mathf.Max(0, current.RenderFrame - previous.RenderFrame - 1);
            Vector3 circleStep = current.CircleCenter - previous.CircleCenter;
            Vector3 displayStep = current.DisplayPosition - previous.DisplayPosition;
            Vector3 rootStep = current.RootPosition - previous.RootPosition;
            Vector3 healthBarStep = current.HealthBarPosition - previous.HealthBarPosition;
            float relativeStepError = HorizontalMagnitude(circleStep - displayStep);
            if (relativeStepError > maxRelativeStepError)
            {
                maxRelativeStepError = relativeStepError;
                worstRelativeIndex = i;
            }
            float circleStepMagnitude = HorizontalMagnitude(circleStep);
            if (circleStepMagnitude > maxCircleStep)
            {
                maxCircleStep = circleStepMagnitude;
                worstCircleStepIndex = i;
            }
            maxDisplayStep = Mathf.Max(maxDisplayStep, HorizontalMagnitude(displayStep));
            maxRootStep = Mathf.Max(maxRootStep, HorizontalMagnitude(rootStep));
            maxHealthBarRootStepError = Mathf.Max(maxHealthBarRootStepError, HorizontalMagnitude(healthBarStep - rootStep));
            float healthBarDisplayStepError = HorizontalMagnitude(healthBarStep - displayStep);
            if (healthBarDisplayStepError > maxHealthBarDisplayStepError)
            {
                maxHealthBarDisplayStepError = healthBarDisplayStepError;
                worstHealthBarDisplayIndex = i;
            }
        }

        Sample first = m_Samples[0];
        Sample last = m_Samples[m_SampleCount - 1];
        var builder = new StringBuilder(1024);
        builder.Append("[HeroRangeRenderProbe] entity=").Append(m_Hero.Id)
            .Append(" samples=").Append(m_SampleCount)
            .Append(" renderFrames=").Append(first.RenderFrame).Append("..").Append(last.RenderFrame)
            .Append(" missingRenderFrames=").Append(missingRenderFrames)
            .Append(" logicFrames=").Append(first.LogicFrame).Append("..").Append(last.LogicFrame)
            .Append(" maxCircleDisplayOffsetXZ=").Append(maxCircleDisplayOffset.ToString("F6"))
            .Append(" maxBoundsCircleOffsetXZ=").Append(maxBoundsCircleOffset.ToString("F6"))
            .Append(" maxRelativeStepErrorXZ=").Append(maxRelativeStepError.ToString("F6"))
            .Append(" maxLogicDisplayOffsetXZ=").Append(maxLogicDisplayOffset.ToString("F6"))
            .Append(" maxStepXZ(circle/display/root)=")
            .Append(maxCircleStep.ToString("F6")).Append('/')
            .Append(maxDisplayStep.ToString("F6")).Append('/')
            .Append(maxRootStep.ToString("F6"))
            .Append(" maxHealthBarStepErrorXZ(root/display)=")
            .Append(maxHealthBarRootStepError.ToString("F6")).Append('/')
            .Append(maxHealthBarDisplayStepError.ToString("F6"))
            .Append(" worstRelativePair=");
        AppendSample(builder, m_Samples[worstRelativeIndex - 1]);
        builder.Append(" -> ");
        AppendSample(builder, m_Samples[worstRelativeIndex]);
        builder.Append(" worstCircleStepPair=");
        AppendSample(builder, m_Samples[worstCircleStepIndex - 1]);
        builder.Append(" -> ");
        AppendSample(builder, m_Samples[worstCircleStepIndex]);
        builder.Append(" worstHealthBarDisplayPair=");
        AppendSample(builder, m_Samples[worstHealthBarDisplayIndex - 1]);
        builder.Append(" -> ");
        AppendSample(builder, m_Samples[worstHealthBarDisplayIndex]);
        return builder.ToString();
    }

    private static void AppendSample(StringBuilder builder, Sample sample)
    {
        builder.Append("{rf=").Append(sample.RenderFrame)
            .Append(",lf=").Append(sample.LogicFrame)
            .Append(",alpha=").Append(sample.Interpolation.ToString("F4"))
            .Append(",logic=").Append(FormatVector(sample.LogicPosition))
            .Append(",root=").Append(FormatVector(sample.RootPosition))
            .Append(",display=").Append(FormatVector(sample.DisplayPosition))
            .Append(",circle=").Append(FormatVector(sample.CircleCenter))
            .Append(",bounds=").Append(FormatVector(sample.RendererBoundsCenter))
            .Append(",healthBar=").Append(FormatVector(sample.HealthBarPosition))
            .Append('}');
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F4},{value.y:F4},{value.z:F4})";
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        return HorizontalMagnitude(left - right);
    }

    private static float HorizontalMagnitude(Vector3 value)
    {
        return Mathf.Sqrt(value.x * value.x + value.z * value.z);
    }

}
