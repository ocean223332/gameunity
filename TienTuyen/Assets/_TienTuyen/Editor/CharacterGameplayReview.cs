#if UNITY_EDITOR
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using TienTuyen.Combat;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>Repeatable live-game animation review; never runs in a player build.</summary>
    public static class CharacterGameplayReview
    {
        private static CombatGame game;
        private static Vector2 movement;
        private static bool fire;
        private static float elapsed;
        private static int lastFrame;
        private static bool previousEnabled;
        private static int warmupFrames;
        public static int FramesObserved { get; private set; }
        public static float MeanFrameMs { get; private set; }
        public static float MaximumFrameMs { get; private set; }
        public static string FinalReviewStatus { get; private set; } = "not started";

        /// <summary>Every skinned profile but the hero's is an enemy's.</summary>
        private static bool IsEnemy(SkinnedCharacterProfile profile) => !profile.name.StartsWith("PAVN", StringComparison.Ordinal);

        public static void ReviewFinal(string directory)
        {
            Stop();
            var current = UnityEngine.Object.FindFirstObjectByType<CombatGame>();
            if (!EditorApplication.isPlaying || current == null) throw new InvalidOperationException("Enter CombatSpike Play mode.");
            current.StartCoroutine(FinalReview(current, directory));
        }

        [Serializable] private sealed class ReviewSample
        {
            public string character, action;
            public int sample, frame;
            public bool meshMeasured;
            public float speed, phase, lowestVertexY, leftSoleY, rightSoleY, leftWristToSupport, rightWristToGrip;
            public Vector3 leftFoot, rightFoot;
        }
        [Serializable] private sealed class ReviewPair
        {
            public string image, columns = "natural front, forced front, natural side, forced side";
            public int frame, differingPixels, maxChannelDifference;
        }
        [Serializable] private sealed class FinalReport
        {
            public string sampling = "Shipping profiles; controlled Tick once per ordinary player loop, dt=1/60, aim=+Z; director disabled during deterministic samples. EndOfFrame natural camera render with forcing false, then forced render at identical bones/phase. Final director_* samples keep the production director enabled.";
            public List<ReviewSample> samples = new List<ReviewSample>();
            public List<ReviewPair> pairs = new List<ReviewPair>();
        }

        private static IEnumerator FinalReview(CombatGame current, string directory)
        {
            Directory.CreateDirectory(directory);
            var art = current.GetComponent<CombatArtDirector>();
            bool enabledGame = current.enabled, enabledArt = art.enabled, paused = EditorApplication.isPaused;
            var originalFlags = new Dictionary<SkinnedMeshRenderer, bool>();
            var report = new FinalReport();
            var baked = new Mesh();
            FinalReviewStatus = "starting";
            try
            {
                Run(0, 0, false, false, 1);
                EditorApplication.update -= Drive;
                current.enabled = false;
                yield return null;
                yield return new WaitForEndOfFrame();
                art.enabled = false;
                var rigs = current.GetComponentsInChildren<CombatCharacterRig>().Where(r => r.IsSkinned).ToArray();
                if (rigs.Length != 2) throw new InvalidOperationException("Expected shipping hero and one Shooter.");
                foreach (var rig in rigs)
                    foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                    { originalFlags.Add(skin, skin.forceMatrixRecalculationPerRender); skin.forceMatrixRecalculationPerRender = false; }
                foreach (var rig in rigs)
                {
                    bool enemy = IsEnemy(rig.Profile);
                    string name = enemy ? "enemy" : "hero";
                    float speed = enemy ? 2.05f : 4.75f;
                    var actor = enemy ? rig.transform.parent.parent : rig.transform.parent;
                    var start = actor.position;
                    var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>();
                    var animator = rig.GetComponentInChildren<Animator>();
                    var leftWrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    var rightWrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var bindVertices = skin.sharedMesh.vertices;
                    var boneWeights = skin.sharedMesh.boneWeights;
                    var footSide = new int[bindVertices.Length];
                    for (int v = 0; v < footSide.Length; v++)
                    {
                        string bone = skin.bones[boneWeights[v].boneIndex0].name;
                        if (bone.Contains("LeftFoot") || bone.Contains("LeftToe")) footSide[v] = 1;
                        if (bone.Contains("RightFoot") || bone.Contains("RightToe")) footSide[v] = 2;
                    }
                    foreach (string action in new[] { "idle", "left", "right", "run_fire", "moving_reload", "death", "reset" })
                    {
                        FinalReviewStatus = name + " " + action;
                        actor.position = start;
                        rig.ResetPose();
                        if (action == "reset") rig.Tick(3f, Vector3.zero, Vector3.forward, -1, 0, 1);
                        if (action == "reset") rig.ResetPose();
                        var direction = action == "left" ? Vector3.left : action == "right" ? Vector3.right :
                            action == "run_fire" || action == "moving_reload" ? Vector3.forward : Vector3.zero;
                        int count = action == "death" ? 190 : action == "idle" || action == "reset" ? 5 : 91;
                        for (int f = 0; f < count; f++)
                        {
                            yield return null;
                            actor.position += direction * speed / 60f;
                            if (action == "run_fire" && f % 6 == 0) rig.Fire();
                            rig.Tick(1f / 60f, direction * speed, Vector3.forward,
                                action == "moving_reload" ? f / 90f : -1f, 0, action == "death" ? 1f : 0f);
                            yield return new WaitForEndOfFrame();
                            if (f == 4 && (action == "idle" || action == "reset") ||
                                (f == 30 || f == 60) && count == 91 || action == "death" && (f == 60 || f == 180))
                            {
                                string prefix = name + "_" + action + "_" + f;
                                PairViews(rig, directory, prefix + "_body", 0, report);
                                if (count == 91) PairViews(rig, directory, prefix + "_hands", 1, report);
                                if (action == "left" || action == "right" || action == "run_fire") PairViews(rig, directory, prefix + "_feet", 2, report);
                            }
                            skin.BakeMesh(baked);
                            var vertices = baked.vertices;
                            float low = float.PositiveInfinity, left = low, right = low;
                            for (int v = 0; v < vertices.Length; v++)
                            {
                                float y = skin.transform.TransformPoint(vertices[v]).y - (actor.position.y - .8f);
                                low = Mathf.Min(low, y);
                                if (footSide[v] == 1) left = Mathf.Min(left, y);
                                if (footSide[v] == 2) right = Mathf.Min(right, y);
                            }
                            report.samples.Add(new ReviewSample { character = name, action = action, sample = f, frame = Time.frameCount, meshMeasured = true,
                                speed = direction.magnitude * speed, phase = rig.GaitPhase, lowestVertexY = low, leftSoleY = left, rightSoleY = right,
                                leftWristToSupport = Vector3.Distance(leftWrist.position, rig.WeaponSupport.position),
                                rightWristToGrip = Vector3.Distance(rightWrist.position, rig.WeaponGrip.position),
                                leftFoot = actor.InverseTransformPoint(rig.LeftFoot.position), rightFoot = actor.InverseTransformPoint(rig.RightFoot.position) });
                        }
                    }
                    actor.position = start;
                    rig.ResetPose();
                }
                // The final movement samples go through the production director's LateUpdate.
                art.enabled = true;
                var hero = rigs.First(r => !IsEnemy(r.Profile));
                var shooter = rigs.First(r => IsEnemy(r.Profile));
                var player = hero.transform.parent;
                var enemyActor = shooter.transform.parent.parent;
                var visualMap = (IDictionary)Get(art, "enemyVisuals");
                var visual = visualMap[enemyActor];
                foreach (int sign in new[] { -1, 1 })
                {
                    FinalReviewStatus = "production director " + sign;
                    for (int f = 0; f < 70; f++)
                    {
                        yield return null;
                        float dt = Time.unscaledDeltaTime;
                        player.position += Vector3.right * (sign * 4.75f * dt);
                        enemyActor.position += Vector3.right * (sign * 2.05f * dt);
                        current.SetViewInput(0, player.position + Vector3.forward * 15, false);
                        Set(art, "lastShotDirection", Vector3.forward); Set(art, "shotAimRemaining", 999f);
                        Set(visual, "shotDirection", Vector3.forward); Set(visual, "shotAimRemaining", 999f);
                        yield return new WaitForEndOfFrame();
                        if (f == 30 || f == 60)
                        {
                            foreach (var rig in rigs)
                            {
                                string character = rig == hero ? "hero" : "enemy";
                                string prefix = "director_" + character + (sign < 0 ? "_left_" : "_right_") + f;
                                PairViews(rig, directory, prefix + "_hands", 1, report);
                                PairViews(rig, directory, prefix + "_body", 0, report);
                                PairViews(rig, directory, prefix + "_feet", 2, report);
                                report.samples.Add(new ReviewSample { character = character, action = "director_" + sign, sample = f,
                                    frame = Time.frameCount, phase = rig.GaitPhase, speed = rig == hero ? 4.75f : 2.05f,
                                    leftFoot = rig.transform.InverseTransformPoint(rig.LeftFoot.position), rightFoot = rig.transform.InverseTransformPoint(rig.RightFoot.position) });
                            }
                        }
                    }
                }
                File.WriteAllText(Path.Combine(directory, "samples.json"), JsonUtility.ToJson(report, true));
                FinalReviewStatus = "completed";
            }
            finally
            {
                foreach (var pair in originalFlags) if (pair.Key != null) pair.Key.forceMatrixRecalculationPerRender = pair.Value;
                UnityEngine.Object.DestroyImmediate(baked);
                Stop();
                if (current != null) current.enabled = enabledGame;
                if (art != null) art.enabled = enabledArt;
                EditorApplication.isPaused = paused;
            }
        }

        private static void PairViews(CombatCharacterRig rig, string directory, string name, int detail, FinalReport report)
        {
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            var flags = skins.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            var parts = rig.GetComponentsInChildren<Transform>(true);
            var layers = parts.Select(p => p.gameObject.layer).ToArray();
            var camera = new GameObject("SynchronizedReviewCamera").AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << 31; camera.nearClipPlane = .01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f,.2f,.22f);
            camera.orthographic = true; camera.orthographicSize = detail == 0 ? 1.55f : detail == 1 ? .7f : .7f;
            var target = new RenderTexture(480, 600, 24); camera.targetTexture = target;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.layer = 31;
            floor.transform.position = new Vector3(rig.transform.position.x, 0, rig.transform.position.z);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color = new Color(.35f,.36f,.37f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var strip = new Texture2D(1920,600,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            int differences = 0, maxDifference = 0;
            try
            {
                foreach (var part in parts) part.gameObject.layer = 31;
                var center = rig.transform.position + Vector3.up * (detail == 0 ? .65f : detail == 1 ? .5f : -.55f);
                if (detail == 1) center = (rig.GripHand.position + rig.SupportHand.position) * .5f;
                if (detail == 2)
                {
                    center = (rig.LeftFoot.position + rig.RightFoot.position) * .5f;
                    center.y = .35f;
                }
                if (detail == 0 && name.Contains("death"))
                {
                    center = rig.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips).position;
                    center.y = .6f;
                }
                for (int view = 0; view < 2; view++)
                {
                    camera.transform.position = center + (view == 0 ? new Vector3(.15f,.05f,4) : new Vector3(4,.05f,.15f));
                    camera.transform.LookAt(center);
                    Color32[] natural = null;
                    for (int mode = 0; mode < 2; mode++)
                    {
                        foreach (var skin in skins) skin.forceMatrixRecalculationPerRender = mode == 1;
                        camera.Render(); RenderTexture.active = target;
                        var pixels = new Texture2D(480,600,TextureFormat.RGB24,false);
                        pixels.ReadPixels(new Rect(0,0,480,600),0,0); pixels.Apply();
                        var colors = pixels.GetPixels32();
                        strip.SetPixels32((view*2+mode)*480,0,480,600,colors);
                        if (mode == 0) natural = colors;
                        else for (int i=0;i<colors.Length;i++)
                        {
                            int d = Math.Max(Math.Abs(colors[i].r-natural[i].r),Math.Max(Math.Abs(colors[i].g-natural[i].g),Math.Abs(colors[i].b-natural[i].b)));
                            if (d > 0) differences++;
                            maxDifference = Math.Max(maxDifference,d);
                        }
                        UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
                strip.Apply(); File.WriteAllBytes(Path.Combine(directory,name+".png"),strip.EncodeToPNG());
                report.pairs.Add(new ReviewPair { image=name+".png",frame=Time.frameCount,differingPixels=differences,maxChannelDifference=maxDifference });
            }
            finally
            {
                RenderTexture.active = previous;
                for (int i=0;i<skins.Length;i++) skins[i].forceMatrixRecalculationPerRender=flags[i];
                for (int i=0;i<parts.Length;i++) parts[i].gameObject.layer=layers[i];
                UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(floor); UnityEngine.Object.DestroyImmediate(mat); UnityEngine.Object.DestroyImmediate(strip);
            }
        }

        public static void Run(float x, float z, bool shooting, bool reload, int shooters = 1)
        {
            Stop();
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
            game = UnityEngine.Object.FindFirstObjectByType<CombatGame>();
            if (game == null) throw new InvalidOperationException("Open CombatSpike first.");
            previousEnabled = game.enabled;
            game.enabled = false;
            game.BeginRun();
            Set(game, "spawnTimer", 999f);
            Set(game, "invulnerability", 999f);
            var pool = (Array)Get(game, "enemies");
            Type contentCatalog = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((contentCatalog = assembly.GetType("TienTuyen.Content.ContentCatalog")) != null) break;
            if (contentCatalog == null) throw new InvalidOperationException("Content catalog is not loaded.");
            var catalog = contentCatalog.GetProperty("P2", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            var definition = catalog.GetType().GetMethod("GetEnemy").Invoke(catalog, new object[] { "enemy.shooter" });
            for (int i = 0; i < Mathf.Min(shooters, pool.Length); i++)
            {
                var enemy = pool.GetValue(i);
                Set(enemy, "active", true);
                Set(enemy, "definition", definition);
                Set(enemy, "hp", 100000f);
                Set(enemy, "shooter", true);
                Set(enemy, "attackTimer", 999f);
                var body = (GameObject)Get(enemy, "body");
                body.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
                body.transform.position = new Vector3((i % 5 - 2) * 2f, .8f, 4f + i / 5 * 2f);
                body.SetActive(true);
            }
            Set(game, "<AliveEnemies>k__BackingField", Mathf.Clamp(shooters, 0, pool.Length));
            if (reload)
            {
                ((int[])Get(game, "weaponAmmo"))[0] = 0;
                game.RequestReload();
            }
            movement = new Vector2(x, z);
            fire = shooting;
            elapsed = 0;
            FramesObserved = 0;
            warmupFrames = 3;
            MeanFrameMs = MaximumFrameMs = 0;
            lastFrame = Time.frameCount;
            EditorApplication.isPaused = false;
            EditorApplication.update += Drive;
        }

        private static void Drive()
        {
            if (!EditorApplication.isPlaying || game == null) { Stop(); return; }
            if (EditorApplication.isPaused || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (warmupFrames-- > 0) return;
            FramesObserved++;
            float frameMs = Time.unscaledDeltaTime * 1000;
            MeanFrameMs += (frameMs - MeanFrameMs) / FramesObserved;
            MaximumFrameMs = Mathf.Max(MaximumFrameMs, frameMs);
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            var player = game.transform.Find("Player");
            game.SetViewInput(0, player.position + Vector3.forward * 15f, fire);
            game.StepSimulation(dt, movement);
            elapsed += dt;
            if (elapsed >= .75f)
            {
                EditorApplication.update -= Drive;
                EditorApplication.isPaused = true;
                Debug.Log("Character review frozen after " + FramesObserved + " advancing gameplay frames.");
            }
        }

        public static void Stop()
        {
            EditorApplication.update -= Drive;
            if (game != null) game.enabled = previousEnabled;
            game = null;
        }

        public static void Capture(string path, bool enemy = false, bool refreshSkin = true)
        {
            var current = UnityEngine.Object.FindFirstObjectByType<CombatGame>();
            var rig = current.GetComponentsInChildren<CombatCharacterRig>().First(r => r.IsSkinned &&
                (enemy ? IsEnemy(r.Profile) : !IsEnemy(r.Profile)));
            CaptureRig(rig, path, refreshSkin);
        }

        private static void CaptureRig(CombatCharacterRig rig, string path, bool refreshSkin = true)
        {
            var camera = new GameObject("CharacterReviewCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = .02f;
            camera.fieldOfView = 40;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .21f, .23f);
            camera.cullingMask = 1 << 31;
            var parts = rig.GetComponentsInChildren<Transform>(true);
            var oldLayers = parts.Select(p => p.gameObject.layer).ToArray();
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var oldMatrixUpdates = skins.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            foreach (var part in parts) part.gameObject.layer = 31;
            var center = rig.transform.position + Vector3.up * .35f;
            camera.transform.position = center + new Vector3(2.5f, .8f, 2.7f);
            camera.transform.LookAt(center);
            var target = new RenderTexture(720, 720, 24);
            var pixels = new Texture2D(720, 720, TextureFormat.RGB24, false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "CharacterReviewFloor";
            floor.layer = 31;
            floor.transform.position = new Vector3(rig.transform.position.x, 0, rig.transform.position.z);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.28f, .30f, .31f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var previous = RenderTexture.active;
            try
            {
                // Synchronous pose reviews can change bones several times in one player loop.
                if (refreshSkin) foreach (var skin in skins) skin.forceMatrixRecalculationPerRender = true;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                for (int i = 0; i < skins.Length; i++) skins[i].forceMatrixRecalculationPerRender = oldMatrixUpdates[i];
                for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = oldLayers[i];
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(floor);
                UnityEngine.Object.DestroyImmediate(floorMaterial);
            }
        }

        [Serializable] private sealed class WeightRow { public int[] indices; public float[] values; }
        [Serializable] private sealed class SourceSkin { public Vector3[] vertices; public string[] bones; public List<WeightRow> weights; }
        [Serializable] private sealed class PoseResult
        {
            public string pose;
            public float capMaxMm, capRmsMm, floorMin = 999, maximumLowestVertexY = -999, minMuzzleDot = 1, maxHandSocketM;
            public int frames;
        }
        [Serializable] private sealed class DriverReport { public List<PoseResult> poses = new List<PoseResult>(); }

        public static void AuditEnemy(string directory)
        {
            if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
                throw new InvalidOperationException("Freeze advancing gameplay before auditing its actual rig.");
            var current = UnityEngine.Object.FindFirstObjectByType<CombatGame>();
            var rig = current.GetComponentsInChildren<CombatCharacterRig>().First(r => r.IsSkinned && IsEnemy(r.Profile));
            var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            var source = JsonUtility.FromJson<SourceSkin>(File.ReadAllText(Path.Combine(directory, "retarget-skin.json")));
            var mesh = skin.sharedMesh;
            if (source.vertices.Length != mesh.vertexCount) throw new InvalidOperationException("Reference topology differs.");
            var mappedBones = source.bones.Select(name => Array.FindIndex(skin.bones, b => b.name == name)).ToArray();
            if (mappedBones.Any(i => i < 0)) throw new InvalidOperationException("Reference bone missing.");
            var bind = mesh.bindposes;
            var baked = new Mesh();
            var matrices = new Matrix4x4[source.bones.Length];
            var report = new DriverReport();
            var directions = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                new Vector3(1,0,1).normalized, new Vector3(-1,0,-1).normalized, Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero };
            var names = new[] { "forward", "backward", "left", "right", "diagonal", "back_diagonal", "fire", "moving_reload", "hit", "death" };
            try
            {
                for (int p = 0; p < names.Length; p++)
                {
                    rig.ResetPose();
                    var result = new PoseResult { pose = names[p] };
                    double squared = 0; long count = 0;
                    int frames = p == 9 ? 180 : 90;
                    for (int f = 0; f < frames; f++)
                    {
                        if (p == 6 && f % 6 == 0) rig.Fire();
                        rig.Tick(1f / 60f, directions[p] * 4.75f, Vector3.forward,
                            p == 7 ? f / 90f : -1f, p == 8 && f < 11 ? 1f - f / 11f : 0f, p == 9 ? 1f : 0f);
                        if (f % 3 != 0) continue;
                        skin.BakeMesh(baked);
                        var vertices = baked.vertices;
                        float lowest = 999;
                        for (int b = 0; b < matrices.Length; b++)
                            matrices[b] = skin.transform.worldToLocalMatrix * skin.bones[mappedBones[b]].localToWorldMatrix * bind[mappedBones[b]];
                        for (int v = 0; v < vertices.Length; v++)
                        {
                            var row = source.weights[v];
                            var original = Vector3.zero;
                            for (int w = 0; w < row.indices.Length; w++)
                                original += matrices[row.indices[w]].MultiplyPoint3x4(source.vertices[v]) * row.values[w];
                            float error = (original - vertices[v]).magnitude;
                            squared += error * error; count++;
                            result.capMaxMm = Mathf.Max(result.capMaxMm, error * 1000);
                            lowest = Mathf.Min(lowest, skin.transform.TransformPoint(vertices[v]).y);
                        }
                        result.floorMin = Mathf.Min(result.floorMin, lowest);
                        result.maximumLowestVertexY = Mathf.Max(result.maximumLowestVertexY, lowest);
                        if (p != 9)
                        {
                            result.minMuzzleDot = Mathf.Min(result.minMuzzleDot, Vector3.Dot(rig.MuzzleTransform.forward, Vector3.forward));
                            if (p != 7) result.maxHandSocketM = Mathf.Max(result.maxHandSocketM,
                                Vector3.Distance(rig.GripHand.position, rig.WeaponGrip.position),
                                Vector3.Distance(rig.SupportHand.position, rig.WeaponSupport.position));
                        }
                        result.frames++;
                        if (f == 60 || (p == 9 && f == 150)) CaptureRig(rig, Path.Combine(directory, "driver_" + names[p] + "_" + f + ".png"));
                    }
                    result.capRmsMm = (float)Math.Sqrt(squared / count) * 1000;
                    report.poses.Add(result);
                }
                rig.ResetPose();
                rig.Tick(1f / 60f, Vector3.zero, Vector3.forward);
                CaptureRig(rig, Path.Combine(directory, "driver_reset.png"));
                File.WriteAllText(Path.Combine(directory, "driver-report.json"), JsonUtility.ToJson(report, true));
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); rig.ResetPose(); }
        }

        public static void MeasureRigCost(string path)
        {
            var rigs = UnityEngine.Object.FindObjectsByType<CombatCharacterRig>(FindObjectsSortMode.None)
                .Where(r => r.IsSkinned && IsEnemy(r.Profile)).ToArray();
            if (rigs.Length != 25) throw new InvalidOperationException("Run review with 25 Shooters first.");
            var velocity = Vector3.forward * 3f;
            for (int f = 0; f < 30; f++) foreach (var rig in rigs) rig.Tick(1f / 60f, velocity, Vector3.forward);
            var watch = new System.Diagnostics.Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int f = 0; f < 120; f++) foreach (var rig in rigs) rig.Tick(1f / 60f, velocity, Vector3.forward);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            File.WriteAllText(path, "{\"shooters\":25,\"measuredTicksPerShooter\":120,\"mean25RigTickMs\":" +
                (watch.Elapsed.TotalMilliseconds / 120).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"managedBytes\":" + allocated + ",\"advancingFrames\":" + FramesObserved +
                ",\"editorMeanFrameMs\":" + MeanFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"editorMaxFrameMs\":" + MaximumFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        }

        private static FieldInfo Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(target.GetType().Name, name);
        private static object Get(object target, string name) => Field(target, name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    }
}
#endif
