using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonShooterController : MonoBehaviour
{
    Transform playerTransform;
    Animator animator;
    Transform cameraTransform;
    CharacterController characterController;

    public enum PlayerPosture//玩家姿势
    {
        Crouch,//蹲下
        Stand,//站立
        Midair,//空中
        Landing//落地状态
    }
    [HideInInspector]
    public PlayerPosture playerPosture = PlayerPosture.Stand;

    float crouchThreshold = 0f;
    float standThreshold = 1f;
    float midairThreshold = 2.1f;
    float landingThreshold = 1f;

    public enum LocomotionState//运动状态
    {
        Idle,
        Walk,
        Run
    }
    [HideInInspector]

    public LocomotionState locomotionState = LocomotionState.Idle;

    public enum ArmState
    {
        Normal,
        Aim//非站立姿态，别的操作都行
    }
    [HideInInspector]
    public ArmState armState = ArmState.Normal;

    float WalkSpeed = 2.5f;
    float crouchSpeed = 1.5f;
    float runSpeed = 5.5f;

    Vector2 moveInput;
    bool isRunning;
    bool isCrouch;
    //别的操作都行
    bool isAiming;
    bool isJumping;

    //哈希值
    int postureHash;
    int moveSpeedHash;
    int turnSpeedHash;
    int verticalVelocityHash;
    int feetTweenHash;

    Vector3 playerMovement = Vector3.zero;
    //重力
    public float gravity = -9.8f;

    //垂直方向速度
    float VerticalVelocity;

    //最大跳起高度
    public float maxHeight = 1.5f;

    //滞空左右脚状态
    float feetTween;

    #region
    Vector3 lastVelOnGround;

    static readonly int CACHE_SIZE = 3;//三帧
    Vector3[] velCache = new Vector3[CACHE_SIZE];
    int currentChacheIndex = 0;
    Vector3 averageVel = Vector3.zero;
    #endregion
    float fallMultiplier = 1.5f;

    //是否处于CD状态
    bool isLanding;

    //是否在地面
    bool isGrounded;

    //地表检测射线的偏移量
    float groundCheckOffset = 0.5f;

    //跳跃CD
    float jumpCD = 0.15f;

    void Start()
    {
        playerTransform = transform;
        animator = GetComponent<Animator>();
        cameraTransform = Camera.main.transform;
        characterController = GetComponent<CharacterController>();

        postureHash = Animator.StringToHash("player pos");
        moveSpeedHash = Animator.StringToHash("move speed");
        turnSpeedHash = Animator.StringToHash("rotation speed");
        verticalVelocityHash = Animator.StringToHash("Vertical speed");
        feetTweenHash = Animator.StringToHash("left right leg");
        Cursor.lockState = CursorLockMode.Locked;//关闭鼠标
    }

    // Update is called once per frame
    void Update()
    {
        CheckGround();
        SwitchPlayerStates();
        CaculateGravity();
        Jump();
        CaculateInputDirection();
        SetupAnimation();
    }

    #region about input
    public void GetMoveInput(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    public void GetRunInput(InputAction.CallbackContext ctx)
    {
        isRunning = ctx.ReadValueAsButton();
    }

    public void GetCrouchInput(InputAction.CallbackContext ctx)
    {
        isCrouch = ctx.ReadValueAsButton();
    }
    public void GetAimInput(InputAction.CallbackContext ctx)
    {
        isAiming = ctx.ReadValueAsButton();
    }

    public void GetJump(InputAction.CallbackContext ctx)
    {
        isJumping = ctx.ReadValueAsButton();
    }
    #endregion

    void SwitchPlayerStates()
    {
        if (!isGrounded)
        {
            playerPosture = PlayerPosture.Midair;
        }
        else if(playerPosture == PlayerPosture.Midair)
        {
            StartCoroutine(CoolDownJump());
        }
        else if (isLanding)
        {
            playerPosture = PlayerPosture.Landing;
        }
        else if (isCrouch)
        {
            playerPosture = PlayerPosture.Crouch;
        }
        else
        {
            playerPosture = PlayerPosture.Stand;
        }

        if(moveInput.magnitude == 0)
        {
            locomotionState = LocomotionState.Idle;
        }
        else if (!isRunning)
        {
            locomotionState = LocomotionState.Walk;
        }
        else
        {
            locomotionState = LocomotionState.Run;
        }

        if (isAiming)
        {
            armState = ArmState.Aim;
        }
        else
        {
            armState = ArmState.Normal;
        }

    }
    void CheckGround()
    {
        //这里可以做layer判断地表，就是地表碰到的物体
        if(Physics.SphereCast(playerTransform.position + (Vector3.up * groundCheckOffset),characterController.radius,Vector3.down,out RaycastHit hit,groundCheckOffset - characterController.radius + 2 * characterController.skinWidth))
        {
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
    }

    IEnumerator CoolDownJump()//CD计算携程
    {
        landingThreshold = Mathf.Clamp(VerticalVelocity, -10, 0);
        landingThreshold /= 20f;
        landingThreshold += 1f;
        isLanding = true;
        playerPosture = PlayerPosture.Landing;
        yield return new WaitForSeconds(jumpCD);
        isLanding = false;
    }
    void Jump()
    {
        if(playerPosture == PlayerPosture.Stand && isJumping)
        {
            VerticalVelocity = Mathf.Sqrt(-2 * gravity * maxHeight);
            feetTween = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            feetTween = feetTween < 0.5f ? 1 : -1;
            if (locomotionState == LocomotionState.Run)
            {
                feetTween *= 3;
            }
            else if (locomotionState == LocomotionState.Walk)
            {
                feetTween *= 2;
            }
            else
            {
                feetTween = Random.Range(0.5f, 1f) * feetTween;
            }
        }
    }
    void CaculateGravity()
    {
        if (playerPosture != PlayerPosture.Midair)//位于地面->用姿态判断是否在地面
        {
            VerticalVelocity = gravity * Time.deltaTime;//isGrounded需要一直有一个向下的速度不能为0
            return;
        }
        else
        {
            if(VerticalVelocity <= 0||!isJumping)
            {
                VerticalVelocity += gravity * fallMultiplier * Time.deltaTime;
            }
            else
            {
                VerticalVelocity += gravity * Time.deltaTime;
            }
        }
    }
    void CaculateInputDirection()
    {
        Vector3 camForwardProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;
        playerMovement = camForwardProjection * moveInput.y + cameraTransform.right * moveInput.x;
        playerMovement = playerTransform.InverseTransformVector(playerMovement);
    }

    void SetupAnimation()
    {
        if(playerPosture == PlayerPosture.Stand)
        {
            animator.SetFloat(postureHash, standThreshold, 0.1f, Time.deltaTime);
            switch(locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * WalkSpeed, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
       
        else if(playerPosture == PlayerPosture.Crouch)
        {
            animator.SetFloat(postureHash, crouchThreshold, 0.1f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                default:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * crouchSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if(playerPosture == PlayerPosture.Midair)
        {
            animator.SetFloat(postureHash, midairThreshold);
            animator.SetFloat(verticalVelocityHash, VerticalVelocity);
            animator.SetFloat(feetTweenHash, feetTween);
        }

        else if(playerPosture == PlayerPosture.Landing)
        {
            animator.SetFloat(postureHash, landingThreshold, 0.03f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * WalkSpeed, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }

        if(armState == ArmState.Normal)
        {
            float rad = Mathf.Atan2(playerMovement.x, playerMovement.z);
            animator.SetFloat(turnSpeedHash, rad, 0.1f, Time.deltaTime);
            playerTransform.Rotate(0, rad * 180 * Time.deltaTime, 0f);
        }
    }

    Vector3 AverageVel(Vector3 newVel)
    {
        velCache[currentChacheIndex] = newVel;
        currentChacheIndex++;
        currentChacheIndex %= CACHE_SIZE;
        Vector3 average = Vector3.zero;
        foreach(Vector3 vel in velCache)
        {
            average += vel;
        }
        return average / CACHE_SIZE;
    }

    private void OnAnimatorMove()
    {
        if(playerPosture != PlayerPosture.Midair)
        {
            Vector3 playerDeltaMovement = animator.deltaPosition;//平均位移(跟帧数有关）
            playerDeltaMovement.y = VerticalVelocity * Time.deltaTime;
            characterController.Move(playerDeltaMovement);//角色下坠
            averageVel = AverageVel(animator.velocity);
        }
        else
        {
            //沿用前几帧的平均速度
            averageVel.y = VerticalVelocity;
            Vector3 playerDeltaMovement = averageVel * Time.deltaTime;
            characterController.Move(playerDeltaMovement);//角色下坠
        }
        
    }
}
