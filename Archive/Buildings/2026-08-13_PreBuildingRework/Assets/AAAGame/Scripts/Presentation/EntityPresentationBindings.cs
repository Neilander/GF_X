using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EntityPresentationBindings : MonoBehaviour
{
    public const string DisplayObjectName = "Display";
    public const string SocketsObjectName = "PresentationSockets";
    public const string ProjectileOriginObjectName = "ProjectileOrigin";

    [SerializeField] private Transform displayRoot;
    [SerializeField] private Renderer sizeReferenceRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform projectileOrigin;
    [SerializeField] private Transform weaponAnchor;
    [SerializeField] private Transform trailPoint;
    [SerializeField] private bool placeholder;

    public Transform DisplayRoot => displayRoot;
    public Renderer SizeReferenceRenderer => sizeReferenceRenderer;
    public Animator Animator => animator;
    public Transform ProjectileOrigin => projectileOrigin;
    public Transform WeaponAnchor => weaponAnchor;
    public Transform TrailPoint => trailPoint;
    public bool IsPlaceholder => placeholder;

    public void Configure(
        Transform newDisplayRoot,
        Renderer newSizeReferenceRenderer,
        Animator newAnimator,
        Transform newProjectileOrigin,
        Transform newWeaponAnchor,
        Transform newTrailPoint,
        bool isPlaceholder)
    {
        displayRoot = newDisplayRoot;
        sizeReferenceRenderer = newSizeReferenceRenderer;
        animator = newAnimator;
        projectileOrigin = newProjectileOrigin;
        weaponAnchor = newWeaponAnchor;
        trailPoint = newTrailPoint;
        placeholder = isPlaceholder;
    }

    public void ValidateOrThrow(bool requireUnitRuntime)
    {
        if (displayRoot == null)
            throw new InvalidOperationException($"Entity presentation is missing Display binding. entity={name}.");
        if (displayRoot.name != DisplayObjectName || displayRoot.parent != transform)
        {
            throw new InvalidOperationException(
                $"Entity presentation Display must be a direct child named '{DisplayObjectName}'. entity={name}, display={GetPath(displayRoot)}.");
        }
        if (displayRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
            throw new InvalidOperationException($"Entity presentation Display contains no Renderer. entity={name}.");
        Collider[] displayColliders = displayRoot.GetComponentsInChildren<Collider>(true);
        if (displayColliders.Length > 0)
        {
            throw new InvalidOperationException(
                $"Entity presentation Display must be visual-only and contains Collider '{GetPath(displayColliders[0].transform)}'. entity={name}.");
        }

        ValidateOptionalBinding(projectileOrigin, nameof(projectileOrigin));
        ValidateOptionalBinding(weaponAnchor, nameof(weaponAnchor));
        ValidateOptionalBinding(trailPoint, nameof(trailPoint));

        bool hasWeaponAnchor = weaponAnchor != null;
        bool hasTrailPoint = trailPoint != null;
        if (hasWeaponAnchor != hasTrailPoint)
        {
            throw new InvalidOperationException(
                $"Entity presentation trail binding must provide both WeaponAnchor and TrailPoint. entity={name}.");
        }

        if (!requireUnitRuntime)
            return;

        if (sizeReferenceRenderer == null)
            throw new InvalidOperationException($"Unit presentation is missing SizeReferenceRenderer binding. entity={name}.");
        if (sizeReferenceRenderer is not MeshRenderer && sizeReferenceRenderer is not SkinnedMeshRenderer)
        {
            throw new InvalidOperationException(
                $"Unit SizeReferenceRenderer must be a MeshRenderer or SkinnedMeshRenderer. entity={name}, renderer={sizeReferenceRenderer.GetType().Name}.");
        }
        if (!sizeReferenceRenderer.transform.IsChildOf(displayRoot))
        {
            throw new InvalidOperationException(
                $"Unit SizeReferenceRenderer must be inside Display. entity={name}, renderer={GetPath(sizeReferenceRenderer.transform)}.");
        }

        CharacterController controller = GetComponent<CharacterController>();
        if (controller == null)
            throw new InvalidOperationException($"Unit presentation requires CharacterController on entity root. entity={name}.");
        if (animator == null)
            throw new InvalidOperationException($"Unit presentation is missing Animator binding. entity={name}.");
        if (animator.runtimeAnimatorController == null)
            throw new InvalidOperationException($"Unit presentation Animator has no controller. entity={name}, animator={GetPath(animator.transform)}.");
        if (animator.transform != transform && !animator.transform.IsChildOf(displayRoot))
        {
            throw new InvalidOperationException(
                $"Unit presentation Animator must be on the entity root or inside Display. entity={name}, animator={GetPath(animator.transform)}.");
        }

    }

    public void ValidateRuntimeAnimatorContractOrThrow()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            throw new InvalidOperationException($"Cannot validate Animator contract because the binding is incomplete. entity={name}.");

        RequireRuntimeAnimatorParameter("Moving", AnimatorControllerParameterType.Bool);
        RequireRuntimeAnimatorParameter("Attack", AnimatorControllerParameterType.Trigger);
    }

    public Transform RequireProjectileOrigin()
    {
        if (projectileOrigin == null)
            throw new InvalidOperationException($"Projectile presentation requires an explicit ProjectileOrigin binding. entity={name}.");
        ValidateOptionalBinding(projectileOrigin, nameof(projectileOrigin));
        return projectileOrigin;
    }

    public void RequireTrailBinding(out Transform requiredWeaponAnchor, out Transform requiredTrailPoint)
    {
        if (weaponAnchor == null || trailPoint == null)
            throw new InvalidOperationException($"Weapon trail presentation requires explicit WeaponAnchor and TrailPoint bindings. entity={name}.");
        ValidateOptionalBinding(weaponAnchor, nameof(weaponAnchor));
        ValidateOptionalBinding(trailPoint, nameof(trailPoint));
        requiredWeaponAnchor = weaponAnchor;
        requiredTrailPoint = trailPoint;
    }

    private void ValidateOptionalBinding(Component binding, string bindingName)
    {
        if (binding == null)
            return;
        Transform bindingTransform = binding.transform;
        if (bindingTransform == transform || !bindingTransform.IsChildOf(transform))
        {
            throw new InvalidOperationException(
                $"Entity presentation binding '{bindingName}' must be inside the entity hierarchy. entity={name}, binding={GetPath(bindingTransform)}.");
        }
    }

    private void RequireRuntimeAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.name == parameterName && parameter.type == parameterType)
                return;
        }

        throw new InvalidOperationException(
            $"Unit presentation Animator is missing {parameterType} parameter '{parameterName}'. entity={name}, controller={animator.runtimeAnimatorController.name}.");
    }

    private static string GetPath(Transform target)
    {
        if (target == null)
            return "<null>";

        string path = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
