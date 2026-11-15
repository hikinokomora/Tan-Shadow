using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Player
{
    // Цингун (轻功): прыжок, толчок от воздуха, бег вдоль стены и вверх по ней, прыжок от стены.
    // Числа — в CombatSettings («Цингун»).
    public partial class PlayerController
    {
        // Оттолкнулись: с земли, от воздуха, от стены или с края стены. Для анимации и звука.
        public event System.Action Jumped;
        public bool IsWallClimb => wallClimb;
        public Vector3 WallNormal => wallNormal;

        Vector3 airVelocity;
        int airJumpsLeft;
        bool climbUsed;
        Vector3 climbWallNormal;
        float lastGroundedTime;
        bool grounded; // земля после вертикального шага этого кадра (см. ApplyKnockbackAndGravity)

        bool wallClimb;
        Vector3 wallNormal;
        Vector3 wallRunDirection;
        Vector3 contactNormal;
        float contactTime = float.NegativeInfinity;
        Vector3 detachedNormal;
        float detachedTime = float.NegativeInfinity;

        float GravityScale => State != PlayerState.WallRun ? 1f : wallClimb ? 0f : settings.wallRunGravityScale;

        float JumpSpeed(float height) => Mathf.Sqrt(2f * Mathf.Abs(settings.gravity) * height);

        Vector3 Chest => transform.TransformPoint(body.center);

        // --- Земля ---

        void TrackGround()
        {
            if (grounded && verticalSpeed <= 0f)
            {
                lastGroundedTime = combatant.LocalTime;
                airJumpsLeft = settings.airJumps;
                climbUsed = false;
            }
            if (State != PlayerState.Dead && transform.position.y < settings.killHeight) Die();
        }

        bool TryJumpFromGround()
        {
            if (combatant.LocalTime - lastGroundedTime > settings.coyoteTime) return false;
            if (!buffer.Consume(BufferedAction.Jump, combatant.LocalTime, settings.inputBuffer)) return false;

            combatant.StopBlock();
            lastGroundedTime = float.NegativeInfinity; // второй раз с той же земли не прыгнуть
            verticalSpeed = JumpSpeed(settings.jumpHeight);
            airVelocity = MoveInput * settings.moveSpeed;
            airJumpsLeft = settings.airJumps;
            SetState(PlayerState.Air);
            Jumped?.Invoke();
            return true;
        }

        // Сошли с края — падаем (и можем ещё прыгнуть в воздухе).
        bool StartFallingIfUnsupported()
        {
            if (grounded || combatant.LocalTime - lastGroundedTime <= settings.coyoteTime) return false;
            airVelocity = MoveInput * settings.moveSpeed;
            SetState(PlayerState.Air);
            return true;
        }

        void ResetAirState()
        {
            airVelocity = Vector3.zero;
            airJumpsLeft = settings.airJumps;
            climbUsed = false;
            contactTime = float.NegativeInfinity;
        }

        // --- Воздух ---

        void UpdateAir(float dt)
        {
            if (TryJumpFromGround()) return;
            if (TryStealthKillFromAir()) return;
            if (airJumpsLeft > 0 && buffer.Consume(BufferedAction.Jump, combatant.LocalTime, settings.inputBuffer))
            {
                airJumpsLeft--;
                verticalSpeed = JumpSpeed(settings.airJumpHeight);
                if (MoveInput.sqrMagnitude > 0.01f) airVelocity = MoveInput * settings.moveSpeed;
                Jumped?.Invoke();
            }

            // Стиком рулим к нужной скорости; без стика инерция (после толчка от стены) гаснет медленнее.
            bool steering = MoveInput.sqrMagnitude > 0.01f;
            Vector3 wanted = steering ? MoveInput * settings.moveSpeed : Vector3.zero;
            float accel = settings.airAcceleration * (steering ? 1f : 0.25f);
            airVelocity = Vector3.MoveTowards(airVelocity, wanted, accel * dt);
            body.Move(airVelocity * dt);
            FaceDuringTraversal(airVelocity, dt);

            if (TryStartWallRun()) return;
            if (grounded && verticalSpeed <= 0f && stateTime > 0.05f) SetState(PlayerState.Locomotion);
        }

        // --- Стена ---

        // Невидимые границы арены (слой ArenaBounds) держат бойцов подальше от стен — по ним не бегаем.
        static int arenaBoundsLayer = -2;
        static int ArenaBoundsLayer => arenaBoundsLayer != -2 ? arenaBoundsLayer : (arenaBoundsLayer = LayerMask.NameToLayer("ArenaBounds"));
        static int WallMask => ArenaBoundsLayer >= 0 ? Physics.DefaultRaycastLayers & ~(1 << ArenaBoundsLayer) : Physics.DefaultRaycastLayers;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(hit.normal.y) > 0.3f) return; // пол или потолок
            if (hit.collider.gameObject.layer == ArenaBoundsLayer) return;
            if (hit.collider.GetComponentInParent<Combatant>() != null) return; // по врагам не бегаем
            contactNormal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            contactTime = combatant.LocalTime;
        }

        bool TryStartWallRun()
        {
            if (combatant.LocalTime - contactTime > 0.1f || MoveInput.sqrMagnitude < 0.09f) return false;
            Vector3 n = contactNormal;
            Vector3 input = MoveInput.normalized;
            if (Vector3.Dot(input, -n) < 0.2f) return false; // стик не в стену
            bool sameWall = Vector3.Dot(n, detachedNormal) > 0.8f;
            if (sameWall && combatant.LocalTime - detachedTime < settings.wallReattachDelay) return false;

            bool climb = Vector3.Angle(input, -n) <= settings.wallClimbAngle;
            Vector3 along = Vector3.ProjectOnPlane(input, n);
            along.y = 0f;
            // Вверх по каждой стене — один раз за прыжок; по другой стене (колодец) — можно снова.
            if (climb && climbUsed && Vector3.Dot(n, climbWallNormal) > 0.5f)
            {
                // Прямо в ту же стену — просто падаем вдоль неё.
                if (along.sqrMagnitude < 0.04f) return false;
                climb = false;
            }
            if (!climb && along.sqrMagnitude < 0.04f) return false;

            wallClimb = climb;
            wallNormal = n;
            wallRunDirection = along.normalized;
            airVelocity = Vector3.zero;
            airJumpsLeft = settings.airJumps; // касание стены возвращает толчок от воздуха
            if (climb)
            {
                climbUsed = true;
                climbWallNormal = n;
                verticalSpeed = settings.wallClimbSpeed;
            }
            else
            {
                verticalSpeed = Mathf.Max(verticalSpeed, settings.wallRunLift);
            }
            SetState(PlayerState.WallRun);
            return true;
        }

        void UpdateWallRun(float dt)
        {
            if (buffer.Consume(BufferedAction.Jump, combatant.LocalTime, settings.inputBuffer))
            {
                WallJump();
                return;
            }

            bool wallThere = Physics.Raycast(Chest, -wallNormal, out var hit, body.radius + 0.45f,
                                 WallMask, QueryTriggerInteraction.Ignore)
                             && Mathf.Abs(hit.normal.y) < 0.3f;
            if (!wallThere)
            {
                // Вверх — выбежали за край: подскок на крышу. Вдоль — стена кончилась: летим дальше.
                if (wallClimb) LedgeHop();
                else LeaveWall(wallRunDirection * settings.wallRunSpeed);
                return;
            }
            wallNormal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;

            float duration = wallClimb ? settings.wallClimbDuration : settings.wallRunDuration;
            bool pullingAway = Vector3.Dot(MoveInput, -wallNormal) < -0.3f;
            if (stateTime >= duration || pullingAway)
            {
                LeaveWall(wallClimb ? wallNormal * 1.5f : wallRunDirection * settings.wallRunSpeed * 0.7f);
                return;
            }

            Vector3 along = Vector3.zero;
            if (wallClimb) verticalSpeed = settings.wallClimbSpeed;
            else along = Vector3.ProjectOnPlane(wallRunDirection, wallNormal).normalized * settings.wallRunSpeed;
            body.Move((along - wallNormal * 2f) * dt); // прижимаемся к стене, чтобы не отлипнуть
            FaceDuringTraversal(wallClimb ? -wallNormal : along, dt);

            if (grounded && verticalSpeed <= 0f && stateTime > 0.1f) SetState(PlayerState.Locomotion);
        }

        void WallJump()
        {
            Detach();
            verticalSpeed = JumpSpeed(settings.wallJumpHeight);
            airVelocity = wallNormal * settings.wallJumpPush;
            if (!wallClimb) airVelocity += wallRunDirection * settings.wallRunSpeed * 0.5f;
            airJumpsLeft = settings.airJumps;
            transform.rotation = Quaternion.LookRotation(new Vector3(airVelocity.x, 0f, airVelocity.z));
            SetState(PlayerState.Air);
            Jumped?.Invoke();
        }

        void LedgeHop()
        {
            Detach();
            verticalSpeed = JumpSpeed(settings.ledgeHopHeight);
            airVelocity = -wallNormal * settings.ledgeHopForward;
            SetState(PlayerState.Air);
            Jumped?.Invoke();
        }

        void LeaveWall(Vector3 velocity)
        {
            Detach();
            airVelocity = velocity;
            SetState(PlayerState.Air);
        }

        void Detach()
        {
            detachedNormal = wallNormal;
            detachedTime = combatant.LocalTime;
            contactTime = float.NegativeInfinity;
        }

        // В захвате смотрим на цель, иначе — куда летим или бежим.
        void FaceDuringTraversal(Vector3 direction, float dt)
        {
            Vector3 face = direction;
            face.y = 0f;
            if (State != PlayerState.WallRun && TryGetLockDirection(out var toTarget)) face = toTarget;
            if (face.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), settings.turnSpeed * dt);
        }
    }
}
