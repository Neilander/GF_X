using UnityEngine;
using AAAGame.Audio;

public class UnitMovementSound : MonoBehaviour
{
    [Header("移动音效设置")]
    public AudioCue moveSound;
    [Range(0.01f, 1f)] public float moveThreshold = 0.1f;
    public bool enableDebugLog = true;

    private AudioHandle moveAudioHandle;
    private bool isMoving = false;
    private MoveExecutor _moveExecutor;
    private CharacterController _characterController;

    void Awake()
    {
        _moveExecutor = GetComponent<MoveExecutor>();
        _characterController = GetComponent<CharacterController>();
        LogDebug($"Awake: MoveExecutor={_moveExecutor != null}, CharacterController={_characterController != null}");
    }

    void Update()
    {
        bool wasMoving = isMoving;
        isMoving = IsCharacterMoving();

        if (isMoving && !wasMoving)
        {
            LogDebug($"开始移动，调用 PlayMoveSound()");
            SafePlayMoveSound();
        }
        else if (!isMoving && wasMoving)
        {
            LogDebug($"停止移动，调用 StopMoveSound()");
            SafeStopMoveSound();
        }
    }

    private bool IsCharacterMoving()
    {
        if (_moveExecutor != null)
        {
            bool moving = _moveExecutor.IsMoving();
            LogDebug($"MoveExecutor.IsMoving() = {moving}");
            return moving;
        }

        if (_characterController != null)
        {
            Vector3 velocity = _characterController.velocity;
            velocity.y = 0;
            bool moving = velocity.magnitude > moveThreshold;
            LogDebug($"CharacterController.velocity={velocity.magnitude:F4}, moving={moving}");
            return moving;
        }

        LogDebug($"没有移动组件");
        return false;
    }

    /// <summary>
    /// 安全播放移动音效（带异常捕获）
    /// </summary>
    private void SafePlayMoveSound()
    {
        try
        {
            PlayMoveSound();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UnitMovementSound] 播放音效失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void PlayMoveSound()
    {
        LogDebug($"PlayMoveSound() 开始");

        // 检查 AudioManager
        if (AudioManager.Instance == null)
        {
            Debug.LogError($"[UnitMovementSound] AudioManager.Instance 为 null！请在场景中创建 AudioManager 对象！");
            return;
        }
        LogDebug($"AudioManager.Instance 存在");

        // 检查音效资产
        if (moveSound == null)
        {
            Debug.LogError($"[UnitMovementSound] moveSound 未赋值！请在 Inspector 中设置！");
            return;
        }
        LogDebug($"moveSound 已配置: {moveSound.name}");

        // 检查是否已在播放
        if (moveAudioHandle.IsValid)
        {
            LogDebug($"音效已在播放中");
            return;
        }

        // 播放音效
        LogDebug($"调用 AudioManager.Instance.Play({moveSound.name})");
        moveAudioHandle = AudioManager.Instance.Play(moveSound);
        LogDebug($"播放成功，句柄 Id={moveAudioHandle.Id}");
    }

    /// <summary>
    /// 安全停止移动音效（带异常捕获）
    /// </summary>
    private void SafeStopMoveSound()
    {
        try
        {
            StopMoveSound();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UnitMovementSound] 停止音效失败: {ex.Message}");
        }
    }

    private void StopMoveSound()
    {
        LogDebug($"StopMoveSound() 开始");

        if (!moveAudioHandle.IsValid)
        {
            LogDebug($"句柄无效");
            return;
        }

        if (AudioManager.Instance == null)
        {
            Debug.LogError($"[UnitMovementSound] AudioManager.Instance 为 null！");
            return;
        }

        AudioManager.Instance.StopSfx(moveAudioHandle);
        moveAudioHandle = AudioHandle.Invalid;
        LogDebug($"停止成功");
    }

    private void LogDebug(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[UnitMovementSound] [{gameObject.name}] {message}");
        }
    }
}