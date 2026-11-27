using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TanShadow.Combat;
using TanShadow.Seasons;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Spring
{
    /// <summary>
    /// Animation-free Qinglong encounter. The blockout is generated from the scene's
    /// existing Upper third route markers, so replacing the visual dragon later does
    /// not change its patrol, detection, pearl, or defeat logic.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(YinYangCover))]
    public sealed class SpringDragonEncounter : MonoBehaviour
    {
        [Header("Патруль по маркерам Spring")]
        [Min(0)] public float patrolStart;
        [Min(0)] public float patrolEnd;
        [Range(.1f, .5f)] public float dragonLengthOfLocation = .416625f;
        [Range(.05f, .5f)] public float dragonWidthOfCanyon = .25f;
        [Min(0.1f)] public float patrolSpeed = 9.6f;
        [Min(0.5f)] public float minTimeBeforeRetreat = 20f;
        [Min(0.5f)] public float maxTimeBeforeRetreat = 26f;
        [Min(0.5f)] public float minRetreatDuration = 10f;
        [Min(0.5f)] public float maxRetreatDuration = 18f;

        [Header("Обнаружение")]
        [Min(0.1f)] public float noticeTimeNear = .35f;
        [Min(0.1f)] public float noticeTimeFar = 30f;
        [Min(0.1f)] public float noticeNearDistance = 3f;
        [Min(1f)] public float noticeFarDistance;
        [Min(0)] public float awarenessRecovery = 0.28f;
        [SerializeField] BoxCollider approachCeiling;

        [Header("Жемчужина и бой")]
        [Min(1)] public int requiredStrikes = 4;
        [Min(0.1f)] public float fallbackStrikeReach = 2.4f;
        [Min(0.1f)] public float pearlDiameter = 1.4f;
        [Min(0.1f)] public float pearlTossHeightInEggs = 1.5f;
        [Min(1.1f)] public float pearlPushSpeedMultiplier = 2.75f;

        [Header("Дракон")]
        [Min(0.1f)] public float dragonClearanceInEggs = 2.5f;
        [Min(0.1f)] public float dragonFallTilt = 78f;
        [Min(0.1f)] public float dragonFallDuration = 2.3f;

        const string RoutePrefix = "Upper third · ";
        const float AscentTime = 1.4f;
        const float DescentTime = 1.8f;
        struct RoutePoint
        {
            public float station;
            public Vector3 position;
            public RoutePoint(float station, Vector3 position) { this.station = station; this.position = position; }
        }

        readonly List<RoutePoint> route = new List<RoutePoint>();
        readonly List<GameObject> cracks = new List<GameObject>();
        readonly List<Transform> bodySegments = new List<Transform>();
        readonly List<float> bodySegmentOffsets = new List<float>();
        Transform player;
        SpringWalkthrough walkthrough;
        PlayerController playerController;
        Camera viewCamera;
        Transform dragon;
        readonly Transform[] forelegs = new Transform[2];
        readonly Transform[] paws = new Transform[2];
        YinYangCover cover;
        Transform pearl;
        SphereCollider pearlCollider;
        Collider canyonBed;
        Rigidbody pearlBody;
        Material blockoutMaterial, pearlMaterial, crackMaterial;
        AudioSource dangerAudio;
        AudioClip dangerNoise;
        Texture2D guiPixel;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        Vector3 pearlStartPosition;
        Vector3 pawTarget;
        Vector3 lastCameraOffset;
        Quaternion lastCameraRotation = Quaternion.identity;
        float dragonStation, stationDirection = 1f, cycleTimer, awareness;
        float retreatTime, flightHeight;
        float dragonLength, dragonWidth;
        float tallestEggHeight;
        float pearlStartLimit, pearlEndLimit;
        float pearlDirection = 1f;
        float pawReach;
        float nextPearlPushTime;
        bool retractingPaws;
        int strikes, patrolBodyCount;
        bool dragonPresent, defeated, playerDead, hitThisRetreat;
        bool walkthroughWasEnabled, playerControllerWasEnabled;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;

        public int Strikes => strikes;
        public float Awareness => awareness;
        public bool DragonPresent => dragonPresent;
        public bool IsDefeated => defeated;

        void Awake()
        {
            cover = GetComponent<YinYangCover>();
            if (approachCeiling == null)
                approachCeiling = transform.Find("01 · Concrete approach/Ceiling")?.GetComponent<BoxCollider>();
            CollectRoute();
            ResolvePlayer();
            if (player != null)
            {
                spawnPosition = player.position;
                spawnRotation = player.rotation;
            }
            if (route.Count < 2)
            {
                Debug.LogError("Spring dragon encounter needs at least two 'Upper third · N m' route markers.", this);
                enabled = false;
                return;
            }

            float mapStart = route[0].station;
            float mapEnd = route[route.Count - 1].station;
            float mapLength = mapEnd - mapStart;
            dragonLength = mapLength * Mathf.Clamp(dragonLengthOfLocation, .1f, .5f);
            maxTimeBeforeRetreat = Mathf.Max(minTimeBeforeRetreat, maxTimeBeforeRetreat);
            maxRetreatDuration = Mathf.Max(minRetreatDuration, maxRetreatDuration);
            float portalStation = FindPortalStation(mapStart);
            patrolStart = patrolStart > portalStation ? Mathf.Clamp(patrolStart, portalStation, mapEnd - 1f) : portalStation;
            patrolEnd = patrolEnd > patrolStart ? Mathf.Clamp(patrolEnd, patrolStart + 1f, mapEnd) : mapEnd;
            noticeFarDistance = dragonLength;
            dragonStation = patrolStart;
            tallestEggHeight = MeasureTallestEgg();
            BuildBlockout();
            BuildFeedback();
            ResetEncounter(false);
        }

        void OnEnable() => AttackExecutor.AnyPhaseChanged += OnAttackPhaseChanged;

        void OnDisable()
        {
            AttackExecutor.AnyPhaseChanged -= OnAttackPhaseChanged;
            RestoreCamera();
        }

        void OnDestroy()
        {
            if (blockoutMaterial != null) Destroy(blockoutMaterial);
            if (pearlMaterial != null) Destroy(pearlMaterial);
            if (crackMaterial != null) Destroy(crackMaterial);
            if (dangerNoise != null) Destroy(dangerNoise);
            if (guiPixel != null) Destroy(guiPixel);
        }

        void Update()
        {
            if (playerDead || defeated || cover.IsCovering) return;

            ResolvePlayer();
            if (player == null) return;

            float dt = Time.deltaTime;
            TickPatrol(dt);
            if (!dragonPresent || IsUnderApproachCeiling())
                RecoverAwareness(dt);
            else
            {
                Vector3 threatPoint = ClosestDragonPoint(PlayerEyePoint(), out float distance, out float frontness);
                if (IsPlayerOccluded(threatPoint)) RecoverAwareness(dt);
                else
                {
                    awareness = Mathf.Min(1f, awareness + dt / NoticeTime(distance, frontness));
                    if (awareness >= 1f)
                    {
                        BeginDeath();
                        return;
                    }
                }
            }

            // The built-in Spring walkthrough has no combat component yet. F and a click
            // provide a greybox attack; real PlayerController attacks use AttackExecutor below.
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if ((keyboard != null && keyboard.fKey.wasPressedThisFrame) ||
                (mouse != null && mouse.leftButton.wasPressedThisFrame))
                TryFallbackStrike();
        }

        void RecoverAwareness(float dt) => awareness = Mathf.MoveTowards(awareness, 0f, awarenessRecovery * dt);

        bool IsUnderApproachCeiling()
        {
            if (approachCeiling == null) return false;
            Vector3 local = approachCeiling.transform.InverseTransformPoint(player.position) - approachCeiling.center;
            Vector3 halfSize = approachCeiling.size * .5f;
            return Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.z) <= halfSize.z && local.y <= -halfSize.y;
        }

        void LateUpdate()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            // Undo our last offset before applying the new one; this composes with the follow camera.
            viewCamera.transform.localPosition -= lastCameraOffset;
            lastCameraOffset = Vector3.zero;
            viewCamera.transform.localRotation *= Quaternion.Inverse(lastCameraRotation);
            lastCameraRotation = Quaternion.identity;
            float intensity = awareness * 0.045f;
            if (intensity <= 0.0001f) return;
            float t = Time.time;
            lastCameraOffset = new Vector3(
                (Mathf.PerlinNoise(t * 17f, 2.1f) - .5f) * intensity,
                (Mathf.PerlinNoise(6.4f, t * 19f) - .5f) * intensity,
                0f);
            viewCamera.transform.localPosition += lastCameraOffset;
            lastCameraRotation = Quaternion.Euler(
                (Mathf.PerlinNoise(t * 15f, 8.2f) - .5f) * intensity * 28f,
                (Mathf.PerlinNoise(9.2f, t * 13f) - .5f) * intensity * 24f,
                (Mathf.PerlinNoise(t * 11f, t * 7f) - .5f) * intensity * 10f);
            viewCamera.transform.localRotation *= lastCameraRotation;
        }

        void CollectRoute()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                string label = transforms[i].name;
                if (label == "Continuous canyon bed") canyonBed = transforms[i].GetComponent<Collider>();
                if (!label.StartsWith(RoutePrefix, StringComparison.Ordinal)) continue;
                string raw = label.Substring(RoutePrefix.Length).Replace("m", "").Trim();
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float station))
                    route.Add(new RoutePoint(station, transforms[i].position));
            }
            route.Sort((a, b) => a.station.CompareTo(b.station));
        }

        float FindPortalStation(float fallback)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            Vector3 portalCenter = Vector3.zero;
            int count = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!string.Equals(transforms[i].name, "Portal cheek", StringComparison.OrdinalIgnoreCase)) continue;
                portalCenter += transforms[i].position;
                count++;
            }
            if (count == 0 || route.Count < 2) return fallback;
            portalCenter /= count;
            Vector3 tangent = route[1].position - route[0].position;
            tangent.y = 0f;
            Vector3 toPortal = portalCenter - route[0].position;
            toPortal.y = 0f;
            return route[0].station + Vector3.Dot(toPortal, tangent.normalized);
        }

        void ResolvePlayer()
        {
            if (player != null) return;
            walkthrough = GetComponentInChildren<SpringWalkthrough>(true);
            playerController = GetComponentInChildren<PlayerController>(true);
            if (walkthrough != null) player = walkthrough.transform;
            else if (playerController != null) player = playerController.transform;
            else
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }
            if (player != null && viewCamera == null) viewCamera = Camera.main;
        }

        void BuildBlockout()
        {
            blockoutMaterial = MakeMaterial("Dragon greybox", new Color(.38f, .42f, .44f));
            pearlMaterial = MakeMaterial("Pearl greybox", new Color(.72f, .76f, .77f));
            crackMaterial = MakeMaterial("Pearl cracks", new Color(.16f, .18f, .19f));

            dragonWidth = MeasureCanyonWidth() * dragonWidthOfCanyon;
            // Leave enough floor for the dragon to get behind the pearl at either end.
            float edgeRoom = Mathf.Min(Mathf.Max(6f, dragonWidth * 1.5f), (patrolEnd - patrolStart) * .2f);
            pearlStartLimit = Mathf.Max(patrolStart, route[0].station) + edgeRoom;
            pearlEndLimit = patrolEnd - edgeRoom;
            patrolBodyCount = Mathf.Clamp(Mathf.CeilToInt(dragonLength / 4f), 24, 80);
            float segmentLength = dragonLength / patrolBodyCount;
            var dragonObject = new GameObject("Qinglong · Dragon Greybox");
            dragonObject.transform.SetParent(transform, false);
            dragon = dragonObject.transform;
            // A chain of blockout sections follows the bends of the canyon.
            float bodyHeight = dragonWidth * .42f;
            for (int i = 0; i < paws.Length; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                forelegs[i] = MakeBox(dragon, side + " foreleg", Vector3.zero, Vector3.one);
                paws[i] = MakeBox(dragon, side + " paw", Vector3.zero,
                    new Vector3(dragonWidth * .23f, bodyHeight * .32f, dragonWidth * .34f));
            }

            for (int i = 0; i < patrolBodyCount; i++)
            {
                float offset = dragonLength - (i + .5f) * segmentLength;
                float taper = Mathf.Lerp(.2f, 1f, 1f - offset / dragonLength);
                var segment = MakeBox(dragon, "Curved body · " + i.ToString("00"), Vector3.zero,
                    new Vector3(dragonWidth * taper, bodyHeight * taper, segmentLength * 1.12f));
                bodySegments.Add(segment);
                bodySegmentOffsets.Add(offset);
                if (i % 4 == 2)
                    MakeBox(segment, "Dorsal blockout", new Vector3(0f, bodyHeight * .62f, 0f),
                        new Vector3(.2f, .55f, .26f));
            }

            var pearlObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pearlObject.name = "Qinglong · Pearl Greybox";
            pearlObject.transform.SetParent(transform, false);
            pearl = pearlObject.transform;
            pearl.localScale = Vector3.one * pearlDiameter;
            pearlObject.GetComponent<Renderer>().sharedMaterial = pearlMaterial;
            pearlCollider = pearlObject.GetComponent<SphereCollider>();
            pearlCollider.isTrigger = false;
            pearlBody = pearlObject.AddComponent<Rigidbody>();
            pearlBody.mass = 1f;
            pearlBody.linearDamping = .12f;
            pearlBody.angularDamping = .08f;
            pearlBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * .25f;
                var crack = MakeBox(pearl, "Crack " + i, new Vector3(Mathf.Sin(a) * .36f, Mathf.Cos(a) * .31f, .49f), new Vector3(.035f, .42f, .035f));
                crack.localRotation = Quaternion.Euler(0f, 0f, -i * 45f);
                crack.GetComponent<Renderer>().sharedMaterial = crackMaterial;
                crack.gameObject.SetActive(false);
                cracks.Add(crack.gameObject);
            }
            PlaceDragon();
            PlacePearl();
            pearlStartPosition = pearl.position;
        }

        float MeasureCanyonWidth()
        {
            float start = route[0].station;
            float length = route[route.Count - 1].station - start;
            float total = 0f;
            int samples = 0;
            for (int i = 0; i < 16; i++)
            {
                float station = start + length * (i + .5f) / 16f;
                float width = CanyonWidthAt(station);
                if (width <= .1f) continue;
                total += width;
                samples++;
            }
            return samples > 0 ? total / samples : 16f;
        }

        float MeasureTallestEgg()
        {
            float height = 0f;
            var children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i].name.StartsWith("Egg boulder", StringComparison.OrdinalIgnoreCase)) continue;
                var renderer = children[i].GetComponent<Renderer>();
                if (renderer != null) height = Mathf.Max(height, renderer.bounds.size.y);
            }
            return height > .1f ? height : pearlDiameter * 2f;
        }

        float CanyonWidthAt(float station)
        {
            Vector3 point = PositionAt(station);
            Vector3 tangent = PositionAt(station + .5f) - PositionAt(station - .5f);
            tangent.y = 0f;
            if (tangent.sqrMagnitude < .001f) return 0f;
            Vector3 across = Vector3.Cross(Vector3.up, tangent.normalized);
            float floor = GroundHeight(point);
            Vector3 origin = new Vector3(point.x, floor + Mathf.Max(1f, dragonWidth * .1f), point.z);
            float left = DistanceToCliff(origin, across);
            float right = DistanceToCliff(origin, -across);
            return left > 0f && right > 0f ? left + right : 0f;
        }

        static float DistanceToCliff(Vector3 origin, Vector3 direction)
        {
            var hits = Physics.RaycastAll(origin, direction, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                string name = hits[i].collider.name;
                if ((name.IndexOf("rock face", StringComparison.OrdinalIgnoreCase) >= 0) && hits[i].distance < closest)
                    closest = hits[i].distance;
            }
            return float.IsPositiveInfinity(closest) ? 0f : closest;
        }

        bool TryCanyonBedHeight(Vector3 point, out float height)
        {
            height = transform.position.y;
            if (canyonBed == null) return false;
            Vector3 origin = new Vector3(point.x, canyonBed.bounds.max.y + 2f, point.z);
            if (!canyonBed.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, canyonBed.bounds.size.y + 4f))
                return false;
            height = hit.point.y;
            return true;
        }

        float GroundHeight(Vector3 point)
        {
            TryCanyonBedHeight(point, out float height);
            return height;
        }

        void BuildFeedback()
        {
            var host = viewCamera != null ? viewCamera.gameObject : gameObject;
            dangerAudio = host.GetComponent<AudioSource>();
            if (dangerAudio == null) dangerAudio = host.AddComponent<AudioSource>();
            dangerAudio.playOnAwake = false;
            dangerAudio.loop = true;
            dangerAudio.spatialBlend = 0f;
            dangerAudio.volume = 0f;
            dangerNoise = MakeNoiseClip();
            dangerAudio.clip = dangerNoise;
            dangerAudio.Play();
            guiPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            guiPixel.SetPixel(0, 0, Color.white);
            guiPixel.Apply();
        }

        void TickPatrol(float dt)
        {
            cycleTimer = Mathf.Max(0f, cycleTimer - dt);
            if (dragonPresent)
            {
                if (cycleTimer > 0f)
                {
                    ChasePearl(dt);
                    return;
                }
                dragonPresent = false;
                hitThisRetreat = false;
                retreatTime = UnityEngine.Random.Range(minRetreatDuration, maxRetreatDuration);
                cycleTimer = retreatTime;
            }

            // The retreat countdown includes both ascent and descent.
            float elapsed = retreatTime - cycleTimer;
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / AscentTime));
            float descend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cycleTimer / DescentTime));
            flightHeight = Mathf.Max(dragonLength * .45f, tallestEggHeight * 20f) * Mathf.Min(rise, descend);
            pawReach = Mathf.MoveTowards(pawReach, 0f, dt / .42f);
            retractingPaws = false;

            if (elapsed >= AscentTime)
            {
                float pearlStation = RouteStation(pearlBody.position);
                UpdatePearlDirection(pearlStation);
                MoveAlongRoute(ApproachStation(pearlStation), patrolSpeed * dt);
            }
            PlaceDragon();

            if (cycleTimer <= 0f)
            {
                dragonPresent = true;
                cycleTimer = UnityEngine.Random.Range(minTimeBeforeRetreat, maxTimeBeforeRetreat);
            }
        }

        float ApproachStation(float pearlStation)
        {
            float distance = Mathf.Min(dragonWidth * .85f, Mathf.Max(.1f, (pearlEndLimit - pearlStartLimit) * .2f));
            return Mathf.Clamp(pearlStation - pearlDirection * distance, patrolStart, patrolEnd);
        }

        float MoveAlongRoute(float target, float step)
        {
            float travel = target - dragonStation;
            dragonStation = Mathf.MoveTowards(dragonStation, target, step);
            stationDirection = Mathf.Abs(travel) <= step + .5f ? pearlDirection : Mathf.Sign(travel);
            return Mathf.Abs(target - dragonStation);
        }

        void UpdatePearlDirection(float station)
        {
            if (station <= pearlStartLimit + .15f) pearlDirection = 1f;
            else if (station >= pearlEndLimit - .15f) pearlDirection = -1f;
        }

        void ChasePearl(float dt)
        {
            float pearlStation = RouteStation(pearlBody.position);
            UpdatePearlDirection(pearlStation);
            float distance = MoveAlongRoute(ApproachStation(pearlStation), patrolSpeed * dt);

            Vector3 velocity = pearlBody.linearVelocity;
            bool canPush = distance <= 1f && stationDirection == pearlDirection &&
                velocity.sqrMagnitude < patrolSpeed * patrolSpeed && Mathf.Abs(velocity.y) < 2f &&
                Time.time >= nextPearlPushTime;

            if (retractingPaws || !canPush)
            {
                pawReach = Mathf.MoveTowards(pawReach, 0f, dt / .42f);
                if (pawReach <= 0f) retractingPaws = false;
            }
            else pawReach = Mathf.MoveTowards(pawReach, 1f, dt / .28f);

            PlaceDragon(canPush && !retractingPaws);
            if (pawReach >= 1f && canPush && !retractingPaws)
            {
                PushPearl(pearlStation);
                retractingPaws = true;
            }
        }

        void PlaceDragon(bool aimPawsAtPearl = false)
        {
            Vector3 routePosition = PositionAt(dragonStation);
            float clearance = tallestEggHeight * dragonClearanceInEggs + flightHeight;
            routePosition.y = GroundHeight(routePosition) + clearance + dragonWidth * .21f;
            dragon.position = routePosition;
            Vector3 tangent = RouteForward(dragonStation) * stationDirection;
            if (tangent.sqrMagnitude > .001f) dragon.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);

            for (int i = 0; i < bodySegments.Count; i++)
            {
                float station = dragonStation - stationDirection * bodySegmentOffsets[i];
                Transform segment = bodySegments[i];
                Vector3 position = PositionAt(station);
                position.y = GroundHeight(position) + clearance + segment.localScale.y * .5f;
                segment.position = position;
                Vector3 segmentTangent = RouteForward(station) * stationDirection;
                if (segmentTangent.sqrMagnitude > .001f)
                    segment.rotation = Quaternion.LookRotation(segmentTangent.normalized, Vector3.up);
            }

            if (aimPawsAtPearl) pawTarget = dragon.InverseTransformPoint(pearlBody.position);
            float reach = Mathf.SmoothStep(0f, 1f, pawReach);
            float pearlRadius = pearl.localScale.y * .5f;
            for (int i = 0; i < paws.Length; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 shoulder = new Vector3(side * .38f, 0f, -.65f) * dragonWidth;
                Vector3 folded = shoulder + new Vector3(side * .18f, -.24f, .15f) * dragonWidth;
                Vector3 contact = pawTarget + new Vector3(side * pearlRadius * .6f, 0f, -pearlRadius - paws[i].localScale.z * .4f);
                Vector3 wrist = Vector3.Lerp(folded, contact, reach);
                Vector3 leg = wrist - shoulder;
                paws[i].localPosition = wrist;
                forelegs[i].localPosition = shoulder + leg * .5f;
                forelegs[i].localRotation = Quaternion.FromToRotation(Vector3.forward, leg);
                forelegs[i].localScale = new Vector3(dragonWidth * .16f, dragonWidth * .14f, leg.magnitude);
            }
        }

        void PlacePearl()
        {
            if (pearl == null || !pearl.gameObject.activeSelf) return;
            float station = Mathf.Lerp(patrolStart, patrolEnd, .5f);
            Vector3 point = PositionAt(station);
            point.y = GroundHeight(point) + pearl.localScale.y * .5f;
            pearl.position = point;
            if (pearlBody != null) pearlBody.position = point;
        }

        void PushPearl(float pearlStation)
        {
            Vector3 forward = RouteForward(pearlStation) * stationDirection;
            float upwardSpeed = Mathf.Sqrt(2f * Mathf.Max(0f, -Physics.gravity.y) * tallestEggHeight * pearlTossHeightInEggs);
            Vector3 wantedVelocity = forward * (patrolSpeed * pearlPushSpeedMultiplier) + Vector3.up * upwardSpeed;
            pearlBody.AddForce(wantedVelocity - pearlBody.linearVelocity, ForceMode.VelocityChange);
            nextPearlPushTime = Time.time + Mathf.Max(.8f, 2f * upwardSpeed / Mathf.Max(.1f, -Physics.gravity.y));
        }

        void FixedUpdate()
        {
            if (pearlBody == null || defeated || playerDead) return;
            Vector3 position = pearlBody.position;
            float station = RouteStation(position);
            float limitedStation = Mathf.Clamp(station, pearlStartLimit, pearlEndLimit);
            Vector3 velocity = pearlBody.linearVelocity;
            if (limitedStation != station)
            {
                Vector3 inward = RouteForward(limitedStation) * Mathf.Sign(limitedStation - station);
                Vector3 corrected = position + PositionAt(limitedStation) - PositionAt(station);
                corrected.y = position.y;
                if (!TryCanyonBedHeight(corrected, out _))
                {
                    Vector3 center = PositionAt(limitedStation);
                    corrected = new Vector3(center.x, position.y, center.z);
                }
                pearlBody.position = corrected;
                position = corrected;
                float outwardSpeed = Vector3.Dot(velocity, inward);
                if (outwardSpeed < 0f) velocity -= inward * outwardSpeed;
            }

            if (TryCanyonBedHeight(position, out float floor) && position.y <= floor + pearlCollider.bounds.extents.y + .15f)
            {
                Vector3 rolling = new Vector3(velocity.x, 0f, velocity.z);
                velocity -= Vector3.ClampMagnitude(rolling, patrolSpeed * .8f * Time.fixedDeltaTime);
            }
            if ((velocity - pearlBody.linearVelocity).sqrMagnitude > .000001f) pearlBody.linearVelocity = velocity;
        }

        Vector3 RouteForward(float station)
        {
            Vector3 tangent = PositionAt(station + .5f) - PositionAt(station - .5f);
            tangent.y = 0f;
            return tangent.normalized;
        }

        float RouteStation(Vector3 position)
        {
            position.y = 0f;
            float bestDistance = float.PositiveInfinity;
            float bestStation = route[0].station;
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 start = route[i - 1].position;
                Vector3 end = route[i].position;
                start.y = end.y = 0f;
                Vector3 segment = end - start;
                if (segment.sqrMagnitude < .001f) continue;
                float fraction = Vector3.Dot(position - start, segment) / segment.sqrMagnitude;
                if (i > 1) fraction = Mathf.Max(0f, fraction);
                if (i < route.Count - 1) fraction = Mathf.Min(1f, fraction);
                float distance = (position - start - segment * fraction).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestStation = Mathf.LerpUnclamped(route[i - 1].station, route[i].station, fraction);
            }
            return bestStation;
        }

        Vector3 PositionAt(float station)
        {
            if (route.Count == 0) return transform.position;
            if (station < route[0].station)
            {
                Vector3 tangent = (route[1].position - route[0].position).normalized;
                return route[0].position + tangent * (station - route[0].station);
            }
            for (int i = 1; i < route.Count; i++)
            {
                if (station > route[i].station) continue;
                float t = Mathf.InverseLerp(route[i - 1].station, route[i].station, station);
                Vector3 a = route[Mathf.Max(0, i - 2)].position;
                Vector3 b = route[i - 1].position;
                Vector3 c = route[i].position;
                Vector3 d = route[Mathf.Min(route.Count - 1, i + 1)].position;
                return .5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (-a + 3f * b - 3f * c + d) * t * t * t);
            }
            Vector3 endTangent = (route[route.Count - 1].position - route[route.Count - 2].position).normalized;
            return route[route.Count - 1].position + endTangent * (station - route[route.Count - 1].station);
        }

        float NoticeTime(float distance, float frontness)
        {
            float sectionTime = Mathf.Lerp(noticeTimeFar, noticeTimeNear, Mathf.Clamp01(frontness));
            float distanceFactor = Mathf.InverseLerp(noticeNearDistance, noticeFarDistance, distance);
            return Mathf.Lerp(sectionTime, noticeTimeFar, distanceFactor) * 2f / 1.25f;
        }

        Vector3 ClosestDragonPoint(Vector3 from, out float distance, out float frontness)
        {
            Vector3 closest = dragon.position;
            distance = FlatDistance(from, closest);
            frontness = 1f;
            for (int i = 0; i < bodySegments.Count; i++)
            {
                Vector3 point = bodySegments[i].position;
                float candidate = FlatDistance(from, point);
                if (candidate >= distance) continue;
                distance = candidate;
                closest = point;
                frontness = 1f - Mathf.Clamp01(bodySegmentOffsets[i] / dragonLength);
            }
            return closest;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        Vector3 PlayerEyePoint() => player != null ? player.position + Vector3.up * 1.5f : Vector3.zero;

        bool IsPlayerOccluded(Vector3 threatPoint)
        {
            if (player == null || dragon == null) return false;
            Vector3 eye = PlayerEyePoint();
            Vector3 target = threatPoint;
            // Treat the threat line along the ground plane so nearby boulders and
            // shelves can shelter the player from a dragon flying well above them.
            target.y = eye.y;
            Vector3 delta = target - eye;
            float distance = delta.magnitude;
            if (distance <= .05f) return false;
            var hits = Physics.RaycastAll(eye, delta / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].transform;
                if (hitTransform == player || hitTransform.IsChildOf(player)) continue;
                if (hitTransform == dragon || hitTransform.IsChildOf(dragon)) continue;
                if (hitTransform == pearl) continue;
                return true; // The canyon walls, shelves, and egg boulders all block sight.
            }
            return false;
        }

        void OnAttackPhaseChanged(AttackExecutor executor, AttackPhase phase)
        {
            if (phase != AttackPhase.Active || pearlCollider == null || !pearl.gameObject.activeInHierarchy) return;
            ResolvePlayer();
            if (player == null || executor.transform != player) return;
            float reach = executor.Current != null && executor.Current.range > 0f
                ? executor.Current.range
                : executor.reach + executor.radius;
            Vector3 center = executor.transform.position + Vector3.up * executor.height + executor.transform.forward * Mathf.Max(0f, reach - executor.radius);
            float hitDistance = executor.radius + pearlDiameter * .5f;
            if ((pearl.position - center).sqrMagnitude > hitDistance * hitDistance) return;
            AttemptStrike();
        }

        void TryFallbackStrike()
        {
            if (pearl == null || !pearl.gameObject.activeInHierarchy || player == null) return;
            Vector3 origin = PlayerEyePoint();
            Vector3 toPearl = pearl.position - origin;
            if (toPearl.magnitude > fallbackStrikeReach || Vector3.Dot(player.forward, toPearl.normalized) < .05f) return;
            AttemptStrike();
        }

        void AttemptStrike()
        {
            // A retreat is the readable attack window. Striking the pearl never changes
            // Qinglong's route or awareness; at most one strike counts in each window.
            if (dragonPresent || hitThisRetreat || playerDead || defeated || cover.IsCovering) return;
            hitThisRetreat = true;

            strikes = Mathf.Min(requiredStrikes, strikes + 1);
            int crackCount = Mathf.RoundToInt((float)strikes / requiredStrikes * cracks.Count);
            for (int i = 0; i < cracks.Count; i++) cracks[i].SetActive(i < crackCount);
            float remaining = 1f - (float)strikes / requiredStrikes;
            pearl.localScale = Vector3.one * pearlDiameter * Mathf.Lerp(.34f, 1f, remaining);
            pearlMaterial.color = Color.Lerp(new Color(.24f, .27f, .28f), new Color(.72f, .76f, .77f), remaining);

            if (strikes < requiredStrikes) return;
            defeated = true;
            SeasonProgress.MarkDefeated(Season.Spring);
            awareness = 0f;
            PausePlayerInput();
            cover.Run(YinYangCover.Kind.Yang, VictoryUnderCover, ResumePlayerInput);
        }

        void BeginDeath()
        {
            if (playerDead || defeated) return;
            playerDead = true;
            awareness = 0f;
            PausePlayerInput();
            cover.Run(YinYangCover.Kind.Yin, RestartUnderCover, () =>
            {
                playerDead = false;
                ResumePlayerInput();
            });
        }

        void PausePlayerInput()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            walkthroughWasEnabled = walkthrough != null && walkthrough.enabled;
            playerControllerWasEnabled = playerController != null && playerController.enabled;
            if (walkthrough != null) walkthrough.enabled = false;
            if (playerController != null) playerController.enabled = false;
        }

        void ResumePlayerInput()
        {
            if (walkthrough != null) walkthrough.enabled = walkthroughWasEnabled;
            if (playerController != null) playerController.enabled = playerControllerWasEnabled;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        IEnumerator RestartUnderCover()
        {
            if (player != null)
            {
                var controller = player.GetComponent<CharacterController>();
                bool wasEnabled = controller != null && controller.enabled;
                if (controller != null) controller.enabled = false;
                player.SetPositionAndRotation(spawnPosition, spawnRotation);
                if (controller != null) controller.enabled = wasEnabled;
            }
            ResetEncounter(true);
            yield return null;
        }

        void ResetEncounter(bool resetPearl)
        {
            awareness = 0f;
            stationDirection = 1f;
            pearlDirection = 1f;
            pawReach = 0f;
            retractingPaws = false;
            flightHeight = 0f;
            nextPearlPushTime = 0f;
            dragonPresent = true;
            hitThisRetreat = false;
            cycleTimer = UnityEngine.Random.Range(minTimeBeforeRetreat, maxTimeBeforeRetreat);
            if (resetPearl)
            {
                strikes = 0;
                pearl.gameObject.SetActive(true);
                pearl.localScale = Vector3.one * pearlDiameter;
                pearlMaterial.color = new Color(.72f, .76f, .77f);
                for (int i = 0; i < cracks.Count; i++) cracks[i].SetActive(false);
                pearl.position = pearlStartPosition;
                if (pearlBody != null)
                {
                    pearlBody.position = pearlStartPosition;
                    pearlBody.linearVelocity = Vector3.zero;
                    pearlBody.angularVelocity = Vector3.zero;
                }
            }
            else PlacePearl();
            float pearlStation = RouteStation(pearlBody.position);
            dragonStation = Mathf.Clamp(pearlStation - Mathf.Max(12f, dragonWidth * 3f), patrolStart, patrolEnd);
            PlaceDragon();
        }

        IEnumerator VictoryUnderCover()
        {
            pearl.gameObject.SetActive(false);
            Vector3 start = dragon.position;
            Vector3 end = start;
            end.y = GroundHeight(start) + dragonWidth * .21f;
            float duration = Mathf.Min(dragonFallDuration, cover.yangMinHold);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = elapsed / duration;
                dragon.position = Vector3.Lerp(start, end, progress * progress);
                yield return null;
            }
            dragon.position = end;
            float roll = dragonFallTilt * Mathf.Deg2Rad;
            foreach (Transform segment in bodySegments)
            {
                segment.Rotate(0f, 0f, dragonFallTilt, Space.Self);
                Vector3 point = segment.position;
                Vector3 scale = segment.localScale;
                point.y = GroundHeight(point) + (Mathf.Abs(Mathf.Cos(roll)) * scale.y + Mathf.Abs(Mathf.Sin(roll)) * scale.x) * .5f;
                segment.position = point;
            }
        }

        void SetDangerAudio()
        {
            if (dangerAudio != null) dangerAudio.volume = awareness * .16f;
        }

        void OnGUI()
        {
            SetDangerAudio();
            if (guiPixel == null || cover.IsCovering) return;
            GUI.color = Color.white;
            GUI.Label(new Rect(18f, 16f, 260f, 26f), "Жемчужина: " + strikes + " / " + requiredStrikes);
            if (!defeated && !playerDead)
            {
                int seconds = Mathf.CeilToInt(Mathf.Max(0f, cycleTimer));
                string timer = dragonPresent ? "Отлёт через " + seconds + " с" : "Возврат через " + seconds + " с";
                GUI.Label(new Rect(18f, 40f, 430f, 24f), timer + " · F / ЛКМ — удар");
                if (awareness > .02f)
                {
                    GUI.color = new Color(1f, .25f, .2f, .82f);
                    GUI.DrawTexture(new Rect(18f, 68f, 190f * awareness, 5f), guiPixel);
                }
            }

            if (defeated)
                GUI.Label(new Rect(Screen.width * .4f, 24f, 320f, 30f), "Цинлун повержен");
            GUI.color = Color.white;
        }

        void RestoreCamera()
        {
            if (viewCamera == null) return;
            viewCamera.transform.localPosition -= lastCameraOffset;
            lastCameraOffset = Vector3.zero;
            viewCamera.transform.localRotation *= Quaternion.Inverse(lastCameraRotation);
            lastCameraRotation = Quaternion.identity;
        }

        Transform MakeBox(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;
            box.GetComponent<Renderer>().sharedMaterial = blockoutMaterial;
            var collider = box.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return box.transform;
        }

        static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            return material;
        }

        static AudioClip MakeNoiseClip()
        {
            const int sampleRate = 11025;
            var samples = new float[sampleRate];
            var random = new System.Random(38471);
            for (int i = 0; i < samples.Length; i++) samples[i] = ((float)random.NextDouble() * 2f - 1f) * .12f;
            var clip = AudioClip.Create("Qinglong · danger hiss", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
