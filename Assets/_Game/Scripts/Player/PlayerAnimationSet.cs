using System;
using UnityEngine;

namespace TanShadow.Player
{
    // Настройки анимации героини: варианты ударов, скорости клипов, разворот, взгляд, отдача блока и переходы.
    // Числа тюнятся здесь, без кода. Имена состояний должны совпадать с AC_Yinniang_Combat.
    [CreateAssetMenu(fileName = "PlayerAnimationSet", menuName = "Tan Shadow/Player Animation Set")]
    public class PlayerAnimationSet : ScriptableObject
    {
        [Serializable]
        public class AttackClip
        {
            [Tooltip("Состояние в аниматоре")]
            public string state;
            [Tooltip("С какой секунды клипа начинать (пропускаем стойку до замаха), с")]
            [Min(0)] public float start;
            [Tooltip("Момент удара в клипе (максимальная скорость руки), с. Совмещается с активной фазой AttackData")]
            [Min(0)] public float impact = 0.3f;
            [Tooltip("Насколько прижать свободную (левую) руку к телу, если в клипе она сильно выбрасывается вперёд и кажется, что бьёт она")]
            [Range(0, 1)] public float offHandRest;
        }

        [Serializable]
        public class AttackStep
        {
            [Tooltip("Варианты удара для этого шага комбо — выбирается случайный, без повтора подряд")]
            public AttackClip[] variants;
        }

        [Header("Комбо")]
        [Tooltip("По шагу на каждый удар из PlayerController.combo")]
        public AttackStep[] comboSteps;
        [Tooltip("Пределы ускорения/замедления клипа удара, чтобы не было «пластилина» или мельтешения")]
        public Vector2 attackSpeedRange = new Vector2(0.6f, 3.5f);

        [Header("Передвижение (скорости клипов, м/с, как в blend tree)")]
        [Tooltip("Бег вперёд, назад и вбок — по ним считаем, насколько ускорять цикл, если персонаж быстрее клипа")]
        public float runForwardSpeed = 4.23f;
        public float runBackwardSpeed = 3.42f;
        public float runSideSpeed = 4.4f;
        public float walkForwardSpeed = 1.6f;
        public float walkBackwardSpeed = 1.9f;
        public float walkSideSpeed = 1.66f;
        [Tooltip("Медленнее шага ноги не смешиваются со стойкой, а шаг замедляется — но не медленнее этой доли")]
        [Range(0.2f, 1f)] public float minWalkRate = 0.5f;
        [Tooltip("Сглаживание скорости при разгоне, с")]
        [Min(0)] public float velocityDamp = 0.1f;
        [Tooltip("Сглаживание скорости при торможении, с — чуть дольше, чтобы остановка не была «вкопанной»")]
        [Min(0)] public float velocityDampStop = 0.16f;
        [Tooltip("Насколько можно ускорить цикл шагов под скорость персонажа")]
        [Min(1)] public float maxPlaybackWarp = 1.35f;

        [Header("Поворот на месте в захвате")]
        [Tooltip("Скорость поворота клипа Turn, град/с")]
        public float turnClipRate = 109f;
        [Tooltip("Медленнее этого персонаж просто стоит, без переступаний, град/с")]
        public float turnDeadZone = 25f;
        [Tooltip("Ниже этой скорости персонаж считается стоящим, м/с")]
        public float standSpeed = 0.35f;

        [Header("Разворот (свободное движение)")]
        [Tooltip("С места или с шага: разворачиваться клипом, если стик направлен дальше этого угла от взгляда, °")]
        [Range(30, 180)] public float turnStartAngle = 65f;
        [Tooltip("На бегу: разворот клипом только при резкой смене направления, °")]
        [Range(90, 180)] public float runPivotAngle = 135f;
        [Tooltip("До какой скорости действует turnStartAngle, м/с")]
        public float turnStandMaxSpeed = 1.2f;
        [Tooltip("Длительность разворота на 90° и 180°, с (клип ускоряется под них)")]
        [Min(0.05f)] public float turn90Duration = 0.26f;
        [Min(0.05f)] public float turn180Duration = 0.36f;
        [Tooltip("Скорость шага во время разворота относительно обычной")]
        [Range(0, 1)] public float turnMoveScale = 0.35f;
        [Tooltip("Как корпус поворачивается по ходу клипа (снято с самого Turn90, чтобы стопы не скользили)")]
        public AnimationCurve turnProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public float turnBlendIn = 0.08f;
        public float turnBlendOut = 0.18f;

        [Header("Наклоны тела")]
        [Tooltip("Наклон в поворот: градусов на (град/с × м/с)")]
        public float leanPerTurn = 0.0045f;
        [Tooltip("Наклон вперёд при разгоне: градусов на м/с²")]
        public float leanPerAcceleration = 0.4f;
        [Range(0, 30)] public float maxLean = 10f;
        [Min(0.01f)] public float leanSmoothing = 0.15f;

        [Header("Взгляд")]
        [Tooltip("Насколько голова смотрит вперёд/на врага поверх анимации (в передвижении и блоке)")]
        [Range(0, 1)] public float lookWeight = 0.85f;
        [Tooltip("То же в ударах и дефлекте")]
        [Range(0, 1)] public float lookActionWeight = 0.5f;
        [Range(0, 90)] public float maxLookYaw = 70f;
        [Range(0, 60)] public float maxLookPitch = 35f;
        [Tooltip("Куда смотреть на враге, высота над его ногами, м")]
        public float lookTargetHeight = 1.5f;
        [Tooltip("Без врага взгляд чуть вниз от горизонта, °")]
        public float lookDownAngle = 6f;

        [Header("Отдача блока и дефлекта")]
        [Tooltip("На сколько градусов удар отбрасывает руку с кинжалом и корпус")]
        public float kickArm = 16f;
        public float kickChest = 5f;
        [Tooltip("Время до пика отдачи, с (дальше затухает)")]
        [Min(0.01f)] public float kickTime = 0.06f;
        [Range(0, 2)] public float deflectKick = 1f;
        [Range(0, 2)] public float blockKick = 0.6f;

        [Header("Стойка")]
        [Tooltip("Сколько вариантов стойки в blend tree Idle")]
        [Min(1)] public int idleVariants = 2;
        [Tooltip("Сколько секунд перетекает одна стойка в другую")]
        [Min(0.05f)] public float idleBlendTime = 0.8f;
        [Tooltip("Через сколько секунд (случайно в диапазоне) сменить стойку")]
        public Vector2 idleSwitchInterval = new Vector2(5f, 9f);

        [Header("Переходы (инерционные), с")]
        public float attackBlend = 0.1f;
        public float reactionBlend = 0.08f;
        public float dodgeBlend = 0.1f;
        public float finisherBlend = 0.15f;
        public float toLocomotionBlend = 0.28f;
        [Tooltip("Старт с места: ноги сразу переходят в шаг/бег через инерционный бленд (без скольжения на разгоне)")]
        public float startBlend = 0.16f;
        [Tooltip("Резкая остановка: поза бега гасится инерционно, ноги не «бегут на месте»")]
        public float stopBlend = 0.24f;
        [Tooltip("За сколько секунд прижимается/отпускается свободная рука (слой OffHand)")]
        [Min(0.01f)] public float offHandBlend = 0.12f;
    }
}
